using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VLOptimizer.Core;

public sealed record UwpPackageDefinition(string Id, string Title, string Impact)
{
    public string StoreSearchUrl => "https://apps.microsoft.com/search?query=" + Uri.EscapeDataString(Title);
    public string SourceUrl => "https://learn.microsoft.com/powershell/module/appx/remove-appxpackage?view=windowsserver2025-ps";
}
public sealed record UwpInstalledPackage(string Name, string PackageFullName, string Version, string Publisher, string FamilyName,
    bool NonRemovable = false, bool Framework = false, bool ResourcePackage = false);
public sealed record UwpEnvironment(bool SupportedOS, bool DualCcdX3d, string UserSid);
public sealed record UwpCommandResult(int? ExitCode, string Output, bool TimedOut = false);
public sealed record UwpRemovalPreview(Guid Id, UwpPackageDefinition Definition, UwpInstalledPackage Package,
    string UserSid, DateTimeOffset CreatedAt, bool CanApply, string? BlockReason)
{
    public string Consequence => "Remove o pacote somente do usuário atual. Sem desfazer pelo VL; dados do aplicativo podem ser perdidos. A busca na Store não garante disponibilidade de reinstalação.";
}
public sealed record UwpRemovalResult(OperationStatus Status, string Message, int? ExitCode)
{
    public bool CanUndo => false;
}
public interface IUwpSystemAccess
{
    UwpEnvironment InspectEnvironment();
    Task<IReadOnlyList<UwpInstalledPackage>> ReadCurrentUserPackagesAsync(CancellationToken token);
    Task<UwpCommandResult> RemoveCurrentUserPackageAsync(UwpInstalledPackage package, CancellationToken token);
}

/// <summary>Removal of ten explicitly listed optional packages in the current user's registration.
/// Provisioned packages, other accounts and protocol registrations are never changed.</summary>
public sealed class UwpManagementService
{
    private readonly IUwpSystemAccess system;
    private readonly Func<DateTimeOffset> clock;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<UwpRemovalPreview, DateTimeOffset> issued = new(ReferenceEqualityComparer.Instance);
    private static readonly IReadOnlyList<UwpPackageDefinition> catalog = Array.AsReadOnly(new[] {
        new UwpPackageDefinition("Microsoft.Xbox.TCUI", "Xbox TCUI", "Interface de conta Xbox; jogos e login Xbox podem depender deste pacote."),
        new UwpPackageDefinition("Microsoft.XboxApp", "Xbox Console Companion", "Aplicativo Xbox legado; funções associadas deixam de estar disponíveis."),
        new UwpPackageDefinition("Microsoft.XboxGamingOverlay", "Xbox Game Bar", "Remove Game Bar do usuário. Captura, overlay e reconhecimento de jogos podem ser afetados."),
        new UwpPackageDefinition("Microsoft.Cortana", "Cortana", "Remove o pacote Cortana legado se ele estiver registrado com este nome."),
        new UwpPackageDefinition("Microsoft.BingWeather", "Clima Microsoft", "Remove o aplicativo Clima e seus dados do usuário."),
        new UwpPackageDefinition("Microsoft.GetHelp", "Obter Ajuda", "Remove a interface Obter Ajuda; alguns fluxos de suporte podem depender dela."),
        new UwpPackageDefinition("Microsoft.People", "Pessoas Microsoft", "Remove o aplicativo Pessoas e suas integrações locais."),
        new UwpPackageDefinition("Microsoft.WindowsMaps", "Mapas Microsoft", "Remove o aplicativo Mapas. Conteúdo e funções offline podem ser perdidos."),
        new UwpPackageDefinition("Microsoft.ZuneMusic", "Media Player Microsoft", "Remove o pacote de música/Media Player identificado por Microsoft.ZuneMusic."),
        new UwpPackageDefinition("Microsoft.ZuneVideo", "Filmes e TV Microsoft", "Remove o aplicativo Filmes e TV e suas configurações locais.") });
    public UwpManagementService(IUwpSystemAccess? system = null, Func<DateTimeOffset>? clock = null)
    { this.system = system ?? new UwpWindowsAccess(); this.clock = clock ?? (() => DateTimeOffset.UtcNow); }
    public IReadOnlyList<UwpPackageDefinition> GetDefinitions() => catalog;
    public async Task<IReadOnlyList<UwpInstalledPackage>> InventoryAsync(CancellationToken token = default)
    {
        var env = system.InspectEnvironment();
        if (!env.SupportedOS) throw new PlatformNotSupportedException("Inventário de pacotes disponível no Windows 10/11 x64 cliente compatível.");
        return (await system.ReadCurrentUserPackagesAsync(token).ConfigureAwait(false)).Where(x => catalog.Any(d => d.Id == x.Name)).OrderBy(x => x.Name, StringComparer.Ordinal).ThenBy(x => x.PackageFullName, StringComparer.Ordinal).ToArray();
    }
    public async Task<UwpRemovalPreview> PreviewRemoveAsync(string packageFullName, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageFullName); await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (packageFullName.Length > 400) throw new ArgumentException("Identidade de pacote excede o limite.");
            var env = system.InspectEnvironment();
            var packages = await system.ReadCurrentUserPackagesAsync(token).ConfigureAwait(false);
            var found = packages.Where(x => x.PackageFullName == packageFullName).Take(2).ToArray();
            if (found.Length != 1) throw new InvalidOperationException("O pacote não está registrado de forma única para o usuário atual. Atualize o inventário.");
            var package = found[0];
            var definition = catalog.SingleOrDefault(x => x.Id == package.Name) ?? throw new InvalidOperationException("Pacote fora do catálogo fechado.");
            string? block = Guard(env, package);
            var preview = new UwpRemovalPreview(Guid.NewGuid(), definition, package, env.UserSid, clock(), block is null, block);
            foreach (var old in issued.Where(x => clock() - x.Value > TimeSpan.FromMinutes(10)).Select(x => x.Key).ToArray()) issued.Remove(old);
            if (preview.CanApply) issued[preview] = preview.CreatedAt;
            return preview;
        }
        finally { gate.Release(); }
    }
    public async Task<UwpRemovalResult> RemoveAsync(UwpRemovalPreview preview, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(preview); await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            if (!issued.Remove(preview, out var date) || clock() - date > TimeSpan.FromMinutes(10) || !preview.CanApply)
                return new(OperationStatus.Conflict, "A prévia expirou, já foi utilizada ou não pertence a esta sessão. Nenhum pacote foi removido.", null);
            var env = system.InspectEnvironment();
            string? block = Guard(env, preview.Package);
            if (block is not null || env.UserSid != preview.UserSid) return new(OperationStatus.Conflict, block ?? "O usuário atual mudou após a prévia. Nenhum pacote foi removido.", null);
            var current = (await system.ReadCurrentUserPackagesAsync(token).ConfigureAwait(false)).Where(x => x.PackageFullName == preview.Package.PackageFullName).Take(2).ToArray();
            if (current.Length != 1 || current[0] != preview.Package) return new(OperationStatus.Conflict, "A identidade ou as propriedades do pacote mudaram após a prévia. Atualize o inventário.", null);
            token.ThrowIfCancellationRequested();
            UwpCommandResult command;
            try { command = await system.RemoveCurrentUserPackageAsync(preview.Package, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return new(OperationStatus.Unverified, "A solicitação foi interrompida. Consulte novamente os pacotes; uma remoção já iniciada não é desfeita.", null); }
            catch (Exception ex) { return new(OperationStatus.Unverified, "A ferramenta não confirmou a remoção. Atualize o inventário. " + ex.Message, null); }
            if (command.TimedOut) return new(OperationStatus.Unverified, "O prazo de observação terminou. A implantação do Windows pode continuar; o processo não foi encerrado à força. Atualize o inventário antes de tentar novamente. " + command.Output, command.ExitCode);
            bool present;
            try { present = (await system.ReadCurrentUserPackagesAsync(CancellationToken.None).ConfigureAwait(false)).Any(x => x.PackageFullName == preview.Package.PackageFullName); }
            catch (Exception ex) { return new(OperationStatus.Unverified, "A ferramenta encerrou, mas a releitura de pacotes falhou. " + ex.Message + " " + command.Output, command.ExitCode); }
            if (command.ExitCode != 0) return new(present ? OperationStatus.Failed : OperationStatus.PartiallyCompleted, "O Windows retornou erro de implantação; ausência posterior não equivale a uma reversão disponível. " + command.Output, command.ExitCode);
            return present ? new(OperationStatus.Failed, "A ferramenta terminou com código zero, mas o pacote continua registrado para o usuário. " + command.Output, command.ExitCode)
                : new(OperationStatus.Completed, $"{preview.Definition.Title} não está mais registrado para este usuário. Não há desfazer no VL; reinstalação depende da disponibilidade na Microsoft Store.", command.ExitCode);
        }
        finally { gate.Release(); }
    }
    private static string? Guard(UwpEnvironment env, UwpInstalledPackage package)
    {
        if (!env.SupportedOS) return "Remoção disponível somente no Windows 10/11 x64 cliente compatível.";
        if (!Regex.IsMatch(env.UserSid, @"^S-1-(?:\d+-)*\d+$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))) return "Identidade do usuário não confirmada.";
        if (!ValidIdentity(package)) return "A identidade do pacote não corresponde ao catálogo Microsoft permitido.";
        if (package.NonRemovable || package.Framework || package.ResourcePackage) return "O Windows marca este pacote como protegido, framework ou recurso. A remoção não será solicitada.";
        if (env.DualCcdX3d && package.Name is "Microsoft.Xbox.TCUI" or "Microsoft.XboxGamingOverlay") return "Processador X3D com dois CCDs: preserve Game Bar e TCUI para o reconhecimento de jogos pelo escalonamento AMD.";
        return null;
    }
    public static bool ValidIdentity(UwpInstalledPackage package)
    {
        if (!catalog.Any(x => x.Id == package.Name) || package.PackageFullName.Length > 400) return false;
        string pattern = "^" + Regex.Escape(package.Name) + @"_\d+\.\d+\.\d+\.\d+_(?:x64|x86|arm64|arm|neutral)_[A-Za-z0-9.~\-]*_(8wekyb3d8bbwe|cw5n1h2txyewy)$";
        var match = Regex.Match(package.PackageFullName, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return match.Success && package.FamilyName == package.Name + "_" + match.Groups[1].Value;
    }
}

public sealed class UwpWindowsAccess : IUwpSystemAccess
{
    public UwpEnvironment InspectEnvironment()
    {
        var env = new ConfigurationWindowsAccess().InspectEnvironment();
        using var identity = WindowsIdentity.GetCurrent();
        return new(env.IsSupportedOS, env.IsDualCcdX3d, identity.User?.Value ?? "");
    }
    public async Task<IReadOnlyList<UwpInstalledPackage>> ReadCurrentUserPackagesAsync(CancellationToken token)
    {
        const string script = "Get-AppxPackage -ErrorAction Stop | Where-Object { $_.Name -in @('Microsoft.Xbox.TCUI','Microsoft.XboxApp','Microsoft.XboxGamingOverlay','Microsoft.Cortana','Microsoft.BingWeather','Microsoft.GetHelp','Microsoft.People','Microsoft.WindowsMaps','Microsoft.ZuneMusic','Microsoft.ZuneVideo') } | ForEach-Object { [pscustomobject]@{Name=$_.Name;PackageFullName=$_.PackageFullName;Version=[string]$_.Version;Publisher=$_.Publisher;FamilyName=$_.PackageFamilyName;NonRemovable=[bool]$_.NonRemovable;Framework=[bool]$_.IsFramework;ResourcePackage=[bool]$_.IsResourcePackage} }";
        var result = await RunAsync("$items = @(" + script + "); ConvertTo-Json -InputObject $items -Depth 4 -Compress", true, token).ConfigureAwait(false);
        if (result.TimedOut || result.ExitCode != 0) throw new IOException("Inventário Appx não confirmado: " + result.Output);
        return JsonSerializer.Deserialize<List<UwpInstalledPackage>>(result.Output.Trim(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, MaxDepth = 12 }) ?? [];
    }
    public Task<UwpCommandResult> RemoveCurrentUserPackageAsync(UwpInstalledPackage package, CancellationToken token)
    {
        if (!UwpManagementService.ValidIdentity(package) || package.NonRemovable || package.Framework || package.ResourcePackage) throw new InvalidOperationException("Identidade Appx não permitida.");
        string data = Convert.ToBase64String(Encoding.UTF8.GetBytes(package.PackageFullName));
        // Data is encoded only after allowlist/identity validation; neither script text nor arbitrary parameters enter through the UI.
        return RunAsync("$id=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + data + "')); Remove-AppxPackage -Package $id -ErrorAction Stop; Write-Output 'Solicitacao de remocao concluida.'", false, token);
    }
    private static async Task<UwpCommandResult> RunAsync(string closedScript, bool readOnly, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        string preamble = "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; [Console]::OutputEncoding=New-Object Text.UTF8Encoding($false); ";
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false) };
        foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(preamble + closedScript)) }) start.ArgumentList.Add(arg);
        var process = Process.Start(start) ?? throw new IOException("PowerShell do sistema não iniciou."); bool detached = false;
        Task<string> output = ReadBounded(process.StandardOutput), error = ReadBounded(process.StandardError);
        Task exited = process.WaitForExitAsync(CancellationToken.None);
        Task captured = Task.WhenAll(exited, output, error);
        try
        {
            // A child may inherit a pipe after the PowerShell parent exits. The deadline must
            // cover process exit AND both EOFs; waiting for the exit alone leaves an unbounded drain.
            try { await WaitForCaptureAsync(captured, TimeSpan.FromSeconds(readOnly ? 45 : 180), readOnly ? token : CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
            {
                if (readOnly)
                {
                    try { process.Kill(true); } catch { }
                    // Killing and draining an inventory helper also has a finite observation budget.
                    // If EOF remains held by another process, close only these reader handles.
                    try { await WaitForCaptureAsync(captured, TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false); }
                    catch
                    {
                        try { process.StandardOutput.Dispose(); } catch { }
                        try { process.StandardError.Dispose(); } catch { }
                        detached = true; _ = ObserveCompletionAsync(process, captured);
                    }
                    token.ThrowIfCancellationRequested();
                }
                else { detached = true; _ = ObserveCompletionAsync(process, captured); }
                return new(null, "O prazo de observação da ferramenta terminou.", true);
            }
            string stdout = await output.ConfigureAwait(false), stderr = await error.ConfigureAwait(false);
            return new(process.ExitCode, process.ExitCode == 0 ? stdout : stderr + " " + stdout);
        }
        finally { if (!detached) process.Dispose(); }
    }
    private static async Task<string> ReadBounded(StreamReader reader)
    {
        var text = new StringBuilder(); char[] buffer = new char[4096]; int count;
        while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0) if (text.Length < 1_048_576) text.Append(buffer, 0, Math.Min(count, 1_048_576 - text.Length));
        return text.ToString();
    }
    internal static Task WaitForCaptureAsync(Task captured, TimeSpan deadline, CancellationToken token) => captured.WaitAsync(deadline, token);
    private static async Task ObserveCompletionAsync(Process process, Task captured)
    {
        try { await captured.ConfigureAwait(false); }
        catch { }
        finally { process.Dispose(); }
    }
}
