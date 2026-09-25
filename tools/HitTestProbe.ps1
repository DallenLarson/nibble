# Asks a window what it would do at points over its own buttons, via WM_NCHITTEST.
# HTCLIENT(1) = normal; HTCAPTION(2) = drag the window; 12..17 = resize borders.
param([Parameter(Mandatory = $true)][int]$ProcessId)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -Namespace Hit -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
[DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
public struct POINT { public int X; public int Y; }
'@

$auto = [System.Windows.Automation.AutomationElement]
$condition = New-Object System.Windows.Automation.PropertyCondition($auto::ProcessIdProperty, $ProcessId)
$window = $null
foreach ($w in $auto::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)) { $window = $w; break }
if (-not $window) { throw "no window" }

$buttonCondition = New-Object System.Windows.Automation.PropertyCondition(
    $auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)

function Hit([int]$x, [int]$y) {
    $point = New-Object Hit.Win+POINT
    $point.X = $x; $point.Y = $y
    $target = [Hit.Win]::WindowFromPoint($point)
    if ($target -eq [IntPtr]::Zero) { return "no-window" }
    $packed = [IntPtr](($y -band 0xFFFF) -shl 16 -bor ($x -band 0xFFFF))
    return [int][Hit.Win]::SendMessage($target, 0x0084, [IntPtr]::Zero, $packed)
}

foreach ($b in $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $buttonCondition)) {
    $r = $b.Current.BoundingRectangle
    if ($r.Width -le 0) { continue }
    $cx = [int]($r.X + $r.Width / 2)
    $cy = [int]($r.Y + $r.Height / 2)
    $top = [int]($r.Y + 2)
    $bottom = [int]($r.Y + $r.Height - 2)
    $left = [int]($r.X + 2)
    $right = [int]($r.X + $r.Width - 2)
    $values = @(
        "mid=$(Hit $cx $cy)",
        "top=$(Hit $cx $top)",
        "bottom=$(Hit $cx $bottom)",
        "left=$(Hit $left $cy)",
        "right=$(Hit $right $cy)"
    ) -join " "
    "{0,-22} {1}" -f $b.Current.Name, $values
}
