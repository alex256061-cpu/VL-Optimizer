$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
function Assert-PlainPath([string]$Path) {
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'A instalação contém um link. Desinstalação automática interrompida.' }
        $parent = Split-Path $cursor -Parent
        if ($parent -eq $cursor) { break }
        $cursor = $parent
    }
}
try {
    $expected = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\VLOptimizer'))
    $target = [IO.Path]::GetFullPath($PSScriptRoot)
    if ($target -ne $expected) { throw 'Execute este desinstalador apenas na pasta instalada do VL Optimizer.' }
    $cursor = $target
    while ($cursor) {
        if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Pasta de instalação contém um link. Desinstalação automática interrompida.' }
        $parent = Split-Path $cursor -Parent
        if ($parent -eq $cursor) { break }
        $cursor = $parent
    }
    $answer = [Windows.Forms.MessageBox]::Show('Remover o VL Optimizer? O histórico será preservado. Desinstalar não desfaz ajustes; use o histórico do aplicativo antes de continuar.','Desinstalar VL Optimizer',[Windows.Forms.MessageBoxButtons]::YesNo,[Windows.Forms.MessageBoxIcon]::Question)
    if ($answer -ne [Windows.Forms.DialogResult]::Yes) { exit 0 }
    foreach ($running in (Get-Process VLOptimizer -ErrorAction SilentlyContinue)) { if ($running.Path -eq (Join-Path $target 'VLOptimizer.exe')) { throw 'Feche o VL Optimizer antes de desinstalar.' } }
    $menu = Join-Path ([Environment]::GetFolderPath('Programs')) 'VL Optimizer'
    $shortcut = Join-Path $menu 'VL Optimizer.lnk'
    foreach ($pathToCheck in @($menu, $shortcut) + @('VLOptimizer.exe','LEIA-ME.txt','Uninstall.ps1' | ForEach-Object { Join-Path $target $_ })) {
        Assert-PlainPath $pathToCheck
    }
    if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut -Force }
    if ((Test-Path -LiteralPath $menu) -and -not (Get-ChildItem -LiteralPath $menu -Force)) { Remove-Item -LiteralPath $menu }
    foreach ($name in @('VLOptimizer.exe','LEIA-ME.txt','Lossless.dll','PresentMon.exe')) {
        $file = Join-Path $target $name
        if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file -Force }
    }
    $uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\VLOptimizer'
    if (Test-Path -LiteralPath $uninstallKey) {
        Remove-Item -LiteralPath $uninstallKey -ErrorAction Stop
        if (Test-Path -LiteralPath $uninstallKey) { throw 'Não foi possível remover o registro de instalação. O desinstalador foi preservado para nova tentativa.' }
    }
    # O processo do PowerShell já carregou o script; remover somente este arquivo conhecido.
    Remove-Item -LiteralPath (Join-Path $target 'Uninstall.ps1') -Force
    if (-not (Get-ChildItem -LiteralPath $target -Force)) { Remove-Item -LiteralPath $target }
    [Windows.Forms.MessageBox]::Show('Aplicativo removido. O histórico local foi preservado.','VL Optimizer') | Out-Null
} catch {
    [Windows.Forms.MessageBox]::Show($_.Exception.Message,'Desinstalação não concluída',[Windows.Forms.MessageBoxButtons]::OK,[Windows.Forms.MessageBoxIcon]::Error) | Out-Null
    exit 1
}
