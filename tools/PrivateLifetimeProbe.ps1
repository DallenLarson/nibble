# Shows which process owns which window at each step, so "the browser exited" can be told
# apart from "the windows live in two processes".
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$Profile
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function Snapshot([string]$label) {
    "--- $label ---"
    $procs = Get-Process Nibble -ErrorAction SilentlyContinue
    if (-not $procs) { "  no Nibble process"; return }
    $auto = [System.Windows.Automation.AutomationElement]
    foreach ($p in $procs) {
        $condition = New-Object System.Windows.Automation.PropertyCondition($auto::ProcessIdProperty, $p.Id)
        $names = @()
        foreach ($w in $auto::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)) {
            if ($w.Current.Name) { $names += $w.Current.Name }
        }
        "  pid $($p.Id): " + ($names -join " | ")
    }
}

$env:NIBBLE_PROFILE = $Profile
$first = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds 9
Snapshot "after the normal launch"

$second = Start-Process -FilePath $Exe -ArgumentList "--private" -PassThru
Start-Sleep -Seconds 8
"second process exited: $($second.HasExited)"
Snapshot "after the private launch"

# close the normal window(s) only
$auto = [System.Windows.Automation.AutomationElement]
foreach ($p in (Get-Process Nibble -ErrorAction SilentlyContinue)) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($auto::ProcessIdProperty, $p.Id)
    foreach ($w in $auto::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)) {
        if ($w.Current.Name -and $w.Current.Name -notlike "*private*") {
            try { $w.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close() } catch { }
        }
    }
}
Start-Sleep -Seconds 4
Snapshot "after closing the normal window"

Get-Process Nibble -ErrorAction SilentlyContinue | Stop-Process -Force
