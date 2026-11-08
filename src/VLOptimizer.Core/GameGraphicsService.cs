using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace VLOptimizer.Core;

public sealed record GameGraphicsPreview(Guid Id, string Executable, string ExecutableHash, ConfigurationValue Before, ConfigurationValue Desired, DateTimeOffset CreatedAt);
public sealed record GameGraphicsBackup(Guid Id, string Executable, string ExecutableHash, ConfigurationValue Before, ConfigurationValue Applied, DateTimeOffset CreatedAt, bool Verified = false, bool Restored = false);
public sealed record GameGraphicsResult(OperationStatus Status, string Message, GameGraphicsBackup? Backup);

/// <summary>Per-executable Windows GPU preference. Preserves unrelated preference tokens and exact original state.</summary>
public sealed class GameGraphicsService
{
    private const string RegistryPath = @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences";
    private readonly string directory;
    private readonly IConfigurationSystemAccess system;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<GameGraphicsPreview, DateTimeOffset> issued = new(ReferenceEqualityComparer.Instance);
    public GameGraphicsService(string dataDirectory, IConfigurationSystemAccess? system = null)
    { directory = Path.Combine(Path.GetFullPath(dataDirectory), "game-graphics"); this.system = system ?? new ConfigurationWindowsAccess(); }

    public async Task<GameGraphicsPreview> PreviewAsync(string executable, CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            RequirePlatform(); string path = ValidateExecutable(executable); token.ThrowIfCancellationRequested();
            string hash = await HashAsync(path, token).ConfigureAwait(false); var before = system.Read(Target(path));
            if (before.Exists && before.Kind != (int)RegistryValueKind.String) throw new InvalidDataException("A preferência existente não é texto. Preserve-a e revise no Windows.");
            string text = before.Exists ? JsonSerializer.Deserialize<string>(before.Json) ?? throw new InvalidDataException("Preferência inválida.") : "";
            string desired = MergeHighPerformance(text);
            if (before.Exists && text == desired) throw new InvalidOperationException("A preferência de alto desempenho já está configurada para este executável.");
            var preview = new GameGraphicsPreview(Guid.NewGuid(), path, hash, before, ConfigurationValue.String(desired), DateTimeOffset.UtcNow);
            foreach (var old in issued.Where(x => DateTimeOffset.UtcNow - x.Value > TimeSpan.FromMinutes(10)).Select(x => x.Key).ToArray()) issued.Remove(old);
            issued[preview] = preview.CreatedAt; return preview;
        }
        finally { gate.Release(); }
    }

    public async Task<GameGraphicsResult> ApplyAsync(GameGraphicsPreview preview, CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!issued.Remove(preview, out var when) || DateTimeOffset.UtcNow - when > TimeSpan.FromMinutes(10))
                return new(OperationStatus.Conflict, "A prévia expirou, não pertence a esta execução ou já foi utilizada.", null);
            RequirePlatform(); using var journalLock = Acquire(); var records = Load(); string path = ValidateExecutable(preview.Executable);
            if (await HashAsync(path, token).ConfigureAwait(false) != preview.ExecutableHash || !Equal(system.Read(Target(path)), preview.Before))
                return new(OperationStatus.Conflict, "O executável ou sua preferência mudou depois da prévia. Consulte novamente.", null);
            token.ThrowIfCancellationRequested();
            var backup = new GameGraphicsBackup(preview.Id, path, preview.ExecutableHash, preview.Before, preview.Desired, DateTimeOffset.UtcNow);
            records.Add(backup); Save(records); // Durable original before the first registry write.
            try
            {
                system.Write(Target(path), preview.Desired);
                if (!Equal(system.Read(Target(path)), preview.Desired)) throw new IOException("A releitura não confirmou a preferência solicitada.");
                backup = backup with { Verified = true }; records[^1] = backup; Save(records);
                return new(OperationStatus.Completed, "Preferência gravada e verificada. Reinicie o jogo; Windows, driver e jogo determinam a GPU efetivamente usada.", backup);
            }
            catch (Exception ex) { return new(OperationStatus.Unverified, "Aplicação não confirmada. O estado original está registrado para recuperação: " + ex.Message, backup); }
        }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<GameGraphicsBackup>> GetBackupsAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { using var journalLock = Acquire(); return Load().OrderByDescending(x => x.CreatedAt).ToArray(); }
        finally { gate.Release(); }
    }

    public async Task<GameGraphicsResult> RestoreAsync(Guid id, CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            RequirePlatform(); using var journalLock = Acquire(); var records = Load(); int index = records.FindIndex(x => x.Id == id);
            if (index < 0) return new(OperationStatus.Failed, "Backup não encontrado.", null);
            var backup = records[index]; if (backup.Restored) return new(OperationStatus.Skipped, "Preferência já restaurada.", backup);
            if (records.Skip(index + 1).Any(x => !x.Restored && x.Executable.Equals(backup.Executable, StringComparison.OrdinalIgnoreCase)))
                return new(OperationStatus.Conflict, "Restaure primeiro a preferência mais recente deste executável.", backup);
            string path = ValidateExecutable(backup.Executable);
            if (await HashAsync(path, token).ConfigureAwait(false) != backup.ExecutableHash) return new(OperationStatus.Conflict, "O executável foi substituído. O estado atual foi preservado.", backup);
            var current = system.Read(Target(path));
            if (!Equal(current, backup.Before) && !Equal(current, backup.Applied)) return new(OperationStatus.Conflict, "Outro programa alterou a preferência. O estado atual foi preservado.", backup);
            token.ThrowIfCancellationRequested();
            if (!Equal(current, backup.Before)) system.Write(Target(path), backup.Before);
            if (!Equal(system.Read(Target(path)), backup.Before)) return new(OperationStatus.Unverified, "Não foi possível confirmar a restauração.", backup);
            backup = backup with { Restored = true }; records[index] = backup; Save(records);
            return new(OperationStatus.Reverted, "Valor anterior restaurado e verificado. Reinicie o jogo para renovar a seleção gráfica.", backup);
        }
        finally { gate.Release(); }
    }

    public static string MergeHighPerformance(string value)
    {
        if (value.Length > 32767) throw new InvalidDataException("Preferência ultrapassa o limite permitido.");
        // Preserve each unrelated token, including its whitespace and ordering.
        string rest = string.Join(';', value.Split(';').Where(x => x.Length > 0 && !x.TrimStart().StartsWith("GpuPreference=", StringComparison.OrdinalIgnoreCase)));
        return rest.Length > 0 ? rest + ";GpuPreference=2;" : "GpuPreference=2;";
    }
    private void RequirePlatform() { if (!system.InspectEnvironment().IsSupportedOS) throw new PlatformNotSupportedException("Disponível no Windows 10/11 x64 compatível."); }
    private static ConfigurationTarget Target(string path) => new(ConfigurationTargetKind.Registry, RegistryPath, path);
    private static bool Equal(ConfigurationValue a, ConfigurationValue b) => a.Exists == b.Exists && (!a.Exists || a.Kind == b.Kind && a.Json == b.Json);
    private static string ValidateExecutable(string executable)
    {
        if (!Path.IsPathFullyQualified(executable) || executable.StartsWith(@"\\", StringComparison.Ordinal)) throw new ArgumentException("Escolha um executável em volume local.");
        string path = Path.GetFullPath(executable);
        if (!Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Selecione um executável .exe de jogo.");
        for (string? entry = path; entry is not null; entry = Path.GetDirectoryName(entry))
            if ((File.GetAttributes(entry) & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0) throw new IOException("Links e arquivos offline não são aceitos.");
        using var stream = File.OpenRead(path); using var reader = new BinaryReader(stream);
        if (stream.Length < 64 || stream.Length > 512L * 1024 * 1024 || reader.ReadUInt16() != 0x5a4d) throw new InvalidDataException("Executável PE inválido ou acima de 512 MiB.");
        stream.Position = 0x3c; int pe = reader.ReadInt32(); if (pe < 64 || pe > stream.Length - 24) throw new InvalidDataException("Cabeçalho PE inválido.");
        stream.Position = pe; if (reader.ReadUInt32() != 0x4550) throw new InvalidDataException("Cabeçalho PE inválido.");
        return path;
    }
    private static async Task<string> HashAsync(string path, CancellationToken token)
    { using var stream = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)); }
    private FileStream Acquire()
    { EnsureStorage(directory); Directory.CreateDirectory(directory); EnsureStorage(directory); string file = Path.Combine(directory, "journal.lock"); EnsureStorage(file); return new(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
    private static void EnsureStorage(string path)
    {
        for (string? entry = Path.GetFullPath(path); entry is not null; entry = Path.GetDirectoryName(entry))
        {
            if (!File.Exists(entry) && !Directory.Exists(entry)) continue;
            if (((uint)File.GetAttributes(entry) & (0x400u | 0x1000u | 0x40000u | 0x400000u)) != 0) throw new IOException("Histórico não pode atravessar links ou arquivos cloud.");
        }
    }
    private List<GameGraphicsBackup> Load()
    {
        string file = Path.Combine(directory, "journal.json"); EnsureStorage(file); if (!File.Exists(file)) return [];
        if (new FileInfo(file).Length > 2 * 1024 * 1024) throw new InvalidDataException("Histórico de preferências excedeu o limite.");
        var store = JsonSerializer.Deserialize<Store>(File.ReadAllText(file)) ?? throw new InvalidDataException("Histórico de preferências inválido.");
        if (store.Version != 1 || store.Records is null || store.Records.Count > 1000 || store.Records.Select(x => x.Id).Distinct().Count() != store.Records.Count)
            throw new InvalidDataException("Versão ou registros de preferências inválidos.");
        foreach (var record in store.Records)
        {
            if (record is null || record.Id == Guid.Empty || string.IsNullOrEmpty(record.Executable) || !Path.IsPathFullyQualified(record.Executable) || !Path.GetExtension(record.Executable).Equals(".exe", StringComparison.OrdinalIgnoreCase)
                || record.ExecutableHash is not { Length: 64 } || record.Before is null || record.Applied is null || record.Before.Exists && record.Before.Kind != (int)RegistryValueKind.String
                || record.Applied.Kind != (int)RegistryValueKind.String || !record.Applied.Exists)
                throw new InvalidDataException("Backup de preferência inválido.");
            string applied = JsonSerializer.Deserialize<string>(record.Applied.Json) ?? throw new InvalidDataException("Valor aplicado inválido.");
            string before = record.Before.Exists ? JsonSerializer.Deserialize<string>(record.Before.Json) ?? throw new InvalidDataException("Valor anterior inválido.") : "";
            if (applied != MergeHighPerformance(before)) throw new InvalidDataException("Preferência aplicada não corresponde ao contrato.");
        }
        return store.Records;
    }
    private void Save(List<GameGraphicsBackup> records)
    {
        if (records.Count > 1000) throw new IOException("Histórico atingiu 1000 registros. Preserve os backups antes de prosseguir.");
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new Store(1, records));
        if (bytes.Length > 2 * 1024 * 1024) throw new IOException("Histórico atingiu 2 MiB. Preserve os backups antes de prosseguir.");
        string file = Path.Combine(directory, "journal.json"), temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        EnsureStorage(file); EnsureStorage(temp);
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { stream.Write(bytes); stream.Flush(true); }
            EnsureStorage(file); File.Move(temp, file, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private sealed record Store(int Version, List<GameGraphicsBackup> Records);
}
