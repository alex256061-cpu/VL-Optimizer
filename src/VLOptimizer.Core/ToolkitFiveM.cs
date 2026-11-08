using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace VLOptimizer.Core;

/// <summary>XML-aware, path-bound FiveM profiles with exact backup, post-verification and conflict-aware undo.</summary>
public sealed class ToolkitFiveMService
{
    private readonly string _directory;
    private readonly Func<IReadOnlyList<string>> _processNames;
    private readonly ConcurrentDictionary<Guid, (ToolkitFiveMPreview Preview, byte[] Original, byte[] Planned)> _previews = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly IReadOnlyDictionary<string, string> Fps = new Dictionary<string, string>
    {
        ["MSAA"] = "0", ["MSAAFragments"] = "0", ["MSAAQuality"] = "0", ["ReflectionMSAA"] = "0", ["GrassQuality"] = "0",
        ["ReflectionQuality"] = "0", ["PostFX"] = "0", ["SSAO"] = "0", ["Tessellation"] = "0", ["ShadowQuality"] = "1", ["Shadow_SoftShadows"] = "1",
        ["MaxLodScale"] = "0.000000", ["Shadow_LongShadows"] = "false", ["UltraShadows_Enabled"] = "false", ["HdStreamingInFlight"] = "false",
        ["DoF"] = "false", ["TXAA_Enabled"] = "false", ["MotionBlurStrength"] = "0.000000"
    };
    private static readonly IReadOnlyDictionary<string, string> Balanced = new Dictionary<string, string>
    {
        ["MSAA"] = "0", ["MSAAFragments"] = "0", ["MSAAQuality"] = "0", ["ReflectionMSAA"] = "0", ["GrassQuality"] = "1",
        ["ReflectionQuality"] = "1", ["PostFX"] = "2", ["SSAO"] = "1", ["Tessellation"] = "1", ["ShadowQuality"] = "2", ["Shadow_SoftShadows"] = "2",
        ["MaxLodScale"] = "0.000000", ["Shadow_LongShadows"] = "false", ["UltraShadows_Enabled"] = "false", ["DoF"] = "false", ["MotionBlurStrength"] = "0.000000"
    };
    public ToolkitFiveMService(string dataDirectory, Func<IReadOnlyList<string>>? processNames = null)
    { _directory = ToolkitPaths.Full(dataDirectory); _processNames = processNames ?? ReadProcessNames; }
    public IReadOnlyList<string> GetPresets() => new[] { "fps", "balanced" };
    public IReadOnlyList<string> FindSettingsPaths() => new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CitizenFX", "gta5_settings.xml"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CitizenFX", "gta5_settings.xml")
    }.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public Task<ToolkitFiveMPreview> PreviewPresetAsync(string path, string preset, int? scaling = null, bool? borderless = null, CancellationToken token = default) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested(); EnsureGameClosed(); path = ValidatePath(path);
        if (scaling is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(scaling));
        var values = new Dictionary<string, string>(preset switch { "fps" => Fps, "balanced" => Balanced, _ => throw new ArgumentException("Perfil desconhecido.", nameof(preset)) });
        if (scaling is int index) values["SamplingMode"] = new[] { "0", "4", "3", "2", "1" }[index];
        if (borderless is bool border) values["Windowed"] = border ? "2" : "0";
        var bytes = ReadBounded(path); var doc = Parse(bytes); var changes = new List<ToolkitFiveMChange>();
        if (doc.Root?.Name.LocalName != "Settings") throw new InvalidDataException("O arquivo não possui a raiz Settings esperada pelo GTA/FiveM.");
        foreach (var pair in values)
        {
            var existing = doc.Descendants().Where(e => e.Name.LocalName == pair.Key).ToArray();
            if (existing.Length > 1) throw new InvalidDataException($"A tag {pair.Key} está duplicada; nenhuma alteração será aplicada.");
            var element = existing.SingleOrDefault(); var before = element?.Attribute("value")?.Value;
            if (before == pair.Value) continue;
            if (element is null)
            {
                string sectionName = pair.Key is "SamplingMode" or "Windowed" ? "video" : "graphics";
                var section = doc.Root.Elements().SingleOrDefault(e => e.Name.LocalName == sectionName)
                    ?? throw new InvalidDataException($"Seção {sectionName} ausente; abra o jogo para gerar configurações válidas.");
                element = new XElement(section.Name.Namespace + pair.Key); section.Add(element);
            }
            element.SetAttributeValue("value", pair.Value);
            changes.Add(new(pair.Key, before, pair.Value, existing.Length == 0));
        }
        byte[] planned;
        using (var memory = new MemoryStream())
        {
            using (var writer = XmlWriter.Create(memory, new XmlWriterSettings { Encoding = new UTF8Encoding(bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })),
                Indent = false, OmitXmlDeclaration = doc.Declaration is null, NewLineHandling = NewLineHandling.None, CloseOutput = false })) doc.Save(writer);
            planned = memory.ToArray();
        }
        // Parsing our output is part of preparation, before the preview is shown.
        Parse(planned);
        var preview = new ToolkitFiveMPreview(Guid.NewGuid(), path, preset, changes.AsReadOnly(), Hash(bytes), Hash(planned), DateTimeOffset.UtcNow);
        foreach (var old in _previews.Where(p => p.Value.Preview.CreatedAt < DateTimeOffset.UtcNow.AddMinutes(-15)).Select(p => p.Key)) _previews.TryRemove(old, out _);
        _previews[preview.Id] = (preview, bytes, planned); return preview;
    }, token);

    public async Task<ToolkitFiveMResult> ApplyAsync(ToolkitFiveMPreview preview, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!_previews.TryRemove(preview.Id, out var prepared) || !ReferenceEquals(prepared.Preview, preview)) throw new InvalidOperationException("Prévia inválida ou já utilizada.");
        if (preview.CreatedAt < DateTimeOffset.UtcNow.AddMinutes(-15)) throw new InvalidOperationException("A prévia expirou.");
        var mutex = Locks.GetOrAdd(_directory, _ => new(1, 1)); await mutex.WaitAsync(token).ConfigureAwait(false);
        try
        {
            EnsureGameClosed(); var path = ValidatePath(preview.Path);
            if (Hash(ReadBounded(path)) != preview.OriginalSha256) return new(false, true, "O XML mudou após a prévia; solicite uma nova análise.", null);
            if (preview.Changes.Count == 0) return new(true, false, "Os valores já estão configurados; nenhum arquivo foi escrito.", null);
            EnsureStorage(); using var journalLock = await AcquireJournalLockAsync(token).ConfigureAwait(false);
            var records = await ReadJournalAsync(token).ConfigureAwait(false);
            var record = new ToolkitFiveMBackup(Guid.NewGuid(), path, preview.OriginalSha256, preview.PlannedSha256, DateTimeOffset.UtcNow, false);
            var backupPath = BackupPath(record.Id);
            // Backups and journal are durable before the first change to the game XML.
            await WriteNewDurablyAsync(backupPath, prepared.Original, token).ConfigureAwait(false);
            records.Add(record); await WriteJournalAsync(records, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            EnsureGameClosed();
            if (Hash(ReadBounded(path)) != preview.OriginalSha256) return new(false, true, "O XML foi alterado durante a preparação; backup preservado, sem sobrescrever.", record);
            try
            {
                await ReplaceCheckedAsync(path, prepared.Planned, preview.OriginalSha256, token).ConfigureAwait(false);
                if (Hash(ReadBounded(path)) != preview.PlannedSha256) return new(false, false, "A verificação final divergiu; o backup foi preservado para recuperação.", record);
                return new(true, false, $"Perfil {preview.Preset} aplicado e verificado ({preview.Changes.Count} campos).", record);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            { return new(false, false, "Falha ao aplicar; backup preservado: " + ex.Message, record); }
        }
        finally { mutex.Release(); }
    }

    public async Task<IReadOnlyList<ToolkitFiveMBackup>> GetBackupsAsync(CancellationToken token = default)
    {
        if (!Directory.Exists(_directory)) return [];
        ToolkitPaths.EnsureNoLinks(_directory);
        return (await ReadJournalAsync(token).ConfigureAwait(false)).AsReadOnly();
    }
    public async Task<ToolkitFiveMResult> RestoreAsync(Guid backupId, CancellationToken token = default)
    {
        var mutex = Locks.GetOrAdd(_directory, _ => new(1, 1)); await mutex.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested(); EnsureGameClosed(); EnsureStorage();
            using var journalLock = await AcquireJournalLockAsync(token).ConfigureAwait(false);
            var records = await ReadJournalAsync(token).ConfigureAwait(false); var record = records.SingleOrDefault(r => r.Id == backupId);
            if (record is null || record.Restored) return new(false, false, "Backup ausente ou já restaurado.", record);
            string path = ValidatePath(record.Path), backupPath = BackupPath(record.Id); ToolkitPaths.EnsureNoLinks(backupPath);
            var backup = ReadBounded(backupPath);
            if (Hash(backup) != record.OriginalSha256) return new(false, false, "O backup está corrompido; restauração bloqueada.", record);
            string current = Hash(ReadBounded(path));
            if (current == record.OriginalSha256)
            {
                record = record with { Restored = true }; records[records.FindIndex(r => r.Id == record.Id)] = record;
                await WriteJournalAsync(records, token).ConfigureAwait(false);
                return new(true, false, "O arquivo já corresponde ao backup original; registro reconciliado.", record);
            }
            if (current != record.AppliedSha256) return new(false, true, "O XML mudou desde esse perfil. Restaure primeiro os perfis mais recentes ou revise suas alterações manuais.", record);
            EnsureGameClosed(); await ReplaceCheckedAsync(path, backup, current, token).ConfigureAwait(false);
            if (Hash(ReadBounded(path)) != record.OriginalSha256) return new(false, false, "A restauração não foi confirmada; backup preservado.", record);
            record = record with { Restored = true }; records[records.FindIndex(r => r.Id == record.Id)] = record;
            await WriteJournalAsync(records, token).ConfigureAwait(false);
            return new(true, false, "XML original restaurado byte a byte no mesmo caminho.", record);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { return new(false, false, ex.Message, null); }
        finally { mutex.Release(); }
    }
    public Task<IReadOnlyDictionary<string, string?>> ReadValuesAsync(string path, CancellationToken token = default) => Task.Run<IReadOnlyDictionary<string, string?>>(() =>
    { token.ThrowIfCancellationRequested(); return Parse(ReadBounded(ValidatePath(path))).Descendants().Where(e => e.Attribute("value") is not null).GroupBy(e => e.Name.LocalName).ToDictionary(g => g.Key, g => (string?)g.First().Attribute("value")); }, token);

    private static string ValidatePath(string path)
    {
        path = ToolkitPaths.Full(path); ToolkitPaths.EnsureNoLinks(path);
        if (!Path.GetFileName(path).Equals("gta5_settings.xml", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Selecione gta5_settings.xml do FiveM.");
        if (!File.Exists(path)) throw new FileNotFoundException("Configurações ainda não foram geradas pelo jogo.", path);
        return path;
    }
    private static XDocument Parse(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 });
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }
    private static byte[] ReadBounded(string path)
    {
        ToolkitPaths.EnsureNoLinks(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 4 * 1024 * 1024) throw new InvalidDataException("Arquivo excede 4 MiB.");
        var bytes = new byte[stream.Length]; stream.ReadExactly(bytes); return bytes;
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private void EnsureGameClosed()
    {
        if (_processNames().Any(n => n.Equals("FiveM", StringComparison.OrdinalIgnoreCase) || n.Equals("FiveM.exe", StringComparison.OrdinalIgnoreCase) ||
            System.Text.RegularExpressions.Regex.IsMatch(n, @"^FiveM_.*GTAProcess(?:\.exe)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
            throw new InvalidOperationException("Feche o FiveM antes de aplicar ou restaurar o perfil.");
    }
    private static IReadOnlyList<string> ReadProcessNames()
    {
        var names = new List<string>();
        foreach (var process in Process.GetProcesses()) using (process) { try { names.Add(process.ProcessName); } catch (InvalidOperationException) { } }
        return names;
    }
    private void EnsureStorage() { ToolkitPaths.EnsureNoLinks(_directory); Directory.CreateDirectory(_directory); ToolkitPaths.EnsureNoLinks(_directory); }
    private string BackupPath(Guid id) => Path.Combine(_directory, id.ToString("N") + ".xml.bak");
    private async Task<FileStream> AcquireJournalLockAsync(CancellationToken token)
    {
        string path = Path.Combine(_directory, "profiles.lock"); ToolkitPaths.EnsureNoLinks(path);
        for (int attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 50) { await Task.Delay(100, token).ConfigureAwait(false); }
        }
    }
    private async Task<List<ToolkitFiveMBackup>> ReadJournalAsync(CancellationToken token)
    {
        var path = Path.Combine(_directory, "profiles.json"); if (!File.Exists(path)) return [];
        ToolkitPaths.EnsureNoLinks(path);
        if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new InvalidDataException("Índice de backups excede o limite.");
        return JsonSerializer.Deserialize<List<ToolkitFiveMBackup>>(await File.ReadAllTextAsync(path, token).ConfigureAwait(false)) ?? throw new InvalidDataException("Índice de backups inválido.");
    }
    private Task WriteJournalAsync(List<ToolkitFiveMBackup> records, CancellationToken token) => WriteAtomicAsync(Path.Combine(_directory, "profiles.json"), JsonSerializer.SerializeToUtf8Bytes(records, Json), token);
    private static async Task WriteNewDurablyAsync(string path, byte[] bytes, CancellationToken token)
    {
        ToolkitPaths.EnsureNoLinks(path);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough | FileOptions.Asynchronous);
        await stream.WriteAsync(bytes, token).ConfigureAwait(false); await stream.FlushAsync(token).ConfigureAwait(false); stream.Flush(true);
    }
    private static async Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken token)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await WriteNewDurablyAsync(temporary, bytes, token).ConfigureAwait(false); ToolkitPaths.EnsureNoLinks(path); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static async Task ReplaceCheckedAsync(string path, byte[] bytes, string expectedHash, CancellationToken token)
    {
        // File.Replace is atomic within the same volume. A concurrent external editor between the final
        // check and replacement remains a filesystem race; the exact pre-apply backup is always retained.
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, ".vl-fivem-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await WriteNewDurablyAsync(temporary, bytes, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); ToolkitPaths.EnsureNoLinks(path);
            if (Hash(ReadBounded(path)) != expectedHash) throw new IOException("O arquivo foi alterado por outro processo; substituição cancelada.");
            File.Replace(temporary, path, null);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
