# Measures where the caret goes when a tab is opened. A new tab page hands the keyboard to the
# address bar the moment it has painted, so Ctrl+T and then typing is one motion; a restored
# page keeps the keyboard, and a page you click into never has it taken back.
#
# Every keystroke here is a real one, and the window is put in front with the foreground
# thread's own right to, because keyboard focus is only real if the key actually lands
# somewhere. What is read back is the UI Automation focused element and the address bar's own
# value, so a pass means the text is in the address bar, not that a flag was set.
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [string]$Profile = "",
    [int]$Port = 8824,
    [string]$Shots = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

Add-Type -Namespace NF -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr param);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
[DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int max);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
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
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
public delegate bool EnumProc(IntPtr hWnd, IntPtr param);
'@
[NF.Win]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

$here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $Profile) { $Profile = Join-Path $here "..\scratch\newtabfocus\profile" }
New-Item -ItemType Directory -Force -Path $Profile | Out-Null
$Profile = (Resolve-Path $Profile).Path

$auto = [System.Windows.Automation.AutomationElement]
$scope = [System.Windows.Automation.TreeScope]
$editCond = New-Object System.Windows.Automation.PropertyCondition(
    $auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
$buttonCond = New-Object System.Windows.Automation.PropertyCondition(
    $auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)

# A second launch hands its URL to the instance already running, and this probe would then be
# typing into whatever window that is. It drives a browser of its own or nothing at all.
$running = @(Get-Process Nibble -ErrorAction SilentlyContinue)
if ($running.Count) {
    "SKIP  Nibble is already running (pid $(($running.Id) -join ', ')); close it and run this again"
    exit 0
}

$app = $null
$pidCond = $null
$failed = 0
$passed = 0

function Check([string]$what, [bool]$ok, [string]$detail = "") {
    if ($ok) { "  PASS  $what"; $script:passed++ }
    else { "  FAIL  $what $detail"; $script:failed++ }
}

# -Shots <folder> keeps a picture of each state as the probe goes, for looking at afterwards.
function Grab([string]$file) {
    if (-not $Shots) { return }
    & (Join-Path $here "Shot.ps1") -ProcessId $app.Id -Out (Join-Path $Shots $file) -Screen | Out-Null
}

function Save-Settings([bool]$restoreSession) {
    $settings = [ordered]@{
        Theme = "Dark"; SearchEngine = "duckduckgo"; Blocker = $false; SuspendSeconds = 600
        RestoreSession = $restoreSession; Onboarded = $true; AccentColor = "#A3E635"; UserName = "Probe"
        Clock = "24"; ThemeId = "default"; ChromeUserAgent = $false; AllowSitePermissions = $true
        Updates = $false; WindowWidth = 1180; WindowHeight = 780; WindowLeft = 60; WindowTop = 50
    }
    $settings | ConvertTo-Json | Set-Content -Path (Join-Path $Profile "settings.json") -Encoding UTF8
}

function Save-History {
    # Two marker entries, named so that nothing on the page could be mistaken for them, and
    # two so the file is an array rather than a bare object.
    @(
        [pscustomobject]@{
            Url = "https://www.deckrise.net/"; Title = "marker: a page I visited earlier"
            Visited = (Get-Date).ToString("o")
        }
        [pscustomobject]@{
            Url = "https://news.ycombinator.com/"; Title = "marker: another page"
            Visited = (Get-Date).AddMinutes(-5).ToString("o")
        }
    ) | ConvertTo-Json | Set-Content -Path (Join-Path $Profile "history.json") -Encoding UTF8
}

function Start-Nibble([string]$Url = "") {
    $env:NIBBLE_PROFILE = $Profile
    $script:app = if ($Url) {
        Start-Process -FilePath $Exe -ArgumentList $Url -PassThru
    } else {
        Start-Process -FilePath $Exe -PassThru
    }
    $script:pidCond = New-Object System.Windows.Automation.PropertyCondition(
        $auto::ProcessIdProperty, $script:app.Id)
}

function Stop-Nibble {
    if (-not $script:app) { return }
    Stop-Process -Id $script:app.Id -Force -ErrorAction SilentlyContinue
    for ($i = 0; $i -lt 40; $i++) {
        # The next launch hands its URL to a running window instead of starting one, so the
        # process has to be gone - not merely asked to go - before the next run begins.
        if (-not (Get-Process -Id $script:app.Id -ErrorAction SilentlyContinue)) { break }
        Start-Sleep -Milliseconds 250
    }
    Start-Sleep -Milliseconds 600
    $script:app = $null
}

function Windows {
    $script:hits = @()
    $cb = [NF.Win+EnumProc] {
        param($h, $p)
        $owner = 0
        [NF.Win]::GetWindowThreadProcessId($h, [ref]$owner) | Out-Null
        if ($owner -eq $app.Id -and [NF.Win]::IsWindowVisible($h)) {
            $t = New-Object System.Text.StringBuilder 256
            [NF.Win]::GetWindowText($h, $t, 256) | Out-Null
            if ($t.Length -gt 0) { $script:hits += [pscustomobject]@{ Handle = $h; Title = $t.ToString() } }
        }
        return $true
    }
    [NF.Win]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
    @($script:hits)
}

function Wait-Main([string]$TitleLike = "*") {
    for ($i = 0; $i -lt 40; $i++) {
        $w = Windows | Where-Object { $_.Title -like $TitleLike } | Select-Object -First 1
        if ($w) { return $w }
        Start-Sleep -Milliseconds 500
    }
    $null
}

# A process that is not in front may not put itself there, so it borrows the foreground
# thread's right to before any real key or click is sent.
function Focus-Main([IntPtr]$handle) {
    [NF.Win]::ShowWindow($handle, 9) | Out-Null
    [NF.Win]::SetWindowPos($handle, [IntPtr](-1), 0, 0, 0, 0, 0x43) | Out-Null
    $front = [NF.Win]::GetForegroundWindow()
    $frontPid = 0
    $frontThread = [NF.Win]::GetWindowThreadProcessId($front, [ref]$frontPid)
    $mine = [NF.Win]::GetCurrentThreadId()
    [NF.Win]::AttachThreadInput($mine, $frontThread, $true) | Out-Null
    [NF.Win]::BringWindowToTop($handle) | Out-Null
    [NF.Win]::SetForegroundWindow($handle) | Out-Null
    [NF.Win]::AttachThreadInput($mine, $frontThread, $false) | Out-Null
    Start-Sleep -Milliseconds 400
}

function Focused {
    $f = [System.Windows.Automation.AutomationElement]::FocusedElement
    [pscustomobject]@{
        Id = $f.Current.AutomationId
        Name = $f.Current.Name
        Kind = $f.Current.ControlType.ProgrammaticName.Replace("ControlType.", "")
        Handle = [IntPtr]$f.Current.NativeWindowHandle
    }
}

function CaretInAddressBar { (Focused).Id -eq "Omni" }

# The rows of the address bar's suggestion list are the only things in the window whose
# accessible name reads "title, subtitle". Nothing on the page has that shape, so matching on
# it can only find that list.
function RecentList {
    @((AllButtons) | Where-Object { $_ -like "*, *" })
}

function TopLevels {
    $script:topLevel = @()
    $cb = [NF.Win+EnumProc] {
        param($h, $p)
        $owner = 0
        [NF.Win]::GetWindowThreadProcessId($h, [ref]$owner) | Out-Null
        if ($owner -eq $app.Id -and [NF.Win]::IsWindowVisible($h)) { $script:topLevel += $h }
        return $true
    }
    [NF.Win]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
    @($script:topLevel)
}

# Every button the process shows, in any window of its own - popups included, because the
# suggestion list is a window of its own rather than part of the browser window.
function AllButtons {
    $names = @()
    foreach ($h in (TopLevels)) {
        $root = $auto::FromHandle($h)
        if (-not $root) { continue }
        foreach ($b in $root.FindAll($scope::Descendants, $buttonCond)) {
            if ($b.Current.Name) { $names += $b.Current.Name }
        }
    }
    @($names)
}

function Click-Button([string]$name) {
    foreach ($h in (TopLevels)) {
        $root = $auto::FromHandle($h)
        if (-not $root) { continue }
        foreach ($b in $root.FindAll($scope::Descendants, $buttonCond)) {
            if ($b.Current.Name -ne $name) { continue }
            $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            return
        }
    }
    throw "no button named '$name' to click"
}

function MarkerShown {
    @((RecentList) | Where-Object { $_ -like "*marker:*" }).Count -gt 0
}

function Omni {
    foreach ($win in $auto::RootElement.FindAll($scope::Children, $pidCond)) {
        foreach ($e in $win.FindAll($scope::Descendants, $editCond)) {
            if ($e.Current.AutomationId -eq "Omni") { return $e }
        }
    }
    $null
}

function AddressBarText {
    $o = Omni
    if (-not $o) { return "<no address bar>" }
    try { $o.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }
    catch { "<unreadable>" }
}

function Clear-AddressBar {
    $o = Omni
    if ($o) { $o.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue("") }
}

function Type-Text([string]$text) { [System.Windows.Forms.SendKeys]::SendWait($text) }

function Press-Ctrl([byte]$vk) {
    [NF.Win]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 140
    [NF.Win]::keybd_event($vk, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 90
    [NF.Win]::keybd_event($vk, 0, 0x0002, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [NF.Win]::keybd_event(0x11, 0, 0x0002, [UIntPtr]::Zero)
}

# A real click low and to the left of the window, where the built-in page has nothing but
# scenery, so the only thing it can do is ask for the keyboard.
function Click-Page([IntPtr]$handle) {
    $rect = New-Object "NF.Win+RECT"
    [NF.Win]::GetWindowRect($handle, [ref]$rect) | Out-Null
    $x = [int]($rect.Left + ($rect.Right - $rect.Left) * 0.25)
    $y = [int]($rect.Top + ($rect.Bottom - $rect.Top) * 0.88)
    [NF.Win]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 250
    [NF.Win]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 90
    [NF.Win]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 700
}

$server = Start-Job -FilePath (Join-Path $here "MediaServer.ps1") -ArgumentList $Port, 900, (Join-Path $here "..\scratch\newtabfocus\samples.jsonl"), (Join-Path $here "MediaPage.html")
Start-Sleep -Seconds 2

try {
    "run 1 - a window that opens on the new tab page"
    Save-Settings $false
    Save-History
    Remove-Item -LiteralPath (Join-Path $Profile "session.json") -ErrorAction SilentlyContinue
    Start-Nibble
    $main = Wait-Main
    if (-not $main) { throw "no Nibble window appeared; windows: " + ((Windows | ForEach-Object { $_.Title }) -join " | ") }
    Focus-Main $main.Handle
    Start-Sleep -Seconds 2

    $f = Focused
    Check "the address bar has the keyboard when the window opens" (CaretInAddressBar) `
        "(the caret is in $($f.Kind) '$($f.Name)' of window $($f.Handle), main window $($main.Handle))"
    Check "and the recent-sites list is not covering the page it opened on" (-not (MarkerShown)) `
        "(the list is showing: $((RecentList) -join ' | '))"

    # The other half of that question, and the same keystroke that a person would make: typing
    # asks for the list, so the check above really did look for one.
    Type-Text "deck"
    Start-Sleep -Milliseconds 800
    Check "typing asks for the list of recent sites" (MarkerShown) `
        "(the list is showing: $((RecentList) -join ' | '); buttons on screen: $((AllButtons) -join ' | '))"
    Check "typing with no click first arrives in the address bar" ((AddressBarText) -eq "deck") `
        "(the address bar holds '$((AddressBarText))')"
    Grab "1-window-opens.png"

    Press-Ctrl 0x54
    Start-Sleep -Milliseconds 1800
    $f = Focused
    Check "Ctrl+T puts the caret in the new tab's address bar" (CaretInAddressBar) `
        "(the caret is in $($f.Kind) '$($f.Name)')"
    Check "and the new tab's address bar is empty, waiting to be typed into" ((AddressBarText) -eq "") `
        "(the address bar holds '$((AddressBarText))')"
    Grab "2-new-tab.png"

    Type-Text "deckrise"
    Start-Sleep -Milliseconds 800
    Check "typing straight after Ctrl+T arrives there too" ((AddressBarText) -eq "deckrise") `
        "(the address bar holds '$((AddressBarText))')"
    Grab "2-new-tab-typed.png"

    Click-Page $main.Handle
    $f = Focused
    Check "clicking the page takes the keyboard, and it is not taken back" (-not (CaretInAddressBar)) `
        "(the caret went back to $($f.Kind) '$($f.Name)')"
    Type-Text "zz"
    Start-Sleep -Milliseconds 700
    Check "and typing then stays with the page" ((AddressBarText) -ne "zz") `
        "(the address bar holds '$((AddressBarText))')"

    Press-Ctrl 0x4E
    Start-Sleep -Seconds 3
    $windows = Windows
    $f = Focused
    Check "Ctrl+N opens a second window" ($windows.Count -eq 2) `
        "(windows: $($windows.Title -join ' | '))"
    Check "and that window's address bar has the keyboard" ((CaretInAddressBar) -and ($f.Handle -ne $main.Handle)) `
        "(the caret is in window $($f.Handle), the first window is $($main.Handle))"

    Stop-Nibble

    "run 2 - a window that restores a page it was reading"
    Save-Settings $true
    @{ Urls = @("http://127.0.0.1:$Port/page"); Active = 0 } | ConvertTo-Json -Compress |
        Set-Content -Path (Join-Path $Profile "session.json") -Encoding UTF8
    Start-Nibble
    $restored = Wait-Main
    if (-not $restored) { throw "the restored page never appeared; windows: " + ((Windows | ForEach-Object { $_.Title }) -join " | ") }
    # The window's title follows the page that was restored, and the probe page keeps rewriting
    # its own title, so the page is up once the title is anything but the home page's.
    for ($i = 0; $i -lt 40; $i++) {
        if (Windows | Where-Object { $_.Title -ne "Nibble" }) { break }
        Start-Sleep -Milliseconds 500
    }
    Focus-Main $restored.Handle
    Start-Sleep -Seconds 2

    $f = Focused
    Check "a restored page starts with the keyboard where you left it, not in the address bar" `
        (-not (CaretInAddressBar)) "(the caret is in $($f.Kind) '$($f.Name)'; windows: $((Windows).Title -join ' | '))"
    Type-Text "qq"
    Start-Sleep -Milliseconds 700
    Check "and typing does not land in the address bar" ((AddressBarText) -ne "qq") `
        "(the address bar holds '$((AddressBarText))')"

    Press-Ctrl 0x54
    Start-Sleep -Milliseconds 1800
    $f = Focused
    Check "Ctrl+T from that page still hands the caret to the new tab's address bar" (CaretInAddressBar) `
        "(the caret is in $($f.Kind) '$($f.Name)')"
    Type-Text "nibble"
    Start-Sleep -Milliseconds 800
    Check "and typing right then arrives there" ((AddressBarText) -eq "nibble") `
        "(the address bar holds '$((AddressBarText))')"

    # The home button lands on the same page, so it hands the caret over the same way.
    Click-Button "New tab page"
    Start-Sleep -Seconds 2
    $f = Focused
    Check "the home button hands the caret over too" (CaretInAddressBar) `
        "(the caret is in $($f.Kind) '$($f.Name)')"
}
catch {
    "FAIL  the probe hit line $($_.InvocationInfo.ScriptLineNumber): $($_.Exception.Message)"
    $failed++
}
finally {
    Stop-Nibble
    Stop-Job $server -ErrorAction SilentlyContinue
    Remove-Job $server -Force -ErrorAction SilentlyContinue
}

""
"$passed passed, $failed failed"
exit $failed
