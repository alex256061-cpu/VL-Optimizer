[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\GDZ Optimizer'),
    [string]$OutputDir,
    [string]$PythonExecutable = 'python'
)

# Static inspection only. The target's JavaScript, executables and PowerShell
# strings are data; none are imported, evaluated, launched or invoked.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $PSScriptRoot
$inspector = Join-Path $PSScriptRoot 'inspect-asar.py'
$archive = Join-Path $InstallDir 'resources\app.asar'

if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) {
    throw "Arquivo ASAR nao encontrado: $archive"
}
if (-not (Test-Path -LiteralPath $inspector -PathType Leaf)) {
    throw "Inspector nao encontrado: $inspector"
}
if (-not $OutputDir) {
    $runName = 'run-{0}-{1}' -f [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ'), ([Guid]::NewGuid().ToString('N').Substring(0, 8))
    $OutputDir = Join-Path $projectDir ('artifacts\private\gdz-analysis\' + $runName)
}
$python = Get-Command $PythonExecutable -CommandType Application -ErrorAction Stop | Select-Object -First 1
$resultLines = @(& $python.Source $inspector --archive $archive --output $OutputDir --extract-text)
if ($LASTEXITCODE -ne 0) {
    throw "Inspecao ASAR falhou (codigo $LASTEXITCODE). Nenhum arquivo do programa foi modificado."
}
$inspection = ($resultLines -join [Environment]::NewLine) | ConvertFrom-Json
$manifest = Get-Content -LiteralPath $inspection.manifest -Raw -Encoding UTF8 | ConvertFrom-Json
$runDir = Split-Path -Parent $inspection.manifest
$textDir = Join-Path $runDir 'text'

function Write-JsonExclusive([string]$Name, [object]$Value) {
    $destination = Join-Path $runDir $Name
    $json = ConvertTo-Json -InputObject $Value -Depth 16
    $stream = [IO.File]::Open($destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $bytes = (New-Object Text.UTF8Encoding($false)).GetBytes($json)
        $stream.Write($bytes, 0, $bytes.Length)
    }
    finally { $stream.Dispose() }
}

function Read-Extracted([string]$Name) {
    $path = Join-Path $textDir $Name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Texto necessario nao foi extraido: $Name"
    }
    return [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
}

function Read-ChannelArray([string]$Text, [string]$Name) {
    $expression = '(?s)\bconst\s+' + [regex]::Escape($Name) + '\s*=\s*\[(.*?)\]\s*;'
    $match = [regex]::Match($Text, $expression)
    if (-not $match.Success) { throw "Lista IPC nao encontrada: $Name" }
    return @([regex]::Matches($match.Groups[1].Value, "'([^'\r\n]+)'") | ForEach-Object { $_.Groups[1].Value })
}

$package = Read-Extracted 'package.json' | ConvertFrom-Json
$preload = Read-Extracted 'preload.js'
$main = Read-Extracted 'app-main.js'
$ipc = [ordered]@{
    note = 'Allowlist declarada no preload; nao comprova o comportamento de cada handler.'
    send = @(Read-ChannelArray $preload 'SEND_CHANNELS')
    invoke = @(Read-ChannelArray $preload 'INVOKE_CHANNELS')
    on = @(Read-ChannelArray $preload 'ON_CHANNELS')
}
$functionMatches = [regex]::Matches($main, '\bfunction\s+([A-Za-z_$][\w$]*)\s*\(')
$functions = @($functionMatches | Where-Object { -not $_.Groups[1].Value.StartsWith('_0x') } | ForEach-Object {
    [ordered]@{
        name = $_.Groups[1].Value
        line = 1 + [regex]::Matches($main.Substring(0, $_.Index), "`n").Count
    }
})
$indicatorNames = @(
    'Get-CimInstance', 'Get-ItemProperty', 'Set-ItemProperty', 'Remove-Item',
    'powercfg', 'bcdedit', 'netsh', 'nvidia-smi', 'PresentMon', 'Add-Type',
    'HKLM', 'HKCU', 'Checkpoint-Computer', 'Set-DnsClientServerAddress'
)
$indicators = @($indicatorNames | ForEach-Object {
    [ordered]@{ literal = $_; occurrences = [regex]::Matches($main, [regex]::Escape($_)).Count }
})
$backend = [ordered]@{
    method = 'Busca lexical estatica, sem executar ou desofuscar JavaScript.'
    totalNamedFunctionDeclarations = $functionMatches.Count
    readableFunctionDeclarations = $functions.Count
    functions = $functions
    literalIndicators = $indicators
    browserWindowLiterals = [ordered]@{
        nodeIntegrationFalse = [regex]::IsMatch($main, "'nodeIntegration':!\[\]")
        contextIsolationTrue = [regex]::IsMatch($main, "'contextIsolation':!!\[\]")
        sandboxFalse = [regex]::IsMatch($main, "'sandbox':!\[\]")
    }
    limitation = 'Nomes/literais nao demonstram que todos os caminhos sao acessiveis nem que funcionam.'
}

$binaryNames = @(
    'GDZ Optimizer.exe',
    'resources\elevate.exe',
    'resources\app.asar.unpacked\bin\GDZOptimizer.exe',
    'resources\app.asar.unpacked\bin\GDZOptimizer_original.exe',
    'resources\app.asar.unpacked\bin\LosslessScaling.dll',
    'resources\app.asar.unpacked\bin\Lossless.dll',
    'resources\app.asar.unpacked\bin\PresentMon\PresentMon.exe'
)
$binaries = @($binaryNames | ForEach-Object {
    $path = Join-Path $InstallDir $_
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        $item = Get-Item -LiteralPath $path
        $signature = Get-AuthenticodeSignature -LiteralPath $path
        [ordered]@{
            path = $_
            size = $item.Length
            sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
            productName = $item.VersionInfo.ProductName
            productVersion = $item.VersionInfo.ProductVersion
            signatureStatus = $signature.Status.ToString()
            signer = $(if ($signature.SignerCertificate) { $signature.SignerCertificate.Subject } else { $null })
        }
    }
})

$updater = $null
$updaterPath = Join-Path $InstallDir 'resources\app-update.yml'
if (Test-Path -LiteralPath $updaterPath -PathType Leaf) {
    $updaterText = [IO.File]::ReadAllText($updaterPath)
    $urlMatch = [regex]::Match($updaterText, '(?m)^url:\s*(https?://\S+)\s*$')
    if ($urlMatch.Success) {
        $uri = [Uri]$urlMatch.Groups[1].Value
        $updater = [ordered]@{ host = $uri.Host; path = $uri.AbsolutePath; contacted = $false }
    }
}

$repoPath = Join-Path $projectDir '.tools\Reverse-Engineering-ChatGPT'
$repository = [ordered]@{
    url = 'https://github.com/xianminx/Reverse-Engineering-ChatGPT'
    path = $repoPath
    commit = $null
    purpose = 'Textos/prompts; nao e uma ferramenta de inspecao de executaveis.'
}
if (Test-Path -LiteralPath (Join-Path $repoPath '.git')) {
    $git = Get-Command git -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($git) {
        $revision = @(& $git.Source -C $repoPath rev-parse HEAD)
        if ($LASTEXITCODE -eq 0) { $repository.commit = ($revision -join '').Trim() }
    }
}
$archiveHashAfter = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($archiveHashAfter -ne $manifest.archive.sha256) {
    throw 'O arquivo ASAR mudou durante a leitura. Repita a analise em outro diretorio de saida.'
}
$summary = [ordered]@{
    schemaVersion = 1
    inspectedAtUtc = [DateTime]::UtcNow.ToString('o')
    method = 'Inspecao estatica local; alvos nao executados; configuracoes Windows nao alteradas.'
    installDir = [IO.Path]::GetFullPath($InstallDir)
    package = [ordered]@{ name = $package.name; version = $package.version; main = $package.main }
    archive = $manifest.archive
    archiveUnchangedDuringInspection = $true
    asarSummary = $manifest.summary
    ipcCounts = [ordered]@{ send = $ipc.send.Count; invoke = $ipc.invoke.Count; on = $ipc.on.Count }
    backendReadableFunctionCount = $functions.Count
    binaries = $binaries
    updater = $updater
    repository = $repository
    outputs = @('manifest.json', 'summary.json', 'ipc.json', 'backend-functions.json', 'text/')
    limits = @(
        'Nao testa ganhos de desempenho nem alteracoes/reversoes reais.',
        'Nao chama API remota, nao le perfil Roaming, nao coleta tokens ou sessoes.',
        'Codigo proprietario extraido fica em artefatos privados; nao integra o VL Optimizer.',
        'Assinatura de arquivo e metadado desta maquina; nao e avaliacao de seguranca.'
    )
}
Write-JsonExclusive 'ipc.json' $ipc
Write-JsonExclusive 'backend-functions.json' $backend
Write-JsonExclusive 'summary.json' $summary
Write-Output ('Relatorio: ' + (Join-Path $runDir 'summary.json'))
Write-Output ('GDZ {0}; ASAR inalterado; IPC {1} send / {2} invoke / {3} eventos.' -f $package.version, $ipc.send.Count, $ipc.invoke.Count, $ipc.on.Count)
