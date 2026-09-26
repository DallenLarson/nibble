# Drives a real tab drag the way the mouse does it: the cursor is moved for real (the drag
# code reads the pointer position, not the message), and the button messages are posted to
# the window so the drag handler sees a press, a few moves and a release.
#
# Two things are proved: dragging a tab sideways reorders the strip, and letting go of a tab
# below the strip opens a second window that owns that tab.
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Profile,
    [int]$Port = 8801
)
$ErrorActionPreference = "Stop"
$here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -Namespace TD -Name Win -MemberDefinition @'
[StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
[DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
[DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder t, int max);
[DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
public delegate bool EnumProc(IntPtr h, IntPtr p);
'@
[TD.Win]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

New-Item -ItemType Directory -Force -Path $Profile | Out-Null
$Profile = (Resolve-Path $Profile).Path
$settings = [ordered]@{
    Theme = "Dark"; SearchEngine = "duckduckgo"; Blocker = $false; SuspendSeconds = 600
    RestoreSession = $true; Onboarded = $true; AccentColor = "#A3E635"; UserName = "Probe"
    Clock = "24"; ThemeId = "default"; ChromeUserAgent = $false; AllowSitePermissions = $true
    Updates = $false; WindowWidth = 1180; WindowHeight = 780; WindowLeft = 60; WindowTop = 50
}
$settings | ConvertTo-Json | Set-Content -Path (Join-Path $Profile "settings.json") -Encoding UTF8
$urls = @("http://127.0.0.1:$Port/one", "http://127.0.0.1:$Port/two", "http://127.0.0.1:$Port/three")
@{ Urls = $urls; Active = 0 } | ConvertTo-Json | Set-Content -Path (Join-Path $Profile "session.json") -Encoding UTF8

$server = Start-Job -FilePath (Join-Path $here "MediaServer.ps1") -ArgumentList $Port, 900, (Join-Path $here "..\scratch\drag\samples.jsonl"), (Join-Path $here "MediaPage.html")
Start-Sleep -Seconds 2

$env:NIBBLE_PROFILE = $Profile
$app = Start-Process -FilePath $Exe -PassThru

$auto = [System.Windows.Automation.AutomationElement]
function Get-BrowserWindows {
    $cond = New-Object System.Windows.Automation.PropertyCondition($auto::ProcessIdProperty, $app.Id)
    @($auto::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond) | Where-Object { $_.Current.Name -like "*Nibble*" })
}
function Get-AllTabs {
    # A tab is exposed as a data item named after the view-model type, so the title comes from
    # the text inside it. This collects every tab the process exposes at once; which window each
    # one belongs to is decided by where it sits, because the accessibility tree of two windows
    # in one process is not reliably separated by walking down from the window.
    $cond = New-Object System.Windows.Automation.PropertyCondition($auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)
    $textCond = New-Object System.Windows.Automation.PropertyCondition($auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $rows = @()
    foreach ($t in $auto::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        if ($t.Current.Name -ne "Nibble.ZTab") { continue }
        $r = $t.Current.BoundingRectangle
        if ($r.Width -le 0) { continue }
        $label = ""
        foreach ($inner in $t.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCond)) {
            if ($inner.Current.Name) { $label = $inner.Current.Name; break }
        }
    $rows += [pscustomobject]@{ Name = $label; X = [int]$r.X; Y = [int]$r.Y; W = [int]$r.Width; H = [int]$r.Height }
    }
    # With two windows open the same tab can be reported twice at the same place, so identical
    # entries collapse.
    $seen = @{}
    @($rows | Where-Object {
        $key = "$($_.Name)|$($_.X)|$($_.Y)"
        if ($seen.ContainsKey($key)) { return $false }
        $seen[$key] = $true
        return $true
    })
}
function Get-Tabs($win) {
    $r = $win.Current.BoundingRectangle
    $all = Get-AllTabs
    @($all | Where-Object {
        $_.X -ge ($r.X - 4) -and ($_.X + $_.W) -le ($r.X + $r.Width + 4) -and
        $_.Y -ge ($r.Y - 4) -and $_.Y -lt ($r.Y + $r.Height)
    } | Sort-Object X)
}
function To-Client([IntPtr]$hwnd, [int]$sx, [int]$sy) {
    $p = New-Object TD.Win+POINT
    $p.X = 0; $p.Y = 0
    [TD.Win]::ClientToScreen($hwnd, [ref]$p) | Out-Null
    # The parentheses matter: without them the comma binds first and PowerShell tries to
    # subtract an array.
    return @(($sx - $p.X), ($sy - $p.Y))
}
function Post-At([IntPtr]$hwnd, [uint32]$msg, [int]$wx, [int]$wy, [int]$mk) {
    $packed = [int64]((([uint32]$wy) -band 0xFFFF) -shl 16) -bor (([uint32]$wx) -band 0xFFFF)
    [TD.Win]::PostMessage($hwnd, $msg, [IntPtr]$mk, [IntPtr]$packed) | Out-Null
}
function Drag-From([IntPtr]$hwnd, [int]$fx, [int]$fy, [object[]]$steps) {
    [TD.Win]::SetCursorPos($fx, $fy) | Out-Null
    Start-Sleep -Milliseconds 150
    $c = To-Client $hwnd $fx $fy
    Post-At $hwnd 0x0201 $c[0] $c[1] 1
    Start-Sleep -Milliseconds 200
    foreach ($s in $steps) {
        [TD.Win]::SetCursorPos($s[0], $s[1]) | Out-Null
        Start-Sleep -Milliseconds 70
        $c = To-Client $hwnd $s[0] $s[1]
        Post-At $hwnd 0x0200 $c[0] $c[1] 1
        Start-Sleep -Milliseconds 70
    }
    $last = $steps[$steps.Count - 1]
    [TD.Win]::SetCursorPos($last[0], $last[1]) | Out-Null
    $c = To-Client $hwnd $last[0] $last[1]
    Post-At $hwnd 0x0202 $c[0] $c[1] 0
    Start-Sleep -Milliseconds 600
}

$failed = 0
try {
    $win = $null
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Milliseconds 500
        $win = (Get-BrowserWindows) | Select-Object -First 1
        if ($win -and (Get-Tabs $win).Count -ge 3) { break }
    }
    if (-not $win) { "FAIL  the window never appeared"; exit 1 }
    $tabs = Get-Tabs $win
    "tabs on arrival: " + (($tabs | ForEach-Object { $_.Name }) -join ", ")
    if ($tabs.Count -lt 3) { "FAIL  expected three tabs, saw $($tabs.Count)"; exit 1 }

    $hwnd = [IntPtr]$win.Current.NativeWindowHandle
    $first = $tabs[0]
    $last = $tabs[$tabs.Count - 1]
    $y = [int]($last.Y + $last.H / 2)
    # grab each tab left of centre, away from its close button
    $grabX = [int]($last.X + 24)
    $dropX = [int]($first.X + 12)
    # Named arguments: a bare array would be unrolled into separate parameters.
    $steps = @(@([int](($grabX + $dropX) / 2), $y), @($dropX, $y))
    Drag-From -hwnd $hwnd -fx $grabX -fy $y -steps $steps
    $after = Get-Tabs $win
    "after the sideways drag: " + (($after | ForEach-Object { $_.Name }) -join ", ")
    if ($after[0].Name -eq $last.Name) { "  PASS  the tab followed the pointer to the front" }
    else { "  FAIL  the strip did not reorder"; $failed++ }

    $moved = $after[0]
    $grabX = [int]($moved.X + 24)
    $dropY = [int]($moved.Y + $moved.H + 120)
    $steps = @(@($grabX, [int]($moved.Y + $moved.H + 40)), @($grabX, $dropY))
    Drag-From -hwnd $hwnd -fx $grabX -fy $y -steps $steps

    $script:titles = @()
    $cb = [TD.Win+EnumProc] {
        param($h, $p)
        $owner = 0
        [TD.Win]::GetWindowThreadProcessId($h, [ref]$owner) | Out-Null
        if ($owner -eq $app.Id -and [TD.Win]::IsWindowVisible($h)) {
            $t = New-Object System.Text.StringBuilder 256
            [TD.Win]::GetWindowText($h, $t, 256) | Out-Null
            if ($t.Length -gt 0) { $script:titles += $t.ToString() }
        }
        return $true
    }
    [TD.Win]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
    ""
    "windows after letting go below the strip:"
    $script:titles | ForEach-Object { "  $_" }
    $windows = Get-BrowserWindows
    # Read the names now: once the windows are closed their accessibility elements go quiet.
    $windowNames = @($windows | ForEach-Object { $_.Current.Name })
    ""
    "tabs per window (what accessibility sees; with two windows in one process it also reports"
    "stale copies, so the session file below is the one that decides):"
    foreach ($w in $windows) {
        "  " + $w.Current.Name + " -> " + (((Get-Tabs $w) | ForEach-Object { $_.Name }) -join ", ")
    }

    # The window's own record of itself: closing the windows merges every window's tabs into
    # the session file, so the file says whether the drag moved a tab or copied one.
    foreach ($w in $windows) { Post-At ([IntPtr]$w.Current.NativeWindowHandle) 0x0010 0 0 0 }
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Milliseconds 400
        if ($app.HasExited) { break }
    }
    $session = Get-Content -LiteralPath (Join-Path $Profile "session.json") -Raw | ConvertFrom-Json
    $saved = @($session.Urls | Where-Object { $_ -notlike "*newtab*" })
    ""
    "the saved session after closing both windows:"
    $saved | ForEach-Object { "  $_" }
    ""
    "verdict:"
    if ($windows.Count -ge 2) { "  PASS  the tab opened a second window" } else { "  FAIL  still one window"; $failed++ }
    $fresh = $windowNames | Where-Object { $_ -like "*$($moved.Name)*" }
    if ($fresh) { "  PASS  the new window is named after the dragged tab" }
    else { "  FAIL  no window owns the dragged tab"; $failed++ }
    $distinct = @($saved | Sort-Object -Unique)
    if ($distinct.Count -eq 3 -and $saved.Count -eq 3) { "  PASS  three tabs, none lost and none copied, whichever window close came first" }
    else { "  FAIL  expected exactly three distinct tabs in the session, saw $($saved.Count)"; $failed++ }
}
catch {
    "FAIL  the probe hit line $($_.InvocationInfo.ScriptLineNumber): $($_.Exception.Message)"
    $failed++
}
finally {
    Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
    Stop-Job $server -ErrorAction SilentlyContinue
    Remove-Job $server -Force -ErrorAction SilentlyContinue
}
exit $failed
