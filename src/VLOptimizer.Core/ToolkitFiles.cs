using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace VLOptimizer.Core;

internal static class ToolkitPaths
{
    internal const uint ExcludedAttributes = 0x400u | 0x1000u | 0x40000u | 0x400000u;
    internal static string Full(string value)
    {
        var path = Path.GetFullPath(value);
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("Selecione um caminho local absoluto.");
        return Path.TrimEndingDirectorySeparator(path);
    }
    internal static bool IsWithin(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    internal static void EnsureNoLinks(string path)
    {
        var current = Full(path);
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && ((uint)File.GetAttributes(current) & ExcludedAttributes) != 0)
                throw new IOException("Links, junctions e arquivos cloud/offline não são elegíveis.");
            var parent = Path.GetDirectoryName(current);
            if (parent == current) break;
            current = parent ?? "";
        }
    }
    internal static IEnumerable<string> Children(string directory, List<ToolkitIssue> issues, bool directoriesOnly = false)
    {
        try
        {
            EnsureNoLinks(directory);
            return Directory.EnumerateFileSystemEntries(directory).Where(x => !directoriesOnly || Directory.Exists(x)).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { if (issues.Count < 1000) issues.Add(new(directory, ex.Message)); return []; }
    }
    internal static string? ReadSteamPath()
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        return key?.GetValue("SteamPath") as string;
    }
    internal static string DisplayException(Exception ex) => ex is UnauthorizedAccessException ? "Acesso negado; item preservado." : ex.Message;
}

/// <summary>Exact preview and permanent deletion. No recursive delete and no implicit process termination.</summary>
public sealed class ToolkitCleanupService
{
    private readonly Func<IReadOnlyList<ToolkitCleanupCategory>> _categories;
    private readonly ConcurrentDictionary<Guid, ToolkitCleanupPreview> _previews = new();
    private readonly ConcurrentDictionary<Guid, ToolkitRecycleBinPreview> _recyclePreviews = new();
    private const int MaximumFiles = 200_000;
    public ToolkitCleanupService() => _categories = BuildCategories;
    public ToolkitCleanupService(IReadOnlyList<ToolkitCleanupCategory> categories)
    {
        var copy = categories.Select(c => c with { Roots = Array.AsReadOnly(c.Roots.Select(ToolkitPaths.Full).ToArray()),
            BlockingProcesses = Array.AsReadOnly((c.BlockingProcesses ?? []).ToArray()) }).ToArray();
        if (copy.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != copy.Length || copy.Any(c => c.MinimumAgeDays < 0))
            throw new ArgumentException("Categorias duplicadas ou idade inválida.");
        if (copy.SelectMany(c => c.Roots).Any(p => p.Equals(Path.TrimEndingDirectorySeparator(Path.GetPathRoot(p)!), StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Uma raiz de volume não pode ser categoria de limpeza.");
        _categories = () => Array.AsReadOnly(copy);
    }
    public IReadOnlyList<ToolkitCleanupCategory> GetCategories() => _categories();
    public Task<ToolkitRecycleBinPreview> PreviewRecycleBinAsync(string? driveRoot = null, CancellationToken token = default) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        driveRoot = ValidateDrive(driveRoot); var state = QueryRecycleBin(driveRoot);
        var preview = new ToolkitRecycleBinPreview(Guid.NewGuid(), driveRoot, state.Bytes, state.Items, DateTimeOffset.UtcNow);
        foreach (var old in _recyclePreviews.Where(p => p.Value.CreatedAt < DateTimeOffset.UtcNow.AddMinutes(-5)).Select(p => p.Key)) _recyclePreviews.TryRemove(old, out _);
        _recyclePreviews[preview.Id] = preview; return preview;
    }, token);
    public Task<ToolkitRecycleBinResult> EmptyRecycleBinAsync(ToolkitRecycleBinPreview preview, nint owner = 0, CancellationToken token = default) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        if (!_recyclePreviews.TryRemove(preview.Id, out var original) || !ReferenceEquals(original, preview) || preview.CreatedAt < DateTimeOffset.UtcNow.AddMinutes(-5))
            throw new InvalidOperationException("A prévia da Lixeira expirou ou já foi utilizada.");
        var state = QueryRecycleBin(preview.DriveRoot);
        if (state.Bytes != preview.Bytes || state.Items != preview.Items) return new ToolkitRecycleBinResult(false, "A Lixeira mudou desde a prévia. Analise novamente.", state.Bytes, state.Items);
        if (state.Items == 0) return new(true, "A Lixeira já está vazia.", 0, 0);
        // Zero flags deliberately preserve the operating system's final confirmation UI.
        int status = SHEmptyRecycleBinW(owner, preview.DriveRoot, 0);
        var after = QueryRecycleBin(preview.DriveRoot);
        if (status != 0) return new(false, $"Esvaziamento cancelado ou recusado pelo Windows (0x{status:X8}).", after.Bytes, after.Items);
        return new(after.Items == 0, after.Items == 0 ? "Lixeira esvaziada e verificada. Esta ação não possui reversão." : "A Lixeira ainda contém itens; nenhuma recuperação de espaço total foi presumida.", after.Bytes, after.Items);
    }, token);
    private static string? ValidateDrive(string? drive)
    {
        if (drive is null) return null;
        drive = ToolkitPaths.Full(drive); var root = Path.GetPathRoot(drive)!;
        if (!drive.Equals(Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Selecione a raiz de um volume local para consultar a Lixeira.");
        return root;
    }
    private static (long Bytes, long Items) QueryRecycleBin(string? drive)
    {
        var info = new RecycleBinInformation { Size = Marshal.SizeOf<RecycleBinInformation>() };
        int status = SHQueryRecycleBinW(drive, ref info);
        if (status != 0) throw new System.ComponentModel.Win32Exception(status, "A Lixeira não pôde ser consultada.");
        return (info.Bytes, info.Items);
    }
    [StructLayout(LayoutKind.Sequential, Pack = 8)] private struct RecycleBinInformation { public int Size; public long Bytes, Items; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHQueryRecycleBinW(string? root, ref RecycleBinInformation info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHEmptyRecycleBinW(nint owner, string? root, uint flags);
    public Task<ToolkitCleanupPreview> PreviewAsync(IEnumerable<string> categoryIds, CancellationToken token = default) => Task.Run(() =>
    {
        var requested = categoryIds.Distinct(StringComparer.Ordinal).ToArray();
        var categories = GetCategories().ToDictionary(c => c.Id, StringComparer.Ordinal);
        if (requested.Any(id => !categories.ContainsKey(id))) throw new ArgumentException("Categoria de limpeza desconhecida.");
        var issues = new List<ToolkitIssue>(); var files = new List<ToolkitCleanupFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); bool complete = true;
        var now = DateTime.UtcNow;
        foreach (var id in requested)
        {
            var category = categories[id];
            if (IsBlocked(category)) { issues.Add(new(id, "Aplicativo aberto; feche-o e solicite uma nova prévia. Nenhum processo foi encerrado.")); continue; }
            foreach (var raw in category.Roots)
            {
                token.ThrowIfCancellationRequested();
                var root = ToolkitPaths.Full(raw);
                var stack = new Stack<(string Path, int Depth)>(); stack.Push((root, 0));
                while (stack.TryPop(out var item))
                {
                    token.ThrowIfCancellationRequested();
                    if (files.Count >= MaximumFiles) { complete = false; break; }
                    try
                    {
                        ToolkitPaths.EnsureNoLinks(item.Path);
                        if (File.Exists(item.Path))
                        {
                            if (!seen.Add(item.Path)) continue;
                            var file = Snapshot(item.Path, id);
                            // Age is per file, including files inside old temporary directories.
                            if (file.LastWriteUtc <= now.AddDays(-category.MinimumAgeDays) && ((FileAttributes)file.Attributes & FileAttributes.ReadOnly) == 0)
                                files.Add(file);
                        }
                        else if (Directory.Exists(item.Path))
                        {
                            if (item.Depth >= 100) { complete = false; issues.Add(new(item.Path, "Limite de profundidade atingido.")); continue; }
                            foreach (var child in ToolkitPaths.Children(item.Path, issues)) stack.Push((child, item.Depth + 1));
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
                    { if (issues.Count < 1000) issues.Add(new(item.Path, ToolkitPaths.DisplayException(ex))); }
                }
                if (!complete && files.Count >= MaximumFiles) break;
            }
        }
        if (!complete) issues.Add(new("Prévia", "Inventário parcial; somente os arquivos explicitamente listados poderão ser excluídos."));
        var preview = new ToolkitCleanupPreview(Guid.NewGuid(), DateTimeOffset.UtcNow, Array.AsReadOnly(files.ToArray()),
            Array.AsReadOnly(requested), Array.AsReadOnly(issues.ToArray()), complete && issues.Count == 0);
        foreach (var old in _previews.Where(p => p.Value.CreatedAt < DateTimeOffset.UtcNow.AddMinutes(-15)).Select(p => p.Key)) _previews.TryRemove(old, out _);
        _previews[preview.Id] = preview;
        return preview;
    }, token);

    public Task<ToolkitCleanupResult> ApplyAsync(ToolkitCleanupPreview preview, CancellationToken token = default) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        if (!_previews.TryRemove(preview.Id, out var original) || !ReferenceEquals(original, preview))
            throw new InvalidOperationException("A prévia não pertence a esta sessão ou já foi consumida.");
        if (preview.CreatedAt < DateTimeOffset.UtcNow.AddMinutes(-15)) throw new InvalidOperationException("A prévia expirou; analise novamente.");
        var categories = GetCategories().ToDictionary(c => c.Id, StringComparer.Ordinal);
        var processChecks = new Dictionary<string, (DateTime At, bool Blocked)>();
        var issues = new List<ToolkitIssue>(); long bytes = 0; int deleted = 0, skipped = 0, failed = 0; bool canceled = false;
        foreach (var expected in preview.Files)
        {
            if (token.IsCancellationRequested) { canceled = true; break; }
            try
            {
                if (!categories.TryGetValue(expected.CategoryId, out var category))
                { skipped++; issues.Add(new(expected.Path, "Aplicativo aberto ou categoria indisponível; preservado.")); continue; }
                if (!processChecks.TryGetValue(category.Id, out var check) || check.At < DateTime.UtcNow.AddSeconds(-1))
                    processChecks[category.Id] = check = (DateTime.UtcNow, IsBlocked(category));
                if (check.Blocked) { skipped++; if (issues.Count < 1000) issues.Add(new(expected.Path, "Aplicativo aberto; arquivo preservado.")); continue; }
                if (!category.Roots.Any(r => ToolkitPaths.IsWithin(expected.Path, ToolkitPaths.Full(r))))
                    throw new IOException("O arquivo deixou de pertencer à categoria.");
                ToolkitPaths.EnsureNoLinks(expected.Path);
                if (!File.Exists(expected.Path)) { skipped++; continue; }
                // Windows deletion is performed on the verified handle, not a second path lookup.
                using (var handle = Open(expected.Path, delete: true))
                {
                    var current = Snapshot(handle, expected.Path, expected.CategoryId);
                    if (current != expected) { skipped++; issues.Add(new(expected.Path, "Estado alterado após a prévia; preservado.")); continue; }
                    var delete = new FileDisposition { Delete = true };
                    if (!SetFileInformationByHandle(handle, 4, ref delete, 1)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                }
                if (File.Exists(expected.Path)) throw new IOException("A remoção ainda não pôde ser confirmada.");
                bytes += expected.Bytes; deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
            { failed++; if (issues.Count < 1000) issues.Add(new(expected.Path, ToolkitPaths.DisplayException(ex))); }
        }
        return new ToolkitCleanupResult(bytes, deleted, skipped, failed, Array.AsReadOnly(issues.ToArray()), canceled);
    }, token);

    private static bool IsBlocked(ToolkitCleanupCategory category)
    {
        if (category.BlockingProcesses is not { Count: > 0 }) return false;
        var processes = System.Diagnostics.Process.GetProcesses();
        try
        {
            foreach (var p in processes)
                try { if (category.BlockingProcesses.Any(pattern => pattern.EndsWith('*') ? p.ProcessName.StartsWith(pattern.TrimEnd('*'), StringComparison.OrdinalIgnoreCase) : p.ProcessName.Equals(pattern, StringComparison.OrdinalIgnoreCase))) return true; }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            return false;
        }
        finally { foreach (var p in processes) p.Dispose(); }
    }
    internal static ToolkitCleanupFile Snapshot(string path, string category)
    { using var handle = Open(path, false); return Snapshot(handle, path, category); }
    private static SafeFileHandle Open(string path, bool delete)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("A exclusão por identidade usa handles do Windows.");
        var handle = CreateFileW(path, 0x80u | (delete ? 0x10000u : 0), delete ? 1u : 7u, IntPtr.Zero, 3, 0x200000, IntPtr.Zero);
        if (handle.IsInvalid) { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new System.ComponentModel.Win32Exception(error); }
        return handle;
    }
    private static ToolkitCleanupFile Snapshot(SafeFileHandle handle, string path, string category)
    {
        if (!GetFileInformationByHandle(handle, out var info)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        if ((info.Attributes & (ToolkitPaths.ExcludedAttributes | 0x10u | 0x1u)) != 0) throw new IOException("Link, cloud, diretório ou arquivo somente leitura; preservado.");
        var buffer = new System.Text.StringBuilder(32768);
        if (GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0) == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var actual = buffer.ToString(); if (actual.StartsWith(@"\\?\")) actual = actual[4..];
        if (!ToolkitPaths.Full(actual).Equals(ToolkitPaths.Full(path), StringComparison.OrdinalIgnoreCase)) throw new IOException("O caminho físico diverge da prévia.");
        return new(path, category, checked((long)(((ulong)info.SizeHigh << 32) | info.SizeLow)), FileTime(info.Write), FileTime(info.Creation),
            info.Attributes, $"{info.VolumeSerial:X8}:{info.IndexHigh:X8}{info.IndexLow:X8}");
    }
    private static DateTime FileTime(System.Runtime.InteropServices.ComTypes.FILETIME value) => DateTime.FromFileTimeUtc(((long)value.dwHighDateTime << 32) | (uint)value.dwLowDateTime);
    [StructLayout(LayoutKind.Sequential)] private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
        public uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [StructLayout(LayoutKind.Sequential)] private struct FileDisposition { [MarshalAs(UnmanagedType.U1)] public bool Delete; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, System.Text.StringBuilder path, uint length, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, ref FileDisposition information, uint size);

    private static IReadOnlyList<ToolkitCleanupCategory> BuildCategories()
    {
        string l = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), r = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            w = Environment.GetFolderPath(Environment.SpecialFolder.Windows), pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var issues = new List<ToolkitIssue>();
        string[] Sub(string root, params string[] children) => children.Select(c => Path.Combine(root, c)).ToArray();
        string[] Profiles(string root, params string[] children) => new[] { root }.Concat(Directory.Exists(root) ? ToolkitPaths.Children(root, issues, true) : []).SelectMany(p => Sub(p, children)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var cache = new[] { "Cache", @"Cache\Cache_Data", "Code Cache", "GPUCache", @"Service Worker\CacheStorage" };
        var steam = ToolkitPaths.ReadSteamPath();
        return Array.AsReadOnly(new[]
        {
            new ToolkitCleanupCategory("temp", "Arquivos temporários", "Arquivos com mais de um dia; a idade é revalidada por arquivo.",
                new[] { Path.GetTempPath(), Path.Combine(w, "Temp") }.Where(p => System.Text.RegularExpressions.Regex.IsMatch(p, @"\\(?:Temp|Tmp)(?:\\\d+)?\\?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToArray(), 1, SelectedByDefault: true),
            new ToolkitCleanupCategory("crash", "Relatórios de falhas", "Remove dumps e relatórios; preserve-os se estiver investigando travamentos.",
                Sub(pd, @"Microsoft\Windows\WER\ReportArchive", @"Microsoft\Windows\WER\ReportQueue", @"Microsoft\Windows\WER\Temp").Concat(Sub(l, @"Microsoft\Windows\WER", "CrashDumps")).Concat(Sub(w, "Minidump", "MEMORY.DMP", "LiveKernelReports")).ToArray()),
            new ToolkitCleanupCategory("logs", "Logs antigos do Windows", "Logs de instalação e atualização com mais de sete dias.", Sub(Path.Combine(w, "Logs"), "CBS", "DISM", "MoSetup", "WindowsUpdate", "NetSetup", "SIH", "waasmedic", "dosvc"), 7),
            new ToolkitCleanupCategory("delivery", "Cache de entrega de atualizações", "Exclusão por arquivo sem parar serviços. Itens em uso e protegidos serão preservados; o Windows pode baixar o cache novamente.", Sub(w, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache")),
            new ToolkitCleanupCategory("shaders", "Cache de shaders", "DirectX/NVIDIA/AMD. Pode causar recompilação e travadas na primeira execução seguinte. Feche os jogos antes de selecionar.", Sub(l, "D3DSCache", @"NVIDIA\DXCache", @"NVIDIA\GLCache", @"AMD\DxCache", @"AMD\GLCache"), 1),
            new ToolkitCleanupCategory("drivers", "Instaladores de drivers", "Arquivos descompactados em AMD/NVIDIA; confirme que nenhum instalador está aberto.", Sub(Path.GetPathRoot(w)!, "AMD", "NVIDIA")),
            new ToolkitCleanupCategory("chrome", "Google Chrome", "Cache de sites; não inclui senhas, cookies, favoritos ou histórico.", Profiles(Path.Combine(l, @"Google\Chrome\User Data"), cache), BlockingProcesses: ["chrome"]),
            new ToolkitCleanupCategory("edge", "Microsoft Edge", "Cache de sites; exige navegador completamente fechado.", Profiles(Path.Combine(l, @"Microsoft\Edge\User Data"), cache), BlockingProcesses: ["msedge"]),
            new ToolkitCleanupCategory("brave", "Brave", "Cache de sites, sem dados de login.", Profiles(Path.Combine(l, @"BraveSoftware\Brave-Browser\User Data"), cache), BlockingProcesses: ["brave"]),
            new ToolkitCleanupCategory("opera", "Opera", "Cache de sites, sem dados de login.", Profiles(Path.Combine(r, @"Opera Software\Opera Stable"), cache).Concat(Profiles(Path.Combine(l, @"Opera Software\Opera Stable"), cache)).ToArray(), BlockingProcesses: ["opera"]),
            new ToolkitCleanupCategory("operagx", "Opera GX", "Cache de sites, sem dados de login.", Profiles(Path.Combine(r, @"Opera Software\Opera GX Stable"), cache).Concat(Profiles(Path.Combine(l, @"Opera Software\Opera GX Stable"), cache)).ToArray(), BlockingProcesses: ["opera"]),
            new ToolkitCleanupCategory("firefox", "Mozilla Firefox", "Cache, miniaturas e cache de inicialização.", Profiles(Path.Combine(l, @"Mozilla\Firefox\Profiles"), "cache2", "startupCache", "thumbnails", "jumpListCache"), BlockingProcesses: ["firefox"]),
            new ToolkitCleanupCategory("discord", "Discord", "Cache de mídia; a sessão e os dados de login são preservados.", new[] { "discord", "discordptb", "discordcanary" }.SelectMany(p => Sub(Path.Combine(r, p), cache)).ToArray(), BlockingProcesses: ["Discord", "DiscordPTB", "DiscordCanary"]),
            new ToolkitCleanupCategory("steam", "Steam", "Cache da loja e logs; os jogos e saves são preservados.", (steam is null ? [] : Sub(steam, @"appcache\httpcache", "logs", "dumps")).Concat(Sub(l, @"Steam\htmlcache")).ToArray(), BlockingProcesses: ["steam", "steamwebhelper"]),
            new ToolkitCleanupCategory("epic", "Epic Games Launcher", "Webcache e logs do launcher; os jogos são preservados.", Sub(l, @"EpicGamesLauncher\Saved\Logs").Concat(Directory.Exists(Path.Combine(l, @"EpicGamesLauncher\Saved")) ? ToolkitPaths.Children(Path.Combine(l, @"EpicGamesLauncher\Saved"), issues, true).Where(p => Path.GetFileName(p).StartsWith("webcache", StringComparison.OrdinalIgnoreCase)) : []).ToArray(), BlockingProcesses: ["EpicGamesLauncher"]),
            new ToolkitCleanupCategory("fivem", "FiveM: relatórios", "Logs e relatórios de crash; o cache dos servidores não é apagado.", Sub(l, @"FiveM\FiveM.app\logs", @"FiveM\FiveM.app\crashes"), BlockingProcesses: ["FiveM*"])
        });
    }
}

public sealed class ToolkitDiskService
{
    public Task<ToolkitDiskScan> ScanAsync(string root, IProgress<ToolkitScanProgress>? progress = null, CancellationToken token = default) => Task.Run(() =>
    {
        root = ToolkitPaths.Full(root); ToolkitPaths.EnsureNoLinks(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var issues = new List<ToolkitIssue>(); var nodes = new Dictionary<string, (long Own, long Total, long Count)>(StringComparer.OrdinalIgnoreCase);
        var largest = new PriorityQueue<ToolkitCleanupFile, long>(); var pending = new Stack<(string Path, int Depth)>(); pending.Push((root, 0));
        long total = 0, count = 0; bool complete = true; var watch = System.Diagnostics.Stopwatch.StartNew();
        while (pending.TryPop(out var item))
        {
            token.ThrowIfCancellationRequested();
            if (nodes.Count >= 100_000 || count >= 2_000_000) { complete = false; break; }
            long own = 0, ownCount = 0;
            foreach (var path in ToolkitPaths.Children(item.Path, issues))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var attributes = (uint)File.GetAttributes(path);
                    if ((attributes & ToolkitPaths.ExcludedAttributes) != 0) continue;
                    if ((attributes & 0x10) != 0) { if (item.Depth < 100) pending.Push((path, item.Depth + 1)); else complete = false; }
                    else
                    {
                        var info = new FileInfo(path); var bytes = info.Length; own += bytes; ownCount++; total += bytes; count++;
                        var file = new ToolkitCleanupFile(path, "scan", bytes, info.LastWriteTimeUtc, info.CreationTimeUtc, attributes, "");
                        if (largest.Count < 40) largest.Enqueue(file, bytes);
                        else if (largest.TryPeek(out _, out var smallest) && bytes > smallest) { largest.Dequeue(); largest.Enqueue(file, bytes); }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { if (issues.Count < 1000) issues.Add(new(path, ex.Message)); }
            }
            nodes[item.Path] = (own, own, ownCount);
            if (watch.ElapsedMilliseconds >= 250) { progress?.Report(new(nodes.Count, count, total, item.Path)); watch.Restart(); }
        }
        foreach (var path in nodes.Keys.OrderByDescending(p => p.Length).ToArray())
        {
            var parent = Path.GetDirectoryName(path);
            if (parent is not null && nodes.TryGetValue(parent, out var p)) { var child = nodes[path]; nodes[parent] = (p.Own, p.Total + child.Total, p.Count + child.Count); }
        }
        double threshold = Math.Max(8 * 1024 * 1024, total / 20000.0);
        while (nodes.Count(n => n.Value.Total >= threshold) > 30_000) threshold *= 2;
        return new ToolkitDiskScan(root, total, count, nodes.Where(n => n.Key == root || n.Value.Total >= threshold).Select(n => new ToolkitDiskNode(n.Key, n.Value.Own, n.Value.Total, n.Value.Count)).OrderByDescending(n => n.TotalBytes).ToArray(),
            largest.UnorderedItems.Select(x => x.Element).OrderByDescending(x => x.Bytes).ToArray(), issues.AsReadOnly(), complete && issues.Count == 0);
    }, token);
}
