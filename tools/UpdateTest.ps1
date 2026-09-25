# End-to-end test of the self-updater, against a feed this script builds rather than GitHub:
#
#   1. build a "newer release" (the same browser, installed as 9.9.9) with ISCC /DAppVersion
#   2. write a feed JSON pointing at it, in the shape GitHub's API returns
#   3. run Nibble --check-updates against that feed and check it downloaded the installer
#   4. start Nibble normally and check it applied the update and came back
#   5. put the real version back
#
#   powershell -File tools/UpdateTest.ps1
#
# Needs Inno Setup 6 for step 1 (the installer build), exactly like tools/package.ps1.
param(
    [string]$Exe = (Join-Path $env:LOCALAPPDATA "Programs\Nibble\Nibble.exe"),
    [string]$Scratch = "$PSScriptRoot\..\scratch\update-test",
    [string]$FakeVersion = "9.9.9"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$scratchPath = if ([System.IO.Path]::IsPathRooted($Scratch)) { $Scratch } else { Join-Path $root $Scratch }
$feedDir = Join-Path $scratchPath "feed"
$profileDir = Join-Path $scratchPath "profile"
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{8A1D2B6C-3E4F-4A55-9C1B-7D2E5F0A1B33}_is1"

$failures = 0
function Check([string]$label, [bool]$ok, [string]$detail = "") {
    if (-not $ok) { $script:failures++ }
    "{0}  {1}{2}" -f $(if ($ok) { "PASS" } else { "FAIL" }), $label, $(if ($detail) { "  -> $detail" } else { "" })
}

if (-not (Test-Path $Exe)) { throw "no Nibble at $Exe - install it first (dist\Nibble-*-Setup.exe)" }
$installed = (Get-Item $Exe).VersionInfo.FileVersion
"testing with the installed build $installed at $Exe"

# --- 1. a "newer release" ---------------------------------------------------------------
New-Item -ItemType Directory -Force -Path $feedDir, $profileDir | Out-Null
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)
$iscc += @(Get-ChildItem "$env:ProgramFiles*\Inno Setup*\ISCC.exe" -ErrorAction SilentlyContinue |
    ForEach-Object { $_.FullName })
$iscc = @($iscc | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1)
if ($iscc.Count -eq 0) { throw "Inno Setup 6 not found - install it from https://jrsoftware.org/isdl.php" }

"building a $FakeVersion installer..."
& $iscc[0] "/DAppVersion=$FakeVersion" "/O$feedDir" (Join-Path $root "installer\Nibble.iss") |
    Select-String -Pattern "Successful compile|Error" | ForEach-Object { "  " + $_.Line }
$fake = Join-Path $feedDir "Nibble-$FakeVersion-Setup.exe"
Check "a $FakeVersion release was built" (Test-Path $fake) $fake
if (-not (Test-Path $fake)) { exit 1 }

# --- 2. the feed ------------------------------------------------------------------------
$feed = Join-Path $feedDir "latest.json"
$payload = [ordered]@{
    tag_name = "v$FakeVersion"
    body     = "A test release, built by tools/UpdateTest.ps1."
    html_url = "https://github.com/DallenLarson/nibble/releases/tag/v$FakeVersion"
    assets   = @(
        [ordered]@{
            name                 = "Nibble-$FakeVersion-Setup.exe"
            size                 = (Get-Item $fake).Length
            browser_download_url = ([System.Uri]$fake).AbsoluteUri
        }
    )
}
$payload | ConvertTo-Json -Depth 6 | Set-Content $feed -Encoding utf8
Check "feed written" (Test-Path $feed)

# --- 3. --check-updates ----------------------------------------------------------------
$env:NIBBLE_PROFILE = $profileDir
$env:NIBBLE_UPDATE_FEED = ([System.Uri]$feed).AbsoluteUri
Get-Process Nibble -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

"asking the feed with --check-updates..."
$check = Start-Process -FilePath $Exe -ArgumentList "--check-updates" -PassThru -Wait
Check "check exits cleanly" ($check.ExitCode -eq 0) "exit=$($check.ExitCode)"

$statusPath = Join-Path $profileDir "updates\last-check.json"
Check "a status file was written" (Test-Path $statusPath)
$status = Get-Content $statusPath -Raw | ConvertFrom-Json
Check "feed read as $FakeVersion" ($status.latest -eq $FakeVersion) "latest=$($status.latest) state=$($status.state)"
Check "the installer came down" ($status.downloaded -eq $true -and (Test-Path $status.path)) "path=$($status.path)"

# --- 4. apply on the next start --------------------------------------------------------
# Start from a clean updater state: a previous run's attempt marker would (correctly) stop
# this version being tried again for six hours, which is the point of the marker, not a bug.
foreach ($leftover in @("apply-attempt.json", "install.log")) {
    $path = Join-Path $profileDir "updates\$leftover"
    if (Test-Path $path) { [System.IO.File]::Delete($path) }
}
"starting Nibble - it should hand over to the installer and come back on its own..."
$before = (Get-ItemProperty $uninstallKey -ErrorAction SilentlyContinue).DisplayName
$app = Start-Process -FilePath $Exe -PassThru
$applied = $false
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 2
    $now = (Get-ItemProperty $uninstallKey -ErrorAction SilentlyContinue).DisplayName
    if ($now -and $now -ne $before) { $applied = $true; break }
}
Check "the update installed itself" $applied "uninstall entry was '$before', now '$now'"

$back = $false
for ($i = 0; $i -lt 20; $i++) {
    Start-Sleep -Seconds 1
    if (Get-Process Nibble -ErrorAction SilentlyContinue) { $back = $true; break }
}
Check "Nibble came back by itself" $back
Start-Sleep -Seconds 2
Get-Process Nibble -ErrorAction SilentlyContinue | Stop-Process -Force

$marker = Join-Path $profileDir "updates\apply-attempt.json"
Check "the attempt was recorded (so a bad installer cannot loop)" (Test-Path $marker)

# --- 5. put the real build back --------------------------------------------------------
"putting the real build back..."
$real = Get-ChildItem (Join-Path $root "dist\Nibble-*-Setup.exe") |
    Where-Object { $_.Name -notlike "*$FakeVersion*" } |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($real) {
    $restore = Start-Process -FilePath $real.FullName `
        -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" -PassThru -Wait
    $after = (Get-ItemProperty $uninstallKey -ErrorAction SilentlyContinue).DisplayName
    Check "the real version is installed again" ($restore.ExitCode -eq 0 -and $after -notlike "*$FakeVersion*") `
        "exit=$($restore.ExitCode) now '$after'"
} else {
    "no real installer in dist\ to restore with - run tools\package.ps1"
}

"`n$failures check(s) failed"
exit $failures
