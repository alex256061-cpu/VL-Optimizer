$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
$workspace = Split-Path $PSScriptRoot -Parent
$release = Join-Path $workspace 'release'
New-Item -ItemType Directory -Force -Path $release | Out-Null
$files = @()
foreach ($folder in @('src','tests','docs','scripts','installer','.github')) {
    $files += Get-ChildItem -LiteralPath (Join-Path $workspace $folder) -File -Recurse | Where-Object { $_.FullName -notmatch '[\\/](bin|obj|__pycache__)[\\/]' -and $_.Extension -notin '.pyc','.pyo' }
}
foreach ($name in @('README.md','TESTAR.md','VLOptimizer.sln','Directory.Build.props','global.json','.gitignore','artifacts\test-results.json','artifacts\smoke-summary.json','artifacts\catalogo-0.4.0.json','artifacts\menu-functions-0.4.0.json','artifacts\release-validation-0.4.0.json','artifacts\VL-Optimizer-preview.png')) {
    $path = Join-Path $workspace $name
    if (Test-Path -LiteralPath $path) { $files += Get-Item -LiteralPath $path }
}
$destination = Join-Path $release 'VL-Optimizer-0.4.0-codigo-fonte.zip'
$stream = [IO.File]::Open($destination,[IO.FileMode]::Create,[IO.FileAccess]::Write,[IO.FileShare]::None)
$archive = New-Object IO.Compression.ZipArchive($stream,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files | Sort-Object FullName -Unique) {
        $prefix = [IO.Path]::GetFullPath($workspace) + [IO.Path]::DirectorySeparatorChar
        if (-not $file.FullName.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Arquivo fora do projeto' }
        $relative = $file.FullName.Substring($prefix.Length).Replace('\','/')
        if ($relative.StartsWith('../') -or [IO.Path]::IsPathRooted($relative)) { throw 'Arquivo fora do projeto' }
        $entry = $archive.CreateEntry($relative,[IO.Compression.CompressionLevel]::Optimal)
        $entryStream = $entry.Open(); $sourceStream = $file.OpenRead()
        try { $sourceStream.CopyTo($entryStream) } finally { $sourceStream.Dispose(); $entryStream.Dispose() }
    }
} finally { $archive.Dispose(); $stream.Dispose() }
Get-ChildItem -LiteralPath $release -File | Where-Object Extension -in '.exe','.zip' | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
} | Set-Content -Encoding ascii (Join-Path $release 'SHA256SUMS.txt')
Get-Item -LiteralPath $destination | Select-Object Name,Length
