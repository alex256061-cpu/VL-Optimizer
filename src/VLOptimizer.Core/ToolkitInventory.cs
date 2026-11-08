using Microsoft.Win32;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VLOptimizer.Core;

public sealed class ToolkitInventoryService
{
    public Task<IReadOnlyList<ToolkitInstalledApp>> ReadInstalledAppsAsync(CancellationToken token = default) => Task.Run<IReadOnlyList<ToolkitInstalledApp>>(() =>
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var apps = new List<ToolkitInstalledApp>();
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                token.ThrowIfCancellationRequested();
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null) continue;
                foreach (var name in uninstall.GetSubKeyNames())
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        using var key = uninstall.OpenSubKey(name);
                        if (key?.GetValue("DisplayName") is not string display || string.IsNullOrWhiteSpace(display) || key.GetValue("SystemComponent") is int and 1) continue;
                        long? estimated = key.GetValue("EstimatedSize") is int kb && kb > 0 ? (long)kb * 1024 : null;
                        apps.Add(new($"{hive}:{view}:{name}", display, key.GetValue("DisplayVersion") as string, key.GetValue("Publisher") as string,
                            key.GetValue("InstallLocation") as string, $"Registro {hive} ({view})", estimated));
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
                }
            }
        // Identity includes hive/view so similarly named per-user and machine installations stay distinct.
        return apps.DistinctBy(a => a.Id, StringComparer.OrdinalIgnoreCase).OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }, token);

    public Task<IReadOnlyList<ToolkitProcess>> ReadProcessesAsync(CancellationToken token = default) => Task.Run<IReadOnlyList<ToolkitProcess>>(() =>
    {
        var results = new List<ToolkitProcess>(); var processes = Process.GetProcesses();
        try
        {
            foreach (var p in processes)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    string? path = null; DateTimeOffset? started = null;
                    try { path = p.MainModule?.FileName; } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                    try { started = new DateTimeOffset(p.StartTime); } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                    results.Add(new(p.Id, p.ProcessName, path, p.WorkingSet64, started, p.MainWindowHandle != IntPtr.Zero));
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            }
        }
        finally { foreach (var p in processes) p.Dispose(); }
        return results.OrderByDescending(p => p.WorkingSetBytes).ToArray();
    }, token);
}

public sealed class ToolkitGameService
{
    public async Task<ToolkitGameDiscovery> DiscoverAsync(CancellationToken token = default)
    {
        var processes = await new ToolkitInventoryService().ReadProcessesAsync(token).ConfigureAwait(false);
        return await Task.Run(() => Discover(ToolkitPaths.ReadSteamPath(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FiveM"), processes, token), token).ConfigureAwait(false);
    }
    /// <summary>Explicit roots also make manifest parsing testable without querying the host registry.</summary>
    public ToolkitGameDiscovery Discover(string? steamRoot, string? epicManifestRoot, string? fiveMRoot, IReadOnlyList<ToolkitProcess>? processes = null, CancellationToken token = default)
    {
        var games = new List<ToolkitGame>(); var issues = new List<ToolkitIssue>(); processes ??= [];
        if (!string.IsNullOrWhiteSpace(steamRoot))
        {
            try
            {
                steamRoot = ToolkitPaths.Full(steamRoot); var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steamRoot };
                string folders = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
                if (File.Exists(folders))
                {
                    foreach (var item in ToolkitValveData.Parse(ReadMetadata(folders)))
                        if (item.Key == "libraryfolders" && item.Children is { } libraryItems)
                            foreach (var library in libraryItems)
                            {
                                var path = library.Children?.FirstOrDefault(n => n.Key == "path")?.Value ?? (int.TryParse(library.Key, out _) ? library.Value : null);
                                if (!string.IsNullOrWhiteSpace(path)) libraries.Add(ToolkitPaths.Full(path));
                            }
                }
                foreach (var library in libraries)
                {
                    string apps = Path.Combine(library, "steamapps"); if (!Directory.Exists(apps)) continue;
                    foreach (var file in ToolkitPaths.Children(apps, issues).Where(p => Regex.IsMatch(Path.GetFileName(p), @"^appmanifest_\d+\.acf$", RegexOptions.IgnoreCase)))
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            var state = ToolkitValveData.Parse(ReadMetadata(file)).FirstOrDefault(i => i.Key.Equals("AppState", StringComparison.OrdinalIgnoreCase))?.Children;
                            string? Get(string key) => state?.FirstOrDefault(n => n.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value;
                            string? id = Get("appid"), name = Get("name"), dir = Get("installdir");
                            if (!int.TryParse(id, out _) || id == "228980" || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(dir) || Regex.IsMatch(name, @"(?:Steamworks|Proton|Redistributable|Vulkan Shader|Dedicated Server|SDK|Runtime)", RegexOptions.IgnoreCase)) continue;
                            var installRoot = ToolkitPaths.Full(Path.Combine(apps, "common")); var install = ToolkitPaths.Full(Path.Combine(installRoot, dir));
                            if (!ToolkitPaths.IsWithin(install, installRoot) || install == installRoot || !Directory.Exists(install)) continue;
                            var exes = FindExecutables(install, null, issues, token);
                            games.Add(new("steam-" + id, name, "Steam", install, exes, Running(exes, processes)));
                        }
                        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or FormatException or ArgumentException) { issues.Add(new(file, ex.Message)); }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or FormatException or ArgumentException) { issues.Add(new(steamRoot ?? "Steam", ex.Message)); }
        }
        if (!string.IsNullOrWhiteSpace(epicManifestRoot) && Directory.Exists(epicManifestRoot))
        {
            foreach (var file in ToolkitPaths.Children(epicManifestRoot, issues).Where(p => Path.GetExtension(p).Equals(".item", StringComparison.OrdinalIgnoreCase)))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using var json = JsonDocument.Parse(ReadMetadata(file), new JsonDocumentOptions { MaxDepth = 32 }); var root = json.RootElement;
                    string? Get(string key) => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                    string? name = Get("DisplayName"), id = Get("AppName"), installRaw = Get("InstallLocation"), launch = Get("LaunchExecutable");
                    if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installRaw)) continue;
                    if (root.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.ValueKind == JsonValueKind.True) continue;
                    if (root.TryGetProperty("AppCategories", out var categories) && categories.ValueKind == JsonValueKind.Array && categories.GetArrayLength() > 0 && !categories.EnumerateArray().Any(c => c.ValueKind == JsonValueKind.String && c.GetString() == "games")) continue;
                    var install = ToolkitPaths.Full(installRaw); if (!Directory.Exists(install)) continue;
                    var preferred = string.IsNullOrWhiteSpace(launch) ? null : ToolkitPaths.Full(Path.Combine(install, launch));
                    if (preferred is not null && !ToolkitPaths.IsWithin(preferred, install)) throw new InvalidDataException("LaunchExecutable sai da pasta instalada.");
                    var exes = FindExecutables(install, preferred, issues, token);
                    games.Add(new("epic-" + id, name, "Epic Games", install, exes, Running(exes, processes)));
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException) { issues.Add(new(file, ex.Message)); }
            }
        }
        if (!string.IsNullOrWhiteSpace(fiveMRoot))
        {
            var exe = Path.Combine(ToolkitPaths.Full(fiveMRoot), "FiveM.exe");
            if (File.Exists(exe)) games.Add(new("fivem", "FiveM", "Instalação local", ToolkitPaths.Full(fiveMRoot), [exe], processes.Any(p => p.Name.StartsWith("FiveM", StringComparison.OrdinalIgnoreCase))));
        }
        // A running process is matched by full executable path when that information is accessible.
        return new(games.DistinctBy(g => g.Id, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(), issues.AsReadOnly());
    }
    private static bool Running(IReadOnlyList<string> exes, IReadOnlyList<ToolkitProcess> processes) => processes.Any(p => p.ExecutablePath is not null && exes.Contains(p.ExecutablePath, StringComparer.OrdinalIgnoreCase));
    private static string ReadMetadata(string path)
    {
        ToolkitPaths.EnsureNoLinks(path);
        if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new InvalidDataException("Manifesto excede 8 MiB.");
        return File.ReadAllText(path);
    }
    private static IReadOnlyList<string> FindExecutables(string install, string? preferred, List<ToolkitIssue> issues, CancellationToken token)
    {
        ToolkitPaths.EnsureNoLinks(install);
        var candidates = new List<(string Path, long Size)>();
        if (preferred is not null && File.Exists(preferred)) { ToolkitPaths.EnsureNoLinks(preferred); candidates.Add((preferred, long.MaxValue)); }
        var pending = new Stack<(string Path, int Depth)>(); pending.Push((install, 0)); int entries = 0;
        while (pending.TryPop(out var item))
        {
            token.ThrowIfCancellationRequested();
            foreach (var path in ToolkitPaths.Children(item.Path, issues))
            {
                if (++entries > 20_000) { issues.Add(new(install, "Busca de executáveis limitada a 20.000 entradas.")); return candidates.OrderByDescending(c => c.Size).Select(c => c.Path).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray(); }
                try
                {
                    var attr = File.GetAttributes(path); if (((uint)attr & ToolkitPaths.ExcludedAttributes) != 0) continue;
                    if (attr.HasFlag(FileAttributes.Directory)) { if (item.Depth < 3 && !Regex.IsMatch(Path.GetFileName(path), @"^(?:redist|_CommonRedist|Engine|EasyAntiCheat|BattlEye|DirectX|DotNet)$", RegexOptions.IgnoreCase)) pending.Push((path, item.Depth + 1)); }
                    else if (Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(Path.GetFileName(path), @"(?:unins|setup|install|crash|report|redist|anticheat|easyanticheat|battleye|updat|prereq|launcherhelper|^node\.|^python|^java|^cmd\.|^powershell)", RegexOptions.IgnoreCase))
                        candidates.Add((path, new FileInfo(path).Length));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { if (issues.Count < 1000) issues.Add(new(path, ex.Message)); }
            }
        }
        return candidates.OrderByDescending(c => c.Size).Select(c => c.Path).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
    }
}

internal record ToolkitValveNode(string Key, string? Value, IReadOnlyList<ToolkitValveNode>? Children);
internal static class ToolkitValveData
{
    internal static IReadOnlyList<ToolkitValveNode> Parse(string input)
    {
        if (input.Length > 8 * 1024 * 1024) throw new FormatException("VDF excede o limite.");
        int position = 0, nodes = 0;
        string? Read()
        {
            while (position < input.Length)
            {
                if (char.IsWhiteSpace(input[position]) || input[position] == '\uFEFF') { position++; continue; }
                if (input[position] == '/' && position + 1 < input.Length && input[position + 1] == '/') { while (position < input.Length && input[position] != '\n') position++; continue; }
                break;
            }
            if (position == input.Length) return null;
            char first = input[position++]; if (first is '{' or '}') return first.ToString();
            var value = new System.Text.StringBuilder();
            if (first == '"')
            {
                while (position < input.Length)
                {
                    char c = input[position++]; if (c == '"') return value.ToString();
                    if (c == '\\' && position < input.Length && input[position] is '\\' or '"') c = input[position++];
                    value.Append(c);
                }
                throw new FormatException("String VDF não terminada.");
            }
            value.Append(first);
            while (position < input.Length && !char.IsWhiteSpace(input[position]) && input[position] is not ('{' or '}')) value.Append(input[position++]);
            return value.ToString();
        }
        List<ToolkitValveNode> ParseLevel(int depth, bool nested)
        {
            if (depth > 32) throw new FormatException("VDF aninhado além do limite.");
            var result = new List<ToolkitValveNode>(); string? key;
            while ((key = Read()) is not null)
            {
                if (key == "}") { if (nested) return result; throw new FormatException("Fechamento VDF inesperado."); }
                if (++nodes > 100_000 || key == "{") throw new FormatException("Estrutura VDF inválida.");
                string value = Read() ?? throw new FormatException("Valor VDF ausente.");
                if (value == "}") throw new FormatException("Valor VDF inválido.");
                result.Add(value == "{" ? new(key, null, ParseLevel(depth + 1, true)) : new(key, value, null));
            }
            if (nested) throw new FormatException("Bloco VDF não terminado.");
            return result;
        }
        return ParseLevel(0, false);
    }
}
