# Right-clicks a tab, then uses the menu it puts up to close everything else. That is the
# answer to "closing lots of tabs is hard": one gesture, one click, a clean strip. It also
# measures the close mark, which is the other half of that complaint.
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Profile,
    [int]$Port = 8802
)
$ErrorActionPreference = "Stop"
$here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -Namespace TM -Name Win -MemberDefinition @'
[StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
[DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
[DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
'@
[TM.Win]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

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

$server = Start-Job -FilePath (Join-Path $here "MediaServer.ps1") -ArgumentList $Port, 900, (Join-Path $here "..\scratch\tabmenu\samples.jsonl"), (Join-Path $here "MediaPage.html")
Start-Sleep -Seconds 2

$env:NIBBLE_PROFILE = $Profile
$app = Start-Process -FilePath $Exe -PassThru

$auto = [System.Windows.Automation.AutomationElement]
$pidCond = New-Object System.Windows.Automation.PropertyCondition($auto::ProcessIdProperty, $app.Id)
function Get-Window { @($auto::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $pidCond) | Where-Object { $_.Current.Name -like "*Nibble*" }) | Select-Object -First 1 }
function Get-Tabs($win) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::DataItem)
    $textCond = New-Object System.Windows.Automation.PropertyCondition($auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $rows = @()
    foreach ($t in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        if ($t.Current.Name -ne "Nibble.ZTab") { continue }
        $r = $t.Current.BoundingRectangle
        if ($r.Width -le 0) { continue }
        $label = ""
        foreach ($inner in $t.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCond)) {
            if ($inner.Current.Name) { $label = $inner.Current.Name; break }
        }
        $rows += [pscustomobject]@{ Name = $label; X = [int]$r.X; Y = [int]$r.Y; W = [int]$r.Width; H = [int]$r.Height }
    }
    $seen = @{}
    @($rows | Where-Object {
        $key = "$($_.Name)|$($_.X)|$($_.Y)"
        if ($seen.ContainsKey($key)) { return $false }
        $seen[$key] = $true
        return $true
    } | Sort-Object X)
}
function Post-At([IntPtr]$hwnd, [uint32]$msg, [int]$wx, [int]$wy, [int]$mk) {
    $packed = [int64]((([uint32]$wy) -band 0xFFFF) -shl 16) -bor (([uint32]$wx) -band 0xFFFF)
    [TM.Win]::PostMessage($hwnd, $msg, [IntPtr]$mk, [IntPtr]$packed) | Out-Null
}

$failed = 0
try {
    $win = $null
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Milliseconds 500
        $win = Get-Window
        if ($win -and @(Get-Tabs $win).Count -ge 3) { break }
    }
    if (-not $win) { "FAIL  the window never appeared"; exit 1 }
    $tabs = @(Get-Tabs $win)
    "tabs on arrival: " + (($tabs | ForEach-Object { $_.Name }) -join ", ")
    if ($tabs.Count -lt 3) { "FAIL  expected three tabs, saw $($tabs.Count)"; exit 1 }

    $close = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.AndCondition(
            (New-Object System.Windows.Automation.PropertyCondition($auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)),
            (New-Object System.Windows.Automation.PropertyCondition($auto::NameProperty, "Close tab")))))
    $size = if ($close) { "$([int]$close.Current.BoundingRectangle.Width)x$([int]$close.Current.BoundingRectangle.Height)" } else { "missing" }

    # Right-click the first tab, away from its close mark.
    $hwnd = [IntPtr]$win.Current.NativeWindowHandle
    $target = $tabs[0]
    $x = [int]($target.X + 24)
    $y = [int]($target.Y + $target.H / 2)
    [TM.Win]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 200
    $p = New-Object TM.Win+POINT
    $p.X = 0; $p.Y = 0
    [TM.Win]::ClientToScreen($hwnd, [ref]$p) | Out-Null
    $cx = $x - $p.X; $cy = $y - $p.Y
    Post-At $hwnd 0x0204 $cx $cy 0
    Post-At $hwnd 0x0205 $cx $cy 0
    Start-Sleep -Milliseconds 900

    $uia = Join-Path $here "Uia.ps1"
    $menu = (& powershell -NoProfile -ExecutionPolicy Bypass -File $uia -Mode dump -ProcessId $app.Id) -join " / "
    ""
    "the close mark on a tab measures: $size"
    "after right-clicking the first tab, the menu offers:"
    foreach ($row in ($menu -split ' / ' | Where-Object { $_ -match 'Close|Duplicate|Pin|Move|right' })) { "  $row" }

    $clicked = (& powershell -NoProfile -ExecutionPolicy Bypass -File $uia -Mode click -ProcessId $app.Id -Name "Close other tabs*" -Match like) -join " "
    ""
    "clicking it: $clicked"
    Start-Sleep -Seconds 2

    $left = @(Get-Tabs $win)
    ""
    "tabs left: " + (($left | ForEach-Object { $_.Name }) -join ", ")
    ""
    "verdict:"
    if ($menu -match "Close other tabs") { "  PASS  right-clicking a tab opens a tab menu" } else { "  FAIL  no tab menu appeared"; $failed++ }
    if ($size -ne "missing") {
        $w = [int]($size.Split("x")[0])
        if ($w -ge 26) { "  PASS  the close mark is a real target ($size)" } else { "  FAIL  the close mark is only $size"; $failed++ }
    } else { "  FAIL  no close mark found"; $failed++ }
    if ($left.Count -eq 1 -and $left[0].Name -eq $target.Name) { "  PASS  one click closed every other tab and kept the one clicked" }
    else { "  FAIL  expected one tab left, saw $($left.Count): " + (($left | ForEach-Object { $_.Name }) -join ", "); $failed++ }
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
