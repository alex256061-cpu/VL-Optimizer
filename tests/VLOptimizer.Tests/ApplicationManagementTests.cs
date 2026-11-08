using System.Text.Json;
using VLOptimizer.Core;

public static class ApplicationManagementTests
{
    public static void Register(Action<string, Func<Task>> test, string root)
    {
        int number = 0; Directory.CreateDirectory(root);
        (ApplicationManagementService Service, AppFake Fake, string Directory) Case()
        {
            var dir = Path.Combine(root, (++number).ToString("D3")); Directory.CreateDirectory(dir); var fake = new AppFake(dir);
            return (new(Path.Combine(dir, "journal"), fake), fake, dir);
        }
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        async Task Expect(Func<Task> action)
        { try { await action(); } catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or ArgumentException or PlatformNotSupportedException or OperationCanceledException or JsonException) { return; } throw new InvalidOperationException("Operação deveria ter sido recusada"); }
        test("Aplicativos prévia de desinstalação apenas lê e mantém argumentos estruturados", async () =>
        {
            var c = Case(); var p = await c.Service.PreviewUninstallAsync(AppFake.Id); Check(c.Fake.Runs == 0 && p.Arguments.SequenceEqual(new[] { "/uninstall", "configuração com espaços" }) && p.Executable.EndsWith("Uninstall.exe"), "Prévia executou ou dividiu argumentos errados");
        });
        test("Aplicativos uninstall persiste antes de abrir e confirma saída mais ausência de entrada", async () =>
        {
            var c = Case(); c.Fake.BeforeRun = () => Check(File.ReadAllText(Path.Combine(c.Directory, "journal", "applications.json")).Contains("UninstallCommand"), "Executou sem journal");
            var r = await c.Service.UninstallAsync(await c.Service.PreviewUninstallAsync(AppFake.Id)); Check(r.Status == ToolkitUninstallStatus.Completed && c.Fake.Runs == 1 && (await c.Service.GetHistoryAsync()).Single().Id == r.Id, "Uninstall não foi confirmado/persistido");
        });
        test("Aplicativos launch zero com entrada ainda presente fica não verificado", async () =>
        {
            var c = Case(); c.Fake.RemoveEntry = false; var r = await c.Service.UninstallAsync(await c.Service.PreviewUninstallAsync(AppFake.Id)); Check(r.Status == ToolkitUninstallStatus.Unverified, "Launch foi tratado como uninstall completo");
            await Expect(async () => await c.Service.PreviewLeftoversAsync(AppFake.Id));
        });
        test("Aplicativos código de falha nunca vira desinstalação comprovada", async () =>
        {
            var c = Case(); c.Fake.ExitCode = 5; var r = await c.Service.UninstallAsync(await c.Service.PreviewUninstallAsync(AppFake.Id)); Check(r.Status == ToolkitUninstallStatus.Failed, "Código de falha ignorado");
        });
        test("Aplicativos desinstalador ou metadados alterados invalidam prévia", async () =>
        {
            var c = Case(); var p = await c.Service.PreviewUninstallAsync(AppFake.Id); c.Fake.Hash = "changed"; await Expect(async () => await c.Service.UninstallAsync(p)); Check(c.Fake.Runs == 0, "EXE alterado executado");
            c = Case(); p = await c.Service.PreviewUninstallAsync(AppFake.Id); c.Fake.Metadata = c.Fake.Metadata! with { UninstallCommand = @"C:\Fixtures\Other.exe" }; await Expect(async () => await c.Service.UninstallAsync(p)); Check(c.Fake.Runs == 0, "Metadados alterados executados");
        });
        test("Aplicativos prévia fabricada consumida ou de outra instância é recusada", async () =>
        {
            var c = Case(); var p = await c.Service.PreviewUninstallAsync(AppFake.Id); await Expect(async () => await c.Service.UninstallAsync(p with { })); Check(c.Fake.Runs == 0, "Clone executado");
            p = await c.Service.PreviewUninstallAsync(AppFake.Id); await c.Service.UninstallAsync(p); await Expect(async () => await c.Service.UninstallAsync(p)); Check(c.Fake.Runs == 1, "Prévia reaplicada");
        });
        test("Aplicativos scripts interpretadores e caminhos relativos são bloqueados", async () =>
        {
            foreach (var command in new[] { "cmd.exe /c remove.bat", @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -File remove.ps1", @"C:\Fixtures\remove.bat", "Uninstall.exe /S" })
            { var c = Case(); c.Fake.Metadata = c.Fake.Metadata! with { UninstallCommand = command }; await Expect(async () => await c.Service.PreviewUninstallAsync(AppFake.Id)); Check(c.Fake.Runs == 0, "Comando proibido executado"); }
        });
        test("Aplicativos MSI usa produto exato com /x e não reinicia automaticamente", async () =>
        {
            var c = Case(); var guid = "{12345678-1234-5678-9012-123456789012}"; c.Fake.Metadata = c.Fake.Metadata! with { Id = "LocalMachine:Registry64:" + guid, WindowsInstaller = true, UninstallCommand = "MsiExec.exe /I" + guid };
            var p = await c.Service.PreviewUninstallAsync(c.Fake.Metadata.Id); Check(p.Arguments.SequenceEqual(new[] { "/x", guid, "/norestart" }) && p.Executable.EndsWith(@"System32\msiexec.exe"), "MSI não normalizado");
            c.Fake.ExitCode = 3010; var r = await c.Service.UninstallAsync(p); Check(r.Status == ToolkitUninstallStatus.RestartRequired, "Reinício não exposto");
        });
        test("Aplicativos sobras exigem prova e excluem só arquivos específicos aprovados", async () =>
        {
            var c = Case(); await Expect(async () => await c.Service.PreviewLeftoversAsync(AppFake.Id));
            var appRoot = Path.Combine(c.Fake.Directories.LocalAppData, "Fixture App"); Directory.CreateDirectory(appRoot); var own = Path.Combine(appRoot, "cache.tmp"); File.WriteAllText(own, "own"); var otherRoot = Path.Combine(c.Fake.Directories.LocalAppData, "Other App"); Directory.CreateDirectory(otherRoot); var other = Path.Combine(otherRoot, "other.dat"); File.WriteAllText(other, "other");
            await c.Service.UninstallAsync(await c.Service.PreviewUninstallAsync(AppFake.Id)); var p = await c.Service.PreviewLeftoversAsync(AppFake.Id); Check(p.Files.FileCount == 1 && File.Exists(own), "Prévia de sobras alterou ou ampliou seleção");
            var r = await c.Service.ApplyLeftoversAsync(p); Check(r.DeletedFiles == 1 && !File.Exists(own) && File.Exists(other) && Directory.Exists(appRoot), "Sobras apagaram conteúdo fora do aplicativo");
        });
        test("Aplicativos reinstalado após prévia de sobras é preservado", async () =>
        {
            var c = Case(); var metadata = c.Fake.Metadata; var appRoot = Path.Combine(c.Fake.Directories.LocalAppData, "Fixture App"); Directory.CreateDirectory(appRoot); var file = Path.Combine(appRoot, "data"); File.WriteAllText(file, "data");
            await c.Service.UninstallAsync(await c.Service.PreviewUninstallAsync(AppFake.Id)); var p = await c.Service.PreviewLeftoversAsync(AppFake.Id); c.Fake.Metadata = metadata;
            await Expect(async () => await c.Service.ApplyLeftoversAsync(p)); Check(File.Exists(file), "Aplicativo reinstalado perdeu arquivo");
        });
        test("Aplicativos cancelamento da espera não afirma conclusão", async () =>
        {
            var c = Case(); c.Fake.WaitingCanceled = true; var r = await c.Service.UninstallAsync(await c.Service.PreviewUninstallAsync(AppFake.Id)); Check(r.Status == ToolkitUninstallStatus.Unverified, "Espera cancelada marcada concluída");
        });
        test("Processos encerramento exige identidade e apenas processo selecionado", async () =>
        {
            var c = Case(); var p = await c.Service.PreviewKillProcessAsync(c.Fake.Process!.Identity.Id); Check(c.Fake.Kills == 0, "Prévia matou processo"); var r = await c.Service.KillAsync(p); Check(r.Success && c.Fake.Kills == 1, "Kill não foi verificado"); await Expect(async () => await c.Service.KillAsync(p));
        });
        test("Processos PID reutilizado e processo crítico são protegidos", async () =>
        {
            var c = Case(); var p = await c.Service.PreviewKillProcessAsync(c.Fake.Process!.Identity.Id); c.Fake.Process = c.Fake.Process with { Identity = c.Fake.Process.Identity with { StartedUtcTicks = 99999 } }; var r = await c.Service.KillAsync(p); Check(!r.Success && r.Conflict && c.Fake.Kills == 0, "PID reutilizado morto");
            c = Case(); c.Fake.Process = c.Fake.Process! with { IsCritical = true }; await Expect(async () => await c.Service.PreviewKillProcessAsync(c.Fake.Process.Identity.Id));
        });
        test("Aplicativos plataforma incompatível bloqueia uninstall e kill", async () =>
        {
            var c = Case(); c.Fake.IsSupportedOS = false; await Expect(async () => await c.Service.UninstallAsync(await c.Service.PreviewUninstallAsync(AppFake.Id))); await Expect(async () => await c.Service.KillAsync(await c.Service.PreviewKillProcessAsync(c.Fake.Process!.Identity.Id))); Check(c.Fake.Runs == 0 && c.Fake.Kills == 0, "Sistema incompatível modificado");
        });
    }
    private sealed class AppFake : IToolkitApplicationSystem
    {
        public const string Id = "CurrentUser:Registry64:fixture-app";
        public bool IsSupportedOS { get; set; } = true;
        public ToolkitApplicationDirectories Directories { get; }
        public ToolkitApplicationMetadata? Metadata;
        public ToolkitManagedProcess? Process = new(new(12345, 10000, @"C:\Fixtures\Fixture.exe"), "Fixture", 100, false);
        public string Hash = "fake-hash";
        public bool RemoveEntry = true, WaitingCanceled;
        public int Runs, Kills, ExitCode;
        public Action? BeforeRun;
        public AppFake(string root)
        {
            Directories = new(Path.Combine(root, "Local"), Path.Combine(root, "Roaming"), Path.Combine(root, "ProgramFiles"), Path.Combine(root, "ProgramFilesX86"), @"C:\Windows");
            Metadata = new(Id, "Fixture App", "Fixture Publisher", Path.Combine(Directories.ProgramFiles, "Fixture App"), "\"C:\\Fixtures\\Uninstall.exe\" /uninstall \"configuração com espaços\"", false);
        }
        public ToolkitApplicationMetadata? ReadApplication(string id) => Metadata?.Id == id ? Metadata : null;
        public string ReadExecutableSha256(string executable) => Hash;
        public Task<ToolkitUninstallerExit> RunUninstallerAsync(string executable, IReadOnlyList<string> arguments, string expectedExecutableSha256, CancellationToken token)
        { BeforeRun?.Invoke(); Runs++; if (RemoveEntry && !WaitingCanceled) Metadata = null; return Task.FromResult(new ToolkitUninstallerExit(WaitingCanceled ? null : ExitCode, WaitingCanceled, "Fixture")); }
        public ToolkitManagedProcess? ReadProcess(int processId) => Process?.Identity.Id == processId ? Process : null;
        public Task<bool> TerminateProcessAsync(ToolkitProcessIdentity identity, CancellationToken token)
        { if (Process?.Identity != identity) return Task.FromResult(false); Process = null; Kills++; return Task.FromResult(true); }
    }
}
