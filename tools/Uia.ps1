# Tiny UI Automation helper used to drive Nibble during verification runs.
#   -Mode dump  -ProcessId 123                 list every button the process exposes
#   -Mode click -ProcessId 123 -Name "Menu"    invoke a button by accessible name
param(
    [Parameter(Mandatory = $true)][ValidateSet("dump", "click", "set", "focus", "texts", "all")][string]$Mode,
    [Parameter(Mandatory = $true)][int]$ProcessId,
    [string]$Name,
    [string]$Value,
    [string]$Match = "exact"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$auto = [System.Windows.Automation.AutomationElement]
$scope = [System.Windows.Automation.TreeScope]

$pidCondition = New-Object System.Windows.Automation.PropertyCondition($auto::ProcessIdProperty, $ProcessId)
$root = $auto::RootElement

$windows = $root.FindAll($scope::Descendants, $pidCondition)
if ($windows.Count -eq 0) {
    Write-Output "NO-ELEMENTS for pid $ProcessId"
    exit 2
}

$buttonCondition = New-Object System.Windows.Automation.PropertyCondition(
    $auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
$textCondition = New-Object System.Windows.Automation.PropertyCondition(
    $auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
$buttons = @()
foreach ($window in $windows) {
    foreach ($found in $window.FindAll($scope::Descendants, $buttonCondition)) { $buttons += $found }
}

# Actions may target text boxes too (the find bar, the address bar), so keep a wider list.
$editCondition = New-Object System.Windows.Automation.PropertyCondition(
    $auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
$linkCondition = New-Object System.Windows.Automation.PropertyCondition(
    $auto::ControlTypeProperty, [System.Windows.Automation.ControlType]::Hyperlink)
$targets = @()
foreach ($window in $windows) {
    foreach ($found in $window.FindAll($scope::Descendants, $buttonCondition)) { $targets += $found }
    foreach ($found in $window.FindAll($scope::Descendants, $editCondition)) { $targets += $found }
    foreach ($found in $window.FindAll($scope::Descendants, $linkCondition)) { $targets += $found }
}

if ($Mode -eq "dump") {
    $seen = @{}
    foreach ($button in $buttons) {
        $label = $button.Current.Name
        $id = $button.Current.AutomationId
        $rect = $button.Current.BoundingRectangle
        $key = "$label|$id|$($rect.X),$($rect.Y)"
        if ($seen.ContainsKey($key)) { continue }
        $seen[$key] = $true
        "{0,-34} id={1,-16} off={2,-5} at={3},{4} size={5}x{6}" -f `
            $label, $id, $button.Current.IsOffscreen, `
            [int]$rect.X, [int]$rect.Y, [int]$rect.Width, [int]$rect.Height
    }
    foreach ($window in $windows) {
        foreach ($label in $window.FindAll($scope::Descendants, $textCondition)) {
            $text = $label.Current.Name
            if ($text -match '^[\d/]+$|found|cookie|private') {
                "TEXT  {0}" -f $text
            }
        }
    }
    exit 0
}

if ($Mode -eq "texts") {
    foreach ($window in $windows) {
        foreach ($label in $window.FindAll($scope::Descendants, $textCondition)) {
            if ($label.Current.Name) { "TEXT  {0}" -f $label.Current.Name }
        }
    }
    exit 0
}

if ($Mode -eq "all") {
    foreach ($window in $windows) {
        foreach ($element in $window.FindAll($scope::Descendants,
                [System.Windows.Automation.Condition]::TrueCondition)) {
            $label = $element.Current.Name
            if (-not $label) { continue }
            $rect = $element.Current.BoundingRectangle
            if ($rect.Width -le 0) { continue }
            "ELEMENT {0,-14} {1,-30} at {2},{3} {4}x{5}" -f `
                $element.Current.ControlType.ProgrammaticName.Replace("ControlType.", ""), `
                $label.Substring(0, [Math]::Min(30, $label.Length)), `
                [int]$rect.X, [int]$rect.Y, [int]$rect.Width, [int]$rect.Height
        }
    }
    exit 0
}

if (-not $Name) { Write-Output "NAME-REQUIRED"; exit 3 }

$target = $null
foreach ($button in $targets) {
    $label = $button.Current.Name
    $hit = if ($Match -eq "exact") { $label -eq $Name } else { $label -like $Name }
    if ($hit) { $target = $button; break }
}

if (-not $target) { Write-Output "NOT-FOUND: $Name"; exit 4 }

try {
    if ($Mode -eq "set") {
        $target.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Value)
        Write-Output "SET: $($target.Current.Name) = $Value"
        exit 0
    }
    if ($Mode -eq "focus") {
        $target.SetFocus()
        Write-Output "FOCUSED: $($target.Current.Name)"
        exit 0
    }
    $pattern = $target.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
    Write-Output "CLICKED: $($target.Current.Name)"
}
catch {
    Write-Output "CLICK-FAILED: $($_.Exception.Message)"
    exit 5
}
