using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VLOptimizer.Core;

public record OptiSetting(string Id, string Title, IReadOnlyList<string> Values);
public sealed record OptiSettingsPreview(Guid Id, string Game, string GameHash, string BeforeHash, string AfterHash, IReadOnlyDictionary<string, string> Changes, DateTimeOffset At);
public sealed record OptiSettingsBackup(Guid Id, string Game, string GameHash, string BeforeHash, string AfterHash, long Sequence, DateTimeOffset At, bool Restored = false);
public record OptiSettingsResult(OperationStatus Status, string Message, Guid? BackupId);

/// <summary>Conservative, version-aware editing of existing OptiScaler INI keys; no package code executes.</summary>
public sealed class OptiScalerSettingsService(string dataDirectory)
{
    private readonly string directory = Path.Combine(Path.GetFullPath(dataDirectory), "optiscaler-settings");
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<OptiSettingsPreview, byte[]> issued = new(ReferenceEqualityComparer.Instance);
    private static OptiSetting Field(string id, string title, params string[] values) => new(id, title, Array.AsReadOnly(values));
    public static IReadOnlyList<OptiSetting> Definitions { get; } = Array.AsReadOnly(new[] {
        Field("Upscalers/Dx11Upscaler", "Upscaler DirectX 11", "auto", "fsr22", "fsr31", "xess", "xess_12", "fsr21_12", "fsr22_12", "ffx_12", "dlss"),
        Field("Upscalers/Dx12Upscaler", "Upscaler DirectX 12", "auto", "xess", "fsr21", "fsr22", "ffx", "dlss"),
        Field("Upscalers/VulkanUpscaler", "Upscaler Vulkan", "auto", "fsr21", "fsr22", "ffx", "xess", "fsr21_12", "ffx_12", "dlss"),
        Field("FrameGen/Enabled", "Geração de quadros", "auto", "true", "false"),
        Field("FrameGen/FGInput", "Origem da geração", "auto", "nofg", "dlssg", "nvngxfg", "fsrfg", "upscaler", "fsrfg30", "xefg"),
        Field("FrameGen/FGOutput", "Saída da geração", "auto", "nofg", "fsrfg", "xefg", "dlssg"),
        Field("Menu/ShowFps", "Contador do pacote no jogo", "auto", "true", "false"),
        Field("Menu/ShortcutKey", "Tecla do menu · Home/Insert/End", "auto", "0x24", "0x2D", "0x23", "-1"),
        Field("Spoofing/Dxgi", "Compatibilidade de identificação DXGI", "auto", "true", "false"),
        Field("Spoofing/StreamlineSpoofing", "Compatibilidade de identificação Streamline", "auto", "true", "false")
    });

    public static IReadOnlyDictionary<string, string> ReadValues(byte[] bytes) => Parse(bytes).Fields
        .Where(x => Definitions.Any(d => d.Id.Equals(x.Key, StringComparison.OrdinalIgnoreCase)))
        .ToDictionary(x => x.Key, x => x.Value.Value, StringComparer.OrdinalIgnoreCase);

    public static byte[] Edit(byte[] bytes, IReadOnlyDictionary<string, string> changes)
    {
        if (changes.Count is < 1 or > 10) throw new ArgumentException("Selecione de uma a dez alterações.");
        var parsed = Parse(bytes); var replacements = new List<(int At, int Length, string Value)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, value) in changes)
        {
            if (!seen.Add(id)) throw new ArgumentException("Opção duplicada: " + id);
            var definition = Definitions.SingleOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("Opção fora do catálogo: " + id);
            if (!definition.Values.Contains(value, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Valor não permitido: " + id);
            if (!parsed.Fields.TryGetValue(id, out var original)) throw new InvalidDataException("Esta versão do INI não contém a opção: " + id);
            replacements.Add((original.At, original.Length, value));
        }
        string text = parsed.Text;
        foreach (var edit in replacements.OrderByDescending(x => x.At)) text = text[..edit.At] + edit.Value + text[(edit.At + edit.Length)..];
        byte[] result = parsed.Bom.Concat(parsed.Encoding.GetBytes(text)).ToArray();
        if (result.Length > 1024 * 1024) throw new InvalidDataException("INI resultante excede 1 MiB.");
        return result;
    }

    public Task<IReadOnlyDictionary<string, string>> ReadAsync(string game, CancellationToken token = default) => Task.Run(() =>
    { token.ThrowIfCancellationRequested(); string path = OptiScalerService.ValidateConfigurationGame(game); return ReadValues(ReadIni(path)); }, token);

    public async Task<OptiSettingsPreview> PreviewAsync(string game, IReadOnlyDictionary<string, string> changes, CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            string path = OptiScalerService.ValidateConfigurationGame(game); byte[] before = ReadIni(path), after = Edit(before, changes);
            if (before.AsSpan().SequenceEqual(after)) throw new InvalidOperationException("As opções escolhidas já estão configuradas.");
            var preview = new OptiSettingsPreview(Guid.NewGuid(), path, HashFile(path), Hash(before), Hash(after), new Dictionary<string, string>(changes, StringComparer.OrdinalIgnoreCase), DateTimeOffset.UtcNow);
            foreach (var old in issued.Keys.Where(x => DateTimeOffset.UtcNow - x.At > TimeSpan.FromMinutes(10)).ToArray()) issued.Remove(old);
            issued[preview] = after; return preview;
        }
        finally { gate.Release(); }
    }
    public async Task<OptiSettingsResult> ApplyAsync(OptiSettingsPreview preview, CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (!issued.Remove(preview, out var after) || DateTimeOffset.UtcNow - preview.At > TimeSpan.FromMinutes(10)) return new(OperationStatus.Conflict, "Prévia inválida, expirada ou utilizada.", null);
            using var lease = Acquire(); string game = OptiScalerService.ValidateConfigurationGame(preview.Game); byte[] before = ReadIni(game); var backups = Load();
            if (backups.Count >= 1000) throw new IOException("Limite de backups atingido. Preserve os backups antes de continuar.");
            if (HashFile(game) != preview.GameHash || Hash(before) != preview.BeforeHash) return new(OperationStatus.Conflict, "Jogo ou INI mudou após a prévia.", null);
            token.ThrowIfCancellationRequested();
            var backup = new OptiSettingsBackup(preview.Id, game, preview.GameHash, preview.BeforeHash, preview.AfterHash, backups.Count == 0 ? 1 : checked(backups.Max(x => x.Sequence) + 1), DateTimeOffset.UtcNow);
            string folder = Folder(backup.Id); Directory.CreateDirectory(folder); Atomic(Path.Combine(folder, "original.ini"), before); Save(backup);
            try
            {
                if (Hash(ReadIni(game)) != preview.BeforeHash) return new(OperationStatus.Conflict, "INI mudou durante a preparação. Nada foi gravado no jogo.", backup.Id);
                Atomic(Ini(game), after);
                if (Hash(ReadIni(game)) != preview.AfterHash) throw new IOException("Releitura não confirmou o arquivo.");
                return new(OperationStatus.Completed, "Opções gravadas e verificadas. O efeito depende da versão do pacote, bibliotecas e jogo; reinicie o jogo.", backup.Id);
            }
            catch (Exception ex) { return new(OperationStatus.Unverified, "Pode haver alteração não confirmada; backup preservado. " + ex.Message, backup.Id); }
        }
        finally { gate.Release(); }
    }
    public async Task<IReadOnlyList<OptiSettingsBackup>> GetBackupsAsync(CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try { using var lease = Acquire(); return Load().OrderByDescending(x => x.Sequence).ToArray(); }
        finally { gate.Release(); }
    }
    public async Task<OptiSettingsResult> RestoreAsync(Guid id, CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var lease = Acquire(); var backups = Load(); var backup = backups.SingleOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException("Backup não encontrado.");
            if (backup.Restored) return new(OperationStatus.Skipped, "Já restaurado.", id);
            if (backups.Any(x => !x.Restored && x.Sequence > backup.Sequence && x.Game.Equals(backup.Game, StringComparison.OrdinalIgnoreCase))) return new(OperationStatus.Conflict, "Restaure primeiro as opções mais recentes deste jogo.", id);
            string game = OptiScalerService.ValidateConfigurationGame(backup.Game);
            if (HashFile(game) != backup.GameHash) return new(OperationStatus.Conflict, "Executável substituído. Backup preservado.", id);
            string current = Hash(ReadIni(game)); if (current != backup.BeforeHash && current != backup.AfterHash) return new(OperationStatus.Conflict, "INI alterado por outro programa. Edição preservada.", id);
            byte[] original = File.ReadAllBytes(Path.Combine(Folder(id), "original.ini"));
            if (Hash(original) != backup.BeforeHash) throw new InvalidDataException("Backup foi alterado."); token.ThrowIfCancellationRequested();
            if (current != backup.BeforeHash) Atomic(Ini(game), original);
            if (Hash(ReadIni(game)) != backup.BeforeHash) return new(OperationStatus.Unverified, "Restauração não confirmada.", id);
            Save(backup with { Restored = true }); return new(OperationStatus.Reverted, "INI anterior restaurado byte a byte. Restaure as opções antes de remover a instalação OptiScaler.", id);
        }
        finally { gate.Release(); }
    }
    private static string Ini(string game) => Path.Combine(Path.GetDirectoryName(game)!, "OptiScaler.ini");
    private static byte[] ReadIni(string game)
    {
        string path = Ini(game); OptiScalerService.EnsureNoLinks(path);
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("INI excede 1 MiB."); return File.ReadAllBytes(path);
    }
    private string Folder(Guid id) => Path.Combine(directory, id.ToString("N"));
    private FileStream Acquire()
    {
        OptiScalerService.EnsureNoLinks(directory); Directory.CreateDirectory(directory); string file = Path.Combine(directory, "settings.lock"); OptiScalerService.EnsureNoLinks(file);
        return new(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private List<OptiSettingsBackup> Load()
    {
        var result = new List<OptiSettingsBackup>();
        foreach (string folder in Directory.EnumerateDirectories(directory).Take(1001))
        {
            if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out var id)) continue;
            if (result.Count >= 1000) throw new InvalidDataException("Limite de backups atingido.");
            string file = Path.Combine(folder, "manifest.json"); OptiScalerService.EnsureNoLinks(file); if (!File.Exists(file)) continue;
            if (new FileInfo(file).Length > 65536) throw new InvalidDataException("Manifesto inválido.");
            var b = JsonSerializer.Deserialize<OptiSettingsBackup>(File.ReadAllText(file)) ?? throw new InvalidDataException("Backup inválido.");
            if (b.Id != id || b.Sequence <= 0 || new[] { b.BeforeHash, b.AfterHash, b.GameHash }.Any(x => x is not { Length: 64 } || !x.All(Uri.IsHexDigit)) || !Path.IsPathFullyQualified(b.Game) || !Path.GetExtension(b.Game).Equals(".exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Snapshot inválido.");
            string original = Path.Combine(folder, "original.ini"); OptiScalerService.EnsureNoLinks(original);
            if (new FileInfo(original).Length > 1024 * 1024 || HashFile(original) != b.BeforeHash) throw new InvalidDataException("Backup corrompido."); result.Add(b);
        }
        if (result.Select(x => x.Sequence).Distinct().Count() != result.Count) throw new InvalidDataException("Sequências de backup duplicadas."); return result;
    }
    private void Save(OptiSettingsBackup backup) => Atomic(Path.Combine(Folder(backup.Id), "manifest.json"), JsonSerializer.SerializeToUtf8Bytes(backup));
    private static void Atomic(string path, byte[] bytes)
    {
        OptiScalerService.EnsureNoLinks(path); string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) { stream.Write(bytes); stream.Flush(true); } OptiScalerService.EnsureNoLinks(path); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string HashFile(string path) { using var source = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(source)); }
    private sealed record FieldValue(int At, int Length, string Value);
    private sealed record Parsed(string Text, Encoding Encoding, byte[] Bom, Dictionary<string, FieldValue> Fields);
    private static Parsed Parse(byte[] bytes)
    {
        if (bytes.Length > 1024 * 1024) throw new InvalidDataException("INI excede o limite.");
        Encoding encoding = new UTF8Encoding(false, true); int skip = 0;
        if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) skip = 3;
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) { encoding = new UnicodeEncoding(false, false, true); skip = 2; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) { encoding = new UnicodeEncoding(true, false, true); skip = 2; }
        string text = encoding.GetString(bytes, skip, bytes.Length - skip); if (text.Contains('\0')) throw new InvalidDataException("Codificação do INI não suportada.");
        string section = ""; var sections = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var fields = new Dictionary<string, FieldValue>(StringComparer.OrdinalIgnoreCase);
        foreach (Match line in Regex.Matches(text, @"[^\r\n]*(?:\r\n|\n|$)"))
        {
            if (line.Length == 0) continue; string content = line.Value.TrimEnd('\r', '\n');
            var header = Regex.Match(content, @"^[ \t]*\[([^\]\r\n]+)\][ \t]*(?:[;#].*)?$");
            if (header.Success) { section = header.Groups[1].Value.Trim(); if (!sections.Add(section)) throw new InvalidDataException("Seção duplicada: " + section); continue; }
            var value = Regex.Match(content, @"^[ \t]*([^=;#\[\]]+?)[ \t]*=[ \t]*(?<value>[^;#\r\n]*?)(?:[ \t]*(?:[;#].*)?)$");
            if (!value.Success) continue; string id = section + "/" + value.Groups[1].Value.Trim(); var group = value.Groups["value"];
            if (!fields.TryAdd(id, new(line.Index + group.Index, group.Length, group.Value))) throw new InvalidDataException("Chave duplicada: " + id);
        }
        return new(text, encoding, bytes[..skip], fields);
    }
}
