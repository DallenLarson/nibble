# Opens the theme shop, applies each bundled theme, and captures what it looks like.
param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [string]$Prefix = "$PSScriptRoot\..\scratch\theme"
)

$ErrorActionPreference = "Stop"
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
Grab "$Prefix-shop.png" "Theme shop"
"shop open, applied: " + ((& powershell -NoProfile -ExecutionPolicy Bypass -File $uia -Mode texts -ProcessId $ProcessId |
    Where-Object { $_ -match "^TEXT\s+(Applied|Apply)$" }) -join " ")

Click "Apply Grass Block" | Out-Null
Start-Sleep -Seconds 3
Grab "$Prefix-shop-minecraft.png" "Theme shop"
Grab "$Prefix-chrome-minecraft.png" $null

Click "Done" | Out-Null
Start-Sleep -Seconds 3
Grab "$Prefix-home-minecraft.png" $null

OpenShop
Click "Apply Deep Water" | Out-Null
Start-Sleep -Seconds 3
Click "Done" | Out-Null
Start-Sleep -Seconds 2
Grab "$Prefix-home-water.png" $null
Start-Sleep -Milliseconds 700
Grab "$Prefix-home-water2.png" $null
