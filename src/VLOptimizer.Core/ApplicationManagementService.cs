using Microsoft.Win32;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VLOptimizer.Core;

public record ToolkitApplicationMetadata(string Id, string Name, string? Publisher, string? InstallLocation, string UninstallCommand, bool WindowsInstaller);
public record ToolkitApplicationDirectories(string LocalAppData, string RoamingAppData, string ProgramFiles, string ProgramFilesX86, string Windows);
public record ToolkitManagedProcess(ToolkitProcessIdentity Identity, string Name, long MemoryBytes, bool IsCritical);
public sealed record ToolkitUninstallPreview(Guid Id, ToolkitApplicationMetadata Application, string Executable, IReadOnlyList<string> Arguments, string ExecutableSha256, DateTimeOffset CreatedAt);
public record ToolkitUninstallerExit(int? ExitCode, bool WaitingCanceled, string Message);
public enum ToolkitUninstallStatus { Prepared, Started, Completed, RestartRequired, Unverified, Failed }
public record ToolkitUninstallResult(Guid Id, string ApplicationId, string Name, ToolkitUninstallStatus Status, string Message, DateTimeOffset CreatedAt,
    ToolkitApplicationMetadata Metadata, int? ExitCode = null, DateTimeOffset? FinishedAt = null);
public sealed record ToolkitLeftoversPreview(Guid Id, string ApplicationId, string ApplicationName, ToolkitCleanupPreview Files, IReadOnlyList<string> CandidateRoots, DateTimeOffset CreatedAt);
public sealed record ToolkitKillProcessPreview(Guid Id, ToolkitManagedProcess Process, DateTimeOffset CreatedAt)
{
    public string Consequence => "Encerramento forçado apenas desse processo; dados não salvos podem ser perdidos. Sem reversão.";
}
public record ToolkitKillProcessResult(bool Success, bool Conflict, string Message);
public interface IToolkitApplicationSystem
{
    bool IsSupportedOS { get; }
    ToolkitApplicationDirectories Directories { get; }
    ToolkitApplicationMetadata? ReadApplication(string id);
    string ReadExecutableSha256(string executable);
    Task<ToolkitUninstallerExit> RunUninstallerAsync(string executable, IReadOnlyList<string> arguments, string expectedExecutableSha256, CancellationToken token);
    ToolkitManagedProcess? ReadProcess(int processId);
    Task<bool> TerminateProcessAsync(ToolkitProcessIdentity identity, CancellationToken token);
}

/// <summary>Explicit application management, distinct from diagnosis. No commands are run on construction or inventory.</summary>
public sealed class ApplicationManagementService
{
    private readonly string _directory;
    private readonly IToolkitApplicationSystem _system;
    private readonly ConcurrentDictionary<Guid, ToolkitUninstallPreview> _uninstall = new();
    private readonly ConcurrentDictionary<Guid, ToolkitKillProcessPreview> _kill = new();
    private readonly ConcurrentDictionary<Guid, (ToolkitLeftoversPreview Preview, ToolkitCleanupService Cleaner)> _leftovers = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly HashSet<string> DisallowedExecutables = new(StringComparer.OrdinalIgnoreCase)
    { "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "mshta.exe", "rundll32.exe", "regsvr32.exe", "reg.exe", "sc.exe", "schtasks.exe", "dotnet.exe", "java.exe", "javaw.exe", "python.exe", "pythonw.exe", "node.exe", "bash.exe", "wsl.exe", "certutil.exe", "bitsadmin.exe", "ftp.exe", "msbuild.exe", "installutil.exe" };
    public ApplicationManagementService(string dataDirectory, IToolkitApplicationSystem? system = null)
    { _directory = ToolkitPaths.Full(dataDirectory); _system = system ?? new ToolkitNativeApplicationSystem(); }

    public Task<ToolkitUninstallPreview> PreviewUninstallAsync(string installedAppId, CancellationToken token = default) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested(); var metadata = _system.ReadApplication(installedAppId) ?? throw new InvalidOperationException("O aplicativo já não consta no inventário.");
        var (executable, arguments) = ParseUninstallCommand(metadata);
        string hash = _system.ReadExecutableSha256(executable);
        var preview = new ToolkitUninstallPreview(Guid.NewGuid(), metadata, executable, Array.AsReadOnly(arguments), hash, DateTimeOffset.UtcNow);
        Prune(); _uninstall[preview.Id] = preview; return preview;
    }, token);
    public Task<ToolkitUninstallPreview> UninstallPreviewAsync(string installedAppId, CancellationToken token = default) => PreviewUninstallAsync(installedAppId, token);

    public async Task<ToolkitUninstallResult> UninstallAsync(ToolkitUninstallPreview preview, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); EnsureSupported();
        if (!_uninstall.TryRemove(preview.Id, out var original) || !ReferenceEquals(original, preview) || Expired(preview.CreatedAt)) throw new InvalidOperationException("Prévia inválida, expirada ou já consumida.");
        var mutex = Locks.GetOrAdd(_directory, _ => new(1, 1)); await mutex.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_system.ReadApplication(preview.Application.Id) != preview.Application || _system.ReadExecutableSha256(preview.Executable) != preview.ExecutableSha256)
                throw new InvalidOperationException("O aplicativo ou desinstalador mudou desde a prévia. Analise novamente.");
            EnsureStorage(); using var fileLock = await FileLockAsync(token).ConfigureAwait(false); var history = await ReadHistoryCoreAsync(token).ConfigureAwait(false);
            var record = new ToolkitUninstallResult(Guid.NewGuid(), preview.Application.Id, preview.Application.Name, ToolkitUninstallStatus.Prepared,
                "Estado registrado antes de iniciar o desinstalador. O resultado ainda não foi verificado.", DateTimeOffset.UtcNow, preview.Application);
            history.Add(record); await SaveHistoryAsync(history, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (_system.ReadApplication(preview.Application.Id) != preview.Application || _system.ReadExecutableSha256(preview.Executable) != preview.ExecutableSha256)
                throw new InvalidOperationException("Metadados mudaram durante a preparação; desinstalador não iniciado.");
            // Record Started before launching: interruption here is explicitly unverified on next opening.
            record = record with { Status = ToolkitUninstallStatus.Started }; Replace(history, record); await SaveHistoryAsync(history, token).ConfigureAwait(false);
            ToolkitUninstallerExit exit;
            try { exit = await _system.RunUninstallerAsync(preview.Executable, preview.Arguments, preview.ExecutableSha256, token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
            { exit = new(null, false, ex.Message); }
            bool absent = _system.ReadApplication(preview.Application.Id) is null;
            ToolkitUninstallStatus status = exit.WaitingCanceled ? ToolkitUninstallStatus.Unverified :
                exit.ExitCode is 0 or 1641 or 3010 ? absent ? exit.ExitCode is 1641 or 3010 ? ToolkitUninstallStatus.RestartRequired : ToolkitUninstallStatus.Completed : ToolkitUninstallStatus.Unverified : ToolkitUninstallStatus.Failed;
            string message = status switch
            {
                ToolkitUninstallStatus.Completed => "Desinstalador encerrou sem erro e a entrada de desinstalação desapareceu. Isso não certifica ausência de todo arquivo ou serviço.",
                ToolkitUninstallStatus.RestartRequired => "Desinstalador pediu reinicialização e a entrada desapareceu. Reinicie para concluir os componentes pendentes.",
                ToolkitUninstallStatus.Unverified => "Desinstalação não confirmada. O instalador pode continuar em outro processo; confira a lista e o assistente do fabricante.",
                _ => "O desinstalador não confirmou conclusão. " + exit.Message
            };
            record = record with { Status = status, Message = message, ExitCode = exit.ExitCode, FinishedAt = DateTimeOffset.UtcNow };
            Replace(history, record); await SaveHistoryAsync(history, CancellationToken.None).ConfigureAwait(false); return record;
        }
        finally { mutex.Release(); }
    }
    public async Task<IReadOnlyList<ToolkitUninstallResult>> GetHistoryAsync(CancellationToken token = default) => (await ReadHistoryCoreAsync(token).ConfigureAwait(false)).AsReadOnly();

    public async Task<ToolkitLeftoversPreview> PreviewLeftoversAsync(string applicationId, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var proof = (await ReadHistoryCoreAsync(token).ConfigureAwait(false)).LastOrDefault(r => r.ApplicationId == applicationId && r.Status is ToolkitUninstallStatus.Completed or ToolkitUninstallStatus.RestartRequired)
            ?? throw new InvalidOperationException("Sobras só podem ser analisadas depois de uma desinstalação confirmada pelo VL Optimizer.");
        if (_system.ReadApplication(applicationId) is not null) throw new InvalidOperationException("O aplicativo ainda está instalado; limpeza de sobras bloqueada.");
        var roots = CandidateRoots(proof.Metadata);
        var cleaner = new ToolkitCleanupService(new[] { new ToolkitCleanupCategory("leftovers", proof.Name, "Arquivos remanescentes em pastas específicas do aplicativo desinstalado.", roots) });
        var files = await cleaner.PreviewAsync(["leftovers"], token).ConfigureAwait(false);
        var preview = new ToolkitLeftoversPreview(Guid.NewGuid(), applicationId, proof.Name, files, roots, DateTimeOffset.UtcNow);
        Prune(); _leftovers[preview.Id] = (preview, cleaner); return preview;
    }
    public async Task<ToolkitCleanupResult> ApplyLeftoversAsync(ToolkitLeftoversPreview preview, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); EnsureSupported();
        if (!_leftovers.TryRemove(preview.Id, out var original) || !ReferenceEquals(original.Preview, preview) || Expired(preview.CreatedAt)) throw new InvalidOperationException("Prévia de sobras inválida ou consumida.");
        if (_system.ReadApplication(preview.ApplicationId) is not null) throw new InvalidOperationException("O aplicativo voltou ao inventário; arquivos preservados.");
        return await original.Cleaner.ApplyAsync(preview.Files, token).ConfigureAwait(false);
    }
    public Task<ToolkitKillProcessPreview> PreviewKillProcessAsync(int processId, CancellationToken token = default) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested(); var process = _system.ReadProcess(processId) ?? throw new InvalidOperationException("O processo já encerrou ou não pode ser verificado.");
        ValidateProcess(process); var preview = new ToolkitKillProcessPreview(Guid.NewGuid(), process, DateTimeOffset.UtcNow);
        Prune(); _kill[preview.Id] = preview; return preview;
    }, token);
    public async Task<ToolkitKillProcessResult> KillAsync(ToolkitKillProcessPreview preview, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); EnsureSupported();
        if (!_kill.TryRemove(preview.Id, out var original) || !ReferenceEquals(original, preview) || Expired(preview.CreatedAt)) throw new InvalidOperationException("Prévia de processo inválida ou consumida.");
        var current = _system.ReadProcess(preview.Process.Identity.Id);
        if (current is null) return new(true, false, "O processo original já encerrou; nenhuma ação foi executada.");
        ValidateProcess(current);
        if (current.Identity != preview.Process.Identity) return new(false, true, "A identidade do processo mudou; o novo processo foi preservado.");
        bool stopped = await _system.TerminateProcessAsync(current.Identity, token).ConfigureAwait(false);
        var after = _system.ReadProcess(current.Identity.Id);
        return new(stopped && (after is null || after.Identity != current.Identity), false,
            stopped && (after is null || after.Identity != current.Identity) ? "Encerramento do processo original confirmado." : "O encerramento não foi confirmado; nenhum processo descendente foi encerrado automaticamente.");
    }

    private (string Executable, string[] Arguments) ParseUninstallCommand(ToolkitApplicationMetadata metadata)
    {
        var arguments = ToolkitCommandLine.Parse(metadata.UninstallCommand);
        if (arguments.Length == 0) throw new InvalidDataException("Comando de desinstalação ausente.");
        string name = Path.GetFileName(arguments[0]);
        if (metadata.WindowsInstaller || name.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("msiexec", StringComparison.OrdinalIgnoreCase))
        {
            var matches = Regex.Matches(metadata.UninstallCommand, @"\{[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}");
            string? product = matches.Count == 1 ? matches[0].Value : Guid.TryParse(metadata.Id.Split(':').Last(), out var id) ? id.ToString("B") : null;
            if (product is null || !Guid.TryParse(product, out _)) throw new InvalidDataException("Não foi possível determinar um único código de produto MSI.");
            if (metadata.WindowsInstaller && Guid.TryParse(metadata.Id.Split(':').Last(), out var registeredProduct) && registeredProduct != Guid.Parse(product))
                throw new InvalidDataException("O produto MSI do comando diverge da entrada selecionada.");
            return (Path.Combine(_system.Directories.Windows, "System32", "msiexec.exe"), new[] { "/x", product, "/norestart" });
        }
        var executable = ToolkitPaths.Full(arguments[0]);
        if (!Path.IsPathFullyQualified(arguments[0]) || !Path.GetExtension(executable).Equals(".exe", StringComparison.OrdinalIgnoreCase) || DisallowedExecutables.Contains(Path.GetFileName(executable)) || ToolkitPaths.IsWithin(executable, _system.Directories.Windows))
            throw new InvalidDataException("Comando não suportado: use desinstalador EXE absoluto ou produto MSI. Interpretadores e scripts não são executados.");
        return (executable, arguments.Skip(1).ToArray());
    }
    private void ValidateProcess(ToolkitManagedProcess process)
    {
        string path = ToolkitPaths.Full(process.Identity.ExecutablePath);
        if (process.Identity.Id <= 4 || process.Identity.Id == Environment.ProcessId || process.Identity.StartedUtcTicks <= 0 || process.IsCritical || ToolkitPaths.IsWithin(path, _system.Directories.Windows))
            throw new InvalidOperationException("Processo crítico, do Windows ou do próprio VL Optimizer; encerramento bloqueado.");
        var deny = new[] { "system", "idle", "registry", "memory compression", "secure system", "csrss", "wininit", "winlogon", "lsass", "services", "smss", "svchost", "msmpeng", "securityhealthservice" };
        if (deny.Contains(Path.GetFileNameWithoutExtension(process.Name), StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("Processo protegido.");
    }
    private IReadOnlyList<string> CandidateRoots(ToolkitApplicationMetadata metadata)
    {
        string Clean(string value) => Regex.Replace(value, @"[^\p{L}\p{N} ._()-]", "").Trim(' ', '.');
        string Canon(string value) => Regex.Replace(value, @"[^\p{L}\p{N}]", "").ToLowerInvariant();
        var directories = _system.Directories; var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string name = Clean(Regex.Replace(metadata.Name, @"\s+(?:v?\d+(?:\.\d+)+|x64|x86|\(64-bit\)|\(32-bit\))\s*$", "", RegexOptions.IgnoreCase));
        var generic = new HashSet<string>(new[] { "microsoft", "windows", "programfiles", "programs", "applications", "appdata", "local", "roaming", "tools", "common", "shared", "vendor", "steam", "epicgames", "google", "mozilla" }, StringComparer.OrdinalIgnoreCase);
        if (name.Length >= 3 && !generic.Contains(Canon(name)))
        {
            foreach (var folder in new[] { directories.LocalAppData, directories.RoamingAppData })
            {
                roots.Add(ToolkitPaths.Full(Path.Combine(folder, name)));
                if (!string.IsNullOrWhiteSpace(metadata.Publisher))
                {
                    string publisher = Clean(metadata.Publisher); if (publisher.Length >= 3) roots.Add(ToolkitPaths.Full(Path.Combine(folder, publisher, name)));
                }
            }
        }
        if (!string.IsNullOrWhiteSpace(metadata.InstallLocation))
        {
            string install = ToolkitPaths.Full(metadata.InstallLocation); string leaf = Canon(Path.GetFileName(install)); string app = Canon(name);
            bool specific = leaf.Length >= 4 && app.Length >= 4 && !generic.Contains(leaf) && (leaf.Contains(app, StringComparison.Ordinal) || app.Contains(leaf, StringComparison.Ordinal));
            string? profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            bool broad = new[] { directories.LocalAppData, directories.RoamingAppData, directories.ProgramFiles, directories.ProgramFilesX86, directories.Windows, profile, Path.GetPathRoot(install) }.Where(p => !string.IsNullOrWhiteSpace(p)).Any(p => ToolkitPaths.Full(p!).Equals(install, StringComparison.OrdinalIgnoreCase));
            if (specific && !broad && !ToolkitPaths.IsWithin(install, directories.Windows)) roots.Add(install);
        }
        return Array.AsReadOnly(roots.Where(Directory.Exists).ToArray());
    }
    private void EnsureSupported() { if (!_system.IsSupportedOS) throw new PlatformNotSupportedException("Alterações de aplicativos exigem Windows 10/11 x64 cliente compatível."); }
    private static bool Expired(DateTimeOffset at) => at < DateTimeOffset.UtcNow.AddMinutes(-10);
    private void Prune()
    {
        foreach (var item in _uninstall.Where(x => Expired(x.Value.CreatedAt)).Select(x => x.Key)) _uninstall.TryRemove(item, out _);
        foreach (var item in _kill.Where(x => Expired(x.Value.CreatedAt)).Select(x => x.Key)) _kill.TryRemove(item, out _);
        foreach (var item in _leftovers.Where(x => Expired(x.Value.Preview.CreatedAt)).Select(x => x.Key)) _leftovers.TryRemove(item, out _);
    }
    private void EnsureStorage() { ToolkitPaths.EnsureNoLinks(_directory); Directory.CreateDirectory(_directory); ToolkitPaths.EnsureNoLinks(_directory); }
    private async Task<FileStream> FileLockAsync(CancellationToken token)
    {
        string path = Path.Combine(_directory, "applications.lock"); ToolkitPaths.EnsureNoLinks(path);
        for (int retry = 0; ; retry++) { token.ThrowIfCancellationRequested(); try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); } catch (IOException) when (retry < 50) { await Task.Delay(100, token).ConfigureAwait(false); } }
    }
    private async Task<List<ToolkitUninstallResult>> ReadHistoryCoreAsync(CancellationToken token)
    {
        string path = Path.Combine(_directory, "applications.json"); if (!File.Exists(path)) return [];
        ToolkitPaths.EnsureNoLinks(path); if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("Histórico excede o limite.");
        return JsonSerializer.Deserialize<List<ToolkitUninstallResult>>(await File.ReadAllTextAsync(path, token).ConfigureAwait(false)) ?? throw new InvalidDataException("Histórico inválido.");
    }
    private async Task SaveHistoryAsync(List<ToolkitUninstallResult> history, CancellationToken token)
    {
        string path = Path.Combine(_directory, "applications.json"), temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            { await JsonSerializer.SerializeAsync(stream, history, Json, token).ConfigureAwait(false); await stream.FlushAsync(token).ConfigureAwait(false); stream.Flush(true); }
            ToolkitPaths.EnsureNoLinks(path); File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static void Replace(List<ToolkitUninstallResult> records, ToolkitUninstallResult record) => records[records.FindIndex(r => r.Id == record.Id)] = record;
}

internal static class ToolkitCommandLine
{
    internal static string[] Parse(string command)
    {
        if (string.IsNullOrWhiteSpace(command) || command.Length > 32768 || command.Contains('\0')) throw new InvalidDataException("Comando vazio ou inválido.");
        nint arguments = CommandLineToArgvW(command, out int count);
        if (arguments == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try { return Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(arguments, i * IntPtr.Size)) ?? "").ToArray(); }
        finally { LocalFree(arguments); }
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CommandLineToArgvW(string command, out int arguments);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
}

public sealed class ToolkitNativeApplicationSystem : IToolkitApplicationSystem
{
    public bool IsSupportedOS => new WindowsSystemAccess().IsSupportedOS;
    public ToolkitApplicationDirectories Directories => new(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.Windows));
    public ToolkitApplicationMetadata? ReadApplication(string id)
    {
        var parts = id.Split(':', 3);
        if (parts.Length != 3 || !Enum.TryParse<RegistryHive>(parts[0], out var hive) || hive is not (RegistryHive.LocalMachine or RegistryHive.CurrentUser) ||
            !Enum.TryParse<RegistryView>(parts[1], out var view) || view is not (RegistryView.Registry32 or RegistryView.Registry64) || parts[2].IndexOfAny(['\\', '/', '\0']) >= 0 || parts[2].Length is 0 or > 255)
            throw new ArgumentException("ID de aplicativo inválido.");
        using var root = RegistryKey.OpenBaseKey(hive, view); using var key = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + parts[2]);
        if (key?.GetValue("DisplayName") is not string name) return null;
        return new(id, name, key.GetValue("Publisher") as string, key.GetValue("InstallLocation") as string, key.GetValue("UninstallString") as string ?? "", key.GetValue("WindowsInstaller") is int and 1);
    }
    public string ReadExecutableSha256(string executable)
    {
        executable = ToolkitPaths.Full(executable); ToolkitPaths.EnsureNoLinks(executable);
        using var stream = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 1024L * 1024 * 1024) throw new InvalidDataException("Desinstalador excede 1 GiB.");
        Span<byte> signature = stackalloc byte[2]; stream.ReadExactly(signature); if (signature[0] != 'M' || signature[1] != 'Z') throw new InvalidDataException("O desinstalador não é um executável PE."); stream.Position = 0;
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    public async Task<ToolkitUninstallerExit> RunUninstallerAsync(string executable, IReadOnlyList<string> arguments, string expectedExecutableSha256, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); if (!IsSupportedOS) throw new PlatformNotSupportedException();
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)!, WindowStyle = ProcessWindowStyle.Normal };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        Process? launched;
        // Deny writes/replacement while validating and creating the process image; release immediately
        // after launch so uninstallers that remove their own executable are not blocked by this reader.
        ToolkitPaths.EnsureNoLinks(executable);
        using (var image = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (Convert.ToHexString(SHA256.HashData(image)) != expectedExecutableSha256) throw new IOException("O executável mudou antes da criação do processo.");
            launched = Process.Start(start);
        }
        using var process = launched ?? throw new IOException("Não foi possível abrir o desinstalador.");
        try { await process.WaitForExitAsync(token).ConfigureAwait(false); return new(process.ExitCode, false, "Desinstalador encerrou."); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return new(null, true, "A espera foi cancelada; o assistente do fabricante continua independente e não foi encerrado à força."); }
    }
    public ToolkitManagedProcess? ReadProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId); if (process.HasExited) return null;
            var path = process.MainModule?.FileName; if (path is null) return null;
            bool critical = !IsProcessCritical(process.SafeHandle.DangerousGetHandle(), out bool isCritical) || isCritical;
            return new(new(process.Id, process.StartTime.ToUniversalTime().Ticks, ToolkitPaths.Full(path)), process.ProcessName, process.WorkingSet64, critical);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception or UnauthorizedAccessException) { return null; }
    }
    public async Task<bool> TerminateProcessAsync(ToolkitProcessIdentity identity, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); if (!IsSupportedOS) throw new PlatformNotSupportedException();
        try
        {
            using var process = Process.GetProcessById(identity.Id); _ = process.SafeHandle;
            if (process.HasExited || process.Id <= 4 || process.Id == Environment.ProcessId || process.StartTime.ToUniversalTime().Ticks != identity.StartedUtcTicks || !string.Equals(process.MainModule?.FileName, identity.ExecutablePath, StringComparison.OrdinalIgnoreCase)) return false;
            if (!IsProcessCritical(process.SafeHandle.DangerousGetHandle(), out bool critical) || critical) return false;
            process.Kill(entireProcessTree: false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try { await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false); return true; } catch (OperationCanceledException) { return process.HasExited; }
        }
        catch (ArgumentException) { return true; }
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool IsProcessCritical(nint process, [MarshalAs(UnmanagedType.Bool)] out bool critical);
}
