param(
    [switch]$SelfContained,
    [switch]$Verify,
    [string]$Output = "artifacts/package"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$sdk = Join-Path $root ".tools/dotnet/dotnet.exe"
if (!(Test-Path -LiteralPath $sdk)) { throw "Local .NET SDK not found: $sdk" }
$staging = Join-Path $root ".tools/package-staging"
$output = [IO.Path]::GetFullPath((Join-Path $root $Output))
$kind = if ($SelfContained) { "self-contained" } else { "framework-dependent" }
$kindOutput = Join-Path $output $kind
Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $kindOutput -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $staging, $kindOutput | Out-Null
$selfContainedValue = if ($SelfContained) { "true" } else { "false" }
& $sdk publish (Join-Path $root "src/KotobaSUB.Windows/KotobaSUB.Windows.csproj") -c Release -r win-x64 --self-contained $selfContainedValue --no-restore -o $staging
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Copy-Item -LiteralPath (Join-Path $root "LICENSE"), (Join-Path $root "README.md"), (Join-Path $root "THIRD_PARTY_NOTICES.md") -Destination $staging
$forbidden = Get-ChildItem -LiteralPath $staging -Recurse -File | Where-Object { $_.Name -eq "ggml-base.bin" -or $_.Name -eq "konnichiwa.mp3" -or $_.FullName -match "(^|[\\/])artifacts([\\/]|$)" }
if ($forbidden) { throw "Package contains a developer model, fixture, or artifact: $($forbidden.FullName -join ', ')" }
$kind = if ($SelfContained) { "self-contained" } else { "framework-dependent" }
$manifest = [ordered]@{ name = "KotobaSUB"; runtime = "win-x64"; kind = $kind; generatedUtc = [DateTimeOffset]::UtcNow.ToString("O"); files = @(Get-ChildItem -LiteralPath $staging -Recurse -File | ForEach-Object { $_.FullName.Substring($staging.Length + 1) } | Sort-Object) }
$manifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $staging "package-manifest.json") -NoNewline
$archive = Join-Path $kindOutput ("KotobaSUB-win-x64-" + $kind + ".zip")
Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $archive -Force
if ($Verify) {
    $verification = Join-Path $kindOutput "verify"
    Expand-Archive -LiteralPath $archive -DestinationPath $verification -Force
    $exe = Join-Path $verification "KotobaSUB.exe"
    $smoke = Start-Process -FilePath $exe -ArgumentList @("--app-smoke") -WorkingDirectory $verification -Wait -PassThru
    if ($smoke.ExitCode -ne 0) { throw "Extracted application smoke exited with code $($smoke.ExitCode)." }
    $resultFile = Join-Path $verification "artifacts/app-smoke/result.txt"
    for ($i = 0; $i -lt 120 -and !(Test-Path -LiteralPath $resultFile); $i++) { Start-Sleep -Milliseconds 250 }
    $resultFile = Join-Path $verification "artifacts/app-smoke/result.txt"
    for ($i = 0; $i -lt 20 -and !(Test-Path -LiteralPath $resultFile); $i++) { Start-Sleep -Milliseconds 250 }
    if (!(Select-String -LiteralPath $resultFile -Pattern "TrayVisible=True" -Quiet)) { throw "Extracted application smoke did not report a visible tray icon." }
}
Write-Output "Package=$archive"