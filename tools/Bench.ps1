# Measures Nibble's cold-start cost: time until the window appears, and time until the
# WebView2 engine process exists. Run with the app closed (a shared profile skews it).
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [int]$Runs = 3
)

$results = @()

for ($i = 1; $i -le $Runs; $i++) {
    $process = Start-Process -FilePath $Exe -PassThru
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $windowMs = -1
    $engineMs = -1
    $cpuAtWindowMs = -1

    while ($watch.ElapsedMilliseconds -lt 30000) {
        $proc = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
        if (-not $proc) { break }

        if ($windowMs -lt 0 -and $proc.MainWindowHandle -ne 0) {
            $windowMs = $watch.ElapsedMilliseconds
            $proc.Refresh()
            $cpuAtWindowMs = [int]$proc.TotalProcessorTime.TotalMilliseconds
        }
        if ($engineMs -lt 0) {
            $child = Get-CimInstance Win32_Process -Filter "ParentProcessId = $($process.Id)" -ErrorAction SilentlyContinue |
                     Where-Object { $_.Name -like 'msedgewebview2*' } | Select-Object -First 1
            if ($child) { $engineMs = $watch.ElapsedMilliseconds }
        }
        if ($windowMs -ge 0 -and $engineMs -ge 0) { break }

        Start-Sleep -Milliseconds 10
    }
    $watch.Stop()

    $proc = Get-Process -Id $process.Id -ErrorAction SilentlyContinue
    if ($proc) {
        $proc.CloseMainWindow() | Out-Null
        Start-Sleep -Milliseconds 500
        if (-not $proc.HasExited) { $proc.Kill() }
    }
    Start-Sleep -Milliseconds 900

    $results += [pscustomobject]@{ Run = $i; WindowMs = $windowMs; CpuMs = $cpuAtWindowMs; EngineMs = $engineMs }
}

$results | Format-Table -AutoSize | Out-String | Write-Output
$warm = $results | Select-Object -Skip 1
$median = { param($values) ($values | Sort-Object)[[int]($values.Count / 2)] }
"window visible (warm runs): median {0} ms | min {1} | max {2}" -f
    (& $median $warm.WindowMs), ($warm.WindowMs | Measure-Object -Minimum).Minimum, ($warm.WindowMs | Measure-Object -Maximum).Maximum
"cpu time when window appears (warm): median {0} ms (JIT + CLR + WPF work)" -f (& $median $warm.CpuMs)
"engine process spawned (warm): median {0} ms" -f (& $median $warm.EngineMs)
