# A launch smoke test: brand-new profile, first-run wizard, then every surface the shell has.
# Prints a checklist and fails loudly if any step does not land.
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Profile,
    [string]$Shots = "$PSScriptRoot\..\scratch\smoke"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$uia = Join-Path $PSScriptRoot "Uia.ps1"
$shot = Join-Path $PSScriptRoot "Shot.ps1"

$failures = 0
function Step([string]$label, [string]$result) {
    $ok = $result -match "CLICKED|SET|OK"
    if (-not $ok) { $failures++ }
    "{0}  {1}  {2}" -f $(if ($ok) { "PASS" } else { "FAIL" }), $label, $result.Trim()
}
function Click([int]$id, [string]$name) {
    (& powershell -NoProfile -ExecutionPolicy Bypass -File $uia -Mode click -ProcessId $id -Name $name) -join " "
}
function ClickLike([int]$id, [string]$pattern) {
    (& powershell -NoProfile -ExecutionPolicy Bypass -File $uia -Mode click -ProcessId $id -Name $pattern -Match like) -join " "
}
function Texts([int]$id) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $uia -Mode texts -ProcessId $id
}
function Grab([int]$id, [string]$file, [string]$title) {
    $arguments = @("-ProcessId", $id, "-Out", $file)
    if ($title) { $arguments += @("-Title", $title) }
    & powershell -NoProfile -ExecutionPolicy Bypass -File $shot @arguments | Out-Null
}

$env:NIBBLE_PROFILE = $Profile
$app = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds 10
$id = $app.Id

# ---- first run ----
$titles = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "WindowsOf.ps1") -ProcessId $id
Step "first launch shows the setup wizard" $(if ($titles -match "Set up Nibble") { "OK" } else { "missing: $titles" })
Grab $id "$Shots-1-wizard.png" "Set up Nibble"

# step 1 accent -> step 2 name + clock -> step 3 search engine -> step 4 ready
Step "wizard: pick an accent"     (Click $id "Accent colour Sky")
Step "wizard: go to the name step" (Click $id "Next")
Start-Sleep -Milliseconds 900
Grab $id "$Shots-2-wizard-clock.png" "Set up Nibble"
Step "wizard: go to the engine step" (Click $id "Next")
Start-Sleep -Milliseconds 900
Step "wizard: pick a search engine" (Click $id "Search engine DuckDuckGo")
Step "wizard: go to the last step"  (Click $id "Next")
Start-Sleep -Milliseconds 900
Step "wizard: start browsing"     (Click $id "Start browsing")
Start-Sleep -Seconds 4

$titles = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "WindowsOf.ps1") -ProcessId $id
Step "the browser window opens" $(if ($titles -match "Nibble") { "OK" } else { "missing: $titles" })
Grab $id "$Shots-3-newtab.png" $null

# ---- the version the app reports ----
# ---- chrome surfaces ----
Step "menu opens"        (Click $id "Menu")
Start-Sleep -Milliseconds 900
$menu = Texts $id
Step "menu lists the theme shop" $(if (($menu -join " ") -match "Theme shop") { "OK" } else { "missing" })
Step "menu lists the version"    $(if (($menu -join " ") -match "Nibble 1\.0") { "OK" } else { "missing" })
Grab $id "$Shots-4-menu.png" $null
Step "theme shop opens"  (ClickLike $id "Theme shop*")
Start-Sleep -Seconds 2
$shop = Texts $id
Step "theme shop lists both themes" $(if ((($shop -join " ") -match "Grass Block") -and (($shop -join " ") -match "Deep Water")) { "OK" } else { "missing" })
Grab $id "$Shots-5-shop.png" "Theme shop"
Step "apply a theme"     (Click $id "Apply Grass Block")
Start-Sleep -Seconds 3
Grab $id "$Shots-6-minecraft.png" $null
Step "theme shop closes" (Click $id "Done")
Start-Sleep -Seconds 2
Step "back to the default look" (Click $id "Menu")
Start-Sleep -Milliseconds 800
ClickLike $id "Theme shop*" | Out-Null
Start-Sleep -Seconds 2
Step "apply the default theme" (Click $id "Apply Nibble")
Start-Sleep -Seconds 2
Click $id "Done" | Out-Null
Start-Sleep -Seconds 2

Step "private window opens" (Click $id "Menu")
Start-Sleep -Milliseconds 800
Step "private window row"   (ClickLike $id "New private window*")
Start-Sleep -Seconds 6
$titles = & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "WindowsOf.ps1") -ProcessId $id
Step "a private window exists" $(if ($titles -match "private") { "OK" } else { "missing: $titles" })
Grab $id "$Shots-7-private.png" $null

Step "find bar opens" (Click $id "Menu")
Start-Sleep -Milliseconds 800
ClickLike $id "Find in page*" | Out-Null
Start-Sleep -Milliseconds 900
$found = (& powershell -NoProfile -ExecutionPolicy Bypass -File $uia -Mode dump -ProcessId $id | Select-String "Close find") -join " "
Step "find bar is on screen" $(if ($found -match "Close find") { "OK" } else { "missing" })
Click $id "Close find" | Out-Null
Start-Sleep -Milliseconds 600

Step "new tab"      (Click $id "New tab")
Start-Sleep -Seconds 2
Click $id "Menu" | Out-Null
Start-Sleep -Milliseconds 800
Click $id "Close tab" | Out-Null
Start-Sleep -Seconds 2

# ---- shutdown ----
# close every window: the private one may outlive the normal one, so close them all and
# expect the process to go with the last of them
for ($pass = 0; $pass -lt 4; $pass++) {
    $windows = @(Get-Process -Id $id -ErrorAction SilentlyContinue | ForEach-Object { $_.MainWindowHandle })
    if (-not (Get-Process -Id $id -ErrorAction SilentlyContinue)) { break }
    foreach ($w in (Get-Process -Id $id -ErrorAction SilentlyContinue)) { $w.CloseMainWindow() | Out-Null }
    Start-Sleep -Seconds 2
}
Step "closing the last window ends the process" $(if (Get-Process -Id $id -ErrorAction SilentlyContinue) { "still running" } else { "OK" })

# ---- what did it write? ----
$log = Join-Path $Profile "nibble.log"
$logLines = if (Test-Path $log) { (Get-Content $log | Where-Object { $_ -notmatch "^\s*$" } | Measure-Object).Count } else { 0 }
Step "no errors logged" $(if ($logLines -eq 0) { "OK" } else { "$logLines lines: " + ((Get-Content $log | Select-Object -Last 3) -join " | ") })

$settings = Get-Content (Join-Path $Profile "settings.json") -Raw | ConvertFrom-Json
"`nsettings after the run:"
"  onboarded        : $($settings.Onboarded)"
"  accent           : $($settings.AccentColor)"
"  engine           : $($settings.SearchEngine)"
"  theme            : $($settings.ThemeId)"
"  clock            : $($settings.Clock)"
"  window           : $([int]$settings.WindowWidth)x$([int]$settings.WindowHeight) at $([int]$settings.WindowLeft),$([int]$settings.WindowTop)"
"`n$failures step(s) failed"
exit $failures
