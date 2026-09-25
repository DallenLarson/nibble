# Opens the hamburger menu and then checks what the pointer meets over the button itself:
# the popup window may be sitting on top of the button it was opened from.
param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [int]$Step = 4,
    [string]$Name = "Menu"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -Namespace MH -Name Win -MemberDefinition @'
[StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
[StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
[StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public MOUSEINPUT mi; }
[DllImport("user32.dll")] public static extern uint SendInput(uint count, INPUT[] inputs, int size);
[DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CURSORINFO info);
[DllImport("user32.dll")] public static extern IntPtr LoadCursor(IntPtr instance, int name);
[DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr param);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
public delegate bool EnumProc(IntPtr hWnd, IntPtr param);
public struct CURSORINFO { public int cbSize; public int flags; public IntPtr hCursor; public POINT ptScreenPos; }
'@

$auto = [System.Windows.Automation.AutomationElement]
$condition = New-Object System.Windows.Automation.PropertyCondition($auto::ProcessIdProperty, $ProcessId)
$window = $null
foreach ($w in $auto::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)) { $window = $w; break }
if (-not $window) { throw "no window" }

$buttonCondition = New-Object System.Windows.Automation.PropertyCondition(
    $auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
function FindButton([string]$label) {
    foreach ($b in $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $buttonCondition)) {
        if ($b.Current.Name -eq $label) { return $b }
    }
    return $null
}

$menu = FindButton $Name
if (-not $menu) { throw "no hamburger" }
$r = $menu.Current.BoundingRectangle
"hamburger at $([int]$r.X),$([int]$r.Y) $([int]$r.Width)x$([int]$r.Height)"

$screenW = [MH.Win]::GetSystemMetrics(0)
$screenH = [MH.Win]::GetSystemMetrics(1)
function MoveTo([int]$x, [int]$y) {
    $input = New-Object MH.Win+INPUT
    $input.type = 0
    $input.mi.dx = [int](($x * 65535) / ($screenW - 1))
    $input.mi.dy = [int](($y * 65535) / ($screenH - 1))
    $input.mi.dwFlags = 0x0001 -bor 0x8000
    [MH.Win]::SendInput(1, @($input), [System.Runtime.InteropServices.Marshal]::SizeOf($input)) | Out-Null
}
function Shape([IntPtr]$h) {
    if ($h -eq [MH.Win]::LoadCursor([IntPtr]::Zero, 32512)) { return "arrow" }
    if ($h -eq [MH.Win]::LoadCursor([IntPtr]::Zero, 32649)) { return "hand" }
    return "other"
}
function VitalsAt([int]$x, [int]$y) {
    $point = New-Object MH.Win+POINT
    $point.X = $x; $point.Y = $y
    $under = [MH.Win]::WindowFromPoint($point)
    $rect = New-Object MH.Win+RECT
    [MH.Win]::GetWindowRect($under, [ref]$rect) | Out-Null
    $info = New-Object MH.Win+CURSORINFO
    $info.cbSize = [System.Runtime.InteropServices.Marshal]::SizeOf($info)
    [MH.Win]::GetCursorInfo([ref]$info) | Out-Null
    return "cursor=$(Shape $info.hCursor) under=$($rect.Left),$($rect.Top) $($rect.Right - $rect.Left)x$($rect.Bottom - $rect.Top)"
}
function VisibleWindows {
    $script:list = @()
    $cb = [MH.Win+EnumProc] {
        param($h, $p)
        $owner = 0
        [MH.Win]::GetWindowThreadProcessId($h, [ref]$owner) | Out-Null
        if ($owner -eq $ProcessId -and [MH.Win]::IsWindowVisible($h)) {
            $rect = New-Object MH.Win+RECT
            [MH.Win]::GetWindowRect($h, [ref]$rect) | Out-Null
            $script:list += "  $($rect.Left),$($rect.Top) $($rect.Right - $rect.Left)x$($rect.Bottom - $rect.Top)"
        }
        return $true
    }
    [MH.Win]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
    return $script:list
}

[MH.Win]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null
$cx = [int]($r.X + $r.Width / 2)
$cy = [int]($r.Y + $r.Height / 2)

MoveTo ($cx - 80) ($cy + 10)
Start-Sleep -Milliseconds 400
"--- menu closed ---"
"  over button: " + (VitalsAt $cx $cy)

$menu.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 1200
"--- menu open ---"
"visible windows:"; VisibleWindows
"  over button centre: " + (VitalsAt $cx $cy)

"walking across the button with the menu open:"
for ($x = [int]$r.X + 1; $x -lt $r.X + $r.Width; $x += $Step) {
    MoveTo $x $cy
    Start-Sleep -Milliseconds 160
    "  x=$x  " + (VitalsAt $x $cy)
}

# Deterministic check: every point of the button must belong to the main window, not to a
# popup that happens to be sitting over it.
$mainHandle = [IntPtr]$window.Current.NativeWindowHandle
$covered = 0
$total = 0
$examples = @()
for ($y = [int]$r.Y + 1; $y -lt $r.Y + $r.Height; $y += 2) {
    for ($x = [int]$r.X + 1; $x -lt $r.X + $r.Width; $x += 2) {
        $total++
        $point = New-Object MH.Win+POINT
        $point.X = $x; $point.Y = $y
        $under = [MH.Win]::WindowFromPoint($point)
        if ($under -ne $mainHandle) {
            $covered++
            if ($examples.Count -lt 5) {
                $rect = New-Object MH.Win+RECT
                [MH.Win]::GetWindowRect($under, [ref]$rect) | Out-Null
                $examples += "$x,$y owned by window $($rect.Left),$($rect.Top) $($rect.Right - $rect.Left)x$($rect.Bottom - $rect.Top)"
            }
        }
    }
}
"grid over the button: $total points, $covered belonging to another window" +
    $(if ($covered -gt 0) { " (e.g. " + ($examples -join " ") + ")" } else { "" })

# The real number that matters: how far the popup's top edge sits above the button's bottom
# edge. Zero or below is clean; a couple of pixels is UIA's integer rounding; tens of pixels
# is the bug this test exists for.
$popupTop = $null
$script:foundTop = $null
$script:targetPid = $ProcessId
$mainRect = New-Object MH.Win+RECT
[MH.Win]::GetWindowRect($mainHandle, [ref]$mainRect) | Out-Null
$script:mainHandle = $mainHandle
$callback = [MH.Win+EnumProc] {
    param($h, $p)
    $owner = 0
    [MH.Win]::GetWindowThreadProcessId($h, [ref]$owner) | Out-Null
    if ($owner -eq $script:targetPid -and [MH.Win]::IsWindowVisible($h) -and $h -ne $script:mainHandle) {
        $rect = New-Object MH.Win+RECT
        [MH.Win]::GetWindowRect($h, [ref]$rect) | Out-Null
        $w = $rect.Right - $rect.Left
        if ($w -gt 200) { $script:foundTop = $rect.Top }
    }
    return $true
}
[MH.Win]::EnumWindows($callback, [IntPtr]::Zero) | Out-Null
$popupTop = $script:foundTop
$buttonBottom = [int]($r.Y + $r.Height)
if ($popupTop -ne $null) {
    "popup top is {0} px {1} the button's bottom edge" -f
        [Math]::Abs($popupTop - $buttonBottom),
        $(if ($popupTop -ge $buttonBottom) { "at or below" } else { "ABOVE" })
}
