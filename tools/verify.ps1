param([switch]$Native)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$repoRoot = Split-Path $PSScriptRoot -Parent
$localSdk = Join-Path $repoRoot '.tools/dotnet/dotnet.exe'
$sdk = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
Push-Location $repoRoot
try {
    & $sdk build KotobaSUB.slnx -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    $dictionaryArgs = if (Test-Path .data/jmdict.sqlite) { @("--", "--dictionary", ".data/jmdict.sqlite") } else { @() }
    & $sdk run --project tests/KotobaSUB.Tests -c Release --no-build @dictionaryArgs
    if ($LASTEXITCODE -ne 0) { throw 'Regression tests failed' }
    if ($Native) {
        & $sdk run --project src/KotobaSUB.Windows -c Release --no-build -- --smoke --output artifacts/native-smoke
        if ($LASTEXITCODE -ne 0) { throw 'Native smoke failed' }
        if (Test-Path .data/jmdict.sqlite) {
            & $sdk run --project src/KotobaSUB.Windows -c Release --no-build -- --learning-smoke
            if ($LASTEXITCODE -ne 0) { throw 'Learning smoke failed' }
        }
    }
} finally { Pop-Location }
