# Captures the shop and both themes' home pages, and proves the water actually moves by
# comparing two frames a moment apart.
param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [string]$Prefix = "$PSScriptRoot\..\scratch\theme"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$uia = Join-Path $PSScriptRoot "Uia.ps1"
$shot = Join-Path $PSScriptRoot "Shot.ps1"

function Click([string]$name) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $uia -Mode click -ProcessId $ProcessId -Name $name
}
function ClickLike([string]$pattern) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $uia -Mode click -ProcessId $ProcessId -Name $pattern -Match like
}
function Grab([string]$file, [string]$title) {
    $arguments = @("-ProcessId", $ProcessId, "-Out", $file)
    if ($title) { $arguments += @("-Title", $title) }
    & powershell -NoProfile -ExecutionPolicy Bypass -File $shot @arguments | Out-Null
}
function OpenShop {
    Click "Menu" | Out-Null
    Start-Sleep -Milliseconds 800
    ClickLike "Theme shop*" | Out-Null
    Start-Sleep -Seconds 2
}

OpenShop
Click "Apply Grass Block" | Out-Null
Start-Sleep -Seconds 3
Grab "$Prefix-shop.png" "Theme shop"
Click "Done" | Out-Null
Start-Sleep -Seconds 2
Grab "$Prefix-minecraft.png"

OpenShop
Click "Apply Deep Water" | Out-Null
Start-Sleep -Seconds 3
Click "Done" | Out-Null
Start-Sleep -Seconds 2
Grab "$Prefix-water-a.png"
Start-Sleep -Milliseconds 650
Grab "$Prefix-water-b.png"

$a = New-Object System.Drawing.Bitmap("$Prefix-water-a.png")
$b = New-Object System.Drawing.Bitmap("$Prefix-water-b.png")
$changed = 0
$sampled = 0
for ($y = 60; $y -lt $a.Height - 40; $y += 3) {
    for ($x = 0; $x -lt $a.Width; $x += 3) {
        $sampled++
        $pa = $a.GetPixel($x, $y); $pb = $b.GetPixel($x, $y)
        if ([Math]::Abs($pa.R - $pb.R) + [Math]::Abs($pa.G - $pb.G) + [Math]::Abs($pa.B - $pb.B) -gt 12) { $changed++ }
    }
}
$a.Dispose(); $b.Dispose()
"water frames: $changed of $sampled sampled pixels changed between the two captures"
