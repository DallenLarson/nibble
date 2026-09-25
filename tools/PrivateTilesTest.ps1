# Opens a private window, closes the normal one, and checks that the private window is
# left alive showing only the hand-pinned shortcut.
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Profile
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$helper = Join-Path $PSScriptRoot "Uia.ps1"

$env:NIBBLE_PROFILE = $Profile
$app = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds 9
$id = $app.Id

Start-Process -FilePath $Exe -ArgumentList "--private" | Out-Null
Start-Sleep -Seconds 9

$auto = [System.Windows.Automation.AutomationElement]
$condition = New-Object System.Windows.Automation.PropertyCondition($auto::ProcessIdProperty, $id)
$normal = $null
foreach ($w in $auto::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)) {
    if ($w.Current.Name -notlike "*private*") { $normal = $w }
}
if ($normal) {
    $normal.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
    Start-Sleep -Seconds 3
}

"normal window closed; browser still alive: " + [bool](Get-Process -Id $id -ErrorAction SilentlyContinue)

"private window contents:"
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "Titles.ps1") -ProcessId $id
& powershell -NoProfile -ExecutionPolicy Bypass -File $helper -Mode all -ProcessId $id |
    Select-String "Hyperlink|private . nothing saved" | Select-Object -First 6

"sidebar check: tile count in the private window"
$links = & powershell -NoProfile -ExecutionPolicy Bypass -File $helper -Mode all -ProcessId $id
"hyperlinks: " + (($links | Select-String "Hyperlink" | Measure-Object).Count)

Get-Process Nibble -ErrorAction SilentlyContinue | Stop-Process -Force
