using VLOptimizer.Core;

public static class UwpTests
{
    public static void Register(Action<string, Func<Task>> test)
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        test("UWP: catálogo fechado contém dez pacotes e busca oficial sem promessa de restauração", () =>
        {
            var definitions = new UwpManagementService(new FakeUwp()).GetDefinitions();
            Check(definitions.Count == 10 && definitions.Select(x => x.Id).Distinct().Count() == 10 && definitions.All(x => x.StoreSearchUrl.StartsWith("https://apps.microsoft.com/", StringComparison.Ordinal)), "Catálogo/link inválido.");
            return Task.CompletedTask;
        });
        test("UWP: inventário e prévia só consultam pacotes do catálogo", async () =>
        {
            var fake = new FakeUwp(); fake.Packages.Add(Package("Microsoft.WindowsCalculator")); var service = new UwpManagementService(fake);
            var inventory = await service.InventoryAsync(); var p = await service.PreviewRemoveAsync(fake.Packages[0].PackageFullName);
            Check(inventory.Count == 1 && p.CanApply && fake.Removes == 0 && p.Consequence.Contains("Sem desfazer", StringComparison.Ordinal), "Consulta modificou pacote ou perdeu consequência.");
        });
        test("UWP: remove identidade exata e confere ausência sem oferecer undo", async () =>
        {
            var fake = new FakeUwp(); var service = new UwpManagementService(fake);
            var result = await service.RemoveAsync(await service.PreviewRemoveAsync(fake.Packages[0].PackageFullName));
            Check(result.Status == OperationStatus.Completed && !result.CanUndo && result.ExitCode == 0 && fake.Removes == 1 && fake.Packages.Count == 0, "Remoção não confirmou ausência.");
        });
        test("UWP: pacote protegido framework ou recurso fica bloqueado", async () =>
        {
            foreach (var package in new[] { Package() with { NonRemovable = true }, Package() with { Framework = true }, Package() with { ResourcePackage = true } })
            {
                var fake = new FakeUwp(); fake.Packages = [package]; var service = new UwpManagementService(fake);
                var p = await service.PreviewRemoveAsync(package.PackageFullName);
                Check(!p.CanApply && (await service.RemoveAsync(p)).Status == OperationStatus.Conflict && fake.Removes == 0, "Pacote protegido foi removido.");
            }
        });
        test("UWP: X3D com dois CCDs preserva Game Bar e TCUI", async () =>
        {
            foreach (string name in new[] { "Microsoft.XboxGamingOverlay", "Microsoft.Xbox.TCUI", "Microsoft.BingWeather" })
            {
                var fake = new FakeUwp { Environment = new(true, true, "S-1-5-21-100-200-300-1001"), Packages = [Package(name)] }; var service = new UwpManagementService(fake);
                var p = await service.PreviewRemoveAsync(fake.Packages[0].PackageFullName);
                Check(p.CanApply == (name == "Microsoft.BingWeather"), "Bloqueio X3D incorreto para " + name);
            }
        });
        test("UWP: mudança de identidade propriedades ou usuário bloqueia antes de remover", async () =>
        {
            var fake = new FakeUwp(); var service = new UwpManagementService(fake);
            var p = await service.PreviewRemoveAsync(fake.Packages[0].PackageFullName); fake.Packages[0] = fake.Packages[0] with { Publisher = "CN=Outro" };
            Check((await service.RemoveAsync(p)).Status == OperationStatus.Conflict && fake.Removes == 0, "Publisher alterado foi aceito.");
            fake = new FakeUwp(); service = new UwpManagementService(fake); p = await service.PreviewRemoveAsync(fake.Packages[0].PackageFullName); fake.Environment = fake.Environment with { UserSid = "S-1-5-21-1-2-3-1002" };
            Check((await service.RemoveAsync(p)).Status == OperationStatus.Conflict && fake.Removes == 0, "Usuário alterado foi aceito.");
        });
        test("UWP: identidade com injeção publisher desconhecido ou família divergente fica bloqueada", async () =>
        {
            foreach (var package in new[] { Package() with { PackageFullName = "Microsoft.XboxGamingOverlay';Start-Process cmd;'" }, Package() with { PackageFullName = "Microsoft.XboxGamingOverlay_1.2.3.4_x64__123456789abcd" }, Package() with { FamilyName = "OutraFamilia_8wekyb3d8bbwe" } })
            {
                var fake = new FakeUwp { Packages = [package] }; var service = new UwpManagementService(fake);
                var p = await service.PreviewRemoveAsync(package.PackageFullName);
                Check(!p.CanApply && fake.Removes == 0, "Identidade inválida recebeu aprovação.");
            }
        });
        test("UWP: cópia replay e prévia expirada não autorizam remoção", async () =>
        {
            var time = DateTimeOffset.UtcNow; var fake = new FakeUwp(); var service = new UwpManagementService(fake, () => time);
            var p = await service.PreviewRemoveAsync(fake.Packages[0].PackageFullName);
            Check((await service.RemoveAsync(p with { })).Status == OperationStatus.Conflict && fake.Removes == 0, "Cópia aceita.");
            time += TimeSpan.FromMinutes(11);
            Check((await service.RemoveAsync(p)).Status == OperationStatus.Conflict && fake.Removes == 0, "Expirada aceita.");
            p = await service.PreviewRemoveAsync(fake.Packages[0].PackageFullName); await service.RemoveAsync(p);
            Check((await service.RemoveAsync(p)).Status == OperationStatus.Conflict && fake.Removes == 1, "Replay executou novamente.");
        });
        test("UWP: código zero sem ausência verificada é falha", async () =>
        {
            var fake = new FakeUwp { KeepPackage = true }; var service = new UwpManagementService(fake);
            var result = await service.RemoveAsync(await service.PreviewRemoveAsync(fake.Packages[0].PackageFullName));
            Check(result.Status == OperationStatus.Failed && !result.CanUndo, "Código zero ignorou a pós-condição.");
        });
        test("UWP: erro de implantação ou timeout nunca é sucesso", async () =>
        {
            foreach (var command in new[] { new UwpCommandResult(5, "negado"), new UwpCommandResult(null, "timeout", true) })
            {
                var fake = new FakeUwp { KeepPackage = true, Result = command }; var service = new UwpManagementService(fake);
                var result = await service.RemoveAsync(await service.PreviewRemoveAsync(fake.Packages[0].PackageFullName));
                Check(result.Status is OperationStatus.Failed or OperationStatus.Unverified && !result.CanUndo, "Erro/timeout relatado como sucesso.");
            }
        });
        test("UWP: falha na releitura posterior preserva resultado não verificado", async () =>
        {
            var fake = new FakeUwp { FailRead = 3 }; var service = new UwpManagementService(fake);
            var result = await service.RemoveAsync(await service.PreviewRemoveAsync(fake.Packages[0].PackageFullName));
            Check(result.Status == OperationStatus.Unverified && fake.Removes == 1, "Falha de verificação posterior foi ocultada.");
        });
        test("UWP: cancelamento antes de executar não remove pacotes", async () =>
        {
            var fake = new FakeUwp(); var service = new UwpManagementService(fake); var p = await service.PreviewRemoveAsync(fake.Packages[0].PackageFullName);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); bool stopped = false;
            try { await service.RemoveAsync(p, cancelled.Token); } catch (OperationCanceledException) { stopped = true; }
            Check(stopped && fake.Removes == 0 && fake.Packages.Count == 1, "Cancelamento prévio iniciou implantação.");
        });
        test("UWP: deadline continua valendo após processo encerrar com pipe aberto", async () =>
        {
            var pipe = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var captured = Task.WhenAll(Task.CompletedTask, pipe.Task, Task.CompletedTask);
            bool timedOut = false;
            try { await UwpWindowsAccess.WaitForCaptureAsync(captured, TimeSpan.FromMilliseconds(50), CancellationToken.None); }
            catch (TimeoutException) { timedOut = true; }
            finally { pipe.SetResult(); await captured; }
            Check(timedOut, "O exit do pai anulou o deadline enquanto o pipe ainda estava aberto.");
        });
        test("UWP: cancelamento de inventário observa a espera dos pipes", async () =>
        {
            var pipe = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var captured = Task.WhenAll(Task.CompletedTask, Task.CompletedTask, pipe.Task);
            using var cancel = new CancellationTokenSource(); cancel.Cancel(); bool cancelled = false;
            try { await UwpWindowsAccess.WaitForCaptureAsync(captured, TimeSpan.FromSeconds(10), cancel.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            finally { pipe.SetResult(); await captured; }
            Check(cancelled, "Cancelamento não alcançou a drenagem do inventário.");
        });
    }
    private static UwpInstalledPackage Package(string name = "Microsoft.XboxGamingOverlay") => new(name, name + "_1.2.3.4_x64__8wekyb3d8bbwe", "1.2.3.4", "CN=Microsoft Corporation, O=Microsoft Corporation", name + "_8wekyb3d8bbwe");
    private sealed class FakeUwp : IUwpSystemAccess
    {
        public UwpEnvironment Environment = new(true, false, "S-1-5-21-100-200-300-1001");
        public List<UwpInstalledPackage> Packages = [Package()];
        public int Removes, Reads, FailRead;
        public bool KeepPackage;
        public UwpCommandResult Result = new(0, "ok");
        public UwpEnvironment InspectEnvironment() => Environment;
        public Task<IReadOnlyList<UwpInstalledPackage>> ReadCurrentUserPackagesAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); if (++Reads == FailRead) throw new IOException("Inventário indisponível.");
            return Task.FromResult<IReadOnlyList<UwpInstalledPackage>>(Packages.ToArray());
        }
        public Task<UwpCommandResult> RemoveCurrentUserPackageAsync(UwpInstalledPackage package, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Removes++;
            if (!KeepPackage && !Result.TimedOut) Packages.RemoveAll(x => x.PackageFullName == package.PackageFullName);
            return Task.FromResult(Result);
        }
    }
}
