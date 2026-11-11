param([switch]$SkipInstaller)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$dotnetExe = Join-Path $workspace '.tools\dotnet\dotnet.exe'
$publish = Join-Path $workspace 'artifacts\publish\win-x64'
$release = Join-Path $workspace 'release'
New-Item -ItemType Directory -Force -Path $release | Out-Null
& $dotnetExe publish (Join-Path $workspace 'src\VLOptimizer.App\VLOptimizer.App.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false --nologo -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Publicação falhou' }
$exe = Join-Path $publish 'VLOptimizer.exe'
Copy-Item -LiteralPath (Join-Path $workspace 'installer\LEIA-ME.txt') -Destination (Join-Path $publish 'LEIA-ME.txt') -Force

# Motor de apresentação: o VL carrega a DLL que acompanha o pacote, então o cliente não precisa ter o
# Lossless instalado. O binário não fica no repositório (é de terceiros e está no .gitignore); ele é
# procurado nas instalações presentes nesta máquina e copiado para o pacote quando encontrado.
$engineBinary = Join-Path $workspace 'artifacts\private\engine-payload-test\appdata\engine\Lossless.dll'
if (-not (Test-Path -LiteralPath $engineBinary)) {
    $engineBinary = Join-Path $workspace 'artifacts\private\engine-staging\Lossless.dll'
}
if (-not (Test-Path -LiteralPath $engineBinary)) {
    $steam = 'C:\Program Files (x86)\Steam\steamapps\common\Lossless Scaling\Lossless.dll'
    if (Test-Path -LiteralPath $steam) { $engineBinary = $steam }
}
if (Test-Path -LiteralPath $engineBinary) {
    # Confere o hash que o app valida; empacotar outro build faria o motor ser recusado em execução.
    $expected = '626B196D799606CD4250B7B29E04228692AB70CF56A5D1BBB56D748C8219F0EB'
    $actual = (Get-FileHash -LiteralPath $engineBinary -Algorithm SHA256).Hash
    if ($actual -ne $expected) { throw "Motor com hash inesperado: $actual. O pacote recusaria este binário." }
    Copy-Item -LiteralPath $engineBinary -Destination (Join-Path $publish 'Lossless.dll') -Force
    Write-Host "Motor incluído no pacote: $actual" -ForegroundColor Green
} else {
    Write-Warning 'Motor não encontrado: o pacote sairá sem escalonamento. Coloque Lossless.dll em artifacts/private/engine-staging.'
}

# PresentMon: o medidor de FPS da pagina Medições depende dele. Sem isso o botão de captura não
# funciona para o cliente, porque a ferramenta não vem do sistema.
$presentMonSource = Join-Path $workspace 'artifacts\private\presentmon\PresentMon.exe'
if (-not (Test-Path -LiteralPath $presentMonSource)) {
    $gdzPresentMon = Join-Path $env:LOCALAPPDATA 'Programs\GDZ Optimizer\resources\app.asar.unpacked\bin\PresentMon\PresentMon.exe'
    if (Test-Path -LiteralPath $gdzPresentMon) { $presentMonSource = $gdzPresentMon }
}
$hasPresentMon = Test-Path -LiteralPath $presentMonSource
if ($hasPresentMon) {
    Copy-Item -LiteralPath $presentMonSource -Destination (Join-Path $publish 'PresentMon.exe') -Force
    Write-Host "PresentMon incluído no pacote: $((Get-FileHash -LiteralPath $presentMonSource -Algorithm SHA256).Hash)" -ForegroundColor Green
} else {
    Write-Warning 'PresentMon não encontrado: a captura de FPS do VL não funcionará no pacote final.'
}

Copy-Item -LiteralPath $exe -Destination (Join-Path $release 'VL-Optimizer-0.4.0.exe') -Force

# O zip portátil e o instalador também precisam do motor, senão o cliente fica sem escalonamento.
$engineInPublish = Join-Path $publish 'Lossless.dll'
$hasEngine = Test-Path -LiteralPath $engineInPublish
$zip = Join-Path $release 'VL-Optimizer-0.4.0-portatil.zip'
$zipItems = @($exe, (Join-Path $publish 'LEIA-ME.txt'))
if ($hasEngine) { $zipItems += $engineInPublish }
if ($hasPresentMon) { $zipItems += (Join-Path $publish 'PresentMon.exe') }
Compress-Archive -LiteralPath $zipItems -DestinationPath $zip -Force
if (-not $SkipInstaller) {
    $stage = Join-Path $workspace '.tools\pacote-instalação'
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    Copy-Item -LiteralPath $exe -Destination (Join-Path $stage 'VLOptimizer.exe') -Force
    if ($hasEngine) { Copy-Item -LiteralPath $engineInPublish -Destination (Join-Path $stage 'Lossless.dll') -Force }
    if ($hasPresentMon) { Copy-Item -LiteralPath (Join-Path $publish 'PresentMon.exe') -Destination (Join-Path $stage 'PresentMon.exe') -Force }
    foreach ($name in @('Install.ps1','Uninstall.ps1','LEIA-ME.txt')) { Copy-Item -LiteralPath (Join-Path $workspace "installer\$name") -Destination (Join-Path $stage $name) -Force }
    $payloadFiles = @('VLOptimizer.exe','Uninstall.ps1','LEIA-ME.txt')
    if ($hasEngine) { $payloadFiles += 'Lossless.dll' }
    if ($hasPresentMon) { $payloadFiles += 'PresentMon.exe' }
    $hashes = [ordered]@{}
    foreach ($name in $payloadFiles) { $hashes[$name] = (Get-FileHash -LiteralPath (Join-Path $stage $name) -Algorithm SHA256).Hash }
    $hashes | ConvertTo-Json | Set-Content -Encoding utf8 (Join-Path $stage 'payload.json')
    $installer = Join-Path $release 'Instalar-VL-Optimizer-0.4.0.exe'
    $sed = Join-Path $workspace '.tools\installer.sed'
    $configuration = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=0
HideExtractAnimation=0
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=%InstallPrompt%
DisplayLicense=%DisplayLicense%
FinishMessage=%FinishMessage%
TargetName=%TargetName%
FriendlyName=%FriendlyName%
AppLaunched=%AppLaunched%
PostInstallCmd=%PostInstallCmd%
AdminQuietInstCmd=%AdminQuietInstCmd%
UserQuietInstCmd=%UserQuietInstCmd%
SourceFiles=SourceFiles
[Strings]
InstallPrompt=Instalar VL Optimizer no seu usuario? Nenhuma otimizacao sera aplicada.
DisplayLicense=
FinishMessage=
TargetName=$installer
FriendlyName=VL Optimizer 0.4.0
AppLaunched=powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File Install.ps1
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=
FILE0="VLOptimizer.exe"
FILE1="Install.ps1"
FILE2="Uninstall.ps1"
FILE3="LEIA-ME.txt"
FILE4="payload.json"
FILE5="Lossless.dll"
FILE6="PresentMon.exe"
[SourceFiles]
SourceFiles0=$stage\
[SourceFiles0]
%FILE0%=
%FILE1%=
%FILE2%=
%FILE3%=
%FILE4%=
%FILE5%=
%FILE6%=
"@
    # Sem o motor no pacote o IExpress falharia ao procurar o arquivo; remove-se a linha quando ausente.
    if (-not $hasEngine) { $configuration = $configuration -replace '(?m)^FILE5="Lossless\.dll"\r?\n', '' -replace '(?m)^%FILE5%=\r?\n', '' }
    if (-not $hasPresentMon) { $configuration = $configuration -replace '(?m)^FILE6="PresentMon\.exe"\r?\n', '' -replace '(?m)^%FILE6%=\r?\n', '' }
    ($configuration -replace '\r?\n', "`r`n") | Set-Content -LiteralPath $sed -Encoding unicode
    # IExpress interpreta incorretamente o caminho absoluto do SED com espaços,
    # mesmo com aspas. Use basename ASCII e cwd; caminhos no INI podem ter espaços.
    $process = Start-Process -FilePath (Join-Path $env:WINDIR 'System32\iexpress.exe') -ArgumentList @('/N','/Q','installer.sed') -WorkingDirectory (Split-Path $sed -Parent) -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(120000)) { throw 'IExpress não terminou em 120s. Verifique o processo de empacotamento.' }
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $installer)) { throw "IExpress falhou: $($process.ExitCode)" }
}
Get-ChildItem -LiteralPath $release -File | Where-Object Extension -in '.exe','.zip' | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
} | Set-Content -Encoding ascii (Join-Path $release 'SHA256SUMS.txt')
Get-ChildItem -LiteralPath $release -File | Select-Object Name,Length
