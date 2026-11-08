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
Copy-Item -LiteralPath $exe -Destination (Join-Path $release 'VL-Optimizer-0.4.0.exe') -Force
$zip = Join-Path $release 'VL-Optimizer-0.4.0-portatil.zip'
Compress-Archive -LiteralPath @($exe, (Join-Path $publish 'LEIA-ME.txt')) -DestinationPath $zip -Force
if (-not $SkipInstaller) {
    $stage = Join-Path $workspace '.tools\pacote-instalação'
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    Copy-Item -LiteralPath $exe -Destination (Join-Path $stage 'VLOptimizer.exe') -Force
    foreach ($name in @('Install.ps1','Uninstall.ps1','LEIA-ME.txt')) { Copy-Item -LiteralPath (Join-Path $workspace "installer\$name") -Destination (Join-Path $stage $name) -Force }
    $hashes = [ordered]@{}
    foreach ($name in @('VLOptimizer.exe','Uninstall.ps1','LEIA-ME.txt')) { $hashes[$name] = (Get-FileHash -LiteralPath (Join-Path $stage $name) -Algorithm SHA256).Hash }
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
[SourceFiles]
SourceFiles0=$stage\
[SourceFiles0]
%FILE0%=
%FILE1%=
%FILE2%=
%FILE3%=
%FILE4%=
"@
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
