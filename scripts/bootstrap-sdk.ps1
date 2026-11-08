param([string]$Version = '10.0.403')
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$destination = Join-Path $workspace '.tools\dotnet'
$dotnetExe = Join-Path $destination 'dotnet.exe'
if (Test-Path -LiteralPath $dotnetExe) {
    $actualVersion = & $dotnetExe --version
    if ($LASTEXITCODE -ne 0 -or $actualVersion.Trim() -ne $Version) { throw "SDK local difere de $Version. Versão encontrada: $actualVersion" }
    Write-Output $actualVersion
    exit 0
}
New-Item -ItemType Directory -Force -Path $destination | Out-Null
$metadata = Invoke-RestMethod 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
$sdk = $metadata.releases | ForEach-Object { $_.sdks } | Where-Object version -eq $Version | Select-Object -First 1
if (-not $sdk) { throw "SDK oficial $Version não encontrado" }
$download = $sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.zip' } | Select-Object -First 1
if (-not $download -or ([uri]$download.url).Scheme -ne 'https' -or ([uri]$download.url).Host -notin @('builds.dotnet.microsoft.com','download.visualstudio.microsoft.com')) { throw 'Origem de SDK inválida' }
$archive = Join-Path $workspace '.tools\dotnet-sdk.zip'
Invoke-WebRequest -Uri $download.url -OutFile $archive
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash -ne $download.hash) { throw 'SHA512 do SDK não confere' }
Expand-Archive -LiteralPath $archive -DestinationPath $destination -Force
& $dotnetExe --version
exit $LASTEXITCODE
