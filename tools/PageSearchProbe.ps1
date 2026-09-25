# Types into the home page's own search field with real key input and photographs where the
# suggestion list lands relative to the shortcut tiles underneath it.
param(
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [string]$Text = "git",
    [string]$Out = "$PSScriptRoot\..\scratch\page-search.png"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -Namespace PS -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
'@

$auto = [System.Windows.Automation.AutomationElement]
$condition = New-Object System.Windows.Automation.PropertyCondition($auto::ProcessIdProperty, $ProcessId)

$window = $null
$search = $null
# the page can take a moment to appear to UIA after a launch, so give it a few tries
for ($attempt = 0; $attempt -lt 6 -and -not $search; $attempt++) {
    foreach ($w in $auto::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)) {
        if (-not $window) { $window = $w }
        foreach ($e in $w.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition($auto::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::Edit)))) {
            if ($e.Current.Name -eq "Search") { $search = $e }
        }
    }
    if (-not $search) { Start-Sleep -Seconds 3 }
}
if (-not $window) { throw "no window" }
if (-not $search) { throw "no search field on the page" }

function FieldValue {
    try { return $search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value }
    catch { return "" }
}

[PS.Win]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null
Start-Sleep -Milliseconds 800

for ($attempt = 0; $attempt -lt 3; $attempt++) {
    $r = $search.Current.BoundingRectangle
    [PS.Win]::SetCursorPos([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2)) | Out-Null
    Start-Sleep -Milliseconds 250
    [PS.Win]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)   # left down
    [PS.Win]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)   # left up
    Start-Sleep -Milliseconds 500
    [System.Windows.Forms.SendKeys]::SendWait($Text)
    Start-Sleep -Seconds 2
    if ((FieldValue) -eq $Text) { break }
}

"field now contains: '" + (FieldValue) + "'"
if ((FieldValue) -ne $Text) { throw "the page's search field never received the text" }

$rect = $window.Current.BoundingRectangle
$bitmap = New-Object System.Drawing.Bitmap([int]$rect.Width, [int]$rect.Height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen([int]$rect.X, [int]$rect.Y, 0, 0,
    (New-Object System.Drawing.Size([int]$rect.Width, [int]$rect.Height)))
$graphics.Dispose()
$bitmap.Save([System.IO.Path]::GetFullPath($Out), [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()

"typed '$Text' into the page's search field, saved $Out"
"elements named like a suggestion:"
foreach ($w in $auto::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)) {
    foreach ($t in $w.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.PropertyCondition($auto::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Text)))) {
        $name = $t.Current.Name
        if ($name -match "github|gitlab|Search|github\.com") {
            $r = $t.Current.BoundingRectangle
            "  '$name' at $([int]$r.X),$([int]$r.Y) $([int]$r.Width)x$([int]$r.Height)"
        }
    }
}
