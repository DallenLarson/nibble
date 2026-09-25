# Regenerates the screenshots the README shows, from the shipping build, on a profile of
# its own - so they always match what a person gets on a first run.
#
#   powershell -File tools\Shots.ps1
#
# Popups (the menu, the find bar, downloads) live in windows of their own, so those grabs
# use a screen capture rather than a window capture; everything else is a window capture.
param(
    [string]$Exe = "$PSScriptRoot\..\dist\Nibble.exe",
    [string]$Out = "$PSScriptRoot\..\screenshots",
    [string]$Profile = "$PSScriptRoot\..\scratch\shots-profile",
    [string]$Name = "Sam",
    [string]$Engine = "DuckDuckGo"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$uia = Join-Path $PSScriptRoot "Uia.ps1"
$shot = Join-Path $PSScriptRoot "Shot.ps1"
$windowsOf = Join-Path $PSScriptRoot "WindowsOf.ps1"
$attempts = Join-Path $PSScriptRoot "..\scratch\shot-attempts"

function Click([int]$id, [string]$name) {
    (& powershell -NoProfile -ExecutionPolicy Bypass -File $uia -Mode click -ProcessId $id -Name $name) -join " "
}
function ClickLike([int]$id, [string]$pattern) {
    (& powershell -NoProfile -ExecutionPolicy Bypass -File $uia -Mode click -ProcessId $id -Name $pattern -Match like) -join " "
}
function SetText([int]$id, [string]$name, [string]$value) {
    (& powershell -NoProfile -ExecutionPolicy Bypass -File $uia -Mode set -ProcessId $id -Name $name -Value $value) -join " "
}
function Grab([int]$id, [string]$file, [string]$title, [switch]$Screen) {
    $arguments = @("-ProcessId", $id, "-Out", (Join-Path $Out $file))
    if ($title) { $arguments += @("-Title", $title) }
    if ($Screen) { $arguments += "-Screen" }
    & powershell -NoProfile -ExecutionPolicy Bypass -File $shot @arguments | Out-Null
    "  $file"
}

# How far apart the lightest and darkest pixel in the line of tip text are. The tip fades
# out and back on a seven-second timer, so a single capture has a one-in-nine chance of
# catching it half transparent - which is how a README picture ends up with a ghost line in
# it. Measured on the strip the tip lives in, so it works on light and dark themes alike.
function Measure-TipContrast([string]$path) {
    $image = [System.Drawing.Image]::FromFile($path)
    $bitmap = New-Object System.Drawing.Bitmap($image)
    $min = 255.0; $max = 0.0
    for ($y = $image.Height - 80; $y -lt $image.Height - 40; $y++) {
        for ($x = [int]($image.Width * 0.3); $x -lt [int]($image.Width * 0.7); $x += 2) {
            $pixel = $bitmap.GetPixel($x, $y)
            $light = 0.2126 * $pixel.R + 0.7152 * $pixel.G + 0.0722 * $pixel.B
            if ($light -lt $min) { $min = $light }
            if ($light -gt $max) { $max = $light }
        }
    }
    $bitmap.Dispose(); $image.Dispose()
    return $max - $min
}

function Grab-Home([int]$id, [string]$file, [string]$title, [switch]$Screen) {
    New-Item -ItemType Directory -Force -Path $attempts | Out-Null
    $best = -1.0; $bestPath = $null
    for ($try = 1; $try -le 4; $try++) {
        $path = Join-Path $attempts "$file.$try.png"
        $arguments = @("-ProcessId", $id, "-Out", $path)
        if ($title) { $arguments += @("-Title", $title) }
        if ($Screen) { $arguments += "-Screen" }
        & powershell -NoProfile -ExecutionPolicy Bypass -File $shot @arguments | Out-Null
        $score = Measure-TipContrast $path
        if ($score -gt $best) { $best = $score; $bestPath = $path }
        if ($best -ge 110) { break }        # the line is fully drawn; stop looking
        Start-Sleep -Milliseconds 1300
    }
    Copy-Item -LiteralPath $bestPath -Destination (Join-Path $Out $file) -Force
    "  {0}  (tip contrast {1:N0}, attempt {2})" -f $file, $best, (Split-Path $bestPath -Leaf)
}

# a profile of its own, so the wizard is always a first run
$profilePath = [System.IO.Path]::GetFullPath($Profile)
if (Test-Path $profilePath) {
    $previous = Join-Path (Split-Path $profilePath -Parent) "old-profiles"
    New-Item -ItemType Directory -Force -Path $previous | Out-Null
    Move-Item $profilePath (Join-Path $previous ("shots-" + (Get-Date -Format "yyyyMMdd-HHmmss"))) -Force
}
New-Item -ItemType Directory -Force -Path $Out | Out-Null

$env:NIBBLE_PROFILE = $profilePath
$app = Start-Process -FilePath $Exe -PassThru
$id = $app.Id

# wait for the wizard window to exist before touching it
$wizard = $false
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 1
    $titles = & powershell -NoProfile -ExecutionPolicy Bypass -File $windowsOf -ProcessId $id
    if ($titles -match "Set up Nibble") { $wizard = $true; break }
}
if (-not $wizard) { throw "the setup wizard never appeared for pid $id" }
Start-Sleep -Seconds 2

"wizard"
Grab $id "01-setup-color.png" "Set up Nibble"

Click $id "Next" | Out-Null
Start-Sleep -Milliseconds 1200
SetText $id "Your name" $Name | Out-Null
Start-Sleep -Milliseconds 600
Grab $id "02-setup-you.png" "Set up Nibble"

Click $id "Next" | Out-Null
Start-Sleep -Milliseconds 1200
Grab $id "03-setup-search.png" "Set up Nibble"
Click $id "Search engine $Engine" | Out-Null
Start-Sleep -Milliseconds 600

Click $id "Next" | Out-Null
Start-Sleep -Milliseconds 1200
Grab $id "04-setup-ready.png" "Set up Nibble"
Click $id "Start browsing" | Out-Null
Start-Sleep -Seconds 5

"browser"
Grab-Home $id "05-new-tab.png" $null
Click $id "Menu" | Out-Null
Start-Sleep -Milliseconds 1200
Grab $id "06-menu.png" $null -Screen

ClickLike $id "Theme shop*" | Out-Null
Start-Sleep -Seconds 3
Grab $id "07-theme-shop.png" "Theme shop"

Click $id "Apply Deep Water" | Out-Null
Start-Sleep -Seconds 4
Click $id "Done" | Out-Null
Start-Sleep -Seconds 4
Grab $id "08-theme-deep-water.png" $null

Click $id "Menu" | Out-Null
Start-Sleep -Milliseconds 1000
ClickLike $id "Theme shop*" | Out-Null
Start-Sleep -Seconds 3
Click $id "Apply Grass Block" | Out-Null
Start-Sleep -Seconds 4
Click $id "Done" | Out-Null
Start-Sleep -Seconds 4
Grab $id "09-theme-grass-block.png" $null

# back to the stock look, then a private window as the last frame
Click $id "Menu" | Out-Null
Start-Sleep -Milliseconds 1000
ClickLike $id "Theme shop*" | Out-Null
Start-Sleep -Seconds 3
Click $id "Apply Nibble" | Out-Null
Start-Sleep -Seconds 3
Click $id "Done" | Out-Null
Start-Sleep -Seconds 3

Click $id "Menu" | Out-Null
Start-Sleep -Milliseconds 1000
Click $id "Dark theme" | Out-Null
Start-Sleep -Seconds 2
Grab-Home $id "10-dark-mode.png" $null
Click $id "Menu" | Out-Null
Start-Sleep -Milliseconds 1000
Click $id "Light theme" | Out-Null
Start-Sleep -Seconds 2

Click $id "Menu" | Out-Null
Start-Sleep -Milliseconds 1000
ClickLike $id "New private window*" | Out-Null
Start-Sleep -Seconds 7
Grab-Home $id "11-private-window.png" $null

"profile: $profilePath"
"settings:"
Get-Content (Join-Path $profilePath "settings.json") -Raw

# close every window; the private one may outlive the normal one
for ($pass = 0; $pass -lt 6; $pass++) {
    $running = Get-Process -Id $id -ErrorAction SilentlyContinue
    if (-not $running) { break }
    foreach ($window in $running) { $window.CloseMainWindow() | Out-Null }
    Start-Sleep -Seconds 2
}
if (Get-Process -Id $id -ErrorAction SilentlyContinue) { "note: pid $id is still running" }
"done - $((Get-ChildItem $Out -Filter *.png).Count) images in $Out"
