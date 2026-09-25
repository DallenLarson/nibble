# Moves a Nibble window, closes it, and checks that it comes back where it was.
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Profile,
    [int]$X = 100, [int]$Y = 100, [int]$Width = 1200, [int]$Height = 800
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -Namespace Place -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
'@

function Get-AppWindow([int]$Owned) {
    $auto = [System.Windows.Automation.AutomationElement]
    $condition = New-Object System.Windows.Automation.PropertyCondition($auto::ProcessIdProperty, $Owned)
    foreach ($w in $auto::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)) { return $w }
    return $null
}

$env:NIBBLE_PROFILE = $Profile
$settings = Join-Path $Profile "settings.json"

$app = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds 9
$window = Get-AppWindow $app.Id
if (-not $window) { throw "no window" }

[Place.Win]::MoveWindow([IntPtr]$window.Current.NativeWindowHandle, $X, $Y, $Width, $Height, $true) | Out-Null
Start-Sleep -Seconds 2
$moved = (Get-AppWindow $app.Id).Current.BoundingRectangle
"moved to          : $([int]$moved.X),$([int]$moved.Y) $([int]$moved.Width)x$([int]$moved.Height)"

$app.CloseMainWindow() | Out-Null
$app.WaitForExit(8000) | Out-Null
Start-Sleep -Seconds 2
$saved = Get-Content $settings -Raw | ConvertFrom-Json
"saved in settings : $($saved.WindowLeft),$($saved.WindowTop) $($saved.WindowWidth)x$($saved.WindowHeight) maximized=$($saved.WindowMaximized)"

$again = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds 9
$restored = (Get-AppWindow $again.Id).Current.BoundingRectangle
"restored to       : $([int]$restored.X),$([int]$restored.Y) $([int]$restored.Width)x$([int]$restored.Height)"

$dx = [Math]::Abs([int]$restored.X - $X)
$dy = [Math]::Abs([int]$restored.Y - $Y)
$dw = [Math]::Abs([int]$restored.Width - $Width)
$dh = [Math]::Abs([int]$restored.Height - $Height)
"delta             : x=$dx y=$dy w=$dw h=$dh"
if ($dx -le 4 -and $dy -le 4 -and $dw -le 4 -and $dh -le 4) { "PLACEMENT: PASS" } else { "PLACEMENT: FAIL" }

$again.CloseMainWindow() | Out-Null
$again.WaitForExit(8000) | Out-Null
