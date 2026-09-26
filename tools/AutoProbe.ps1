# One page, no clicking: it calls window.open the moment it loads and reports what it got
# back, so the popup path can be told apart from the click path.
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Profile,
    [int]$Port = 8799,
    [int]$Wait = 20
)
$ErrorActionPreference = "Stop"
$here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$Log = Join-Path $here "..\scratch\autopopup\samples.jsonl"
New-Item -ItemType Directory -Force -Path $Profile | Out-Null
$Profile = (Resolve-Path $Profile).Path
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Log) | Out-Null
if (Test-Path $Log) { Move-Item -LiteralPath $Log -Destination "$Log.old" -Force }

$settings = [ordered]@{
    Theme = "Dark"; SearchEngine = "duckduckgo"; Blocker = $false; SuspendSeconds = 600
    RestoreSession = $false; Onboarded = $true; AccentColor = "#A3E635"; UserName = "Probe"
    Clock = "24"; ThemeId = "default"; ChromeUserAgent = $false; AllowSitePermissions = $true
    Updates = $false; WindowWidth = 1180; WindowHeight = 780; WindowLeft = 80; WindowTop = 60
}
$settings | ConvertTo-Json | Set-Content -Path (Join-Path $Profile "settings.json") -Encoding UTF8

$server = Start-Job -FilePath (Join-Path $here "MediaServer.ps1") -ArgumentList $Port, 900, $Log, (Join-Path $here "MediaPage.html")
Start-Sleep -Seconds 2

$env:NIBBLE_PROFILE = $Profile
$app = Start-Process -FilePath $Exe -ArgumentList "http://127.0.0.1:$Port/auto" -PassThru
Start-Sleep -Seconds $Wait

$titles = @()
Add-Type -Namespace AP -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr param);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
[DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int max);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
public delegate bool EnumProc(IntPtr hWnd, IntPtr param);
'@
$script:hits = @()
$cb = [AP.Win+EnumProc] {
    param($h, $p)
    $owner = 0
    [AP.Win]::GetWindowThreadProcessId($h, [ref]$owner) | Out-Null
    if ($owner -eq $app.Id -and [AP.Win]::IsWindowVisible($h)) {
        $t = New-Object System.Text.StringBuilder 256
        [AP.Win]::GetWindowText($h, $t, 256) | Out-Null
        if ($t.Length -gt 0) { $script:hits += $t.ToString() }
    }
    return $true
}
[AP.Win]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
$titles = $script:hits

$rows = @()
if (Test-Path $Log) {
    $rows = @(Get-Content -LiteralPath $Log | Where-Object { $_ -match '^\s*\{' } | ForEach-Object { $_ | ConvertFrom-Json })
}
$appLog = Join-Path $Profile "nibble.log"
Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
Stop-Job $server -ErrorAction SilentlyContinue
Remove-Job $server -Force -ErrorAction SilentlyContinue

"windows: " + ($titles -join " | ")
""
"page reports:"
$rows | ForEach-Object { "  " + ($_ | ConvertTo-Json -Compress) }
""
"nibble.log:"
if (Test-Path $appLog) { Get-Content $appLog -Tail 25 | ForEach-Object { "  $_" } } else { "  (none)" }
