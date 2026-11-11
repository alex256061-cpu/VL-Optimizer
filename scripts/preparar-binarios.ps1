param([switch]$Force)
<#
.SYNOPSIS
    Localiza e valida os binários de terceiros que o VL Optimizer empacota.

.DESCRIPTION
    Nem Lossless.dll nem PresentMon.exe ficam no repositório. Este script procura cópias conhecidas
    nesta máquina, confere o SHA-256 de cada uma e copia para artifacts/private com o nome que o
    publish.ps1 espera.

    Se um binário não for encontrado, o script diz exatamente onde procurar e termina com código 1,
    sem empacotar nada pela metade.

.EXAMPLE
    .\scripts\preparar-binarios.ps1
    .\scripts\preparar-binarios.ps1 -Force   # sobrescreve cópias existentes
#>
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent

$engineSha = '626B196D799606CD4250B7B29E04228692AB70CF56A5D1BBB56D748C8219F0EB'
$presentMonSha = '9BEC3083069F58F911E6A512F4806DB51A27BD096103087BC1D05EF54C80A191'

function Write-Step([string]$Text) { Write-Host $Text -ForegroundColor Cyan }
function Write-Ok([string]$Text) { Write-Host "  OK   $Text" -ForegroundColor Green }
function Write-Bad([string]$Text) { Write-Host "  $Text" -ForegroundColor Yellow }

# Candidatos por binário, na ordem em que se espera encontrá-los.
$engineCandidates = @(
    (Join-Path $workspace 'artifacts\private\engine-payload-test\appdata\engine\Lossless.dll'),
    (Join-Path $workspace 'artifacts\private\engine-staging\Lossless.dll'),
    'C:\Program Files (x86)\Steam\steamapps\common\Lossless Scaling\Lossless.dll',
    (Join-Path $env:LOCALAPPDATA 'Programs\GDZ Optimizer\resources\app.asar.unpacked\bin\Lossless.dll')
)
$presentMonCandidates = @(
    (Join-Path $workspace 'artifacts\private\presentmon\PresentMon.exe'),
    (Join-Path $env:LOCALAPPDATA 'Programs\GDZ Optimizer\resources\app.asar.unpacked\bin\PresentMon\PresentMon.exe')
)

function Find-Verified {
    param([string[]]$Candidates, [string]$Expected, [string]$Label)
    $rejected = @()
    foreach ($path in $Candidates) {
        if (-not (Test-Path -LiteralPath $path)) { continue }
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        if ($hash -eq $Expected) { return @{ Path = $path; Hash = $hash } }
        $rejected += "$path  (hash $($hash.Substring(0,16))...)"
    }
    if ($rejected.Count -gt 0) {
        Write-Step "$Label encontrado, mas com hash diferente do validado:"
        $rejected | ForEach-Object { Write-Bad "    $_" }
        Write-Bad "    esperado: $Expected"
    }
    return $null
}

function Install-Binary {
    param([string]$Source, [string]$Destination)
    $dir = Split-Path $Destination -Parent
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
}

Write-Step 'Procurando o motor de escala (Lossless.dll)'
$engine = Find-Verified -Candidates $engineCandidates -Expected $engineSha -Label 'Lossless.dll'
if ($engine) {
    $target = Join-Path $workspace 'artifacts\private\engine-payload-test\appdata\engine\Lossless.dll'
    if ($Force -or -not (Test-Path -LiteralPath $target) -or (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $engineSha) {
        Install-Binary -Source $engine.Path -Destination $target
    }
    Write-Ok "motor pronto: $target"
} else {
    Write-Bad 'Lossless.dll não encontrado em nenhum caminho conhecido.'
    Write-Bad 'Instale o Lossless Scaling pela Steam, ou copie a DLL do pacote do GDZ Optimizer.'
}

Write-Step 'Procurando o medidor de FPS (PresentMon.exe)'
$presentMon = Find-Verified -Candidates $presentMonCandidates -Expected $presentMonSha -Label 'PresentMon.exe'
if ($presentMon) {
    $target = Join-Path $workspace 'artifacts\private\presentmon\PresentMon.exe'
    if ($Force -or -not (Test-Path -LiteralPath $target) -or (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $presentMonSha) {
        Install-Binary -Source $presentMon.Path -Destination $target
    }
    Write-Ok "medidor pronto: $target"
} else {
    Write-Bad 'PresentMon.exe não encontrado em nenhum caminho conhecido.'
    Write-Bad 'Baixe do repositório oficial GameTechDev/PresentMon (versão 2.5.x) ou copie do GDZ Optimizer.'
}

Write-Step 'Procurando o SDK do .NET'
$dotnet = Join-Path $workspace '.tools\dotnet\dotnet.exe'
if (Test-Path -LiteralPath $dotnet) {
    Write-Ok "SDK local: $dotnet"
} else {
    Write-Bad 'SDK local ausente. Rode .\scripts\bootstrap-sdk.ps1, ou instale o .NET 10 SDK oficial.'
}

Write-Host ''
if ($engine -and $presentMon) {
    Write-Host 'Binários prontos. Agora: .\scripts\publish.ps1' -ForegroundColor Green
    exit 0
}
Write-Host 'Faltam binários. O pacote sairia incompleto; nada foi gerado.' -ForegroundColor Yellow
Write-Host 'Veja docs\RECUPERACAO.md para as origens e os hashes esperados.' -ForegroundColor Yellow
exit 1
