using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace VLOptimizer.Core;

public sealed record OptiScalerFile(string RelativePath, long Bytes, string? BeforeSha256, string PlannedSha256);
public sealed record OptiScalerPreview(Guid Id, string GameExecutable, string PackageDirectory, string ProxyName,
    IReadOnlyList<OptiScalerFile> Files, IReadOnlyList<string> Warnings, bool CanApply, string? BlockReason, DateTimeOffset CreatedAt);
public sealed record OptiScalerResult(bool Success, bool Partial, bool Conflict, string Message, Guid? BackupId);
public sealed record OptiScalerBackup(Guid Id, string GameExecutable, DateTimeOffset CreatedAt, bool Restored, int Files);

/// <summary>Deploys an explicitly selected local package with a durable per-file transaction.
/// No package scripts, executables or target libraries are executed by this service.</summary>
public sealed class OptiScalerService
{
    public static readonly IReadOnlyList<string> ProxyNames = Array.AsReadOnly(new[] { "dxgi.dll", "winmm.dll", "version.dll", "dbghelp.dll", "d3d12.dll", "wininet.dll", "winhttp.dll" });
    private readonly string directory;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<OptiScalerPreview, Plan> issued = new(ReferenceEqualityComparer.Instance);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public OptiScalerService(string dataDirectory) => directory = Path.Combine(Path.GetFullPath(dataDirectory), "optiscaler");

    public async Task<OptiScalerPreview> PreviewInstallAsync(string gameExecutable, string packageDirectory, string proxyName = "dxgi.dll", CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                var id = Guid.NewGuid();
                try
                {
                    string game = LocalPath(gameExecutable), package = LocalPath(packageDirectory);
                    if (!File.Exists(game) || !game.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Escolha o executável real do jogo.");
                    if (!Directory.Exists(package) || !ProxyNames.Contains(proxyName, StringComparer.Ordinal)) throw new ArgumentException("Pacote ou nome de DLL inválido.");
                    EnsureNoLinks(game); EnsureNoLinks(package);
                    string target = Path.GetDirectoryName(game)!;
                    if (Within(package, target) || Within(target, package)) throw new ArgumentException("O pacote deve ficar em uma pasta separada do jogo.");
                    if (IsSystemLocation(target)) throw new ArgumentException("Escolha uma pasta de jogo, fora dos diretórios do Windows.");
                    EnsureGameStopped(game);
                    EnsureNoAntiCheat(target);
                    if (!File.Exists(Path.Combine(package, "OptiScaler.dll")) || !File.Exists(Path.Combine(package, "OptiScaler.ini")))
                        throw new FileNotFoundException("Selecione a pasta descompactada que contém OptiScaler.dll e OptiScaler.ini.");
                    EnsureX64Pe(Path.Combine(package, "OptiScaler.dll")); EnsureX64Pe(game);
                    if (LoadManifests().Any(x => !x.Restored && string.Equals(x.GameExecutable, game, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException("Existe uma instalação VL com backup para este jogo. Restaure antes de instalar outro pacote.");
                    var entries = EnumeratePackage(package, token);
                    var files = new List<OptiScalerFile>(); var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string input in entries)
                    {
                        token.ThrowIfCancellationRequested();
                        string relative = Path.GetRelativePath(package, input);
                        if (relative.Equals("OptiScaler.dll", StringComparison.OrdinalIgnoreCase)) relative = proxyName;
                        if (!sources.TryAdd(relative, input)) throw new InvalidDataException("O pacote contém destinos duplicados.");
                        string output = Destination(target, relative); EnsureNoLinks(output);
                        if (File.Exists(output) && new FileInfo(output).Length > 128L * 1024 * 1024) throw new InvalidDataException("Arquivo anterior excede o limite de backup.");
                        files.Add(new(relative, new FileInfo(input).Length, File.Exists(output) ? Hash(output) : null, Hash(input)));
                    }
                    if (files.Count < 2 || files.Sum(x => x.Bytes) > 1024L * 1024 * 1024) throw new InvalidDataException("Pacote vazio ou maior que 1 GiB.");
                    var warnings = new List<string> { "A biblioteca será carregada pelo jogo quando você o abrir. O VL não verifica compatibilidade gráfica do pacote escolhido.", "Use somente em jogos compatíveis sem proteção anticheat. O pacote e suas opções INI serão preservados; scripts de instalação não são executados." };
                    int replacements = files.Count(x => x.BeforeSha256 is not null);
                    if (replacements > 0) warnings.Add($"{replacements} arquivo(s) existente(s) serão substituídos, com cópia anterior preservada para restauração.");
                    var preview = new OptiScalerPreview(id, game, package, proxyName, files, warnings, true, null, DateTimeOffset.UtcNow);
                    foreach (var old in issued.Keys.Where(x => DateTimeOffset.UtcNow - x.CreatedAt > TimeSpan.FromMinutes(10)).ToArray()) issued.Remove(old);
                    issued[preview] = new(sources, Hash(game));
                    return preview;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { return new(id, gameExecutable, packageDirectory, proxyName, [], [], false, ex.Message, DateTimeOffset.UtcNow); }
            }, token).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public async Task<OptiScalerResult> ApplyAsync(OptiScalerPreview preview, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!issued.Remove(preview, out var plan) || !preview.CanApply || DateTimeOffset.UtcNow - preview.CreatedAt > TimeSpan.FromMinutes(10))
                return new(false, false, true, "A prévia expirou, foi utilizada ou não pertence a esta sessão.", null);
            using var lease = Acquire();
            return await Task.Run(() => Apply(preview, plan, token), CancellationToken.None).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    private OptiScalerResult Apply(OptiScalerPreview preview, Plan plan, CancellationToken token)
    {
        string game = LocalPath(preview.GameExecutable), target = Path.GetDirectoryName(game)!;
        EnsureNoLinks(game); EnsureGameStopped(game); EnsureNoAntiCheat(target);
        if (Hash(game) != plan.GameHash) return new(false, false, true, "O executável do jogo mudou depois da prévia.", null);
        foreach (var file in preview.Files)
        {
            token.ThrowIfCancellationRequested();
            string source = plan.Sources[file.RelativePath], output = Destination(target, file.RelativePath);
            EnsureNoLinks(source); EnsureNoLinks(output);
            if (Hash(source) != file.PlannedSha256 || CurrentHash(output) != file.BeforeSha256)
                return new(false, false, true, "Pacote ou arquivo do jogo mudou após a prévia. Nada foi instalado.", null);
        }
        string backup = Path.Combine(directory, preview.Id.ToString("N")); Directory.CreateDirectory(backup);
        var manifest = new Manifest { Id = preview.Id, GameExecutable = game, GameHash = plan.GameHash, CreatedAt = DateTimeOffset.UtcNow };
        for (int i = 0; i < preview.Files.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var file = preview.Files[i]; string? name = null;
            if (file.BeforeSha256 is not null)
            {
                name = i.ToString("D4") + ".backup";
                File.Copy(Destination(target, file.RelativePath), Path.Combine(backup, name));
                if (Hash(Path.Combine(backup, name)) != file.BeforeSha256) throw new IOException("A cópia de backup não corresponde à prévia.");
            }
            manifest.Files.Add(new(file.RelativePath, file.BeforeSha256, file.PlannedSha256, name));
        }
        SaveManifest(backup, manifest); // durable BEFORE the first game-directory write
        int written = 0; bool mutationAttempted = false;
        foreach (var file in manifest.Files)
        {
            if (token.IsCancellationRequested) return new(false, mutationAttempted, false, $"Cancelado: {written} arquivo(s) gravado(s); o backup permite restaurar.", manifest.Id);
            try
            {
                string output = Destination(target, file.RelativePath), source = plan.Sources[file.RelativePath];
                EnsureNoLinks(source); EnsureNoLinks(output);
                if (CurrentHash(output) != file.BeforeSha256 || Hash(source) != file.PlannedSha256) throw new IOException("Arquivo mudou durante a instalação.");
                // Read-and-hash the same bytes that will be written, avoiding a mutable-source copy.
                byte[] bytes = File.ReadAllBytes(source);
                if (Hash(bytes) != file.PlannedSha256) throw new IOException("O pacote mudou durante a leitura.");
                mutationAttempted = true; WriteAtomic(output, bytes);
                if (Hash(output) != file.PlannedSha256) throw new IOException("Conteúdo instalado não confirmado.");
                written++;
            }
            catch (Exception ex) { return new(false, mutationAttempted, false, $"Instalação interrompida após {written} arquivo(s) verificado(s): {ex.Message}. Pode haver alteração não confirmada; backup preservado.", manifest.Id); }
        }
        manifest.Complete = true; SaveManifest(backup, manifest);
        return new(true, false, false, $"{written} arquivo(s) instalado(s) e verificado(s). Abra o jogo e configure o OptiScaler conforme sua documentação; compatibilidade e ganho não foram presumidos.", manifest.Id);
    }

    public Task<IReadOnlyList<OptiScalerBackup>> GetBackupsAsync(CancellationToken token = default) => Task.Run<IReadOnlyList<OptiScalerBackup>>(() =>
    { token.ThrowIfCancellationRequested(); return LoadManifests().Select(x => new OptiScalerBackup(x.Id, x.GameExecutable, x.CreatedAt, x.Restored, x.Files.Count)).OrderByDescending(x => x.CreatedAt).ToArray(); }, token);

    public async Task<OptiScalerResult> RestoreAsync(Guid id, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var lease = Acquire();
            var manifest = LoadManifests().SingleOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException("Backup não encontrado.");
            if (manifest.Restored) return new(true, false, false, "Esta instalação já foi restaurada.", id);
            string game = LocalPath(manifest.GameExecutable), target = Path.GetDirectoryName(game)!, backup = Path.Combine(directory, id.ToString("N"));
            if (IsSystemLocation(target)) throw new InvalidDataException("O backup não pode apontar para o diretório do Windows.");
            EnsureNoLinks(game); EnsureGameStopped(game);
            if (!File.Exists(game) || Hash(game) != manifest.GameHash) return new(false, false, true, "O jogo foi removido ou atualizado. Preserve o backup para revisão manual.", id);
            foreach (var file in manifest.Files)
            {
                token.ThrowIfCancellationRequested(); string output = Destination(target, file.RelativePath); EnsureNoLinks(output);
                string? current = CurrentHash(output);
                if (current != file.PlannedSha256 && current != file.BeforeSha256) return new(false, false, true, "Um arquivo instalado foi alterado depois. A restauração preservou essa mudança: " + file.RelativePath, id);
                if (file.BackupName is not null)
                {
                    if (Path.GetFileName(file.BackupName) != file.BackupName || file.BeforeSha256 is null) throw new InvalidDataException("Referência de backup inválida.");
                    string original = Path.Combine(backup, file.BackupName); EnsureNoLinks(original);
                    if (Hash(original) != file.BeforeSha256) throw new InvalidDataException("Backup alterado ou incompleto.");
                }
            }
            int restored = 0; bool mutationAttempted = false;
            foreach (var file in manifest.Files.AsEnumerable().Reverse())
            {
                if (token.IsCancellationRequested) return new(false, mutationAttempted, false, "Restauração interrompida entre arquivos; backup preservado.", id);
                try
                {
                    string output = Destination(target, file.RelativePath); EnsureNoLinks(output);
                    string? current = CurrentHash(output);
                    if (current == file.BeforeSha256) continue;
                    if (current != file.PlannedSha256) return new(false, mutationAttempted, true, "Arquivo mudou durante a restauração: " + file.RelativePath, id);
                    if (file.BeforeSha256 is null) { mutationAttempted = true; File.Delete(output); }
                    else
                    {
                        string original = Path.Combine(backup, file.BackupName!); byte[] bytes = File.ReadAllBytes(original);
                        if (Hash(bytes) != file.BeforeSha256) throw new InvalidDataException("Backup mudou durante leitura.");
                        mutationAttempted = true; WriteAtomic(output, bytes);
                    }
                    if (CurrentHash(output) != file.BeforeSha256) throw new IOException("Restauração não confirmada.");
                    restored++;
                }
                catch (Exception ex) { return new(false, mutationAttempted, false, "Restauração interrompida; pode haver alteração não confirmada: " + ex.Message, id); }
            }
            manifest.Restored = true; SaveManifest(backup, manifest);
            return new(true, false, false, "Arquivos originais restaurados e arquivos criados removidos, com verificação de conteúdo.", id);
        }
        finally { gate.Release(); }
    }

    private List<Manifest> LoadManifests()
    {
        if (!Directory.Exists(directory)) return [];
        var manifests = new List<Manifest>();
        foreach (string folder in Directory.EnumerateDirectories(directory).Take(1001))
        {
            EnsureNoLinks(folder);
            if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out _)) continue;
            string path = Path.Combine(folder, "manifest.json");
            if (!File.Exists(path)) continue;
            if (manifests.Count >= 1000) throw new InvalidDataException("Limite de instalações atingido.");
            EnsureNoLinks(path);
            if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("Manifesto excede o limite.");
            var value = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path)) ?? throw new InvalidDataException("Manifesto inválido.");
            if (value.Version != 1 || value.Id == Guid.Empty || value.Files is null || value.Files.Count is < 2 or > 512 || !ValidHash(value.GameHash) || Path.GetFileName(Path.GetDirectoryName(path)) != value.Id.ToString("N")) throw new InvalidDataException("Manifesto inconsistente.");
            if (value.Files.Any(x => x is null)) throw new InvalidDataException("Arquivo nulo no manifesto.");
            if (value.Files.Select(x => x.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != value.Files.Count) throw new InvalidDataException("Alvos duplicados no manifesto.");
            if (value.Files.Where(x => x.BackupName is not null).Select(x => x.BackupName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != value.Files.Count(x => x.BackupName is not null)) throw new InvalidDataException("Referência de backup duplicada.");
            foreach (var file in value.Files)
            {
                if (string.IsNullOrEmpty(file.RelativePath) || !ValidHash(file.PlannedSha256) || (file.BeforeSha256 is null) != (file.BackupName is null)
                    || file.BeforeSha256 is not null && !ValidHash(file.BeforeSha256)
                    || file.BackupName is not null && (!System.Text.RegularExpressions.Regex.IsMatch(file.BackupName, @"\A\d{4}\.backup\z") || Path.GetFileName(file.BackupName) != file.BackupName))
                    throw new InvalidDataException("Estado anterior, hash ou referência de backup inválidos.");
                _ = Destination(Path.GetDirectoryName(LocalPath(value.GameExecutable))!, file.RelativePath);
                if (!AllowedExtension(file.RelativePath)) throw new InvalidDataException("Tipo de arquivo fora do contrato.");
            }
            manifests.Add(value);
        }
        return manifests;
    }
    private static bool ValidHash(string? hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
    private FileStream Acquire() { EnsureNoLinks(directory); Directory.CreateDirectory(directory); EnsureNoLinks(directory); string file = Path.Combine(directory, "operation.lock"); EnsureNoLinks(file); return new(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
    private static void SaveManifest(string backup, Manifest manifest) => WriteAtomic(Path.Combine(backup, "manifest.json"), JsonSerializer.SerializeToUtf8Bytes(manifest, Options));
    private static IEnumerable<string> EnumeratePackage(string root, CancellationToken token)
    {
        var pending = new Stack<string>(); pending.Push(root); int count = 0;
        while (pending.TryPop(out var folder))
        {
            token.ThrowIfCancellationRequested(); EnsureNoLinks(folder);
            foreach (string file in Directory.EnumerateFiles(folder))
            {
                EnsureNoLinks(file); if (!AllowedExtension(file)) continue;
                if (++count > 512 || new FileInfo(file).Length > 128L * 1024 * 1024) throw new InvalidDataException("Pacote excede os limites de arquivos/tamanho.");
                yield return file;
            }
            foreach (string child in Directory.EnumerateDirectories(folder))
            { if (Path.GetRelativePath(root, child).Split(Path.DirectorySeparatorChar).Length > 5) throw new InvalidDataException("Pacote com pastas excessivamente profundas."); pending.Push(child); }
        }
    }
    private static bool AllowedExtension(string path) => new[] { ".dll", ".ini", ".json", ".bin", ".dat" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    private static string LocalPath(string path) { if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal)) throw new ArgumentException("Use um caminho local absoluto."); return Path.GetFullPath(path); }
    private static bool Within(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static string Destination(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(x => x is "" or "." or ".." || x.Contains(':'))) throw new InvalidDataException("Caminho relativo fora do contrato.");
        string full = Path.GetFullPath(Path.Combine(root, relative)); if (!Within(full, root)) throw new InvalidDataException("Destino fora da pasta do jogo."); return full;
    }
    private static bool IsSystemLocation(string path) => Within(path, Environment.GetFolderPath(Environment.SpecialFolder.Windows)) || Within(path, Environment.SystemDirectory);
    internal static void EnsureNoLinks(string path)
    {
        for (string? p = Path.GetFullPath(path); p is not null; p = Path.GetDirectoryName(p))
        {
            if (!File.Exists(p) && !Directory.Exists(p)) continue;
            var attributes = File.GetAttributes(p);
            if (((uint)attributes & (0x400u | 0x1000u | 0x40000u | 0x400000u)) != 0) throw new IOException("Links e arquivos cloud não são processados: " + p);
        }
    }
    private static void EnsureGameStopped(string game)
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(game)))
        {
            using (process)
            {
                try { if (process.MainModule?.FileName is { } path && !path.Equals(game, StringComparison.OrdinalIgnoreCase)) continue; }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                throw new InvalidOperationException("Feche o jogo antes de instalar ou restaurar bibliotecas.");
            }
        }
    }
    internal static string ValidateConfigurationGame(string executable)
    {
        string game = LocalPath(executable), root = Path.GetDirectoryName(game)!;
        if (IsSystemLocation(root) || !Path.GetExtension(game).Equals(".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Escolha o executável local do jogo.");
        EnsureNoLinks(game); EnsureX64Pe(game); EnsureGameStopped(game); EnsureNoAntiCheat(root); return game;
    }
    private static void EnsureNoAntiCheat(string directory)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            string name = Path.GetFileName(entry);
            if (name.Contains("anticheat", StringComparison.OrdinalIgnoreCase) || name.Contains("BattlEye", StringComparison.OrdinalIgnoreCase) || name.StartsWith("start_protected_game", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Proteção anticheat identificada. A instalação de bibliotecas fica indisponível neste diretório.");
        }
    }
    private static void EnsureX64Pe(string path)
    {
        using var stream = File.OpenRead(path); using var reader = new BinaryReader(stream);
        if (stream.Length < 0x40 || reader.ReadUInt16() != 0x5a4d) throw new InvalidDataException("Arquivo não é PE Windows.");
        stream.Position = 0x3c; int pe = reader.ReadInt32(); if (pe < 0x40 || pe > stream.Length - 24) throw new InvalidDataException("Cabeçalho PE inválido.");
        stream.Position = pe; if (reader.ReadUInt32() != 0x4550 || reader.ReadUInt16() != 0x8664) throw new InvalidDataException("Esta integração exige jogo e pacote Windows x64.");
    }
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string? CurrentHash(string path) => File.Exists(path) ? Hash(path) : null;
    private static void WriteAtomic(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); EnsureNoLinks(path);
        string temporary = path + ".vl-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16384, FileOptions.WriteThrough)) { stream.Write(bytes); stream.Flush(true); }
            EnsureNoLinks(path); File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private sealed record Plan(IReadOnlyDictionary<string, string> Sources, string GameHash);
    public sealed class Manifest
    {
        public int Version { get; set; } = 1; public Guid Id { get; set; } public string GameExecutable { get; set; } = "";
        public string GameHash { get; set; } = ""; public DateTimeOffset CreatedAt { get; set; } public bool Complete { get; set; } public bool Restored { get; set; }
        public List<ManifestFile> Files { get; set; } = [];
    }
    public sealed record ManifestFile(string RelativePath, string? BeforeSha256, string PlannedSha256, string? BackupName);
}
