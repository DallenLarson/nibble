# Screenshot one window of a running process, even when it is behind others.
#   -ProcessId 123 -Out work\shot.png [-Title "Set up Nibble"]
param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$Title,
    [string]$NotTitle,
    [switch]$Screen
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type -Namespace Shot -Name Native -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr param);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
[DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int max);
public delegate bool EnumProc(IntPtr hWnd, IntPtr param);
public struct RECT { public int Left, Top, Right, Bottom; }
'@

$auto = [System.Windows.Automation.AutomationElement]
$condition = New-Object System.Windows.Automation.PropertyCondition($auto::ProcessIdProperty, $ProcessId)
$windows = $auto::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)
if ($windows.Count -eq 0) { Write-Output "NO-WINDOWS for pid $ProcessId"; exit 2 }

$target = $null
foreach ($window in $windows) {
    if ($NotTitle -and $window.Current.Name -like $NotTitle) { continue }
    if (-not $Title) { $target = $window; break }
    if ($window.Current.Name -like $Title) { $target = $window; break }
}

$handle = [IntPtr]::Zero
if ($target) { $handle = [IntPtr]$target.Current.NativeWindowHandle }

if ($handle -eq [IntPtr]::Zero -and $Title) {
    # A modal dialog is owned by its parent, so it hangs off that window in the UIA tree
    # rather than sitting at the desktop root. Look one level deeper.
    $nameCondition = New-Object System.Windows.Automation.PropertyCondition(
        $auto::ProcessIdProperty, $ProcessId)
    foreach ($element in $auto::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, $nameCondition)) {
        if ($element.Current.ControlType -ne [System.Windows.Automation.ControlType]::Window) { continue }
        if ($element.Current.Name -like $Title) {
            $candidate = [IntPtr]$element.Current.NativeWindowHandle
            if ($candidate -ne [IntPtr]::Zero) { $handle = $candidate; break }
        }
    }
}

if ($handle -eq [IntPtr]::Zero) { Write-Output "NO-MATCH for '$Title'"; exit 3 }
$rect = New-Object Shot.Native+RECT
[Shot.Native]::GetWindowRect($handle, [ref]$rect) | Out-Null
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top

$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
if ($Screen) {
    # Popups (menus, toasts, the find bar) are separate windows: only a screen grab sees them.
    $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size($width, $height)))
}
else {
    $hdc = $graphics.GetHdc()
    [Shot.Native]::PrintWindow($handle, $hdc, 2) | Out-Null
    $graphics.ReleaseHdc($hdc)
}

$full = [System.IO.Path]::GetFullPath($Out)
New-Item -ItemType Directory -Force -Path (Split-Path $full) | Out-Null
$bitmap.Save($full, [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose(); $bitmap.Dispose()

Write-Output "SAVED $full ($width x $height) window='$($target.Current.Name)'"
