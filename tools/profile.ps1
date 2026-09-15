param(
    [ValidateSet("idle", "routing")]
    [string]$Mode = "idle",
    [string]$Output = "artifacts/profile"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "src/KotobaSUB.Windows/bin/Release/net10.0-windows10.0.19041.0/win-x64/KotobaSUB.exe"
if (!(Test-Path -LiteralPath $exe)) { throw "Build the Release Windows executable before profiling: $exe" }
$directory = [IO.Path]::GetFullPath((Join-Path $root $Output))
New-Item -ItemType Directory -Path $directory | Out-Null
$arguments = if ($Mode -eq "routing") { "--routing-smoke --output `"$directory/routing`"" } else { "--app-smoke" }
$process = Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $root -PassThru
$samples = [Collections.Generic.List[object]]::new()
try {
    while (!$process.HasExited) {
        $process.Refresh()
        $samples.Add([ordered]@{ atUtc = [DateTimeOffset]::UtcNow; workingSetBytes = $process.WorkingSet64; privateBytes = $process.PrivateMemorySize64; cpuMilliseconds = [Math]::Round($process.TotalProcessorTime.TotalMilliseconds, 1) })
        Start-Sleep -Milliseconds 250
    }
    $process.Refresh()
    $samples.Add([ordered]@{ atUtc = [DateTimeOffset]::UtcNow; workingSetBytes = $process.WorkingSet64; privateBytes = $process.PrivateMemorySize64; cpuMilliseconds = [Math]::Round($process.TotalProcessorTime.TotalMilliseconds, 1) })
    if ($process.ExitCode -ne 0) { throw "Profiled process exited with code $($process.ExitCode)." }
    $workingSets = @($samples | ForEach-Object { [int64]$_.workingSetBytes })
    $privateBytes = @($samples | ForEach-Object { [int64]$_.privateBytes })
    $result = [ordered]@{ mode = $Mode; executable = $exe; sampleCount = $samples.Count; peakWorkingSetBytes = ($workingSets | Measure-Object -Maximum).Maximum; peakPrivateBytes = ($privateBytes | Measure-Object -Maximum).Maximum; cpuMilliseconds = ($samples | Select-Object -Last 1).cpuMilliseconds; samples = @($samples) }
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $directory "$Mode.json")
}
finally { if (!$process.HasExited) { $process.Kill(); $process.WaitForExit() } }
