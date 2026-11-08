using System.Text;
using VLOptimizer.Core;

public static class ProductAreasTests
{
    public static void Register(Action<string, Func<Task>> test, string root)
    {
        Directory.CreateDirectory(root); int counter = 0;
        static void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
        (OptiScalerSettingsService Service, string Game, string Ini, string Data) Fixture()
        {
            string folder = Path.Combine(root, (++counter).ToString("D3")); Directory.CreateDirectory(folder);
            string game = Path.Combine(folder, "vl-ini-fixture-" + Guid.NewGuid().ToString("N") + ".exe"); var pe = new byte[256]; pe[0] = 0x4d; pe[1] = 0x5a; pe[0x3c] = 0x80; pe[0x80] = 0x50; pe[0x81] = 0x45; pe[0x84] = 0x64; pe[0x85] = 0x86; File.WriteAllBytes(game, pe);
            string ini = Path.Combine(folder, "OptiScaler.ini"); File.WriteAllBytes(ini, Encoding.UTF8.GetBytes("; usuário ç\r\n[Upscalers]\r\nDx12Upscaler = auto ; manter comentário\r\nUnknown = z\r\n[FrameGen]\r\nEnabled=auto\r\nFGInput=auto\r\nFGOutput=auto\r\n[Menu]\r\nShowFps=auto\r\nShortcutKey=auto\r\n"));
            string data = Path.Combine(folder, "data"); return (new(data), game, ini, data);
        }
        test("Áreas: todo ajuste possui uma página real e nenhum ID fantasma", () =>
        {
            Check(ProductPages.All.Select(p => p.Id).Distinct().Count() == ProductPages.All.Count, "Rotas duplicadas.");
            var ids = ProductPages.All.SelectMany(p => p.ConfigurationIds).Distinct().ToHashSet();
            Check(ids.SetEquals(OptimizationCatalog.All.Select(d => d.Id)), "Configuração sem acesso ou ID inexistente.");
            Check(new[] { "performance", "input", "memory", "storage", "applications", "network", "windows", "startup", "games", "graphics", "scaling", "fps" }.All(id => ProductPages.Find(id) != null), "Área solicitada ausente.");
            return Task.CompletedTask;
        });
        test("Opti INI: edição preserva comentários, Unicode e opções alheias", () =>
        {
            var x = Fixture(); byte[] bytes = File.ReadAllBytes(x.Ini); byte[] updated = OptiScalerSettingsService.Edit(bytes, new Dictionary<string, string> { ["Upscalers/Dx12Upscaler"] = "ffx" });
            Check(Encoding.UTF8.GetString(updated) == Encoding.UTF8.GetString(bytes).Replace("auto ; manter", "ffx ; manter"), "Perdeu formatação ou conteúdo alheio.");
            Check(OptiScalerSettingsService.ReadValues(updated)["Upscalers/Dx12Upscaler"] == "ffx", "Valor editado não foi lido."); return Task.CompletedTask;
        });
        test("Opti INI: preserva UTF16 e rejeita opções ausentes e duplicadas", () =>
        {
            byte[] bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("[Menu]\nShowFps=auto\n")).ToArray();
            byte[] updated = OptiScalerSettingsService.Edit(bytes, new Dictionary<string, string> { ["Menu/ShowFps"] = "true" });
            Check(updated.Take(2).SequenceEqual(new byte[] { 0xff, 0xfe }) && Encoding.Unicode.GetString(updated[2..]).Contains("ShowFps=true"), "Codificação alterada.");
            foreach (string source in new[] { "[Menu]\nShowFps=auto\nShowFps=false\n", "[Menu]\nShortcutKey=auto\n", "[Menu]\nShowFps=auto\n[menu]\nShowFps=false\n" })
            {
                bool denied = false; try { OptiScalerSettingsService.Edit(Encoding.UTF8.GetBytes(source), new Dictionary<string, string> { ["Menu/ShowFps"] = "true" }); } catch (InvalidDataException) { denied = true; }
                Check(denied, "INI ausente/ambíguo foi modificado.");
            }
            return Task.CompletedTask;
        });
        test("Opti INI: valores fora do catálogo não viram comandos ou linhas", () =>
        {
            var x = Fixture(); bool denied = false; try { OptiScalerSettingsService.Edit(File.ReadAllBytes(x.Ini), new Dictionary<string, string> { ["Menu/ShowFps"] = "true\nOther=1" }); } catch (ArgumentException) { denied = true; }
            Check(denied, "Valor com nova linha aceito."); return Task.CompletedTask;
        });
        test("Opti INI: patch não pode editar a mesma chave duas vezes por diferença de caixa", () =>
        {
            var x = Fixture(); bool denied = false;
            try { OptiScalerSettingsService.Edit(File.ReadAllBytes(x.Ini), new Dictionary<string, string> { ["Menu/ShowFps"] = "true", ["menu/showfps"] = "false" }); } catch (ArgumentException) { denied = true; }
            Check(denied, "Patch duplicado produziu substituições sobrepostas."); return Task.CompletedTask;
        });
        test("Windows Update: serviços reativados restauram estados exatos anteriores", async () =>
        {
            var fake = new UpdateFake(); var engine = new ConfigurationEngine(fake);
            var preview = await engine.PreviewAsync("windows-update-services", true, default);
            Check(preview.CanApply && preview.Steps.Count == 2 && fake.Writes == 0, "Prévia inválida.");
            var applied = await engine.ApplyAsync(preview, default);
            Check(applied.Status == ConfigurationResultStatus.Completed && fake.Values.Values.All(x => x == ConfigurationValue.State(new ConfigurationServiceState(3, true))), "Serviços não ficaram em manual/execução.");
            Check((await engine.RestoreAsync(applied.Snapshot, default)).Status == ConfigurationResultStatus.Reverted && fake.Values.Values.All(x => x == ConfigurationValue.State(new ConfigurationServiceState(4, false))), "Não restaurou estado desativado/parado exato.");
            Check(!(await engine.PreviewAsync("windows-update-services", false, default)).CanApply, "Foi oferecida desativação em vez de restauração.");
        });
        test("Opti INI: prévia só lê e restauração sobrevive à reabertura", async () =>
        {
            var x = Fixture(); byte[] original = File.ReadAllBytes(x.Ini); var p = await x.Service.PreviewAsync(x.Game, new Dictionary<string, string> { ["Menu/ShowFps"] = "true" });
            Check(File.ReadAllBytes(x.Ini).SequenceEqual(original), "Prévia escreveu.");
            Check((await x.Service.ApplyAsync(p)).Status == OperationStatus.Completed && (await x.Service.GetBackupsAsync()).Count == 1, "Aplicação não confirmada ou sem backup.");
            var reopened = new OptiScalerSettingsService(x.Data); Check((await reopened.RestoreAsync(p.Id)).Status == OperationStatus.Reverted && File.ReadAllBytes(x.Ini).SequenceEqual(original), "Restauração não exata.");
        });
        test("Opti INI: cópia e replay da prévia são bloqueados", async () =>
        {
            var x = Fixture(); var p = await x.Service.PreviewAsync(x.Game, new Dictionary<string, string> { ["Menu/ShowFps"] = "true" });
            Check((await x.Service.ApplyAsync(p with { })).Status == OperationStatus.Conflict, "Cópia autorizada.");
            await x.Service.ApplyAsync(p); Check((await x.Service.ApplyAsync(p)).Status == OperationStatus.Conflict, "Replay autorizado.");
        });
        test("Opti INI: mudanças após prévia e após aplicação são preservadas", async () =>
        {
            var x = Fixture(); var p = await x.Service.PreviewAsync(x.Game, new Dictionary<string, string> { ["Menu/ShowFps"] = "true" }); File.AppendAllText(x.Ini, "; edição posterior\n");
            Check((await x.Service.ApplyAsync(p)).Status == OperationStatus.Conflict, "Prévia obsoleta aplicada.");
            p = await x.Service.PreviewAsync(x.Game, new Dictionary<string, string> { ["Menu/ShowFps"] = "true" }); await x.Service.ApplyAsync(p); File.AppendAllText(x.Ini, "; outro programa\n");
            Check((await x.Service.RestoreAsync(p.Id)).Status == OperationStatus.Conflict, "Edição externa sobrescrita.");
        });
        test("Opti INI: backups sobrepostos exigem ordem inversa", async () =>
        {
            var x = Fixture(); byte[] original = File.ReadAllBytes(x.Ini); var a = await x.Service.PreviewAsync(x.Game, new Dictionary<string, string> { ["Menu/ShowFps"] = "true" }); await x.Service.ApplyAsync(a);
            var b = await x.Service.PreviewAsync(x.Game, new Dictionary<string, string> { ["Menu/ShortcutKey"] = "0x24" }); await x.Service.ApplyAsync(b);
            Check((await x.Service.RestoreAsync(a.Id)).Status == OperationStatus.Conflict, "Ordem inversa ignorada.");
            Check((await x.Service.RestoreAsync(b.Id)).Status == OperationStatus.Reverted && (await x.Service.RestoreAsync(a.Id)).Status == OperationStatus.Reverted && File.ReadAllBytes(x.Ini).SequenceEqual(original), "Ordem inversa perdeu original.");
        });
        test("Opti INI: backup corrompido bloqueia restauração antes de escrever", async () =>
        {
            var x = Fixture(); var p = await x.Service.PreviewAsync(x.Game, new Dictionary<string, string> { ["Menu/ShowFps"] = "true" }); await x.Service.ApplyAsync(p);
            string backup = Path.Combine(x.Data, "optiscaler-settings", p.Id.ToString("N"), "original.ini"); File.WriteAllText(backup, "corrompido"); byte[] before = File.ReadAllBytes(x.Ini);
            bool denied = false; try { await x.Service.RestoreAsync(p.Id); } catch (InvalidDataException) { denied = true; }
            Check(denied && File.ReadAllBytes(x.Ini).SequenceEqual(before), "Backup inválido tocou jogo.");
        });
        test("Opti INI: jogo com anticheat identificado e cancelamento ficam bloqueados", async () =>
        {
            var x = Fixture(); Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(x.Game)!, "EasyAntiCheat")); bool denied = false;
            try { await x.Service.PreviewAsync(x.Game, new Dictionary<string, string> { ["Menu/ShowFps"] = "true" }); } catch (InvalidOperationException) { denied = true; }
            Check(denied, "Anticheat ignorado."); var y = Fixture(); using var cancel = new CancellationTokenSource(); cancel.Cancel();
            try { await y.Service.PreviewAsync(y.Game, new Dictionary<string, string> { ["Menu/ShowFps"] = "true" }, cancel.Token); throw new InvalidOperationException("Cancelamento ignorado."); } catch (OperationCanceledException) { }
        });
    }
    private sealed class UpdateFake : IConfigurationSystemAccess
    {
        public Dictionary<string, ConfigurationValue> Values { get; } = OptimizationCatalog.Find("windows-update-services")!.Changes.ToDictionary(x => x.Target.ResourceId, _ => ConfigurationValue.State(new ConfigurationServiceState(4, false)));
        public int Writes;
        public ConfigurationEnvironment InspectEnvironment() => new(true, true, 26100);
        public IReadOnlyList<ConfigurationChange> ResolveDynamicChanges(string capability) => [];
        public ConfigurationValue Read(ConfigurationTarget target) => Values[target.ResourceId];
        public void Write(ConfigurationTarget target, ConfigurationValue value) { Writes++; Values[target.ResourceId] = value; }
        public void Refresh(ConfigurationRefresh refresh) { }
    }
}
