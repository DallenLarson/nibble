# Proves what opening a link in a new tab does to your place in the browser. A ctrl-click, a
# middle-click and the link menu's own "Open link in new tab" item open the tab behind you and
# leave you where you were; a plain click still takes you to the tab it opened; and the menu's
# "Open link in new window" item opens a window. None of the tab paths opens one.
#
# The clicks are real ones - a real cursor, real button messages - because that is the only kind
# Chromium acts on; posted window messages are ignored. Two things are read back: the window
# title, which carries the active tab, and the log the pages write, in which the page you were
# reading says whether it is still the one in front and each page that opened says whether it was
# shown. The engine never says which gesture opened a tab, so the page names the gesture through
# the bridge script in src/Nibble/Services/Pages.cs and the shell acts on that name.
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Profile,
    [int]$Port = 8803,
    [string]$Log = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $Log) { $Log = Join-Path $here "..\scratch\linkfocus\samples.jsonl" }

Add-Type -Namespace LF -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr param);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
[DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int max);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
[DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
[DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
[DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
[DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
public delegate bool EnumProc(IntPtr hWnd, IntPtr param);
'@
[LF.Win]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

New-Item -ItemType Directory -Force -Path $Profile | Out-Null
$Profile = (Resolve-Path $Profile).Path
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Log) | Out-Null
$Log = (Resolve-Path (Split-Path -Parent $Log)).Path + "\" + (Split-Path -Leaf $Log)
if (Test-Path $Log) { Move-Item -LiteralPath $Log -Destination "$Log.old" -Force }

$settings = [ordered]@{
    Theme = "Dark"; SearchEngine = "duckduckgo"; Blocker = $false; SuspendSeconds = 600
    RestoreSession = $false; Onboarded = $true; AccentColor = "#A3E635"; UserName = "Probe"
    Clock = "24"; ThemeId = "default"; ChromeUserAgent = $false; AllowSitePermissions = $true
    Updates = $false; WindowWidth = 1180; WindowHeight = 780; WindowLeft = 60; WindowTop = 50
}
$settings | ConvertTo-Json | Set-Content -Path (Join-Path $Profile "settings.json") -Encoding UTF8

$server = Start-Job -FilePath (Join-Path $here "MediaServer.ps1") -ArgumentList $Port, 900, $Log, (Join-Path $here "MediaPage.html")
Start-Sleep -Seconds 2

$env:NIBBLE_PROFILE = $Profile
$app = Start-Process -FilePath $Exe -ArgumentList "http://127.0.0.1:$Port/focus" -PassThru

$auto = [System.Windows.Automation.AutomationElement]
$scope = [System.Windows.Automation.TreeScope]
$pidCond = New-Object System.Windows.Automation.PropertyCondition($auto::ProcessIdProperty, $app.Id)
$linkCond = New-Object System.Windows.Automation.PropertyCondition($auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::Hyperlink)
$rowCond = New-Object System.Windows.Automation.PropertyCondition($auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)
$firstLink = "a link that asks for its own tab"
$secondLink = "a second link that asks for its own tab"

function Windows {
    $script:hits = @()
    $cb = [LF.Win+EnumProc] {
        param($h, $p)
        $owner = 0
        [LF.Win]::GetWindowThreadProcessId($h, [ref]$owner) | Out-Null
        if ($owner -eq $app.Id -and [LF.Win]::IsWindowVisible($h)) {
            $t = New-Object System.Text.StringBuilder 256
            [LF.Win]::GetWindowText($h, $t, 256) | Out-Null
            if ($t.Length -gt 0) { $script:hits += [pscustomobject]@{ Handle = $h; Title = $t.ToString() } }
        }
        return $true
    }
    [LF.Win]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
    @($script:hits)
}

function TabCount([IntPtr]$handle) {
    # The window the probe started with, found by handle because its title follows whichever tab
    # is in front; a window opened by mistake has a tab strip of its own and must not be counted.
    foreach ($win in @($auto::RootElement.FindAll($scope::Children, $pidCond))) {
        if ([IntPtr]$win.Current.NativeWindowHandle -ne $handle) { continue }
        $spots = @()
        foreach ($t in $win.FindAll($scope::Descendants, $rowCond)) {
            if ($t.Current.Name -ne "Nibble.ZTab") { continue }
            $r = $t.Current.BoundingRectangle
            if ($r.Width -le 0) { continue }
            $spots += "$([int]$r.X),$([int]$r.Y),$([int]$r.Height)"
        }
        return @($spots | Sort-Object -Unique).Count
    }
    0
}

function Link($name) {
    foreach ($win in @($auto::RootElement.FindAll($scope::Children, $pidCond))) {
        foreach ($l in $win.FindAll($scope::Descendants, $linkCond)) {
            if ($l.Current.Name -ne $name) { continue }
            $r = $l.Current.BoundingRectangle
            if ($r.Width -le 0) { continue }
            return [pscustomobject]@{ X = [int]($r.X + $r.Width / 2); Y = [int]($r.Y + $r.Height / 2) }
        }
    }
    $null
}

function Samples {
    if (-not (Test-Path $Log)) { return @() }
    try {
        return @(Get-Content -LiteralPath $Log | Where-Object { $_ -match '^\s*\{' } |
            ForEach-Object { $_ | ConvertFrom-Json } | Where-Object { $_.kind })
    }
    catch { return @() }
}

# A real click only lands on the browser if the browser is the window in front, and a process
# that is not already in front is not allowed to put itself there - so it borrows the foreground
# thread's right to, which is what the attach calls are for.
function Focus-Main {
    $main = Windows | Where-Object { $_.Title -like "link focus*" } | Select-Object -First 1
    if (-not $main) { return }
    [LF.Win]::ShowWindow($main.Handle, 9) | Out-Null
    [LF.Win]::SetWindowPos($main.Handle, [IntPtr](-1), 0, 0, 0, 0, 0x43) | Out-Null
    $front = [LF.Win]::GetForegroundWindow()
    $frontPid = 0
    $frontThread = [LF.Win]::GetWindowThreadProcessId($front, [ref]$frontPid)
    $mine = [LF.Win]::GetCurrentThreadId()
    [LF.Win]::AttachThreadInput($mine, $frontThread, $true) | Out-Null
    [LF.Win]::BringWindowToTop($main.Handle) | Out-Null
    [LF.Win]::SetForegroundWindow($main.Handle) | Out-Null
    [LF.Win]::AttachThreadInput($mine, $frontThread, $false) | Out-Null
    Start-Sleep -Milliseconds 500
}

# A click a person would make: the cursor really moves, and the button really goes down and up.
function Click-Link($name, [ValidateSet("left", "ctrl", "middle")][string]$How = "left") {
    Focus-Main
    $point = Link $name
    if (-not $point) { throw "the link '$name' is not on screen" }
    [LF.Win]::SetCursorPos($point.X, $point.Y) | Out-Null
    Start-Sleep -Milliseconds 250
    if ($How -eq "ctrl") { [LF.Win]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero) }
    Start-Sleep -Milliseconds 120
    switch ($How) {
        "middle" { [LF.Win]::mouse_event(0x0020, 0, 0, 0, [UIntPtr]::Zero) }
        default { [LF.Win]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero) }
    }
    Start-Sleep -Milliseconds 90
    switch ($How) {
        "middle" { [LF.Win]::mouse_event(0x0040, 0, 0, 0, [UIntPtr]::Zero) }
        default { [LF.Win]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero) }
    }
    Start-Sleep -Milliseconds 90
    if ($How -eq "ctrl") { [LF.Win]::keybd_event(0x11, 0, 0x0002, [UIntPtr]::Zero) }
}

# One of the link menu's rows, chosen the way the keyboard chooses them: open the menu on the
# link, walk down to the row, press Enter. The rows and their order are what the screenshot of
# the menu shows - "Open link in new tab", then the engine's "Open link in new window".
function Menu-Choose($name, [int]$row) {
    Focus-Main
    $point = Link $name
    if (-not $point) { throw "the link '$name' is not on screen" }
    [LF.Win]::SetCursorPos($point.X, $point.Y) | Out-Null
    Start-Sleep -Milliseconds 300
    [LF.Win]::mouse_event(0x0008, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 120
    [LF.Win]::mouse_event(0x0010, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 1400
    for ($i = 0; $i -lt $row; $i++) {
        [LF.Win]::keybd_event(0x28, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 150
        [LF.Win]::keybd_event(0x28, 0, 0x0002, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 250
    }
    [LF.Win]::keybd_event(0x0D, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 130
    [LF.Win]::keybd_event(0x0D, 0, 0x0002, [UIntPtr]::Zero)
}

function Seen-Hidden($rows) { @($rows | Where-Object { $_.visible -ne "visible" }).Count }
function TitlesOf($rows) { @($rows | ForEach-Object { $_.Title }) }

$failed = 0
try {
    $main = $null
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 500
        $main = Windows | Where-Object { $_.Title -like "link focus*" } | Select-Object -First 1
        if ($main) { break }
    }
    if (-not $main) {
        "FAIL  the page never loaded; windows: " + ((Windows | ForEach-Object { $_.Title }) -join " | ")
        exit 1
    }
    $mainHandle = $main.Handle
    Focus-Main
    $nowPid = 0
    [LF.Win]::GetWindowThreadProcessId([LF.Win]::GetForegroundWindow(), [ref]$nowPid) | Out-Null
    if ($nowPid -ne $app.Id) {
        "FAIL  the browser is not the window in front, so a real click would land elsewhere (front pid $nowPid)"
        exit 1
    }
    for ($i = 0; $i -lt 30; $i++) {
        if ((Link $firstLink) -and (Link $secondLink)) { break }
        Start-Sleep -Milliseconds 500
    }
    if (-not (Link $firstLink) -or -not (Link $secondLink)) {
        "FAIL  the two links are not on the page"
        exit 1
    }

    Click-Link $firstLink "ctrl"
    Start-Sleep -Seconds 5
    $ctrlWindows = TitlesOf (Windows)
    $ctrlTabs = TabCount $mainHandle
    $ctrlRows = Samples

    Click-Link $secondLink "middle"
    Start-Sleep -Seconds 5
    $midWindows = TitlesOf (Windows)
    $midTabs = TabCount $mainHandle
    $midAll = Samples
    $midRows = @(Samples | Select-Object -Skip $ctrlRows.Count)

    Menu-Choose $firstLink 1
    Start-Sleep -Seconds 5
    $menuTabWindows = TitlesOf (Windows)
    $menuTabTabs = TabCount $mainHandle
    $menuTabRows = @(Samples | Select-Object -Skip ($ctrlRows.Count + $midRows.Count))

    Menu-Choose $firstLink 2
    Start-Sleep -Seconds 6
    $menuWinWindows = TitlesOf (Windows)
    $menuWinTabs = TabCount $mainHandle
    $totalWindows = (Windows).Count
    $menuWinRows = @(Samples | Select-Object -Skip ($ctrlRows.Count + $midRows.Count + $menuTabRows.Count))

    Click-Link $secondLink
    Start-Sleep -Seconds 5
    $plainWindows = TitlesOf (Windows)
    $plainTabs = TabCount $mainHandle
    $plainRows = @(Samples | Select-Object -Skip ($ctrlRows.Count + $midRows.Count + $menuTabRows.Count + $menuWinRows.Count))

    ""
    "the ctrl-click:    " + ($ctrlWindows -join "  |  ")
    "the middle-click:  " + ($midWindows -join "  |  ")
    "menu row 1:        " + ($menuTabWindows -join "  |  ")
    "menu row 2:        " + ($menuWinWindows -join "  |  ")
    "the plain click:   " + ($plainWindows -join "  |  ")
    ""
    "what the pages said, in order:"
    (Samples) | ForEach-Object { "  $($_ | ConvertTo-Json -Compress)" }
    ""
    "verdict:"

    # 1. A ctrl-click opens a tab, shows nothing, and leaves you where you are.
    $opener = @($ctrlRows | Where-Object { $_.kind -in @("opener-loaded", "opener-visibility", "opener-focus") })
    $behind = $ctrlRows | Where-Object { $_.kind -eq "target-loaded" -and $_.page -eq "behind" } | Select-Object -Last 1
    if (-not $behind -or $behind.visible -eq "visible") { "  FAIL  the ctrl-click did not open a tab, or put it in front of you"; $failed++ }
    else { "  PASS  a ctrl-click opens its tab behind you" }
    if (-not $opener -or (Seen-Hidden $opener)) { "  FAIL  the ctrl-click took you off the page you were on"; $failed++ }
    else { "  PASS  and you were still reading it ($($opener.Count) look(s))" }
    if ($ctrlTabs -ne 2) { "  FAIL  expected 2 tabs after the ctrl-click, saw $ctrlTabs"; $failed++ }
    else { "  PASS  and it is a tab of its own (2 tabs)" }

    # 2. So does a middle-click, including after the first one: the page must never have been
    #    hidden by either of them.
    $openerMid = @($midAll | Where-Object { $_.kind -in @("opener-loaded", "opener-visibility", "opener-focus") })
    $side = $midRows | Where-Object { $_.kind -eq "target-loaded" -and $_.page -eq "front" } | Select-Object -Last 1
    if (-not $side -or $side.visible -eq "visible") { "  FAIL  the middle-click did not open a tab, or put it in front of you"; $failed++ }
    else { "  PASS  a middle-click opens its tab behind you too" }
    if ($midTabs -ne 3) { "  FAIL  expected 3 tabs after the middle-click, saw $midTabs"; $failed++ }
    else { "  PASS  and that one too (3 tabs)" }

    # 3. The menu's own tab item.
    $menuTab = $menuTabRows | Where-Object { $_.kind -eq "target-loaded" -and $_.page -eq "behind" } | Select-Object -Last 1
    if (-not $menuTab) { "  FAIL  the menu's first row opened nothing at all"; $failed++ }
    elseif ($menuTab.visible -eq "visible") { "  FAIL  the menu's first row opened a page in front of you"; $failed++ }
    else { "  PASS  the menu's 'Open link in new tab' opens it behind you" }
    if ($menuTabTabs -ne 4) { "  FAIL  expected 4 tabs after the menu's tab item, saw $menuTabTabs"; $failed++ }
    else { "  PASS  and it is a tab, not a window (4 tabs)" }
    if ($totalWindows -ne 2 -and $menuTabRows.Count) { "  note  windows so far: $totalWindows" }

    # 4. The menu's window item, which is the one thing that should open a window.
    $menuWin = $menuWinRows | Where-Object { $_.kind -eq "target-loaded" -and $_.page -eq "behind" } | Select-Object -Last 1
    $extra = @($menuWinWindows | Where-Object { $_ -like "opened behind you*" })
    if ($extra.Count) { "  PASS  the menu's 'Open link in new window' opens a window: $($extra -join ', ')" }
    else { "  FAIL  no window was opened by the menu's window item: $($menuWinWindows -join ' | ')"; $failed++ }
    if (-not $menuWin -or $menuWin.visible -ne "visible") { "  FAIL  the window it opened never showed the page"; $failed++ }
    else { "  PASS  and that window is the one showing the page" }
    if ($menuWinTabs -ne 4) { "  FAIL  the window item changed the tabs of the window you were in ($menuWinTabs)"; $failed++ }
    else { "  PASS  it left your window's tabs alone (4)" }

    # 5. And a plain click, which is the one that does move you.
    $front = $plainRows | Where-Object { $_.kind -eq "target-loaded" -and $_.page -eq "front" } | Select-Object -Last 1
    $openerPlain = @($plainRows | Where-Object { $_.kind -in @("opener-visibility", "opener-focus") })
    if (-not $front -or $front.visible -ne "visible") { "  FAIL  a plain click no longer shows its tab"; $failed++ }
    else { "  PASS  a plain click still brings its tab to the front" }
    if (-not $openerPlain -or -not (Seen-Hidden $openerPlain)) { "  FAIL  a plain click left you behind"; $failed++ }
    else { "  PASS  and the page you were on stepped back for it" }
    if ($plainWindows | Where-Object { $_ -like "opened in front of you*" }) { "  PASS  the window title followed you to the new tab" }
    else { "  FAIL  the window title did not follow: $($plainWindows -join ' | ')"; $failed++ }
    if ($plainTabs -ne 5) { "  FAIL  expected 5 tabs after the plain click, saw $plainTabs"; $failed++ }
    else { "  PASS  every click opened exactly one tab (5 in all)" }

    if (Seen-Hidden $openerMid) { "  FAIL  one of the quiet clicks did move you off the page you were on"; $failed++ }
    else { "  PASS  through all three quiet opens, you never left the page you were reading" }
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
