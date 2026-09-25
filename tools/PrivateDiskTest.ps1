# Second private-window check, aimed at the disk: does anything a private window does
# survive in the profile folder? Uses two differently named cookies so the on-disk
# cookie store can be searched for each jar separately.
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Profile,
    [int]$Port = 8792
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

function Read-Shared([string]$Path) {
    try {
        $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        try { return (New-Object System.IO.StreamReader($stream)).ReadToEnd() }
        finally { $stream.Dispose() }
    }
    catch { return "<unreadable>" }
}

function Close-Private([int]$Owned) {
    $auto = [System.Windows.Automation.AutomationElement]
    $condition = New-Object System.Windows.Automation.PropertyCondition($auto::ProcessIdProperty, $Owned)
    foreach ($w in $auto::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)) {
        if ($w.Current.Name -like "*private*") {
            try { $w.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() } catch { }
            Start-Sleep -Seconds 2
        }
    }
}

$env:NIBBLE_PROFILE = $Profile
Set-Content -Path (Join-Path $Profile "history.json") -Value "[]" -NoNewline
$sessionFile = Join-Path $Profile "session.json"
if (Test-Path $sessionFile) { Remove-Item $sessionFile -Force }

$job = Start-Job -FilePath (Join-Path $PSScriptRoot "CookieServer.ps1") -ArgumentList $Port, 600
Start-Sleep -Seconds 2

$main = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds 10
"first process   : pid=$($main.Id) exited=$($main.HasExited)"
"windows now     : " + ((Get-Titles $main.Id) -join " | ")

# normal jar: nibbletest
Start-Process -FilePath $Exe -ArgumentList "http://127.0.0.1:$Port/set?n=nibbletest&c=normal" | Out-Null
Start-Sleep -Seconds 5

# private jar: prvtoken, then read both jars back
Start-Process -FilePath $Exe -ArgumentList "--private", "http://127.0.0.1:$Port/set?n=prvtoken&c=secret" | Out-Null
Start-Sleep -Seconds 7
Start-Process -FilePath $Exe -ArgumentList "--private", "http://127.0.0.1:$Port/check" | Out-Null
Start-Sleep -Seconds 7
"private window  : " + ((Get-Titles $main.Id | Where-Object { $_ -like "*private*" }) -join " | ")

Start-Process -FilePath $Exe -ArgumentList "http://127.0.0.1:$Port/check" | Out-Null
Start-Sleep -Seconds 5
"normal window   : " + ((Get-Titles $main.Id | Where-Object { $_ -notlike "*private*" }) -join " | ")

Close-Private $main.Id
Start-Sleep -Seconds 3

"history from both jars : " + (Read-Shared (Join-Path $Profile "history.json"))
"session after close    : " + (Read-Shared $sessionFile)

Stop-Job $job -ErrorAction SilentlyContinue
Remove-Job $job -Force -ErrorAction SilentlyContinue

# with the browser closed the cookie store is readable: what survived on disk?
Start-Sleep -Seconds 2
$cookies = Join-Path $Profile "WebView2\EBWebView\Default\Network\Cookies"
$bytes = [System.IO.File]::ReadAllBytes($cookies)
$text = [System.Text.Encoding]::GetEncoding(28591).GetString($bytes)
"on-disk cookie names   : nibbletest=" + $text.Contains("nibbletest") + " prvtoken=" + $text.Contains("prvtoken")
