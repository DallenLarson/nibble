# For each dropdown in the chrome: open it on a fresh window and prove that no part of its
# own button belongs to the popup window. Run per window size, because popups that do not
# fit get slid back up by Windows - which is exactly how the button got covered before.
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Profile,
    [string[]]$Buttons = @("Menu", "Tracker shield", "Downloads"),
    [int]$Width = 1200,
    [int]$Height = 800
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -Namespace PO -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);
'@

$env:NIBBLE_PROFILE = $Profile
$probe = Join-Path $PSScriptRoot "MenuHoverProbe.ps1"

foreach ($button in $Buttons) {
    Get-Process Nibble -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 4

    $app = Start-Process -FilePath $Exe -PassThru
    Start-Sleep -Seconds 9

    $auto = [System.Windows.Automation.AutomationElement]
    $condition = New-Object System.Windows.Automation.PropertyCondition($auto::ProcessIdProperty, $app.Id)
    $window = $null
    foreach ($w in $auto::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)) { $window = $w; break }
    if ($window) { [PO.Win]::MoveWindow([IntPtr]$window.Current.NativeWindowHandle, 80, 60, $Width, $Height, $true) | Out-Null }
    Start-Sleep -Seconds 2

    "--- $button on a ${Width}x${Height} window ---"
    & powershell -NoProfile -ExecutionPolicy Bypass -File $probe -ProcessId $app.Id -Step 12 -Name $button 2>&1 |
        Select-String "at \d+,\d+ \d+x|^\s+\d+,\d+ \d+x|grid over"
}

Get-Process Nibble -ErrorAction SilentlyContinue | Stop-Process -Force
