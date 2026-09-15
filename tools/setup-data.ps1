param([switch]$UseExisting)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$repoRoot = Split-Path $PSScriptRoot -Parent
$dataDirectory = Join-Path $repoRoot '.data'
New-Item -ItemType Directory -Force $dataDirectory | Out-Null
$sdk = Join-Path $repoRoot '.tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) { $sdk = 'dotnet' }
$sourceFile = Join-Path $dataDirectory 'JMdict_e.gz'
$downloadFile = Join-Path $dataDirectory 'JMdict_e.download.gz'
Push-Location $repoRoot
try {
    if (-not $UseExisting) {
        Invoke-WebRequest 'https://www.edrdg.org/pub/Nihongo/JMdict_e.gz' -OutFile $downloadFile
        $importFile = $downloadFile
    } else {
        if (-not (Test-Path -LiteralPath $sourceFile)) { throw 'No local JMdict archive. Run without -UseExisting to download it.' }
        $importFile = $sourceFile
    }
    & $sdk run --project tools/KotobaSUB.Data -c Release -- import $importFile (Join-Path $dataDirectory 'jmdict.sqlite')
    if ($LASTEXITCODE -ne 0) { throw 'Dictionary import failed; prior database preserved.' }
    if (-not $UseExisting) { Move-Item -LiteralPath $downloadFile -Destination $sourceFile -Force }
    & $sdk build KotobaSUB.slnx -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed after dictionary import.' }
    Write-Host 'Offline dictionary updated. Restart KotobaSUB to load the new version.'
} finally { Pop-Location }
