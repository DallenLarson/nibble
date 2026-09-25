# Lists every top-level window title a process owns, in tree order.
param([Parameter(Mandatory = $true)][int]$ProcessId)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$auto = [System.Windows.Automation.AutomationElement]
$condition = New-Object System.Windows.Automation.PropertyCondition($auto::ProcessIdProperty, $ProcessId)
foreach ($window in $auto::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)) {
    "WINDOW: $($window.Current.Name)"
}
