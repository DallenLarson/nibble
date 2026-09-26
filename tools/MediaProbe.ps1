# Measures whether audio keeps playing when a tab stops being the active one.
#
# The page (tools/MediaPage.html) plays an audible tone and reports its own
# audio.currentTime once a second. The probe starts it, switches away from it by opening a
# new tab, waits, then says plainly whether the audio kept advancing or was stopped - and
# whether the page stopped reporting at all, which is what a suspended (napping) tab looks
# like. Window titles are sampled as it waits, because Nibble's window title carries the
# active tab's title, so the log shows which tab was really in front.
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Profile,
    [int]$Port = 8797,
    [int]$SuspendSeconds = 30,
    [int]$Background = 50,
    [string]$Log = "",
    [switch]$KeepLog
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$uia = Join-Path $PSScriptRoot "Uia.ps1"
# Note: $PSScriptRoot is empty inside the param block, so paths default here instead.
if (-not $Log) { $Log = Join-Path $PSScriptRoot "..\scratch\media\samples.jsonl" }

Add-Type -Namespace MP -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr param);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
[DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int max);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
public delegate bool EnumProc(IntPtr hWnd, IntPtr param);
'@

function Get-Titles([int]$Owned) {
    $script:hits = @()
    $callback = [MP.Win+EnumProc] {
        param($h, $p)
        $owner = 0
        [MP.Win]::GetWindowThreadProcessId($h, [ref]$owner) | Out-Null
        if ($owner -eq $Owned -and [MP.Win]::IsWindowVisible($h)) {
            $text = New-Object System.Text.StringBuilder 256
            [MP.Win]::GetWindowText($h, $text, 256) | Out-Null
            if ($text.Length -gt 0) { $script:hits += $text.ToString() }
        }
        return $true
    }
    [MP.Win]::EnumWindows($callback, [IntPtr]::Zero) | Out-Null
    return $script:hits
}

# ---- a profile that behaves like a machine where the user is already set up ----
New-Item -ItemType Directory -Force -Path $Profile | Out-Null
$Profile = (Resolve-Path $Profile).Path
$logDir = Split-Path -Parent $Log
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$Log = (Resolve-Path $logDir).Path + "\" + (Split-Path -Leaf $Log)
if (Test-Path $Log) { Move-Item -LiteralPath $Log -Destination "$Log.old" -Force }

$settings = [ordered]@{
    Theme = "Dark"; SearchEngine = "duckduckgo"; Blocker = $false; SuspendSeconds = $SuspendSeconds
    RestoreSession = $false; Onboarded = $true; AccentColor = "#A3E635"; UserName = "Probe"
    Clock = "24"; ThemeId = "default"; ChromeUserAgent = $false; AllowSitePermissions = $true
    Updates = $false; WindowWidth = 1180; WindowHeight = 780; WindowLeft = 80; WindowTop = 60
}
$settings | ConvertTo-Json | Set-Content -Path (Join-Path $Profile "settings.json") -Encoding UTF8

$server = Start-Job -FilePath (Join-Path $PSScriptRoot "MediaServer.ps1") -ArgumentList `
    $Port, 900, $Log, (Join-Path $PSScriptRoot "MediaPage.html")
Start-Sleep -Seconds 2

$env:NIBBLE_PROFILE = $Profile
$app = Start-Process -FilePath $Exe -ArgumentList "http://127.0.0.1:$Port/page" -PassThru

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
    if ($KeepLog) { "samples kept at $Log" }
}

$playing = $false
$pageLoaded = $false
for ($i = 0; $i -lt 40; $i++) {
    Start-Sleep -Milliseconds 500
    $rows = Samples
    if (-not $pageLoaded -and ($rows | Where-Object { $_.kind -eq "tick" })) {
        $pageLoaded = $true
        # Autoplay is usually refused without a gesture, so press the page's own button.
        $pressed = (& powershell -NoProfile -ExecutionPolicy Bypass -File $uia -Mode click -ProcessId $app.Id -Name "start playback") -join " "
        "start button: $pressed"
    }
    if ($rows | Where-Object { ($_.kind -eq "start" -or $_.kind -eq "playing") -and -not $_.paused }) {
        $playing = $true
        break
    }
}
if (-not $playing) {
    "the page never started playing; samples so far:"
    Samples | Select-Object -Last 4 | ForEach-Object { "  $($_ | ConvertTo-Json -Compress)" }
    Finish
    exit 1
}

Start-Sleep -Seconds 6
$baseline = Samples
$switchWall = [DateTimeOffset]::Now.ToUnixTimeMilliseconds()
$switchAt = Get-Date
"switching to another tab"

# Opening a tab makes the media tab inactive (and, past SuspendSeconds, a nap candidate).
$clicked = (& powershell -NoProfile -ExecutionPolicy Bypass -File $uia -Mode click -ProcessId $app.Id -Name "New tab") -join " "
"new tab: $clicked"

$windowTitles = @()
while (((Get-Date) - $switchAt).TotalSeconds -lt $Background) {
    Start-Sleep -Seconds 2
    $windowTitles += "  +{0,4:N0}s  {1}" -f ((Get-Date) - $switchAt).TotalSeconds, ((Get-Titles $app.Id) -join " | ")
}
$after = Samples
Finish

""
"what the window was showing while the media tab sat in the background:"
$windowTitles

$ticksAfter = @($after | Where-Object { $_.kind -eq "tick" -and $_.wall -gt $switchWall } | Sort-Object wall)
""
"timeline (every 5th report after the switch):"
for ($i = 0; $i -lt $ticksAfter.Count; $i += 5) {
    $row = $ticksAfter[$i]
    "  +{0,5:N1}s  ct={1,6:N1}  {2}  frames={3}" -f (($row.wall - $switchWall) / 1000), $row.ct, $row.vis, $row.frames
}

""
"events after the switch:"
@($after | Where-Object { $_.kind -ne "tick" -and $_.wall -gt $switchWall }) | Sort-Object wall | ForEach-Object {
    $extra = if ($_.err) { " ($($_.err))" } elseif ($_.code) { " (code $($_.code))" } else { "" }
    "  +{0,5:N1}s  {1}{2}  {3}" -f (($_.wall - $switchWall) / 1000), $_.kind, $extra, $_.vis
}

function Measure-Slice([object[]]$rows, [int64]$from, [int64]$to) {
    # Only samples where the tone was actually playing say anything about playback.
    $slice = @($rows | Where-Object {
        $_.kind -eq "tick" -and -not $_.paused -and $_.wall -ge $from -and $_.wall -le $to
    } | Sort-Object wall)
    if ($slice.Count -lt 2) { return $null }
    $wall = ($slice[-1].wall - $slice[0].wall) / 1000.0
    $gap = 0
    # Only forward movement counts, so the tone looping back to the start of the file at the
    # end of a long window cannot make the total look like it went backwards.
    $advanced = 0.0
    for ($i = 1; $i -lt $slice.Count; $i++) {
        $gap = [Math]::Max($gap, ($slice[$i].wall - $slice[$i - 1].wall) / 1000.0)
        $step = $slice[$i].ct - $slice[$i - 1].ct
        if ($step -gt 0) { $advanced += $step }
    }
    return [pscustomobject]@{
        Samples = $slice.Count
        Wall = $wall
        Advanced = $advanced
        Ratio = $advanced / [Math]::Max(0.001, $wall)
        Gap = $gap
        SilenceAtEnd = ($to - $slice[-1].wall) / 1000.0
        Paused = @($slice | Where-Object { $_.paused }).Count
        Visibility = $slice[-1].vis
    }
}

function Describe([string]$label, $m) {
    if (-not $m) { return "  [$label]: no samples"; }
    return "  [{0}]: {1} samples over {2:N1}s | audio advanced {3:N1}s of a possible {4:N1}s ({5:P0}) | longest silence between reports {6:N1}s | paused in {7} samples | last visibility {8}" -f `
        $label, $m.Samples, $m.Wall, $m.Advanced, $m.Wall, $m.Ratio, $m.Gap, $m.Paused, $m.Visibility
}

$endWall = $switchWall + ($Background * 1000)
$base = Measure-Slice $baseline 0 $switchWall
$back = Measure-Slice $after $switchWall $endWall

""
"verdict:"
Describe "before the switch" $base
Describe "after the switch" $back
"  suspend setting: {0}s, background window: {1}s" -f $SuspendSeconds, $Background

$failed = 0
if (-not $base -or $base.Ratio -lt 0.8) { "  FAIL  audio was not playing to begin with"; $failed++ }
if ($back) {
    if ($back.Ratio -lt 0.8) {
        "  FAIL  audio stopped ({0:P0} of real time) while the tab was in the background" -f $back.Ratio
        $failed++
    }
    else { "  PASS  audio kept advancing ({0:P0} of real time) while the tab was in the background" -f $back.Ratio }
    if ($back.Gap -gt 8) { "  FAIL  reports stopped for {0:N1}s: the page was frozen" -f $back.Gap; $failed++ }
    else { "  PASS  the page never stopped reporting (longest gap {0:N1}s)" -f $back.Gap }
    if ($back.SilenceAtEnd -gt 8) {
        "  FAIL  the last report was {0:N0}s before the window ended: the tab was put to sleep" -f $back.SilenceAtEnd
        $failed++
    }
    else { "  PASS  the page was still reporting at the end of the window" }
}
else { "  FAIL  no samples at all after the switch"; $failed++ }
if ($Background -lt ($SuspendSeconds + 15)) { "  NOTE  background window is shorter than the suspend timer plus the 10s sweep" }

exit $failed
