param([switch]$UseExisting)
$ErrorActionPreference = 'Stop'
$expectedSha256 = '60ED5BC3DD14EEA856493D334349B405782DDCAF0028D4B5DF4088345FBA2EFE'
$modelUrl = 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin?download=true'
$repoRoot = Split-Path $PSScriptRoot -Parent
$modelDirectory = Join-Path $repoRoot '.data/models'
$target = Join-Path $modelDirectory 'ggml-base.bin'
$download = Join-Path $modelDirectory 'ggml-base.bin.download'
New-Item -ItemType Directory -Force $modelDirectory | Out-Null
if (-not $UseExisting) { Invoke-WebRequest $modelUrl -OutFile $download; $candidate = $download }
else { if (-not (Test-Path -LiteralPath $target)) { throw 'No existing model. Run without -UseExisting to download it.' }; $candidate = $target }
$actual = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash
if ($actual -ne $expectedSha256) { throw "Whisper model checksum mismatch: $actual" }
if (-not $UseExisting) { Move-Item -LiteralPath $download -Destination $target -Force }
Write-Host "Whisper multilingual base model verified: $actual"
Write-Host "Model path: $target"
