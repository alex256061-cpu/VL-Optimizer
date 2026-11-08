param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$dotnetExe = Join-Path $workspace '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetExe)) { & (Join-Path $PSScriptRoot 'bootstrap-sdk.ps1'); if ($LASTEXITCODE -ne 0) { throw 'SDK indisponível' } }
Push-Location $workspace
try {
    & $dotnetExe build VLOptimizer.sln -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Compilação falhou' }
    & $dotnetExe run --project tests\VLOptimizer.Tests -c Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Testes falharam' }
    if ($Publish) { & (Join-Path $PSScriptRoot 'publish.ps1'); if ($LASTEXITCODE -ne 0) { throw 'Publicação falhou' } }
} finally { Pop-Location }
