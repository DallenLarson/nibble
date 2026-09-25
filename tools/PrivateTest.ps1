# End-to-end proof for Nibble private windows:
#   * a private window has its own cookie jar (the normal one is invisible to it)
#   * two private windows share that jar between them
#   * nothing a private window does reaches the profile on disk, history or session
# Needs the app to be running with NIBBLE_PROFILE pointing at a scratch profile.
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Profile,
    [int]$Port = 8791
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function Get-Titles([int]$Owned) {
    $auto = [System.Windows.Automation.AutomationElement]
    $condition = New-Object System.Windows.Automation.PropertyCondition($auto::ProcessIdProperty, $Owned)
    $names = @()
    foreach ($w in $auto::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)) {
        $names += "$($w.Current.Name)"
    }
    return $names
}

function Snapshot([string]$Path) {
    $files = Get-ChildItem $Path -Recurse -File -ErrorAction SilentlyContinue
    [pscustomobject]@{
        Count = ($files | Measure-Object).Count
        Bytes = ($files | Measure-Object Length -Sum).Sum
        Newest = ($files | Sort-Object LastWriteTime -Descending | Select-Object -First 1).LastWriteTime
    }
}

# The engine holds these files open while it runs, so read them with sharing allowed.
function Read-Shared([string]$Path) {
    try {
        $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        try {
            $reader = New-Object System.IO.StreamReader($stream)
            return $reader.ReadToEnd()
        }
        finally { $stream.Dispose() }
    }
    catch { return "" }
}

$env:NIBBLE_PROFILE = $Profile
$job = Start-Job -FilePath (Join-Path $PSScriptRoot "CookieServer.ps1") -ArgumentList $Port, 900
Start-Sleep -Seconds 2

$main = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds 8
$before = Snapshot $Profile

"start: windows=" + ((Get-Titles $main.Id) -join " | ")

# 1. a normal cookie, in the normal window
Start-Process -FilePath $Exe -ArgumentList "http://127.0.0.1:$Port/set?c=normal1" | Out-Null
Start-Sleep -Seconds 5
Start-Process -FilePath $Exe -ArgumentList "http://127.0.0.1:$Port/check" | Out-Null
Start-Sleep -Seconds 5
"normal jar      : " + ((Get-Titles $main.Id) -join " | ")

# 2. a private window must not see it
Start-Process -FilePath $Exe -ArgumentList "--private", "http://127.0.0.1:$Port/check" | Out-Null
Start-Sleep -Seconds 7
$titles = Get-Titles $main.Id
"private jar     : " + ($titles -join " | ")

# 3. private windows keep their own cookie, shared with each other
Start-Process -FilePath $Exe -ArgumentList "--private", "http://127.0.0.1:$Port/set?c=private1" | Out-Null
Start-Sleep -Seconds 6
Start-Process -FilePath $Exe -ArgumentList "--private", "http://127.0.0.1:$Port/check" | Out-Null
Start-Sleep -Seconds 7
$titles = Get-Titles $main.Id
"private jar 2   : " + ($titles -join " | ")

# 4. the normal window still has only its own cookie
Start-Process -FilePath $Exe -ArgumentList "http://127.0.0.1:$Port/check" | Out-Null
Start-Sleep -Seconds 5
"normal jar again: " + ((Get-Titles $main.Id) -join " | ")

# 5. what did the profile on disk do while all that happened?
$during = Snapshot $Profile
$cookies = Join-Path $Profile "WebView2\EBWebView\Default\Network\Cookies"
$cookieText = Read-Shared $cookies
"profile before  : $($before.Count) files, $($before.Bytes) bytes, newest $($before.Newest)"
"profile during  : $($during.Count) files, $($during.Bytes) bytes, newest $($during.Newest)"
"on-disk cookie  : normal1=" + $cookieText.Contains("normal1") + " private1=" + $cookieText.Contains("private1")
"history entries : " + (Read-Shared (Join-Path $Profile "history.json"))

# 6. close the private windows, then look again
$auto = [System.Windows.Automation.AutomationElement]
$condition = New-Object System.Windows.Automation.PropertyCondition($auto::ProcessIdProperty, $main.Id)
foreach ($w in $auto::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)) {
    if ($w.Current.Name -like "*private*") {
        try { $w.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() } catch { }
        Start-Sleep -Seconds 2
    }
}
Start-Sleep -Seconds 3
$after = Snapshot $Profile
"profile after   : $($after.Count) files, $($after.Bytes) bytes, newest $($after.Newest)"
"windows left    : " + ((Get-Titles $main.Id) -join " | ")
"session         : " + (Read-Shared (Join-Path $Profile "session.json"))

Stop-Job $job -ErrorAction SilentlyContinue
Remove-Job $job -Force -ErrorAction SilentlyContinue
