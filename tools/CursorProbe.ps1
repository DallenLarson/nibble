# Measures the thing people describe as "the mouse glitches when I hover a button": a pointer
# that flickers between shapes while it is sitting on, or moving across, one control.
#
#   powershell -File tools\CursorProbe.ps1 -ProcessId 123 [-Name "Menu"] [-Sweep]
#
# For every button it parks the real pointer inside the control and samples the cursor shape,
# then walks it across the control in 1 px steps. One shape while parked, and one change
# (arrow -> hand) across the walk, is healthy. A shape that alternates while the pointer is
# still means the control's hit area is moving under it.
param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [string]$Name,
    [switch]$Sweep,
    [int]$Steps = 10
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -Namespace CP -Name Win -MemberDefinition @'
[StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
[StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
[StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public MOUSEINPUT mi; }
[StructLayout(LayoutKind.Sequential)] public struct CURSORINFO { public int cbSize; public int flags; public IntPtr hCursor; public POINT ptScreenPos; }
[DllImport("user32.dll")] public static extern uint SendInput(uint count, INPUT[] inputs, int size);
[DllImport("user32.dll")] public static extern bool GetCursorInfo(ref CURSORINFO info);
[DllImport("user32.dll")] public static extern IntPtr LoadCursor(IntPtr instance, int name);
[DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
[DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
[DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
'@

# UIA reports physical pixels; a DPI-unaware process gets scaled ones back from
# GetSystemMetrics and SetCursorPos. At 125% scaling that put every target a quarter of the
# screen away from where it was asked to go, which is how an earlier probe "measured" a
# pointer that never moved. Ask for per-monitor awareness first.
[CP.Win]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

$screenW = [CP.Win]::GetSystemMetrics(0)
$screenH = [CP.Win]::GetSystemMetrics(1)

function MoveTo([int]$x, [int]$y) {
    # SetCursorPos, not SendInput: on this machine something (a low-level hook or a policy)
    # swallows injected input events - SendInput reports success and the pointer never moves.
    # SetCursorPos still drives hover, which is what has to be measured.
    [CP.Win]::SetCursorPos($x, $y) | Out-Null
}

function WhereIsPointer() {
    $point = New-Object CP.Win+POINT
    [CP.Win]::GetCursorPos([ref]$point) | Out-Null
    return "$($point.X),$($point.Y)"
}

$cursorNames = @{
    32512 = "arrow"; 32513 = "ibeam"; 32514 = "wait"; 32515 = "cross"; 32516 = "up"
    32642 = "diag-nwse"; 32643 = "diag-nesw"; 32644 = "size-we"; 32645 = "size-ns"
    32646 = "size-all"; 32648 = "no"; 32649 = "hand"; 32650 = "appstarting"; 32651 = "help"
}
$known = @{}
foreach ($id in $cursorNames.Keys) { $known[[CP.Win]::LoadCursor([IntPtr]::Zero, $id)] = $cursorNames[$id] }

function CursorShape() {
    $info = New-Object CP.Win+CURSORINFO
    $info.cbSize = [System.Runtime.InteropServices.Marshal]::SizeOf($info)
    [CP.Win]::GetCursorInfo([ref]$info) | Out-Null
    if ($known.ContainsKey($info.hCursor)) { return $known[$info.hCursor] }
    return "other:$($info.hCursor)"
}

function WindowAt([int]$x, [int]$y) {
    $point = New-Object CP.Win+POINT
    $point.X = $x; $point.Y = $y
    return [CP.Win]::WindowFromPoint($point)
}

$auto = [System.Windows.Automation.AutomationElement]
$condition = New-Object System.Windows.Automation.PropertyCondition($auto::ProcessIdProperty, $ProcessId)
# every top-level window the process owns: the shell, and any popup or shop window that is open
$windows = $auto::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)
if ($windows.Count -eq 0) { throw "no window for pid $ProcessId" }
$window = $windows[0]
$mainHandle = [IntPtr]$window.Current.NativeWindowHandle
"screen {0}x{1} (physical), pointer at {2}" -f $screenW, $screenH, (WhereIsPointer)
[CP.Win]::SetForegroundWindow($mainHandle) | Out-Null
Start-Sleep -Milliseconds 300

$buttonCondition = New-Object System.Windows.Automation.PropertyCondition(
    $auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
$buttons = @()
foreach ($w in $windows) {
    $owner = [IntPtr]$w.Current.NativeWindowHandle
    foreach ($b in $w.FindAll([System.Windows.Automation.TreeScope]::Descendants, $buttonCondition)) {
        if ($b.Current.BoundingRectangle.Width -le 4) { continue }
        if ($Name -and $b.Current.Name -ne $Name) { continue }
        $button = [pscustomobject]@{ Element = $b; Owner = $owner; OwnerName = $w.Current.Name }
        $buttons += $button
    }
}
if ($buttons.Count -eq 0) { throw "no buttons matched" }

$bad = 0
foreach ($item in $buttons) {
    $button = $item.Element
    $r = $button.Current.BoundingRectangle
    $left = [int]$r.X; $right = [int]($r.X + $r.Width) - 1
    $top = [int]$r.Y; $bottom = [int]($r.Y + $r.Height) - 1
    $midY = [int]($r.Y + $r.Height / 2)
    $spots = [ordered]@{
        centre    = @([int]($r.X + $r.Width / 2), $midY)
        # Four pixels inside each edge, not two: a control's stair-stepped outline is inset
        # and its own margin is outside its hit area, so 2 px lands on the boundary and the
        # answer legitimately alternates between the control and its parent. The inner
        # parentheses matter too - @($left + 4, $midY) reads as $left + (4, $midY).
        "left+4"  = @(($left + 4), $midY)
        "right-4" = @(($right - 4), $midY)
        "top+4"   = @([int]($r.X + $r.Width / 2), ($top + 4))
        "bottom-4" = @([int]($r.X + $r.Width / 2), ($bottom - 4))
    }

    $problems = @()
    $moved = $true
    foreach ($spot in $spots.Keys) {
        $x = $spots[$spot][0]; $y = $spots[$spot][1]
        MoveTo ($x - 30) $y
        Start-Sleep -Milliseconds 120
        MoveTo $x $y
        Start-Sleep -Milliseconds 260            # let any hover animation settle
        $here = WhereIsPointer
        if ($here -ne "$x,$y") { $problems += "$spot`: pointer at $here, wanted $x,$y"; $moved = $false }

        $shapes = @()
        $windows = @()
        for ($sample = 0; $sample -lt $Steps; $sample++) {
            $shapes += (CursorShape)
            $windows += (WindowAt $x $y)
            Start-Sleep -Milliseconds 55
        }
        $distinct = @($shapes | Select-Object -Unique)
        $foreign = @($windows | Where-Object { $_ -ne $mainHandle } | Select-Object -Unique).Count
        if ($distinct.Count -gt 1 -or $foreign -gt 0) {
            $problems += "{0}: shapes={1} otherWindow={2}" -f $spot, ($distinct -join ","), $foreign
        }
    }

    # not "$sweep": PowerShell is case-insensitive and that is the -Sweep switch
    $sweepReport = ""
    if ($Sweep) {
        $seen = @()
        MoveTo ($left - 6) $midY
        Start-Sleep -Milliseconds 150
        for ($x = $left - 4; $x -le $right + 4; $x++) {
            MoveTo $x $midY
            Start-Sleep -Milliseconds 30
            $seen += (CursorShape)
        }
        $changes = 0
        for ($i = 1; $i -lt $seen.Count; $i++) { if ($seen[$i] -ne $seen[$i - 1]) { $changes++ } }
        $sweepReport = "sweep changes=$changes ({0})" -f ((@($seen | Select-Object -Unique)) -join "->")
    }

    if (-not $moved) {
        $bad++
        "NOMOVE   {0,-24} {1}" -f $button.Current.Name, ($problems -join "  |  ")
    } elseif ($problems.Count -gt 0) {
        $bad++
        "FLICKER  {0,-24} {1}" -f $button.Current.Name, ($problems -join "  |  ")
    } else {
        "ok       {0,-24} rect={1},{2} {3}x{4}  {5}" -f $button.Current.Name,
            [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height, $sweepReport
    }
}

"`n$bad of $($buttons.Count) controls flicker while the pointer is still on them"
exit $bad
