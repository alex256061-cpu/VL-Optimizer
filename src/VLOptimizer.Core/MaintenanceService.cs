using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace VLOptimizer.Core;

public enum MaintenanceKind { FlushDns, ResetNetwork, RepairWindows, CleanupWindowsUpdate, CreateRestorePoint, RestartExplorer, EnableDefender, EnableSystemRestore }
public enum MaintenanceStageKind { FlushDns, ResetWinsock, ResetIp, DismRepair, SfcScan, DismCleanup, RestorePoint, RestartExplorer, EnableDefender, EnableSystemRestore }
public record MaintenanceDefinition(MaintenanceKind Kind, string Title, string Description, bool RequiresAdmin, bool RequiresRestart, string Warning, string SourceUrl);
public record MaintenanceEnvironment(bool SupportedOS, bool Administrator);
public sealed record MaintenancePreview(Guid Id, MaintenanceDefinition Definition, DateTimeOffset CreatedAt, bool CanApply, string? BlockReason, IReadOnlyList<MaintenanceStageKind> Stages);
public record MaintenanceStage(MaintenanceStageKind Kind, int? ExitCode, string Output, bool Verified = false, bool TimedOut = false)
{
    public bool Accepted => !TimedOut && ExitCode is 0 or 3010;
}
public record MaintenanceResult(OperationStatus Status, string Message, IReadOnlyList<MaintenanceStage> Stages);
public interface IMaintenanceSystemAccess
{
    MaintenanceEnvironment InspectEnvironment();
    Task<MaintenanceStage> ExecuteStageAsync(MaintenanceStageKind stage, IProgress<string>? progress, CancellationToken token);
}

/// <summary>Closed, explicit maintenance operations. Issued approvals are single-use and never accept command text.</summary>
public sealed class MaintenanceService
{
    private readonly IMaintenanceSystemAccess system;
    private readonly Func<DateTimeOffset> clock;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<MaintenancePreview, DateTimeOffset> issued = new(ReferenceEqualityComparer.Instance);
    private static readonly IReadOnlyList<MaintenanceDefinition> Definitions = Array.AsReadOnly(new[] {
        new MaintenanceDefinition(MaintenanceKind.FlushDns, "Limpar cache DNS", "Descarta o cache local de resolução de nomes. Não altera os servidores DNS do adaptador.", false, false, "A próxima consulta de cada nome poderá precisar consultar o servidor novamente.", "https://learn.microsoft.com/windows-server/networking/dns/troubleshoot/troubleshoot-dns-data-collection"),
        new MaintenanceDefinition(MaintenanceKind.ResetNetwork, "Redefinir pilha de rede", "Executa o reset de Winsock e de TCP/IP pelas ferramentas oficiais do Windows.", true, true, "Pode remover ajustes de rede e exigir reconfigurar VPN ou IP estático. Salve os seus dados de conexão. Sem restauração pelo VL.", "https://learn.microsoft.com/troubleshoot/windows-server/networking/reset-tcp-ip-net-shell"),
        new MaintenanceDefinition(MaintenanceKind.RepairWindows, "Verificar e reparar Windows", "Executa DISM RestoreHealth e, se a etapa for aceita, SFC scannow. O laudo completo fica no resultado.", true, false, "Pode demorar e usar o Windows Update. Cancelar é atendido entre ferramentas; uma manutenção iniciada não é encerrada à força. O código de saída não prova ausência de corrupção.", "https://support.microsoft.com/windows/using-system-file-checker-in-windows-365e0031-36b1-6031-f804-8fd86e0ef4ca"),
        new MaintenanceDefinition(MaintenanceKind.CleanupWindowsUpdate, "Limpar componentes do Windows", "Solicita DISM StartComponentCleanup para remover componentes substituídos conforme as regras do Windows.", true, false, "Manutenção do repositório de componentes, sem apagar pastas do Windows manualmente. Não usa ResetBase. Pode demorar; não possui desfazer no VL.", "https://learn.microsoft.com/windows-hardware/manufacture/desktop/clean-up-the-winsxs-folder"),
        new MaintenanceDefinition(MaintenanceKind.CreateRestorePoint, "Criar ponto de restauração", "Solicita um ponto de restauração e confere a presença de uma nova sequência com o rótulo VL Optimizer.", true, false, "Depende da Proteção do Sistema e das regras de frequência do Windows. Não ativa a proteção nem altera seus limites. Não substitui backup de arquivos.", "https://learn.microsoft.com/powershell/module/microsoft.powershell.management/checkpoint-computer?view=powershell-5.1"),
        new MaintenanceDefinition(MaintenanceKind.RestartExplorer, "Reiniciar Explorer", "Reinicia apenas o processo do shell associado à janela do usuário atual e verifica o novo shell.", false, false, "A área de trabalho, a barra de tarefas e janelas do Explorer podem desaparecer brevemente. Termine transferências e salve seu trabalho antes de continuar.", "https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getshellwindow"),
        new MaintenanceDefinition(MaintenanceKind.EnableDefender, "Solicitar proteção em tempo real", "Solicita a ativação pelo cmdlet Defender e confere AntivirusEnabled e RealTimeProtectionEnabled. Não remove políticas nem tenta contornar proteção contra adulteração.", true, false, "Outro antivírus, modo passivo ou política da organização podem impedir a ativação. O resultado é relido e não há opção de desligar a proteção pelo VL.", "https://learn.microsoft.com/powershell/module/defender/set-mppreference"),
        new MaintenanceDefinition(MaintenanceKind.EnableSystemRestore, "Ativar Proteção do Sistema", "Solicita Enable-ComputerRestore somente para a unidade do Windows. A solicitação não cria um ponto nem altera o limite de espaço.", true, false, "Confira depois a Proteção do Sistema. Uma solicitação aceita não é prova independente de recuperação disponível; crie e verifique um ponto antes de depender dele.", "https://learn.microsoft.com/powershell/module/microsoft.powershell.management/enable-computerrestore?view=powershell-5.1") });

    public MaintenanceService(IMaintenanceSystemAccess? system = null, Func<DateTimeOffset>? clock = null)
    { this.system = system ?? new MaintenanceWindowsAccess(); this.clock = clock ?? (() => DateTimeOffset.UtcNow); }
    public IReadOnlyList<MaintenanceDefinition> GetDefinitions() => Definitions;
    private static MaintenanceStageKind[] Plan(MaintenanceKind kind) => kind switch {
        MaintenanceKind.FlushDns => [MaintenanceStageKind.FlushDns], MaintenanceKind.ResetNetwork => [MaintenanceStageKind.ResetWinsock, MaintenanceStageKind.ResetIp],
        MaintenanceKind.RepairWindows => [MaintenanceStageKind.DismRepair, MaintenanceStageKind.SfcScan], MaintenanceKind.CleanupWindowsUpdate => [MaintenanceStageKind.DismCleanup],
        MaintenanceKind.CreateRestorePoint => [MaintenanceStageKind.RestorePoint], MaintenanceKind.RestartExplorer => [MaintenanceStageKind.RestartExplorer],
        MaintenanceKind.EnableDefender => [MaintenanceStageKind.EnableDefender], MaintenanceKind.EnableSystemRestore => [MaintenanceStageKind.EnableSystemRestore], _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
    public async Task<MaintenancePreview> PreviewAsync(MaintenanceKind kind, CancellationToken token = default)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var definition = Definitions.SingleOrDefault(d => d.Kind == kind) ?? throw new ArgumentOutOfRangeException(nameof(kind));
            var environment = await Task.Run(system.InspectEnvironment, token).ConfigureAwait(false);
            var reason = !environment.SupportedOS ? "Esta manutenção exige Windows 10 ou 11 x64 cliente compatível." : definition.RequiresAdmin && !environment.Administrator ? "Reabra o VL Optimizer como administrador para executar esta manutenção." : null;
            var preview = new MaintenancePreview(Guid.NewGuid(), definition, clock(), reason is null, reason, Array.AsReadOnly(Plan(kind)));
            foreach (var old in issued.Where(p => clock() - p.Value > TimeSpan.FromMinutes(10)).Select(p => p.Key).ToArray()) issued.Remove(old);
            if (preview.CanApply) issued[preview] = preview.CreatedAt;
            return preview;
        }
        finally { gate.Release(); }
    }
    public async Task<MaintenanceResult> ExecuteAsync(MaintenancePreview preview, IProgress<string>? progress = null, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(preview); await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            if (!issued.Remove(preview, out var created) || clock() - created > TimeSpan.FromMinutes(10) || !preview.CanApply)
                return new(OperationStatus.Conflict, "A prévia expirou, já foi usada ou não pertence a esta sessão.", []);
            var environment = system.InspectEnvironment();
            if (!environment.SupportedOS || preview.Definition.RequiresAdmin && !environment.Administrator)
                return new(OperationStatus.Failed, "A plataforma ou os privilégios mudaram após a prévia. Nenhuma ferramenta foi iniciada.", []);
            var stages = new List<MaintenanceStage>();
            try
            {
                foreach (var stage in Plan(preview.Definition.Kind))
                {
                    token.ThrowIfCancellationRequested(); progress?.Report("Executando " + stage + "…");
                    MaintenanceStage result;
                    try { result = await system.ExecuteStageAsync(stage, progress, token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { result = new(stage, null, ex.Message); }
                    stages.Add(result);
                    if (!result.Accepted) break;
                }
            }
            catch (OperationCanceledException) when (stages.Count > 0)
            { return new(OperationStatus.PartiallyCompleted, "Cancelado entre etapas. Consulte as ferramentas que chegaram a ser executadas; elas não foram desfeitas.", stages.AsReadOnly()); }
            if (stages.Any(s => s.TimedOut)) return new(OperationStatus.Unverified, "A ferramenta ultrapassou o prazo de observação e pode continuar no Windows. Não foi encerrada à força; nenhuma etapa posterior foi iniciada.", stages.AsReadOnly());
            if (stages.Any(s => !s.Accepted)) return new(stages.Any(s => s.Accepted) ? OperationStatus.PartiallyCompleted : OperationStatus.Failed, "Uma ferramenta não confirmou conclusão pelo código de saída. Consulte a saída de cada etapa; não foi presumido reparo bem-sucedido.", stages.AsReadOnly());
            if (preview.Definition.RequiresRestart || stages.Any(s => s.ExitCode == 3010)) return new(OperationStatus.RestartRequired, "Ferramentas concluídas pelo código de saída. Reinicie o Windows por sua escolha para concluir a alteração.", stages.AsReadOnly());
            if (stages.Any(s => !s.Verified)) return new(OperationStatus.Unverified, "Ferramentas finalizadas com código aceito. Consulte o laudo do Windows; o VL não verificou uma pós-condição independente de saúde do sistema.", stages.AsReadOnly());
            return new(OperationStatus.Completed, "Manutenção concluída e a pós-condição disponível foi conferida.", stages.AsReadOnly());
        }
        finally { gate.Release(); }
    }
}

/// <summary>Only built-in commands at fixed Windows paths are launched. No arbitrary arguments cross this boundary.</summary>
public sealed class MaintenanceWindowsAccess : IMaintenanceSystemAccess
{
    public MaintenanceEnvironment InspectEnvironment()
    { var environment = new ConfigurationWindowsAccess().InspectEnvironment(); return new(environment.IsSupportedOS, environment.IsAdministrator); }
    public Task<MaintenanceStage> ExecuteStageAsync(MaintenanceStageKind stage, IProgress<string>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return stage switch {
            MaintenanceStageKind.FlushDns => Task.Run(() => { if (DnsFlushResolverCache()) return new MaintenanceStage(stage, 0, "A API DNS confirmou a limpeza do cache local.", true); int error = Marshal.GetLastWin32Error(); return new MaintenanceStage(stage, error == 0 ? 1 : error, "A API DNS não confirmou a limpeza."); }, token),
            MaintenanceStageKind.RestartExplorer => RestartExplorerAsync(token),
            _ => RunCommandAsync(stage, progress, token) };
    }
    private static async Task<MaintenanceStage> RunCommandAsync(MaintenanceStageKind stage, IProgress<string>? progress, CancellationToken token)
    {
        var (name, arguments, minutes) = stage switch {
            MaintenanceStageKind.EnableDefender => ("WindowsPowerShell\\v1.0\\powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-Command", "$ErrorActionPreference='Stop'; Set-MpPreference -DisableRealtimeMonitoring $false -ErrorAction Stop; $status=Get-MpComputerStatus -ErrorAction Stop; if(-not $status.AntivirusEnabled -or -not $status.RealTimeProtectionEnabled){throw 'A proteção em tempo real não foi confirmada. Confira a Segurança do Windows e as políticas.'}; Write-Output 'VL_DEFENDER_ENABLED_VERIFIED'" }, 5),
            MaintenanceStageKind.EnableSystemRestore => ("WindowsPowerShell\\v1.0\\powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-Command", "$ErrorActionPreference='Stop'; Enable-ComputerRestore -Drive ($env:SystemDrive + '\\') -ErrorAction Stop; Write-Output 'Solicitacao aceita. Confira a Protecao do Sistema e crie um ponto separado.'" }, 5),
            MaintenanceStageKind.ResetWinsock => ("netsh.exe", new[] { "winsock", "reset" }, 5),
            MaintenanceStageKind.ResetIp => ("netsh.exe", new[] { "int", "ip", "reset" }, 5),
            MaintenanceStageKind.DismRepair => ("Dism.exe", new[] { "/Online", "/Cleanup-Image", "/RestoreHealth", "/NoRestart" }, 90),
            MaintenanceStageKind.SfcScan => ("sfc.exe", new[] { "/scannow" }, 90),
            MaintenanceStageKind.DismCleanup => ("Dism.exe", new[] { "/Online", "/Cleanup-Image", "/StartComponentCleanup", "/NoRestart" }, 90),
            MaintenanceStageKind.RestorePoint => ("WindowsPowerShell\\v1.0\\powershell.exe", new[] { "-NoProfile", "-NonInteractive", "-Command", "$ErrorActionPreference='Stop'; $before=@(Get-ComputerRestorePoint -ErrorAction Stop | Select-Object -ExpandProperty SequenceNumber); Checkpoint-Computer -Description 'VL Optimizer' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop; $created=@(Get-ComputerRestorePoint -ErrorAction Stop | Where-Object {$_.Description -eq 'VL Optimizer' -and $_.SequenceNumber -notin $before}); if($created.Count -eq 0){throw 'Nenhum novo ponto de restauração foi confirmado.'}; $created | Select-Object SequenceNumber,Description,CreationTime | ConvertTo-Json -Compress" }, 20),
            _ => throw new ArgumentOutOfRangeException(nameof(stage)) };
        string executable = Path.Combine(Environment.SystemDirectory, name);
        if (!File.Exists(executable)) throw new FileNotFoundException("A ferramenta oficial do Windows não foi encontrada.", executable);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Environment.SystemDirectory };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        token.ThrowIfCancellationRequested(); var process = Process.Start(start) ?? throw new InvalidOperationException("A ferramenta não iniciou.");
        var stdout = ReadBoundedAsync(process.StandardOutput.BaseStream); var stderr = ReadBoundedAsync(process.StandardError.BaseStream);
        var exited = process.WaitForExitAsync(CancellationToken.None);
        var completed = Task.WhenAll(exited, stdout, stderr);
        // Servicing processes are never killed on a UI cancellation. Cancellation is observed between stages.
        bool handedOff = false;
        try
        {
            if (await Task.WhenAny(completed, Task.Delay(TimeSpan.FromMinutes(minutes))).ConfigureAwait(false) != completed)
            {
                // Keep draining the bounded pipes until exit, rather than breaking a live servicing tool's stdout.
                handedOff = true; _ = ObserveTimedOutProcessAsync(process, exited, stdout, stderr);
                return new(stage, null, $"Tempo de observação excedido ({minutes} min). A ferramenta Windows pode continuar em execução; consulte os logs oficiais.", false, true);
            }
            await completed.ConfigureAwait(false);
            var output = Decode(await stdout.ConfigureAwait(false)); var error = Decode(await stderr.ConfigureAwait(false));
            progress?.Report("Ferramenta finalizada · código " + process.ExitCode);
            string text = (output + "\n" + error).Trim();
            if (output.Length >= 65536 || error.Length >= 65536) text += "\n[Saída limitada; consulte os logs oficiais para o conteúdo integral.]";
            return new(stage, process.ExitCode, text, process.ExitCode == 0 && (stage == MaintenanceStageKind.RestorePoint || stage == MaintenanceStageKind.EnableDefender && text.Contains("VL_DEFENDER_ENABLED_VERIFIED", StringComparison.Ordinal)));
        }
        finally { if (!handedOff) process.Dispose(); }
    }
    private static async Task ObserveTimedOutProcessAsync(Process process, Task exited, Task<byte[]> stdout, Task<byte[]> stderr)
    { try { await Task.WhenAll(exited, stdout, stderr).ConfigureAwait(false); } catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException) { } finally { process.Dispose(); } }
    private static async Task<byte[]> ReadBoundedAsync(Stream stream)
    {
        using var bytes = new MemoryStream(); byte[] buffer = new byte[4096]; int read;
        while ((read = await stream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        { int allowed = Math.Min(read, 131072 - (int)bytes.Length); if (allowed > 0) bytes.Write(buffer, 0, allowed); }
        return bytes.ToArray();
    }
    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 2 && (bytes[0] == 0xff && bytes[1] == 0xfe || bytes.Take(Math.Min(bytes.Length, 100)).Count(b => b == 0) > Math.Min(bytes.Length, 100) / 4)) return Encoding.Unicode.GetString(bytes).TrimStart('\uFEFF');
        return Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
    }
    private static async Task<MaintenanceStage> RestartExplorerAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); IntPtr shell = GetShellWindow(); if (shell == IntPtr.Zero) return new(MaintenanceStageKind.RestartExplorer, 1, "Nenhum shell do Explorer foi encontrado.");
        GetWindowThreadProcessId(shell, out var pid); using var process = Process.GetProcessById(pid);
        string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        var started = ValidateExplorerIdentity(process, explorer); token.ThrowIfCancellationRequested();
        GetWindowThreadProcessId(shell, out var currentPid);
        if (GetShellWindow() != shell || currentPid != pid || process.HasExited || process.StartTime.ToUniversalTime() != started) throw new InvalidOperationException("O shell ou a identidade do Explorer mudou durante a checagem; nenhum processo foi encerrado.");
        process.Kill(false);
        var exited = process.WaitForExitAsync();
        if (await Task.WhenAny(exited, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false) != exited)
        {
            _ = ObserveExitTaskAsync(exited);
            return new(MaintenanceStageKind.RestartExplorer, null, "O encerramento do Explorer anterior não foi confirmado em dez segundos. Nenhum outro Explorer foi iniciado pelo VL; confira o shell atual.", false, true);
        }
        await exited.ConfigureAwait(false);
        if (GetShellWindow() == IntPtr.Zero) Process.Start(new ProcessStartInfo(explorer) { UseShellExecute = true });
        for (int attempt = 0; attempt < 30; attempt++)
        {
            await Task.Delay(200).ConfigureAwait(false); IntPtr next = GetShellWindow(); if (next == IntPtr.Zero) continue;
            GetWindowThreadProcessId(next, out var nextPid);
            try
            {
                using var replacement = Process.GetProcessById(nextPid);
                var nextStarted = ValidateExplorerIdentity(replacement, explorer);
                if (nextPid == pid && nextStarted == started || GetShellWindow() != next) continue;
                return new(MaintenanceStageKind.RestartExplorer, 0, "Novo shell do Explorer confirmado por caminho, sessão, usuário e identidade (PID " + nextPid + ").", true);
            }
            catch (Exception ex) when (ex is Win32Exception or ArgumentException or InvalidOperationException) { }
        }
        return new(MaintenanceStageKind.RestartExplorer, 1, "O Explorer anterior encerrou, mas o novo shell ainda não foi confirmado. Inicie explorer.exe pelo Gerenciador de Tarefas se necessário.");
    }
    private static DateTime ValidateExplorerIdentity(Process process, string expectedPath)
    {
        using var ownProcess = Process.GetCurrentProcess();
        if (process.SessionId != ownProcess.SessionId || !string.Equals(process.MainModule?.FileName, expectedPath, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("O processo do shell não corresponde ao Explorer da sessão atual.");
        if (!OpenProcessToken(process.Handle, 8, out var processToken)) throw new Win32Exception(Marshal.GetLastWin32Error());
        using (processToken) using (var owner = new WindowsIdentity(processToken.DangerousGetHandle())) using (var current = WindowsIdentity.GetCurrent())
            if (owner.User is null || current.User is null || owner.User != current.User) throw new InvalidOperationException("O usuário do shell não foi confirmado.");
        return process.StartTime.ToUniversalTime();
    }
    private static async Task ObserveExitTaskAsync(Task exited)
    { try { await exited.ConfigureAwait(false); } catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or Win32Exception) { } }
    [DllImport("dnsapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DnsFlushResolverCache();
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);
    [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out SafeAccessTokenHandle tokenHandle);
}
