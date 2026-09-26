# Proves that a page which calls window.open gets a real window back.
#
# That is the whole reason a Google sign-in inside Firebase failed before: the popup was
# turned into a tab, the tab had no window.opener and no shared storage, and the flow gave up
# with "unable to process request due to missing initial state". This probe opens the pair of
# pages in tools/PopupOpener.html and tools/Popup.html and asks both of them what they saw.
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Profile,
    [int]$Port = 8798,
    [int]$Wait = 25,
    [string]$Log = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$uia = Join-Path $PSScriptRoot "Uia.ps1"
if (-not $Log) { $Log = Join-Path $PSScriptRoot "..\scratch\popup\samples.jsonl" }

Add-Type -Namespace PW -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr param);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
[DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int max);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
public delegate bool EnumProc(IntPtr hWnd, IntPtr param);
'@

function Get-Titles([int]$Owned) {
    $script:hits = @()
    $callback = [PW.Win+EnumProc] {
        param($h, $p)
        $owner = 0
        [PW.Win]::GetWindowThreadProcessId($h, [ref]$owner) | Out-Null
        if ($owner -eq $Owned -and [PW.Win]::IsWindowVisible($h)) {
            $text = New-Object System.Text.StringBuilder 256
            [PW.Win]::GetWindowText($h, $text, 256) | Out-Null
            if ($text.Length -gt 0) { $script:hits += $text.ToString() }
        }
        return $true
    }
    [PW.Win]::EnumWindows($callback, [IntPtr]::Zero) | Out-Null
    return $script:hits
}

New-Item -ItemType Directory -Force -Path $Profile | Out-Null
$Profile = (Resolve-Path $Profile).Path
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Log) | Out-Null
$Log = (Resolve-Path (Split-Path -Parent $Log)).Path + "\" + (Split-Path -Leaf $Log)
if (Test-Path $Log) { Move-Item -LiteralPath $Log -Destination "$Log.old" -Force }

$settings = [ordered]@{
    Theme = "Dark"; SearchEngine = "duckduckgo"; Blocker = $false; SuspendSeconds = 600
    RestoreSession = $false; Onboarded = $true; AccentColor = "#A3E635"; UserName = "Probe"
    Clock = "24"; ThemeId = "default"; ChromeUserAgent = $false; AllowSitePermissions = $true
    Updates = $false; WindowWidth = 1180; WindowHeight = 780; WindowLeft = 80; WindowTop = 60
}
$settings | ConvertTo-Json | Set-Content -Path (Join-Path $Profile "settings.json") -Encoding UTF8

$server = Start-Job -FilePath (Join-Path $PSScriptRoot "MediaServer.ps1") -ArgumentList `
    $Port, 900, $Log, (Join-Path $PSScriptRoot "MediaPage.html")
Start-Sleep -Seconds 2

$env:NIBBLE_PROFILE = $Profile
$app = Start-Process -FilePath $Exe -ArgumentList "http://127.0.0.1:$Port/popupopener" -PassThru

function Samples {
    if (-not (Test-Path $Log)) { return @() }
    try {
        return @(Get-Content -LiteralPath $Log | Where-Object { $_ -match '^\s*\{' } | ForEach-Object { $_ | ConvertFrom-Json })
    }
    catch { return @() }
}

function Finish {
    Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
    Stop-Job $server -ErrorAction SilentlyContinue
    Remove-Job $server -Force -ErrorAction SilentlyContinue
}

# Wait for the opener page, then press its button the way a person would.
$openerReady = $false
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Milliseconds 500
    if (Get-Titles $app.Id | Where-Object { $_ -like "*popup opener*" }) { $openerReady = $true; break }
}
if (-not $openerReady) {
    "the opener page never loaded; windows: " + ((Get-Titles $app.Id) -join " | ")
    Finish
    exit 1
}

$clicked = (& powershell -NoProfile -ExecutionPolicy Bypass -File $uia -Mode click -ProcessId $app.Id -Name "open popup") -join " "
"open popup: $clicked"

$popupSeen = $false
$titles = @()
for ($i = 0; $i -lt ($Wait * 2); $i++) {
    Start-Sleep -Milliseconds 500
    $titles = Get-Titles $app.Id
    if ($titles | Where-Object { $_ -like "*popup window*" }) { $popupSeen = $true; break }
}

Start-Sleep -Seconds 3
$rows = Samples
$titles = Get-Titles $app.Id
$pageTexts = (& powershell -NoProfile -ExecutionPolicy Bypass -File $uia -Mode texts -ProcessId $app.Id) -join " / "
$pageButtons = (& powershell -NoProfile -ExecutionPolicy Bypass -File $uia -Mode dump -ProcessId $app.Id) -join " / "
Finish

""
"windows while the popup was open:"
$titles | ForEach-Object { "  $_" }

""
"what the page said:"
"  texts:   $pageTexts"
"  buttons: $pageButtons"

""
"what the pages reported:"
$rows | ForEach-Object { "  $($_ | ConvertTo-Json -Compress)" }

$loaded = $rows | Where-Object { $_.kind -eq "popup-loaded" } | Select-Object -Last 1
$posted = $rows | Where-Object { $_.kind -eq "popup-posted" } | Select-Object -Last 1
$reply = $rows | Where-Object { $_.kind -eq "message-from-popup" } | Select-Object -Last 1
$opened = $rows | Where-Object { $_.kind -eq "opened" } | Select-Object -Last 1

$failed = 0
""
"verdict:"
if ($popupSeen) { "  PASS  window.open produced a second window" } else { "  FAIL  no second window appeared"; $failed++ }
if ($opened -and $opened.opened) { "  PASS  the page got a window object back from window.open" }
else { "  FAIL  window.open returned nothing"; $failed++ }
if ($loaded -and $loaded.hasOpener) { "  PASS  the popup can see its opener" }
else { "  FAIL  the popup has no opener (this is the missing-initial-state failure)"; $failed++ }
if ($loaded -and $loaded.storage -eq "kept") { "  PASS  the popup has working session storage" }
else { "  FAIL  session storage was " + $(if ($loaded) { $loaded.storage } else { "never reached" }); $failed++ }
if ($posted -and $posted.ok) { "  PASS  the popup posted a message to the opener" }
else { "  FAIL  the popup could not post to the opener"; $failed++ }
if ($reply -and $reply.data -eq "signed-in-as-probe" -and $reply.hasSource) {
    "  PASS  the opener received it, with a source window attached"
}
else { "  FAIL  the opener never heard from the popup"; $failed++ }

exit $failed
