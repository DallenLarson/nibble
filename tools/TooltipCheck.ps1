# Hovers a button with real input until its tooltip appears, then checks that the tooltip
# sits *below* the button (not under the pointer) and that the cursor never leaves the
# button while the pointer moves around inside it.
param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [string]$Name = "Menu"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -Namespace TC -Name Win -MemberDefinition @'
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
$target = $null
foreach ($b in $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $buttonCondition)) {
    if ($b.Current.Name -eq $Name) { $target = $b; break }
}
if (-not $target) { throw "no button named '$Name'" }
$r = $target.Current.BoundingRectangle

$screenW = [TC.Win]::GetSystemMetrics(0)
$screenH = [TC.Win]::GetSystemMetrics(1)
function MoveTo([int]$x, [int]$y) {
    $input = New-Object TC.Win+INPUT
    $input.type = 0
    $input.mi.dx = [int](($x * 65535) / ($screenW - 1))
    $input.mi.dy = [int](($y * 65535) / ($screenH - 1))
    $input.mi.dwFlags = 0x0001 -bor 0x8000
    [TC.Win]::SendInput(1, @($input), [System.Runtime.InteropServices.Marshal]::SizeOf($input)) | Out-Null
}
function Shape([IntPtr]$h) {
    if ($h -eq [TC.Win]::LoadCursor([IntPtr]::Zero, 32512)) { return "arrow" }
    if ($h -eq [TC.Win]::LoadCursor([IntPtr]::Zero, 32649)) { return "hand" }
    return "other"
}
function TooltipWindow {
    $script:tip = $null
    $script:targetPid = $ProcessId
    $callback = [TC.Win+EnumProc] {
        param($h, $p)
        $owner = 0
        [TC.Win]::GetWindowThreadProcessId($h, [ref]$owner) | Out-Null
        if ($owner -eq $script:targetPid -and [TC.Win]::IsWindowVisible($h)) {
            $rect = New-Object TC.Win+RECT
            [TC.Win]::GetWindowRect($h, [ref]$rect) | Out-Null
            $w = $rect.Right - $rect.Left
            $ht = $rect.Bottom - $rect.Top
            # the tooltip is the small window; the shell and its popups are much bigger
            if ($w -gt 20 -and $w -lt 400 -and $ht -gt 10 -and $ht -lt 160) {
                $script:tip = "$($rect.Left),$($rect.Top) ${w}x${ht}"
            }
        }
        return $true
    }
    [TC.Win]::EnumWindows($callback, [IntPtr]::Zero) | Out-Null
    return $script:tip
}

[TC.Win]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null
Start-Sleep -Milliseconds 500
$cx = [int]($r.X + $r.Width / 2)
$cy = [int]($r.Y + $r.Height / 2)
MoveTo ($cx - 120) ($cy + 30)
Start-Sleep -Milliseconds 300
MoveTo $cx $cy

"hovering '$Name' at $cx,$cy (button $([int]$r.X),$([int]$r.Y) $([int]$r.Width)x$([int]$r.Height))"
$tip = $null
for ($i = 0; $i -lt 12 -and -not $tip; $i++) {
    Start-Sleep -Milliseconds 250
    $tip = TooltipWindow
}
"tooltip window : $(if ($tip) { $tip } else { '(none appeared)' })"

$shapes = @()
$outside = 0
for ($i = 0; $i -lt 12; $i++) {
    $x = [int]($r.X + 2 + ($r.Width - 4) * $i / 11)
    MoveTo $x $cy
    Start-Sleep -Milliseconds 150
    $info = New-Object TC.Win+CURSORINFO
    $info.cbSize = [System.Runtime.InteropServices.Marshal]::SizeOf($info)
    [TC.Win]::GetCursorInfo([ref]$info) | Out-Null
    $shapes += (Shape $info.hCursor)
    $point = New-Object TC.Win+POINT
    $point.X = $x; $point.Y = $cy
    if ([TC.Win]::WindowFromPoint($point) -ne [IntPtr]$window.Current.NativeWindowHandle) { $outside++ }
}
"across the button: cursors " + (($shapes | Group-Object | ForEach-Object { "$($_.Name)x$($_.Count)" }) -join ", ") +
    "; points not over the browser window: $outside"
