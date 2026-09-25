# Every top-level window a process owns, with class, title and rect (EnumWindows, not UIA,
# so popups and dialogs show up too).
param([Parameter(Mandatory = $true)][int]$ProcessId)

$ErrorActionPreference = "Stop"
Add-Type -Namespace WO -Name Win -MemberDefinition @'
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr param);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
[DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int max);
[DllImport("user32.dll")] public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder text, int max);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
public delegate bool EnumProc(IntPtr hWnd, IntPtr param);
'@

$script:rows = @()
$callback = [WO.Win+EnumProc] {
    param($h, $p)
    $owner = 0
    [WO.Win]::GetWindowThreadProcessId($h, [ref]$owner) | Out-Null
    if ($owner -eq $ProcessId) {
        $title = New-Object System.Text.StringBuilder 256
        [WO.Win]::GetWindowText($h, $title, 256) | Out-Null
        $class = New-Object System.Text.StringBuilder 256
        [WO.Win]::GetClassName($h, $class, 256) | Out-Null
        $rect = New-Object WO.Win+RECT
        [WO.Win]::GetWindowRect($h, [ref]$rect) | Out-Null
        $script:rows += [pscustomobject]@{
            Visible = [WO.Win]::IsWindowVisible($h)
            Title = $title.ToString()
            Class = $class.ToString().Substring(0, [Math]::Min(24, $class.Length))
            Rect = "$($rect.Left),$($rect.Top) $($rect.Right - $rect.Left)x$($rect.Bottom - $rect.Top)"
        }
    }
    return $true
}
[WO.Win]::EnumWindows($callback, [IntPtr]::Zero) | Out-Null
$script:rows | Format-Table -AutoSize | Out-String -Width 140
