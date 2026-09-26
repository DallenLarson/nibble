# Proves that finding text on a page works while the page itself has the keyboard.
#
# That is the state every real website leaves the browser in, and it used to be the one state
# where Ctrl+F did nothing: the bar opened, the typing went into the page, and the bar sat
# there empty reporting nothing. The engine's page surface is a child window of the shell and
# takes the keyboard back from its parent, so a popup can only be focused once focus has been
# moved off the surface the way Tab does.
#
# Every keystroke here is real, sent to the front window, and what is read back is the UI
# Automation focused element and the find bar's own text, so a pass means the letters were in
# the box. The page is served from tools/FindPage.html, which holds exactly six
# case-insensitive matches for "pixel" - counted from the file itself rather than by eye.
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [string]$Profile = "",
    [int]$Port = 8811,
    [string]$Shots = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$root = Split-Path $here -Parent
if (-not $Profile) { $Profile = Join-Path $root "scratch\find\profile" }
if (-not $Shots) { $Shots = Join-Path $root "scratch\find" }

Add-Type -Namespace FP -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr param);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
[DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int max);
[DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
[DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
[DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
[DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
[DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
public delegate bool EnumProc(IntPtr hWnd, IntPtr param);
'@
[FP.Win]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

# A second launch hands its URL to the window already running, and this probe would then be
# driving whatever that is. It runs a browser of its own or nothing at all.
if (@(Get-Process Nibble -ErrorAction SilentlyContinue).Count) {
    "SKIP  Nibble is already running; close it and run this again"
    exit 0
}

New-Item -ItemType Directory -Force -Path $Profile, $Shots | Out-Null
$Profile = (Resolve-Path $Profile).Path
$Shots = (Resolve-Path $Shots).Path

$settings = [ordered]@{
    Theme = "Dark"; SearchEngine = "duckduckgo"; Blocker = $false; SuspendSeconds = 600
    RestoreSession = $false; Onboarded = $true; AccentColor = "#A3E635"; UserName = "Probe"
    Clock = "24"; ThemeId = "default"; ChromeUserAgent = $false; AllowSitePermissions = $true
    Updates = $false; WindowWidth = 1280; WindowHeight = 860; WindowLeft = 40; WindowTop = 30
}
$settings | ConvertTo-Json | Set-Content -Path (Join-Path $Profile "settings.json") -Encoding UTF8

$server = Start-Job -FilePath (Join-Path $here "MediaServer.ps1") `
    -ArgumentList $Port, 900, (Join-Path $root "scratch\find\samples.jsonl"), (Join-Path $here "FindPage.html")
Start-Sleep -Seconds 2

$env:NIBBLE_PROFILE = $Profile
$app = Start-Process -FilePath $Exe -ArgumentList "http://127.0.0.1:$Port/page" -PassThru

$auto = [System.Windows.Automation.AutomationElement]
$scope = [System.Windows.Automation.TreeScope]
$failed = 0
$passed = 0

function Check([string]$what, [bool]$ok, [string]$detail = "") {
    if ($ok) { "  PASS  $what"; $script:passed++ }
    else { "  FAIL  $what  $detail"; $script:failed++ }
}

function TopLevels {
    $script:top = @()
    $cb = [FP.Win+EnumProc] {
        param($h, $p)
        $owner = 0
        [FP.Win]::GetWindowThreadProcessId($h, [ref]$owner) | Out-Null
        if ($owner -eq $app.Id -and [FP.Win]::IsWindowVisible($h)) { $script:top += $h }
        return $true
    }
    [FP.Win]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
    @($script:top)
}

function Title([IntPtr]$h) {
    $b = New-Object System.Text.StringBuilder 512
    [FP.Win]::GetWindowText($h, $b, 512) | Out-Null
    $b.ToString()
}

function Elements([System.Windows.Automation.ControlType]$kind) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($auto::ControlTypeProperty, $kind)
    $out = @()
    foreach ($h in (TopLevels)) {
        $rootEl = $auto::FromHandle($h)
        if (-not $rootEl) { continue }
        foreach ($e in $rootEl.FindAll($scope::Descendants, $cond)) { $out += $e }
    }
    @($out)
}

function Box([string]$id) {
    foreach ($e in (Elements ([System.Windows.Automation.ControlType]::Edit))) {
        if ($e.Current.AutomationId -eq $id) { return $e }
    }
    $null
}

function ValueOf($element) {
    if (-not $element) { return "" }
    try { return $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }
    catch { return "" }
}

function Labels { @( (Elements ([System.Windows.Automation.ControlType]::Text)) | ForEach-Object { $_.Current.Name } ) }

function Buttons { @( (Elements ([System.Windows.Automation.ControlType]::Button)) | ForEach-Object { $_.Current.Name } ) }

# The address bar's own list of suggestions, which says "open the address" on the row that
# offers to go to what you typed. Nothing else in the window says that.
function AddressBarList { @((Buttons) | Where-Object { $_ -like "*open the address*" }) }

function Focused { [System.Windows.Automation.AutomationElement]::FocusedElement }

function Focus-Is([string]$id) { (Focused).Current.AutomationId -eq $id }

# The page's accessible root carries the document's own title, so this says the keyboard is
# back on the page rather than merely out of the find bar.
function Page-Has-Keyboard { (Focused).Current.Name -match "Find probe page" }

function Wait-For([scriptblock]$probe, [int]$seconds = 5) {
    for ($i = 0; $i -lt ($seconds * 4); $i++) {
        if (& $probe) { return $true }
        Start-Sleep -Milliseconds 250
    }
    return $false
}

function Type-Text([string]$text) { [System.Windows.Forms.SendKeys]::SendWait($text) }

function Click([int]$x, [int]$y) {
    [FP.Win]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 200
    [FP.Win]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    [FP.Win]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 400
}

# A process that is not in front may not put itself there, so it borrows the foreground
# thread's right to before any real key or click is sent.
function Focus-Main([IntPtr]$handle) {
    [FP.Win]::ShowWindow($handle, 9) | Out-Null
    [FP.Win]::SetWindowPos($handle, [IntPtr](-1), 0, 0, 0, 0, 0x43) | Out-Null
    $front = [FP.Win]::GetForegroundWindow()
    $frontPid = 0
    $frontThread = [FP.Win]::GetWindowThreadProcessId($front, [ref]$frontPid)
    $mine = [FP.Win]::GetCurrentThreadId()
    [FP.Win]::AttachThreadInput($mine, $frontThread, $true) | Out-Null
    [FP.Win]::BringWindowToTop($handle) | Out-Null
    [FP.Win]::SetForegroundWindow($handle) | Out-Null
    [FP.Win]::AttachThreadInput($mine, $frontThread, $false) | Out-Null
    Start-Sleep -Milliseconds 500
}

function Grab([string]$file) {
    & (Join-Path $here "Shot.ps1") -ProcessId $app.Id -Out (Join-Path $Shots $file) -Screen | Out-Null
}

# What the screen really shows, as opposed to what the window tree claims: a magenta marker
# sits in the page exactly where the find bar lands, so counting its pixels says whether the
# bar is painted over the page or merely open and holding the keyboard.
function Magenta-OnScreen([int]$handle, $rect) {
    $bmp = New-Object System.Drawing.Bitmap -ArgumentList ([int]$rect.Width), ([int]$rect.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bmp)
    $graphics.CopyFromScreen([int]$rect.X, [int]$rect.Y, 0, 0,
        (New-Object System.Drawing.Size([int]$rect.Width, [int]$rect.Height)))
    $graphics.Dispose()

    $count = 0
    for ($x = 0; $x -lt $bmp.Width; $x += 4) {
        for ($y = 0; $y -lt $bmp.Height; $y += 4) {
            $c = $bmp.GetPixel($x, $y)
            if ($c.R -gt 200 -and $c.B -gt 200 -and $c.G -lt 90) { $count++ }
        }
    }
    $bmp.Dispose()
    return $count
}

"find in page, with a page holding the keyboard"
""

try {
    # ---- the page ----
    $window = $null
    for ($i = 0; $i -lt 60; $i++) {
        foreach ($h in (TopLevels)) {
            if ((Title $h) -like "*Find probe page*") { $window = $h; break }
        }
        if ($window) { break }
        Start-Sleep -Milliseconds 500
    }
    Check "the served page loads in a window" ($null -ne $window) `
        "(windows: $((TopLevels) | ForEach-Object { Title $_ }) -join ' | ')"
    if (-not $window) { throw "no page to work with" }

    Focus-Main $window
    Start-Sleep -Seconds 2
    $r = New-Object FP.Win+RECT
    [FP.Win]::GetWindowRect($window, [ref]$r) | Out-Null
    $rect = [pscustomobject]@{
        X = $r.Left; Y = $r.Top
        Width = $r.Right - $r.Left; Height = $r.Bottom - $r.Top
    }

    # Clicking the page is how a person gives it the keyboard; this is the state under test.
    Click ([int]($rect.X + $rect.Width / 2)) ([int]($rect.Y + $rect.Height * 0.35))
    Check "clicking the page gives it the keyboard" (Page-Has-Keyboard) `
        "(the keyboard is on '$((Focused).Current.Name)')"

    $markerBefore = Magenta-OnScreen $window $rect
    Check "the page's marker is on screen before the bar opens" ($markerBefore -gt 100) `
        "($markerBefore magenta samples)"

    # ---- Ctrl+F ----
    Type-Text "^f"
    Check "Ctrl+F opens the find bar" (Wait-For { $null -ne (Box "FindBox") }) ""
    Check "the caret goes into the find bar, not the page" (Wait-For { Focus-Is "FindBox" } 3) `
        "(the keyboard is on '$((Focused).Current.Name)')"

    Type-Text "pixel"
    Check "the letters land in the find box" (Wait-For { (ValueOf (Box "FindBox")) -eq "pixel" }) `
        "(the box holds '$(ValueOf (Box 'FindBox'))')"
    Start-Sleep -Milliseconds 600
    # Sampled over the find box itself, taken from the element's own bounds, so the answer does
    # not depend on guessing where the bar lands.
    $markerAfter = Magenta-OnScreen $window ((Box "FindBox").Current.BoundingRectangle)
    Check "the find bar is painted over the page, not merely open" ($markerAfter -lt 20) `
        "($markerAfter magenta samples still showing where the bar should be)"
    Check "and the address bar's list is not left hanging over the page" `
        (-not (Wait-For { (AddressBarList).Count -gt 0 } 2)) "(it is showing: $((AddressBarList) -join ', '))"
    Check "the bar counts all six matches on the page" (Wait-For { (Labels) -contains "1/6" }) `
        "(the bar says: $(((Labels) | Where-Object { $_ -match 'found|/' }) -join ', '))"
    Grab "1-first-match.png"

    Type-Text "{ENTER}"
    Check "Enter walks to the next match" (Wait-For { (Labels) -contains "2/6" }) `
        "(the bar says: $(((Labels) | Where-Object { $_ -match 'found|/' }) -join ', '))"

    Type-Text "+{ENTER}"
    Check "Shift+Enter walks back" (Wait-For { (Labels) -contains "1/6" }) `
        "(the bar says: $(((Labels) | Where-Object { $_ -match 'found|/' }) -join ', '))"

    Type-Text "^a"
    Type-Text "zebra"
    Check "a word that is not on the page says so" (Wait-For { (Labels) -contains "0 found" }) `
        "(the bar says: $(((Labels) | Where-Object { $_ -match 'found|/' }) -join ', '))"

    Type-Text "^a"
    Type-Text "pixel"
    Wait-For { (Labels) -contains "1/6" } | Out-Null

    # ---- and out again ----
    Type-Text "{ESC}"
    Check "Escape closes the find bar" (Wait-For { $null -eq (Box "FindBox") }) ""
    Check "and the page has the keyboard back" (Wait-For { Page-Has-Keyboard }) `
        "(the keyboard is on '$((Focused).Current.Name)')"

    # ---- the command bar had the same hole, for the same reason ----
    Type-Text "^k"
    Check "Ctrl+K takes the keyboard into the command bar" (Wait-For { Focus-Is "PaletteQuery" } 3) `
        "(the keyboard is on '$((Focused).Current.Name)')"
    Type-Text "mute"
    Check "the letters land in the command bar" (Wait-For { (ValueOf (Box "PaletteQuery")) -eq "mute" }) `
        "(the query holds '$(ValueOf (Box 'PaletteQuery'))')"
    Check "and the command bar leaves no address-bar list behind either" `
        (-not (Wait-For { (AddressBarList).Count -gt 0 } 2)) "(it is showing: $((AddressBarList) -join ', '))"
    Grab "2-command-bar.png"
    Type-Text "{ESC}"

    # ---- the route that always worked, kept so it keeps working ----
    Type-Text "^l"
    Start-Sleep -Milliseconds 700
    Type-Text "^f"
    Check "find still opens from the address bar" (Wait-For { Focus-Is "FindBox" } 3) `
        "(the keyboard is on '$((Focused).Current.Name)')"
    Type-Text "pixel"
    Check "and it still counts there" (Wait-For { (Labels) -contains "1/6" }) `
        "(the bar says: $(((Labels) | Where-Object { $_ -match 'found|/' }) -join ', '))"
    Grab "3-from-the-address-bar.png"
    Type-Text "{ESC}"
}
finally {
    Stop-Process -Id $app.Id -Force -ErrorAction SilentlyContinue
    Stop-Job $server -ErrorAction SilentlyContinue
    Remove-Job $server -Force -ErrorAction SilentlyContinue
    Remove-Item Env:NIBBLE_PROFILE -ErrorAction SilentlyContinue
}

""
"{0} passed, {1} failed" -f $passed, $failed
exit $failed
