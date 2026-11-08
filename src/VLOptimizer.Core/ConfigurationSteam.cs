using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace VLOptimizer.Core;

/// <summary>Minimal, strict KeyValues parser for a single Steam preference. It does not rewrite unrelated nodes.</summary>
public sealed class SteamOverlayDocument
{
    private readonly string text;
    private readonly Encoding encoding;
    private readonly byte[] preamble;
    private readonly List<Node> roots;
    private sealed record Token(string Value, int Start, int End, char Symbol = '\0');
    private sealed record Node(Token Key, Token? Value, Token? Open, Token? Close, IReadOnlyList<Node> Children);
    private SteamOverlayDocument(string text, Encoding encoding, byte[] preamble)
    {
        this.text = text; this.encoding = encoding; this.preamble = preamble;
        var tokens = Tokenize(text); int index = 0;
        roots = Parse(tokens, ref index, false, 0).ToList();
        if (index != tokens.Count) throw new InvalidDataException("Estrutura VDF inválida.");
    }
    public static SteamOverlayDocument Parse(byte[] bytes)
    {
        if (bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("localconfig.vdf excede 4 MiB; arquivo preservado.");
        Encoding codec = new UTF8Encoding(false, true); byte[] bom = [];
        if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) bom = [0xef, 0xbb, 0xbf];
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) { codec = new UnicodeEncoding(false, false, true); bom = [0xff, 0xfe]; }
        else if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) { codec = new UnicodeEncoding(true, false, true); bom = [0xfe, 0xff]; }
        return new(codec.GetString(bytes, bom.Length, bytes.Length - bom.Length), codec, bom);
    }
    public ConfigurationValue Read()
    {
        var system = SystemNode();
        var value = Single(system?.Children ?? [], "EnableGameOverlay");
        if (value is not null && value.Value is null) throw new InvalidDataException("EnableGameOverlay deve ser um valor simples.");
        return value?.Value is { } token ? ConfigurationValue.String(token.Value) : ConfigurationValue.Missing(system is not null);
    }
    public byte[] With(ConfigurationValue desired)
    {
        if (desired.Exists && desired.Kind != (int)RegistryValueKind.String) throw new InvalidDataException("Tipo VDF não suportado.");
        string? value = desired.Exists ? JsonSerializer.Deserialize<string>(desired.Json) ?? throw new InvalidDataException("Valor VDF nulo.") : null;
        if (value?.Length > 128 || value?.Any(char.IsControl) == true) throw new InvalidDataException("Valor VDF inválido.");
        var root = RootNode(); var system = SystemNode(); var entry = Single(system?.Children ?? [], "EnableGameOverlay");
        string result = text;
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        if (entry is not null)
        {
            if (entry.Value is null) throw new InvalidDataException("EnableGameOverlay deve ser um valor simples.");
            if (value is not null) result = Replace(entry.Value.Start, entry.Value.End, Quote(value));
            else if (system!.Children.Count == 1 && !desired.KeyExists)
            {
                var (start, end) = WholeLineRange(system.Key.Start, system.Close!.End);
                result = Replace(start, end, "");
            }
            else
            {
                var (start, end) = WholeLineRange(entry.Key.Start, entry.Value.End);
                result = Replace(start, end, "");
            }
        }
        else if (value is not null)
        {
            if (system is not null)
            {
                string indent = IndentAt(system.Key.Start) + "\t";
                result = Replace(system.Open!.End, system.Open.End, newline + indent + "\"EnableGameOverlay\"\t\t" + Quote(value) + newline + indent);
            }
            else
            {
                string indent = IndentAt(root.Key.Start) + "\t";
                result = Replace(root.Open!.End, root.Open.End, newline + indent + "\"system\"" + newline + indent + "{" + newline + indent + "\t\"EnableGameOverlay\"\t\t" + Quote(value) + newline + indent + "}");
            }
        }
        byte[] payload = encoding.GetBytes(result);
        return [.. preamble, .. payload];
    }
    private Node RootNode()
    {
        var root = Single(roots, "UserLocalConfigStore") ?? throw new InvalidDataException("Raiz UserLocalConfigStore ausente; arquivo preservado.");
        if (root.Open is null) throw new InvalidDataException("Raiz VDF inválida.");
        return root;
    }
    private Node? SystemNode()
    {
        var system = Single(RootNode().Children, "system");
        if (system is not null && system.Open is null) throw new InvalidDataException("Bloco system inválido.");
        return system;
    }
    private static Node? Single(IEnumerable<Node> nodes, string name)
    {
        var matches = nodes.Where(x => string.Equals(x.Key.Value, name, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        return matches.Length > 1 ? throw new InvalidDataException($"Chave VDF duplicada: {name}; arquivo preservado.") : matches.FirstOrDefault();
    }
    private string Replace(int start, int end, string content) => text[..start] + content + text[end..];
    private string IndentAt(int index)
    {
        int start = text.LastIndexOf('\n', Math.Max(0, index - 1)); start = start < 0 ? 0 : start + 1;
        return new string(text[start..index].TakeWhile(x => x is ' ' or '\t').ToArray());
    }
    private (int Start, int End) WholeLineRange(int start, int end)
    {
        int line = text.LastIndexOf('\n', Math.Max(0, start - 1)); line = line < 0 ? 0 : line + 1;
        int next = text.IndexOf('\n', end); next = next < 0 ? text.Length : next + 1;
        if (text[line..start].All(x => x is ' ' or '\t') && text[end..next].All(x => char.IsWhiteSpace(x))) return (line, next);
        return (start, end);
    }
    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        for (int i = 0; i < text.Length;)
        {
            if (char.IsWhiteSpace(text[i])) { i++; continue; }
            if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '/') { i = text.IndexOf('\n', i) is int next && next >= 0 ? next : text.Length; continue; }
            int start = i;
            if (text[i] is '{' or '}') { char symbol = text[i++]; tokens.Add(new(symbol.ToString(), start, i, symbol)); continue; }
            if (text[i] == '"')
            {
                i++; var value = new StringBuilder(); bool closed = false;
                while (i < text.Length)
                {
                    char c = text[i++];
                    if (c == '"') { closed = true; break; }
                    if (c == '\\' && i < text.Length)
                    {
                        char escaped = text[i++];
                        value.Append(escaped switch { 'n' => '\n', 'r' => '\r', 't' => '\t', _ => escaped });
                    }
                    else value.Append(c);
                }
                if (!closed) throw new InvalidDataException("String VDF sem fechamento.");
                tokens.Add(new(value.ToString(), start, i));
            }
            else
            {
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('{' or '}')) i++;
                if (i == start) throw new InvalidDataException("Token VDF inválido.");
                tokens.Add(new(text[start..i], start, i));
            }
            if (tokens.Count > 200_000) throw new InvalidDataException("VDF excede o limite de tokens.");
        }
        return tokens;
    }
    private static IReadOnlyList<Node> Parse(IReadOnlyList<Token> tokens, ref int index, bool nested, int depth)
    {
        if (depth > 64) throw new InvalidDataException("VDF excede 64 níveis.");
        var nodes = new List<Node>();
        while (index < tokens.Count && tokens[index].Symbol != '}')
        {
            Token key = tokens[index++];
            if (key.Symbol != '\0' || index >= tokens.Count) throw new InvalidDataException("Par chave/valor VDF incompleto.");
            Token value = tokens[index++];
            if (value.Symbol == '{')
            {
                var children = Parse(tokens, ref index, true, depth + 1);
                if (index >= tokens.Count || tokens[index].Symbol != '}') throw new InvalidDataException("Bloco VDF sem fechamento.");
                nodes.Add(new(key, null, value, tokens[index++], children));
            }
            else if (value.Symbol == '\0') nodes.Add(new(key, value, null, null, []));
            else throw new InvalidDataException("Valor VDF inválido.");
        }
        if (!nested && index < tokens.Count) throw new InvalidDataException("Fechamento VDF inesperado.");
        return nodes;
    }
}

internal static class ConfigurationSteamAccess
{
    internal static IReadOnlyList<ConfigurationChange> Resolve()
    {
        EnsureSteamClosed();
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        string? steamPath = key?.GetValue("SteamPath") as string;
        if (string.IsNullOrWhiteSpace(steamPath)) return [];
        string root = Path.GetFullPath(steamPath);
        CheckPath(root);
        if (!File.Exists(Path.Combine(root, "steam.exe"))) return [];
        string userdata = Path.Combine(root, "userdata");
        if (!Directory.Exists(userdata)) return [];
        CheckPath(userdata);
        var result = new List<ConfigurationChange>();
        foreach (var account in Directory.EnumerateDirectories(userdata).Take(65))
        {
            if (!Path.GetFileName(account).All(char.IsDigit)) continue;
            string file = Path.Combine(account, "config", "localconfig.vdf");
            if (!File.Exists(file)) continue;
            if (result.Count >= 32) throw new IOException("Mais de 32 contas Steam; altere a sobreposição pelo cliente Steam.");
            CheckPath(file);
            _ = Read(file);
            result.Add(new(new(ConfigurationTargetKind.SteamOverlay, file, "EnableGameOverlay"), ConfigurationValue.String("0"), ConfigurationValue.String("1")));
        }
        return result;
    }
    internal static ConfigurationValue Read(string path)
    {
        CheckPath(path);
        return SteamOverlayDocument.Parse(ReadBytes(path)).Read();
    }
    private static byte[] ReadBytes(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 4 * 1024 * 1024) throw new IOException("localconfig.vdf excede 4 MiB.");
        byte[] data = new byte[checked((int)stream.Length)]; stream.ReadExactly(data);
        return data;
    }
    internal static void Write(string path, ConfigurationValue value)
    {
        EnsureSteamClosed(); CheckPath(path);
        byte[] original = ReadBytes(path);
        var document = SteamOverlayDocument.Parse(original); byte[] changed = document.With(value);
        if (!ConfigurationEngine.Equal(SteamOverlayDocument.Parse(changed).Read(), value)) throw new InvalidDataException("A edição VDF não corresponde ao valor solicitado; arquivo preservado.");
        string temporary = path + ".vl-" + Guid.NewGuid().ToString("N") + ".tmp";
        string backup = temporary + ".backup";
        bool replaced = false;
        try
        {
            using (var writer = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) { writer.Write(changed); writer.Flush(true); }
            EnsureSteamClosed(); CheckPath(path);
            if (!ReadBytes(path).AsSpan().SequenceEqual(original)) throw new IOException("O VDF mudou durante a edição; consulte novamente.");
            File.Replace(temporary, path, backup, false); replaced = true;
            if (!ConfigurationEngine.Equal(Read(path), value)) throw new IOException("O arquivo substituído não confirmou o valor solicitado. O backup de emergência foi preservado: " + backup);
            File.Delete(backup);
        }
        catch
        {
            // After replacement, keep the full emergency backup if any post-write check fails.
            if (!replaced && File.Exists(backup)) File.Delete(backup);
            throw;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void EnsureSteamClosed()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        if (key?.GetValue("RunningAppID") is int game && game > 0) throw new InvalidOperationException("Feche o jogo e o Steam antes de alterar a sobreposição.");
        foreach (string name in new[] { "steam", "steamwebhelper" })
        {
            var processes = Process.GetProcessesByName(name); bool running = processes.Length != 0;
            foreach (var process in processes) process.Dispose();
            if (running) throw new InvalidOperationException("Feche o Steam pela bandeja antes de alterar ou restaurar a sobreposição.");
        }
    }
    private static void CheckPath(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("O caminho Steam contém um link ou ponto de junção; arquivo preservado.");
        }
    }
}
