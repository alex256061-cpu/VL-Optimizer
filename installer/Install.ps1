$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
function Assert-PlainPath([string]$Path) {
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'O destino contém um link de sistema de arquivos.' }
        }
        $parent = Split-Path $cursor -Parent
        if ($parent -eq $cursor) { break }
        $cursor = $parent
    }
}
$stage = $null
$installed = $false
$shortcutCreated = $false
$keyCreated = $false
$target = $null
$shortcutPath = $null
$menuCreated = $false
try {
    $localRoot = [Environment]::GetFolderPath('LocalApplicationData')
    $programs = [IO.Path]::GetFullPath((Join-Path $localRoot 'Programs'))
    $target = [IO.Path]::GetFullPath((Join-Path $programs 'VLOptimizer'))
    if ((Split-Path $target -Parent) -ne $programs) { throw 'Destino fora da pasta de programas do usuário.' }
    Assert-PlainPath $target
    if (Test-Path -LiteralPath $target) { throw 'VL Optimizer já possui uma pasta de instalação. Desinstale a versão anterior antes de instalar esta versão. O histórico será preservado.' }
    $uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\VLOptimizer'
    if (Test-Path -LiteralPath $uninstallKey) { throw 'Existe um registro de instalação anterior. Desinstale a versão anterior em Aplicativos do Windows.' }
    $menu = Join-Path ([Environment]::GetFolderPath('Programs')) 'VL Optimizer'
    $shortcutPath = Join-Path $menu 'VL Optimizer.lnk'
    Assert-PlainPath $shortcutPath
    if (Test-Path -LiteralPath $shortcutPath) { throw 'Já existe um atalho do VL Optimizer. Remova a instalação anterior primeiro.' }
    $files = @('VLOptimizer.exe','Uninstall.ps1','LEIA-ME.txt')
    $manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'payload.json') -Raw | ConvertFrom-Json
    foreach ($name in $files) {
        $source = Join-Path $PSScriptRoot $name
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Arquivo ausente no pacote: $name" }
        $expectedHash = $manifest.PSObject.Properties[$name].Value
        if ($expectedHash -notmatch '^[A-Fa-f0-9]{64}$' -or (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $expectedHash) { throw "Verificação de integridade falhou: $name" }
    }
    New-Item -ItemType Directory -Force -Path $programs | Out-Null
    $stage = Join-Path $programs ('.VLOptimizer-install-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage | Out-Null
    foreach ($name in $files) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $stage $name) }
    Assert-PlainPath $target
    [IO.Directory]::Move($stage, $target)
    $stage = $null
    $installed = $true
    Assert-PlainPath $shortcutPath
    if (-not (Test-Path -LiteralPath $menu)) { New-Item -ItemType Directory -Path $menu | Out-Null; $menuCreated = $true }
    $wsh = New-Object -ComObject WScript.Shell
    $shortcut = $wsh.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = Join-Path $target 'VLOptimizer.exe'
    $shortcut.WorkingDirectory = $target
    $shortcut.Description = 'Diagnóstico local e ajustes verificáveis para Windows'
    $shortcut.Save()
    $shortcutCreated = $true
    New-Item -Path $uninstallKey | Out-Null
    $keyCreated = $true
    $uninstallCommand = 'powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + (Join-Path $target 'Uninstall.ps1') + '"'
    $values = @{DisplayName='VL Optimizer';DisplayVersion='0.3.0';Publisher='VL';InstallLocation=$target;DisplayIcon=(Join-Path $target 'VLOptimizer.exe');UninstallString=$uninstallCommand}
    foreach ($pair in $values.GetEnumerator()) { New-ItemProperty -Path $uninstallKey -Name $pair.Key -Value $pair.Value -PropertyType String | Out-Null }
    New-ItemProperty -Path $uninstallKey -Name NoModify -Value 1 -PropertyType DWord | Out-Null
    New-ItemProperty -Path $uninstallKey -Name NoRepair -Value 1 -PropertyType DWord | Out-Null
    [Windows.Forms.MessageBox]::Show('VL Optimizer instalado no seu usuário. Abra pelo menu Iniciar. Nenhuma otimização foi aplicada.','VL Optimizer',[Windows.Forms.MessageBoxButtons]::OK,[Windows.Forms.MessageBoxIcon]::Information) | Out-Null
} catch {
    $reason = $_.Exception.Message
    # Rollback limitado aos arquivos conhecidos criados por esta execução; sem remoção recursiva.
    try {
        if ($keyCreated) { Remove-Item -LiteralPath $uninstallKey }
        if ($shortcutCreated) { Assert-PlainPath $shortcutPath; Remove-Item -LiteralPath $shortcutPath -Force }
        if ($menuCreated -and -not (Get-ChildItem -LiteralPath $menu -Force)) { Remove-Item -LiteralPath $menu }
        $rollback = if ($installed) { $target } else { $stage }
        if ($rollback -and (Test-Path -LiteralPath $rollback)) {
            Assert-PlainPath $rollback
            foreach ($name in @('VLOptimizer.exe','Uninstall.ps1','LEIA-ME.txt')) {
                $file = Join-Path $rollback $name
                Assert-PlainPath $file
                if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force }
            }
            if (-not (Get-ChildItem -LiteralPath $rollback -Force)) { Remove-Item -LiteralPath $rollback }
        }
    } catch { $reason += ' A limpeza parcial não foi concluída: ' + $_.Exception.Message }
    [Windows.Forms.MessageBox]::Show($reason,'Instalação não concluída',[Windows.Forms.MessageBoxButtons]::OK,[Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    exit 1
}
