using System.Text;
using System.Text.Json;
using VLOptimizer.Core;

public static class ProductIntegrationTests
{
    public static void Register(Action<string, Func<Task>> test, string root)
    {
        Directory.CreateDirectory(root); int number = 0;
        string Case() { var path = Path.Combine(root, (++number).ToString("D3")); Directory.CreateDirectory(path); return path; }
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        (WindowsOptimizerBackend Backend, ConfigFake Config, FakeSystem System, string Path) Backend()
        {
            string path = Case(); var system = new FakeSystem(); var config = new ConfigFake(system);
            return (new(path, system, new ConfigurationEngine(config)), config, system, path);
        }
        static async Task<OperationRecord> Apply(WindowsOptimizerBackend backend, string id) =>
            await backend.ApplyAsync(await backend.PreviewAsync(new(OperationKind.Configure, id, true), default), default);
        test("Journal integrado: snapshot durável antes do primeiro ajuste", async () =>
        {
            var context = Backend(); bool sawPending = false;
            context.Config.BeforeWrite = () => { using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(context.Path, "history.json"))); sawPending = doc.RootElement.GetProperty("Records")[0].GetProperty("Status").GetString() == "Pending"; };
            var result = await Apply(context.Backend, "game-mode");
            Check(sawPending && result.CanUndo && result.Status is OperationStatus.Completed or OperationStatus.RestartRequired, "Snapshot não estava durável antes da escrita.");
        });
        test("Revisão em lote: ajustes sobrepostos são bloqueados antes de escrever", async () =>
        {
            var x = Backend(); var a = await x.Backend.PreviewAsync(new(OperationKind.Configure, "dvr-off", true), default);
            var b = await x.Backend.PreviewAsync(new(OperationKind.Configure, "gamebar-off", true), default);
            var c = await x.Backend.PreviewAsync(new(OperationKind.Configure, "mouse-accel", true), default);
            var batch = OperationBatch.PreventOverlaps([a, b, c]);
            Check(!batch[0].CanApply && !batch[1].CanApply && batch[2].CanApply && ReferenceEquals(batch[2], c), "Conflitos não bloqueados ou prévia independente substituída.");
            Check(x.Config.Writes == 0 && (await x.Backend.ApplyAsync(batch[2], default)).Status == OperationStatus.Completed, "Revisão escreveu ou perdeu autorização independente.");
        });
        test("Revisão em lote: plano legado e catálogo compartilham recurso", async () =>
        {
            var x = Backend(); var a = await x.Backend.PreviewAsync(new(OperationKind.SetPowerPlan, FakeSystem.Fast.ToString()), default);
            var b = await x.Backend.PreviewAsync(new(OperationKind.Configure, "power-high", true), default);
            Check(OperationBatch.PreventOverlaps([a, b]).All(p => !p.CanApply) && x.Config.Writes == 0 && x.System.Writes == 0, "Conflito de energia não identificado antes da aprovação.");
        });
        test("Journal integrado: restaurar recupera valor real e não padrão desligado", async () =>
        {
            var context = Backend(); var target = OptimizationCatalog.Find("game-mode")!.Changes[0].Target;
            context.Config.Values[target.ResourceId] = ConfigurationValue.DWord(7);
            var record = await Apply(context.Backend, "game-mode");
            var reopened = new WindowsOptimizerBackend(context.Path, context.System, new ConfigurationEngine(context.Config));
            var result = await reopened.UndoAsync(record.Id, default);
            Check(result.Status == OperationStatus.Reverted && context.Config.Read(target) == ConfigurationValue.DWord(7), "Não restaurou o valor exato do snapshot.");
        });
        test("Journal integrado: falha parcial continua restaurável após reabrir", async () =>
        {
            var context = Backend(); var changes = OptimizationCatalog.Find("game-mode")!.Changes;
            context.Config.Fail = changes[1].Target.ResourceId;
            var record = await Apply(context.Backend, "game-mode");
            Check(record.Status == OperationStatus.PartiallyCompleted && record.CanUndo, "Falha parcial reportada como completa ou sem backup.");
            context.Config.Fail = null;
            var reopened = new WindowsOptimizerBackend(context.Path, context.System, new ConfigurationEngine(context.Config));
            Check((await reopened.UndoAsync(record.Id, default)).Status == OperationStatus.Reverted, "Backup parcial não foi recuperado.");
        });
        test("Journal integrado: prévia configurável é de uso único", async () =>
        {
            var context = Backend(); var preview = await context.Backend.PreviewAsync(new(OperationKind.Configure, "mouse-accel", true), default);
            var record = await context.Backend.ApplyAsync(preview, default); await context.Backend.UndoAsync(record.Id, default); int writes = context.Config.Writes;
            try { await context.Backend.ApplyAsync(preview, default); throw new Exception("Reutilizou aprovação."); } catch (InvalidOperationException) { }
            Check(context.Config.Writes == writes, "Aprovação reutilizada modificou configuração.");
        });
        test("Journal integrado: restauração protege ação composta posterior", async () =>
        {
            var context = Backend(); var first = await Apply(context.Backend, "dvr-off"); var later = await Apply(context.Backend, "gamebar-off");
            Check((await context.Backend.UndoAsync(first.Id, default)).Status == OperationStatus.Conflict, "Ação anterior apagou intenção posterior.");
            Check((await context.Backend.UndoAsync(later.Id, default)).Status == OperationStatus.Reverted, "Não restaurou ação posterior.");
            Check((await context.Backend.UndoAsync(first.Id, default)).Status == OperationStatus.Reverted, "Não restaurou ação anterior na ordem correta.");
        });
        test("Journal integrado: cruzamento catálogo e plano legado respeita ordem", async () =>
        {
            var context = Backend(); var first = await Apply(context.Backend, "power-high");
            var later = await context.Backend.ApplyAsync(await context.Backend.PreviewAsync(new(OperationKind.SetPowerPlan, FakeSystem.Other.ToString()), default), default);
            Check((await context.Backend.UndoAsync(first.Id, default)).Status == OperationStatus.Conflict, "Plano do catálogo sobrescreveu operação posterior.");
            await context.Backend.UndoAsync(later.Id, default); await context.Backend.UndoAsync(first.Id, default);
            Check(context.System.Active == FakeSystem.Balanced, "Plano inicial não voltou após restauração em ordem.");
        });
        test("Journal integrado: falha de persistência aborta ajustes", async () =>
        {
            var context = Backend(); var preview = await context.Backend.PreviewAsync(new(OperationKind.Configure, "game-mode", true), default);
            Directory.CreateDirectory(Path.Combine(context.Path, "history.json"));
            try { await context.Backend.ApplyAsync(preview, default); } catch (IOException) { }
            Check(context.Config.Writes == 0, "Escreveu sem journal durável.");
        });
        test("Journal unificado registra atividade sem oferecer desfazer exclusão", async () =>
        {
            var context = Backend(); var result = await context.Backend.RecordActivityAsync("cleanup", "Limpeza de teste", "Somente registro; nenhum arquivo real excluído.", OperationStatus.Completed, default);
            Check(!result.CanUndo && result.Request.Kind == OperationKind.Maintenance && (await context.Backend.GetHistoryAsync()).Count == 1 && context.Config.Writes == 0, "Atividade não integrada corretamente.");
        });
        test("Scaling: roundtrip de perfis, exportação e ausência de motor", async () =>
        {
            string path = Case(); var service = new ScalingProfileService(path); var profile = new ScalingProfile { Name = "Jogo fixture", ScalingAlgorithm = "FSR", ScaleFactor = 1.8, HdrSupport = true, CropLeft = 12 };
            await service.SaveProfileAsync(profile, default);
            var reopened = new ScalingProfileService(path); var loaded = (await reopened.GetProfilesAsync()).Single();
            Check(loaded == profile && !(await reopened.GetCapabilitiesAsync()).CanApplyProfiles, "Perfil alterado ou motor inventado.");
            string export = Path.Combine(path, "export.json"); await reopened.ExportProfileAsync(profile.Id, export, default);
            Check(JsonDocument.Parse(File.ReadAllText(export)).RootElement.GetProperty("Format").GetString() == "VL-Scaling-Profile", "Exportação sem formato identificado.");
            await reopened.DeleteProfileAsync(profile.Id, default); Check((await service.GetProfilesAsync()).Count == 0, "Exclusão não persistida.");
        });
        test("Scaling: limites e NaN são recusados no serviço", async () =>
        {
            string path = Case(); var service = new ScalingProfileService(path);
            foreach (var profile in new[] { new ScalingProfile { ScaleFactor = double.NaN }, new ScalingProfile { QueueTarget = 3 }, new ScalingProfile { FrameGeneration = "inventado" } })
            { try { await service.SaveProfileAsync(profile, default); throw new Exception("Perfil inválido foi aceito."); } catch (ArgumentException) { } }
            Check((await service.GetProfilesAsync()).Count == 0, "Perfil inválido foi persistido.");
        });
        test("Scaling: dois serviços preservam alterações de perfis distintos", async () =>
        {
            string path = Case(); var first = new ScalingProfileService(path); var second = new ScalingProfileService(path);
            await first.SaveProfileAsync(new() { Name = "Primeiro" }, default); await second.SaveProfileAsync(new() { Name = "Segundo" }, default);
            Check((await first.GetProfilesAsync()).Count == 2, "Um perfil sobrescreveu outro.");
        });
        test("Scaling: arquivo corrompido é preservado sem regravar padrões", async () =>
        {
            string path = Case(); string file = Path.Combine(path, "scaling", "profiles.json"); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, "{corrompido");
            var service = new ScalingProfileService(path); try { await service.SaveProfileAsync(new(), default); } catch (JsonException) { }
            Check(File.ReadAllText(file) == "{corrompido", "Configuração corrompida foi apagada.");
        });

        (OptiScalerService Service, string Game, string Package, string Root) Package()
        {
            string path = Case(), gameRoot = Path.Combine(path, "game"), package = Path.Combine(path, "package"); Directory.CreateDirectory(gameRoot); Directory.CreateDirectory(package);
            string game = Path.Combine(gameRoot, "vlfixture-" + Guid.NewGuid().ToString("N") + ".exe");
            static byte[] Pe(byte marker) { var bytes = new byte[256]; bytes[0] = 0x4d; bytes[1] = 0x5a; bytes[0x3c] = 0x80; bytes[0x80] = 0x50; bytes[0x81] = 0x45; bytes[0x84] = 0x64; bytes[0x85] = 0x86; bytes[200] = marker; return bytes; }
            File.WriteAllBytes(game, Pe(1)); File.WriteAllBytes(Path.Combine(package, "OptiScaler.dll"), Pe(2)); File.WriteAllText(Path.Combine(package, "OptiScaler.ini"), "[Upscalers]\r\nDx12Upscaler=auto\r\n");
            File.WriteAllText(Path.Combine(package, "setup_windows.bat"), "THIS TEST FILE MUST NEVER EXECUTE");
            return (new OptiScalerService(Path.Combine(path, "data")), game, package, path);
        }
        test("OptiScaler: instalação local e restauração preservam DLL anterior", async () =>
        {
            var context = Package(); string original = Path.Combine(Path.GetDirectoryName(context.Game)!, "dxgi.dll"); File.WriteAllText(original, "original do fixture");
            var preview = await context.Service.PreviewInstallAsync(context.Game, context.Package); Check(preview.CanApply && preview.Files.Count == 2, "Pacote não preparado ou script incluído.");
            var result = await context.Service.ApplyAsync(preview, default); Check(result.Success && result.BackupId is not null, result.Message);
            Check(!File.Exists(Path.Combine(Path.GetDirectoryName(context.Game)!, "setup_windows.bat")), "Copiou script executável de instalação.");
            var restored = await context.Service.RestoreAsync(result.BackupId!.Value, default);
            Check(restored.Success && File.ReadAllText(original) == "original do fixture" && !File.Exists(Path.Combine(Path.GetDirectoryName(context.Game)!, "OptiScaler.ini")), "Não restaurou conteúdo/ausência exatos.");
        });
        test("OptiScaler: pacote alterado após prévia não toca jogo", async () =>
        {
            var context = Package(); var preview = await context.Service.PreviewInstallAsync(context.Game, context.Package);
            File.AppendAllText(Path.Combine(context.Package, "OptiScaler.ini"), "alterado");
            var result = await context.Service.ApplyAsync(preview, default);
            Check(result.Conflict && !File.Exists(Path.Combine(Path.GetDirectoryName(context.Game)!, "dxgi.dll")), "Pacote alterado foi instalado.");
        });
        test("OptiScaler: restauração não remove arquivo editado pelo usuário", async () =>
        {
            var context = Package(); var preview = await context.Service.PreviewInstallAsync(context.Game, context.Package); var result = await context.Service.ApplyAsync(preview, default);
            string installed = Path.Combine(Path.GetDirectoryName(context.Game)!, "OptiScaler.ini"); File.WriteAllText(installed, "edição posterior");
            var restored = await context.Service.RestoreAsync(result.BackupId!.Value, default);
            Check(restored.Conflict && File.ReadAllText(installed) == "edição posterior", "Sobrescreveu edição posterior.");
        });
        test("OptiScaler: prévia não pode ser utilizada novamente", async () =>
        {
            var context = Package(); var preview = await context.Service.PreviewInstallAsync(context.Game, context.Package); await context.Service.ApplyAsync(preview, default);
            Check((await context.Service.ApplyAsync(preview, default)).Conflict, "Prévia de instalação reutilizada.");
        });
        test("OptiScaler: referência de backup corrompida bloqueia toda restauração antes de excluir", async () =>
        {
            var context = Package(); string gameRoot = Path.GetDirectoryName(context.Game)!;
            File.WriteAllText(Path.Combine(gameRoot, "dxgi.dll"), "original do fixture");
            var result = await context.Service.ApplyAsync(await context.Service.PreviewInstallAsync(context.Game, context.Package), default);
            string manifestPath = Path.Combine(context.Root, "data", "optiscaler", result.BackupId!.Value.ToString("N"), "manifest.json");
            var manifest = JsonSerializer.Deserialize<OptiScalerService.Manifest>(File.ReadAllText(manifestPath))!;
            int index = manifest.Files.FindIndex(x => x.BeforeSha256 is not null); manifest.Files[index] = manifest.Files[index] with { BackupName = null };
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));
            byte[] dll = File.ReadAllBytes(Path.Combine(gameRoot, "dxgi.dll")), ini = File.ReadAllBytes(Path.Combine(gameRoot, "OptiScaler.ini"));
            try { await context.Service.RestoreAsync(result.BackupId.Value, default); throw new InvalidOperationException("Manifesto inconsistente aceito."); }
            catch (InvalidDataException)
            {
                Check(File.ReadAllBytes(Path.Combine(gameRoot, "dxgi.dll")).SequenceEqual(dll) && File.ReadAllBytes(Path.Combine(gameRoot, "OptiScaler.ini")).SequenceEqual(ini), "A validação ocorreu depois de modificar arquivos.");
            }
        });
        test("OptiScaler: anticheat identificado e pacote inválido ficam bloqueados", async () =>
        {
            var context = Package(); Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(context.Game)!, "EasyAntiCheat"));
            Check(!(await context.Service.PreviewInstallAsync(context.Game, context.Package)).CanApply, "Diretório protegido não bloqueado.");
            var other = Package(); File.WriteAllText(Path.Combine(other.Package, "OptiScaler.dll"), "não é PE");
            Check(!(await other.Service.PreviewInstallAsync(other.Game, other.Package)).CanApply, "Biblioteca inválida aceita.");
        });
    }

    private sealed class ConfigFake(FakeSystem power) : IConfigurationSystemAccess
    {
        public Dictionary<string, ConfigurationValue> Values = new(); public int Writes; public string? Fail; public Action? BeforeWrite;
        public ConfigurationEnvironment InspectEnvironment() => new(true, true, 26100);
        public IReadOnlyList<ConfigurationChange> ResolveDynamicChanges(string capability) => capability == "power-high"
            ? [new(new(ConfigurationTargetKind.PowerPlan, "active"), ConfigurationValue.State(FakeSystem.Fast), ConfigurationValue.State(FakeSystem.Balanced))] : [];
        public ConfigurationValue Read(ConfigurationTarget target) => target.Kind == ConfigurationTargetKind.PowerPlan ? ConfigurationValue.State(power.Active) : Values.GetValueOrDefault(target.ResourceId, ConfigurationValue.Missing(true));
        public void Write(ConfigurationTarget target, ConfigurationValue value)
        {
            BeforeWrite?.Invoke(); if (target.ResourceId == Fail) throw new IOException("Falha isolada de teste."); Writes++;
            if (target.Kind == ConfigurationTargetKind.PowerPlan) power.SetActivePowerPlan(JsonSerializer.Deserialize<Guid>(value.Json));
            else Values[target.ResourceId] = value;
        }
        public void Refresh(ConfigurationRefresh refresh) { }
    }
}
