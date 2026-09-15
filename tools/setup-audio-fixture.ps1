param([switch]$UseExisting)
$ErrorActionPreference = 'Stop'
$expectedSha256 = '13DC9E5E8F7760410A67BFDB08A3F44B2DB379D6C94FD82D37BE282A755F886B'
$fixtureUrl = 'https://upload.wikimedia.org/wikipedia/commons/transcoded/9/97/Wikibooksqsjapanese1-snd005.ogg/Wikibooksqsjapanese1-snd005.ogg.mp3'
$repoRoot = Split-Path $PSScriptRoot -Parent
$fixtureDirectory = Join-Path $repoRoot '.data/fixtures'
$target = Join-Path $fixtureDirectory 'konnichiwa.mp3'
$download = Join-Path $fixtureDirectory 'konnichiwa.mp3.download'
New-Item -ItemType Directory -Force $fixtureDirectory | Out-Null
if (-not $UseExisting) { Invoke-WebRequest $fixtureUrl -OutFile $download; $candidate = $download }
else { if (-not (Test-Path -LiteralPath $target)) { throw 'No existing fixture. Run without -UseExisting to download it.' }; $candidate = $target }
$actual = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash
if ($actual -ne $expectedSha256) { throw "Japanese fixture checksum mismatch: $actual" }
if (-not $UseExisting) { Move-Item -LiteralPath $download -Destination $target -Force }
Write-Host "Licensed Japanese fixture verified: $actual"
Write-Host "Fixture path: $target"