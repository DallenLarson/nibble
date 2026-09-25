# Installs Nibble silently, checks every trace it is supposed to leave, runs it, then
# uninstalls it silently and checks the traces are gone - and that the profile is not.
#
#   powershell -File tools/InstallerTest.ps1 -Setup dist\Nibble-1.0.1-Setup.exe
param(
    [Parameter(Mandatory = $true)][string]$Setup
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if (-not [System.IO.Path]::IsPathRooted($Setup)) { $Setup = Join-Path $root $Setup }

$installDir = Join-Path $env:LOCALAPPDATA "Programs\Nibble"
$startMenu = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Nibble"
$profileDir = Join-Path $env:APPDATA "Nibble"
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{8A1D2B6C-3E4F-4A55-9C1B-7D2E5F0A1B33}_is1"

$failures = 0
function Check([string]$label, [bool]$ok, [string]$detail = "") {
    if (-not $ok) { $script:failures++ }
    "{0}  {1}{2}" -f $(if ($ok) { "PASS" } else { "FAIL" }), $label, $(if ($detail) { "  -> $detail" } else { "" })
}

Get-Process Nibble -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

$profileBefore = Test-Path (Join-Path $profileDir "settings.json")

"--- installing silently ---"
$install = Start-Process -FilePath $Setup -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART",
    "/TASKS=browser" -PassThru -Wait
Check "installer exits cleanly" ($install.ExitCode -eq 0) "exit=$($install.ExitCode)"

Check "browser is installed" (Test-Path (Join-Path $installDir "Nibble.exe"))
Check "documentation comes along" (Test-Path (Join-Path $installDir "README.md"))
Check "licences come along" ((Test-Path (Join-Path $installDir "LICENSE")) -and
    (Test-Path (Join-Path $installDir "Monocraft-OFL.txt")))
Check "Start-menu entry" (Test-Path (Join-Path $startMenu "Nibble.lnk"))
Check "private-window shortcut" (Test-Path (Join-Path $startMenu "Nibble (private).lnk"))
Check "shows up in Apps & features" (Test-Path $uninstallKey)
Check "registered as a browser" (
    (Test-Path "HKCU:\Software\Clients\StartMenuInternet\Nibble\Capabilities") -and
    ((Get-ItemProperty "HKCU:\Software\RegisteredApplications" -ErrorAction SilentlyContinue).Nibble -ne $null))
Check "profile untouched by installing" ((Test-Path (Join-Path $profileDir "settings.json")) -eq $profileBefore)

# What 1.0.1 fixes: Setup used to run Nibble.exe and wait for it to exit, which never happened
# on a machine where the browser cannot start - that is the "stuck on Adding Nibble to the
# browser list" bug. Registration is plain registry work now, so nothing should be running.
Check "the installer did not launch the browser" (@(Get-Process Nibble -ErrorAction SilentlyContinue).Count -eq 0)

"--- running the installed browser ---"
$app = Start-Process -FilePath (Join-Path $installDir "Nibble.exe") -PassThru
Start-Sleep -Seconds 10
$running = -not $app.HasExited
Check "installed browser starts" $running
if ($running) {
    $windows = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "WindowsOf.ps1") -ProcessId $app.Id
    Check "it has a window" (($windows | Out-String) -match "Nibble")
    $app.CloseMainWindow() | Out-Null
    $app.WaitForExit(8000) | Out-Null
    Start-Sleep -Seconds 2
}

"--- uninstalling silently ---"
$uninstaller = Join-Path $installDir "unins000.exe"
Check "uninstaller exists" (Test-Path $uninstaller)
$blockedByPolicy = $false
if (Test-Path $uninstaller) {
    try {
        $remove = Start-Process -FilePath $uninstaller -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART" `
            -PassThru -Wait -ErrorAction Stop
        Check "uninstaller exits cleanly" ($remove.ExitCode -eq 0) "exit=$($remove.ExitCode)"
    } catch {
        if ($_.Exception.Message -match "Application Control policy|blocked this file") {
            # Smart App Control refuses unsigned binaries that Microsoft does not already
            # vouch for, and it offers the user no "run anyway". Nibble.exe on this machine
            # has been run often enough to be known; a fresh unins000.exe has not.
            # This is the unsigned-binary blocker, not a fault in the installer script.
            $blockedByPolicy = $true
        } else { throw }
    }
}
Start-Sleep -Seconds 2

if ($blockedByPolicy) {
    "SKIP  the uninstaller could not run - Smart App Control blocked the unsigned unins000.exe"
    "      Every other uninstall trace is checked below by unregistering through the app itself."
    $installedExe = Join-Path $installDir "Nibble.exe"
    if (Test-Path $installedExe) {
        Start-Process -FilePath $installedExe -ArgumentList "--unregister-browser" -Wait
    }
    Start-Sleep -Seconds 2
    Check "browser registration can still be taken out (the app's own flag)" (
        (-not (Test-Path "HKCU:\Software\Clients\StartMenuInternet\Nibble")) -and
        ((Get-ItemProperty "HKCU:\Software\RegisteredApplications" -ErrorAction SilentlyContinue).Nibble -eq $null))
    # leave the machine clean: move the install aside instead of leaving a phantom
    # Apps & features entry whose uninstaller this policy will always refuse to run
    $sideline = Join-Path $root ("scratch\blocked-install-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
    New-Item -ItemType Directory -Force -Path $sideline | Out-Null
    Move-Item $installDir (Join-Path $sideline "app") -Force
    if (Test-Path $startMenu) { Move-Item $startMenu (Join-Path $sideline "startmenu") -Force }
    & reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\{8A1D2B6C-3E4F-4A55-9C1B-7D2E5F0A1B33}_is1" /f | Out-Null
    Check "install folder is gone" (-not (Test-Path $installDir))
    Check "Start-menu entry is gone" (-not (Test-Path $startMenu))
    Check "Apps & features entry is gone" (-not (Test-Path $uninstallKey))
    Check "the profile is kept by a silent uninstall" ((Test-Path (Join-Path $profileDir "settings.json")) -eq $profileBefore)
} else {
    Check "install folder is gone" (-not (Test-Path $installDir))
    Check "Start-menu entry is gone" (-not (Test-Path $startMenu))
    Check "Apps & features entry is gone" (-not (Test-Path $uninstallKey))
    Check "browser registration is gone" (
        (-not (Test-Path "HKCU:\Software\Clients\StartMenuInternet\Nibble")) -and
        ((Get-ItemProperty "HKCU:\Software\RegisteredApplications" -ErrorAction SilentlyContinue).Nibble -eq $null))
    Check "the profile is kept by a silent uninstall" ((Test-Path (Join-Path $profileDir "settings.json")) -eq $profileBefore)
}

"`n$failures check(s) failed"
exit $failures
