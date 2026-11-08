using Microsoft.Win32;
using System.Text.Json;
using System.Text;
using System.Buffers.Binary;
using VLOptimizer.Core;

public static class ConfigurationTests
{
    public static void Register(Action<string, Func<Task>> test)
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        test("Catálogo cobre os 33 tweaks sem identificadores duplicados", () =>
        {
            string[] tweaks = ["game-mode", "delivery", "edge-bg", "bg-apps", "hibernate", "mpo", "gpu-msi", "dlss-rec", "steam-overlay", "lso", "eee", "nic-intmod", "mouse-accel", "sticky", "numlock", "kbd-repeat", "telemetry", "suggestions", "bing", "activity", "widgets", "location", "end-task", "file-ext", "hidden-files", "this-pc", "classic-menu", "dark", "transparency", "taskbar-left", "task-view", "search-icon", "bsod-detail"];
            Check(tweaks.All(x => OptimizationCatalog.Find(x) is not null), "Faltou um tweak no catálogo.");
            Check(OptimizationCatalog.All.Select(x => x.Id).Distinct().Count() == OptimizationCatalog.All.Count, "IDs duplicados.");
            Check(OptimizationCatalog.All.All(x => x.Changes.Count > 0 || x.Capability is not null || x.UnsupportedReason is not null), "Entrada sem contrato nem motivo de indisponibilidade.");
            return Task.CompletedTask;
        });
        test("Configuração: prévia consulta sem mutação", async () =>
        {
            var fake = new ConfigurationFake(); var engine = new ConfigurationEngine(fake);
            var p = await engine.PreviewAsync("game-mode", true, default);
            Check(p.CanApply && p.Steps.Count == 2 && fake.Writes == 0, "Prévia deve consultar os dois alvos sem alterar nada.");
        });
        test("Configuração: valor e chave ausentes são restaurados após criação de chave compartilhada", async () =>
        {
            var fake = new ConfigurationFake(); var engine = new ConfigurationEngine(fake);
            var p = await engine.PreviewAsync("game-mode", true, default);
            Check(p.Steps.All(x => !x.Before.Exists && !x.Before.KeyExists), "Ausência inicial não foi salva.");
            var applied = await engine.ApplyAsync(p, default);
            Check(applied.Status == ConfigurationResultStatus.Completed && fake.Values.Count == 2, $"Aplicação em chave compartilhada falhou: {applied.Message}");
            var restored = await engine.RestoreAsync(applied.Snapshot, default);
            Check(restored.Status == ConfigurationResultStatus.Reverted && fake.Values.Count == 0 && fake.Keys.Count == 0, "A chave vazia criada pela operação não foi removida.");
        });
        test("Configuração: reversão de valor ausente preserva chave preexistente", async () =>
        {
            var fake = new ConfigurationFake(); var engine = new ConfigurationEngine(fake);
            var target = OptimizationCatalog.Find("file-ext")!.Changes[0].Target;
            fake.Keys.Add(target.Path);
            var applied = await engine.ApplyAsync(await engine.PreviewAsync("file-ext", true, default), default);
            var restored = await engine.RestoreAsync(applied.Snapshot, default);
            Check(restored.Status == ConfigurationResultStatus.Reverted && fake.Keys.Contains(target.Path) && !fake.Read(target).Exists, "Chave preexistente foi removida.");
        });
        test("Configuração: restauração não remove chave com valores de outro programa", async () =>
        {
            var fake = new ConfigurationFake(); var engine = new ConfigurationEngine(fake);
            var applied = await engine.ApplyAsync(await engine.PreviewAsync("file-ext", true, default), default);
            var own = applied.Steps[0].Target; var other = own with { Name = "OutraPreferencia" };
            fake.Put(other, ConfigurationValue.String("preservar"));
            var restored = await engine.RestoreAsync(applied.Snapshot, default);
            Check(restored.Status == ConfigurationResultStatus.Reverted && fake.Read(other).Exists && fake.Keys.Contains(own.Path), "Valor/chave alheios removidos.");
        });
        test("Configuração: registro preserva tipos binário multi-string QWord e expand-string", async () =>
        {
            ConfigurationValue[] values = [ConfigurationValue.Binary([0, 128, 255]), new(true, (int)RegistryValueKind.MultiString, JsonSerializer.Serialize(new[] { "a", "b" })), new(true, (int)RegistryValueKind.QWord, JsonSerializer.Serialize(long.MinValue)), new(true, (int)RegistryValueKind.ExpandString, JsonSerializer.Serialize(@"%USERPROFILE%\arquivo")), new(true, (int)RegistryValueKind.None, JsonSerializer.Serialize(new byte[] { 4, 3, 2 }))];
            foreach (var before in values)
            {
                var fake = new ConfigurationFake(); var engine = new ConfigurationEngine(fake);
                var target = OptimizationCatalog.Find("file-ext")!.Changes[0].Target; fake.Put(target, before);
                var applied = await engine.ApplyAsync(await engine.PreviewAsync("file-ext", true, default), default);
                var restored = await engine.RestoreAsync(applied.Snapshot, default);
                Check(restored.Status == ConfigurationResultStatus.Reverted && fake.Read(target) == before, "Tipo/valor anterior mudou na reversão.");
            }
        });
        test("Configuração: alteração posterior à prévia bloqueia todas as escritas", async () =>
        {
            var fake = new ConfigurationFake(); var engine = new ConfigurationEngine(fake);
            var p = await engine.PreviewAsync("game-mode", true, default);
            fake.Put(p.Steps[1].Target, ConfigurationValue.DWord(9));
            var result = await engine.ApplyAsync(p, default);
            Check(result.Status == ConfigurationResultStatus.Conflict && fake.Writes == 0 && !fake.Read(p.Steps[0].Target).Exists, "O primeiro alvo foi alterado apesar de conflito conhecido.");
        });
        test("Configuração: snapshot de outra opção não escreve fora do catálogo", async () =>
        {
            var fake = new ConfigurationFake(); var engine = new ConfigurationEngine(fake);
            var p = await engine.PreviewAsync("file-ext", true, default);
            var step = p.Steps[0] with { Target = new(ConfigurationTargetKind.Registry, @"HKLM\SOFTWARE\OutraAplicacao", "segredo") };
            var result = await engine.RestoreAsync(p.Snapshot with { Steps = [step] }, default);
            Check(result.Status == ConfigurationResultStatus.Blocked && fake.Writes == 0, "Snapshot adulterado foi aceito.");
        });
        test("Configuração: cópia ou replay de prévia não autoriza alteração", async () =>
        {
            var fake = new ConfigurationFake(); var engine = new ConfigurationEngine(fake);
            var p = await engine.PreviewAsync("file-ext", true, default);
            Check((await engine.ApplyAsync(p with { }, default)).Status == ConfigurationResultStatus.Blocked, "Cópia da prévia foi aceita.");
            Check((await engine.ApplyAsync(p, default)).Status == ConfigurationResultStatus.Completed, "Prévia original não foi aceita.");
            int writes = fake.Writes;
            Check((await engine.ApplyAsync(p, default)).Status == ConfigurationResultStatus.Blocked && fake.Writes == writes, "Replay escreveu novamente.");
        });
        test("Configuração: undo preserva mudança posterior de outro aplicativo", async () =>
        {
            var fake = new ConfigurationFake(); var engine = new ConfigurationEngine(fake);
            var result = await engine.ApplyAsync(await engine.PreviewAsync("file-ext", true, default), default);
            var other = ConfigurationValue.DWord(7); fake.Put(result.Steps[0].Target, other);
            var undo = await engine.RestoreAsync(result.Snapshot, default);
            Check(undo.Status == ConfigurationResultStatus.Conflict && fake.Read(result.Steps[0].Target) == other, "Conflito sobrescrito.");
        });
        test("Configuração: falha parcial guarda snapshot de todos os alvos", async () =>
        {
            var fake = new ConfigurationFake(); var engine = new ConfigurationEngine(fake);
            var p = await engine.PreviewAsync("game-mode", true, default); fake.FailBefore.Add(p.Steps[1].Target.ResourceId);
            var result = await engine.ApplyAsync(p, default);
            Check(result.Status == ConfigurationResultStatus.Partial && result.Snapshot.Steps.Count == 2 && result.CanRestore, "Falha parcial perdeu backup.");
            fake.FailBefore.Clear();
            Check((await engine.RestoreAsync(result.Snapshot, default)).Status == ConfigurationResultStatus.Reverted && fake.Values.Count == 0, "Falha parcial não restaurou o alvo já aplicado.");
        });
        test("Configuração: falha depois da escrita continua recuperável", async () =>
        {
            var fake = new ConfigurationFake(); var engine = new ConfigurationEngine(fake);
            var p = await engine.PreviewAsync("file-ext", true, default); fake.FailAfter.Add(p.Steps[0].Target.ResourceId);
            var result = await engine.ApplyAsync(p, default); fake.FailAfter.Clear();
            Check(result.Status == ConfigurationResultStatus.Failed && result.CanRestore && fake.Read(p.Steps[0].Target).Exists, "Falha depois da escrita foi perdida.");
            Check((await engine.RestoreAsync(result.Snapshot, default)).Status == ConfigurationResultStatus.Reverted, "Falha pós-escrita não pôde ser restaurada.");
        });
        test("Configuração: escrita ignorada falha na releitura", async () =>
        {
            var fake = new ConfigurationFake { IgnoreWrites = true }; var engine = new ConfigurationEngine(fake);
            var result = await engine.ApplyAsync(await engine.PreviewAsync("file-ext", true, default), default);
            Check(result.Status == ConfigurationResultStatus.Failed, "Escrita sem efeito foi considerada concluída.");
        });
        test("Configuração: inspeção distingue before desired divergência e consulta negada", async () =>
        {
            var fake = new ConfigurationFake(); var engine = new ConfigurationEngine(fake);
            var p = await engine.PreviewAsync("file-ext", true, default);
            Check((await engine.CheckSnapshotAsync(p.Snapshot, default))[0].Position == ConfigurationPosition.Before, "Before não identificado.");
            fake.Put(p.Steps[0].Target, p.Steps[0].Desired);
            Check((await engine.CheckSnapshotAsync(p.Snapshot, default))[0].Position == ConfigurationPosition.Desired, "Desired não identificado.");
            fake.Put(p.Steps[0].Target, ConfigurationValue.DWord(11));
            Check((await engine.CheckSnapshotAsync(p.Snapshot, default))[0].Position == ConfigurationPosition.Diverged, "Divergência não identificada.");
            fake.DenyRead = true;
            Check((await engine.CheckSnapshotAsync(p.Snapshot, default))[0].Position == ConfigurationPosition.Unreadable, "Acesso negado foi confundido com ausência.");
        });
        test("Configuração: snapshot pending é suficiente para recuperação depois de reinício", async () =>
        {
            var fake = new ConfigurationFake(); var first = new ConfigurationEngine(fake);
            var p = await first.PreviewAsync("file-ext", true, default);
            var serialized = JsonSerializer.Serialize(p.Snapshot);
            await first.ApplyAsync(p, default);
            var reopened = new ConfigurationEngine(fake);
            var snapshot = JsonSerializer.Deserialize<ConfigurationSnapshot>(serialized)!;
            Check((await reopened.RestoreAsync(snapshot, default)).Status == ConfigurationResultStatus.Reverted, "Snapshot pré-escrita não recuperou a configuração.");
        });
        test("Configuração: serviço preserva manual parado e inicialização automática atrasada", async () =>
        {
            foreach (var before in new[] { new ConfigurationServiceState(3, false), new ConfigurationServiceState(2, true, true), new ConfigurationServiceState(4, true) })
            {
                var fake = new ConfigurationFake(); var engine = new ConfigurationEngine(fake);
                var target = OptimizationCatalog.Find("sysmain-off")!.Changes[0].Target; fake.Put(target, ConfigurationValue.State(before));
                var result = await engine.ApplyAsync(await engine.PreviewAsync("sysmain-off", true, default), default);
                Check(result.Status == ConfigurationResultStatus.Completed && JsonSerializer.Deserialize<ConfigurationServiceState>(fake.Read(target).Json) == new ConfigurationServiceState(4, false), "Serviço não parou/desativou.");
                Check((await engine.RestoreAsync(result.Snapshot, default)).Status == ConfigurationResultStatus.Reverted && JsonSerializer.Deserialize<ConfigurationServiceState>(fake.Read(target).Json) == before, "Estado exato do serviço não foi restaurado.");
            }
        });
        test("Configuração: sem admin permite HKCU e bloqueia HKLM antes de escrever", async () =>
        {
            var fake = new ConfigurationFake { Environment = new(true, false, 26100) }; var engine = new ConfigurationEngine(fake);
            Check((await engine.PreviewAsync("file-ext", true, default)).CanApply, "HKCU foi bloqueado sem necessidade.");
            Check(!(await engine.PreviewAsync("delivery", true, default)).CanApply && fake.Writes == 0, "HKLM foi liberado sem admin.");
        });
        test("Configuração: perda de privilégio após prévia bloqueia escrita", async () =>
        {
            var fake = new ConfigurationFake(); var engine = new ConfigurationEngine(fake);
            var p = await engine.PreviewAsync("delivery", true, default); fake.Environment = fake.Environment with { IsAdministrator = false };
            Check((await engine.ApplyAsync(p, default)).Status == ConfigurationResultStatus.Blocked && fake.Writes == 0, "Privilégios não revalidados.");
        });
        test("Configuração: OS build e X3D bloqueiam combinações incompatíveis", async () =>
        {
            var fake = new ConfigurationFake { Environment = new(false, true, 19045) }; var engine = new ConfigurationEngine(fake);
            Check(!(await engine.PreviewAsync("file-ext", true, default)).CanApply, "OS incompatível aceito.");
            fake.Environment = new(true, true, 22621);
            Check(!(await engine.PreviewAsync("end-task", true, default)).CanApply, "Build inferior aceito.");
            fake.Environment = new(true, true, 26100, IsDualCcdX3d: true);
            Check(!(await engine.PreviewAsync("gamebar-off", true, default)).CanApply && !(await engine.PreviewAsync("power-high", true, default)).CanApply, "Proteção X3D ausente.");
        });
        test("Configuração: GPU NIC e plano indisponíveis não criam alvos sintéticos", async () =>
        {
            var fake = new ConfigurationFake(); var engine = new ConfigurationEngine(fake);
            foreach (var id in new[] { "gpu-msi", "lso", "eee", "nic-intmod", "power-ultimate" }) Check(!(await engine.PreviewAsync(id, true, default)).CanApply, "Alvo sintético oferecido: " + id);
            Check(fake.Writes == 0, "Detecção alterou recursos.");
        });
        test("Configuração: dependências ausentes ficam indisponíveis com motivo", async () =>
        {
            var engine = new ConfigurationEngine(new ConfigurationFake());
            foreach (var id in new[] { "dlss-rec", "steam-overlay" })
            {
                var preview = await engine.PreviewAsync(id, true, default);
                Check(!preview.CanApply && !string.IsNullOrWhiteSpace(preview.BlockReason), "Dependência ausente anunciada como funcionando.");
            }
        });
        test("Configuração: desligar usa defaults declarados e undo restaura o valor real anterior", async () =>
        {
            var fake = new ConfigurationFake(); var engine = new ConfigurationEngine(fake);
            var target = OptimizationCatalog.Find("mouse-accel")!.Changes[0].Target; var old = ConfigurationValue.String("7"); fake.Put(target, old);
            var result = await engine.ApplyAsync(await engine.PreviewAsync("mouse-accel", false, default), default);
            Check(fake.Read(target) == ConfigurationValue.String("1"), "Default desligado incorreto.");
            await engine.RestoreAsync(result.Snapshot, default);
            Check(fake.Read(target) == old && fake.Refreshes == 2, "Undo usou default em vez do estado salvo.");
        });
        test("Configuração: falha de atualização da sessão retorna parcial com snapshot", async () =>
        {
            var fake = new ConfigurationFake { FailRefresh = true }; var engine = new ConfigurationEngine(fake);
            var result = await engine.ApplyAsync(await engine.PreviewAsync("dark", true, default), default);
            Check(result.Status == ConfigurationResultStatus.Partial && result.CanRestore && result.Steps.All(x => x.Status == ConfigurationStepStatus.Applied), "Falha da sessão apagou conclusão das escritas.");
        });
        test("Configuração: valores já corretos não geram prévia aplicável", async () =>
        {
            var fake = new ConfigurationFake(); var engine = new ConfigurationEngine(fake);
            var change = OptimizationCatalog.Find("file-ext")!.Changes[0]; fake.Put(change.Target, change.On);
            var p = await engine.PreviewAsync("file-ext", true, default);
            Check(!p.CanApply && fake.Writes == 0, "Mudança sem efeito oferecida.");
        });
        test("Steam VDF: edição preserva comentários Unicode e campos alheios byte a byte", () =>
        {
            const string source = "// comentário ç\r\n\"UserLocalConfigStore\"\r\n{\r\n\t\"system\"\r\n\t{\r\n\t\t\"EnableGameOverlay\"\t\t\"1\"\r\n\t\t\"Outra\" \"a\\\"b\" // preservar\r\n\t}\r\n}\r\n";
            var document = SteamOverlayDocument.Parse(Encoding.UTF8.GetBytes(source)); var before = document.Read();
            var changed = document.With(ConfigurationValue.String("0"));
            Check(Encoding.UTF8.GetString(changed) == source.Replace("\"EnableGameOverlay\"\t\t\"1\"", "\"EnableGameOverlay\"\t\t\"0\""), "Campos alheios foram reformatados.");
            Check(SteamOverlayDocument.Parse(changed).With(before).AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(source)), "Undo não restaurou bytes do valor original.");
            return Task.CompletedTask;
        });
        test("Steam VDF: criação e remoção do bloco system preservam outras opções", () =>
        {
            byte[] bytes = Encoding.UTF8.GetBytes("\"UserLocalConfigStore\"\n{\n\t\"outro\" { \"EnableGameOverlay\" \"7\" }\n}\n");
            var document = SteamOverlayDocument.Parse(bytes); var before = document.Read();
            Check(!before.Exists && !before.KeyExists, "Bloco system ausente foi inventado.");
            var changed = SteamOverlayDocument.Parse(document.With(ConfigurationValue.String("0")));
            Check(changed.Read() == ConfigurationValue.String("0"), "Bloco system não foi inserido.");
            var reverted = SteamOverlayDocument.Parse(changed.With(before));
            Check(!reverted.Read().Exists && !reverted.Read().KeyExists && Encoding.UTF8.GetString(changed.With(before)).Contains("\"EnableGameOverlay\" \"7\"", StringComparison.Ordinal), "Undo removeu opções de outro bloco.");
            return Task.CompletedTask;
        });
        test("Steam VDF: valor ausente em system preexistente não remove bloco e opções", () =>
        {
            byte[] bytes = Encoding.UTF8.GetBytes("\"UserLocalConfigStore\" { \"system\" { \"Outro\" \"x\" } }");
            var document = SteamOverlayDocument.Parse(bytes); var before = document.Read();
            Check(!before.Exists && before.KeyExists, "System preexistente não identificado.");
            var updated = SteamOverlayDocument.Parse(document.With(ConfigurationValue.String("0")));
            var undone = SteamOverlayDocument.Parse(updated.With(before));
            Check(!undone.Read().Exists && undone.Read().KeyExists, "Undo removeu bloco preexistente.");
            return Task.CompletedTask;
        });
        test("Steam VDF: preserva BOM UTF16 e rejeita chave duplicada ou texto malformado", () =>
        {
            const string source = "\"UserLocalConfigStore\" { \"system\" { \"EnableGameOverlay\" \"1\" } }";
            byte[] bytes = [0xff, 0xfe, .. Encoding.Unicode.GetBytes(source)];
            var result = SteamOverlayDocument.Parse(bytes).With(ConfigurationValue.String("0"));
            Check(result[0] == 255 && result[1] == 254 && SteamOverlayDocument.Parse(result).Read() == ConfigurationValue.String("0"), "Encoding/BOM não preservado.");
            foreach (string invalid in new[] { "\"UserLocalConfigStore\" {", "\"UserLocalConfigStore\" { \"system\" { \"EnableGameOverlay\" \"1\" \"enablegameoverlay\" \"0\" } }" })
            {
                bool rejected = false;
                try { SteamOverlayDocument.Parse(Encoding.UTF8.GetBytes(invalid)).Read(); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "VDF ambíguo ou malformado aceito.");
            }
            return Task.CompletedTask;
        });
        test("NVIDIA DRS: codec corresponde ao layout V1 público pack4", () =>
        {
            var data = NvidiaDrsSettingCodec.Create(NvidiaDrsSettingCodec.DlssPresetId, NvidiaDrsSettingCodec.LatestPreset);
            Check(data.Length == 12320 && BinaryPrimitives.ReadUInt32LittleEndian(data) == 0x13020 && BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4100)) == 0x10e41df3 && BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8220)) == 0x00ffffff, "Layout não corresponde à ABI NVIDIA pública.");
            Check(NvidiaDrsSettingCodec.Decode(data, NvidiaDrsSettingCodec.DlssPresetId) == ConfigurationValue.DWord(16777215), "Valor DWORD não foi lido.");
            return Task.CompletedTask;
        });
        test("NVIDIA DRS: defaults são ausência de override e DWORD preserva bits", () =>
        {
            var data = NvidiaDrsSettingCodec.Create(NvidiaDrsSettingCodec.DlssOverrideId, 0xfedcba98);
            Check(NvidiaDrsSettingCodec.Decode(data, NvidiaDrsSettingCodec.DlssOverrideId) == ConfigurationValue.DWord(unchecked((int)0xfedcba98)), "DWORD perdeu bits de sinal.");
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4112), 1);
            Check(NvidiaDrsSettingCodec.Decode(data, NvidiaDrsSettingCodec.DlssOverrideId) == ConfigurationValue.Missing(true), "Default foi convertido em override persistente.");
            return Task.CompletedTask;
        });
        test("NVIDIA DRS: codec rejeita outros IDs tipos e versões", () =>
        {
            bool rejected = false;
            try { NvidiaDrsSettingCodec.Create(0x12345678); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "ID fora do contrato aceito.");
            foreach (int offset in new[] { 0, 4104 })
            {
                var bytes = NvidiaDrsSettingCodec.Create(NvidiaDrsSettingCodec.DlssOverrideId); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), 5);
                rejected = false;
                try { NvidiaDrsSettingCodec.Decode(bytes, NvidiaDrsSettingCodec.DlssOverrideId); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "Versão/tipo incompatível aceito.");
            }
            return Task.CompletedTask;
        });
        test("NVIDIA DRS: fluxo fake aplica dois IDs e restaura defaults sem criar overrides", async () =>
        {
            var fake = new ConfigurationFake(); var engine = new ConfigurationEngine(fake);
            ConfigurationChange[] changes = [new(new(ConfigurationTargetKind.NvidiaDrs, "base-profile", "10E41E01"), ConfigurationValue.DWord(1), ConfigurationValue.Missing(true)), new(new(ConfigurationTargetKind.NvidiaDrs, "base-profile", "10E41DF3"), ConfigurationValue.DWord(16777215), ConfigurationValue.Missing(true))];
            fake.Dynamic["dlss-rec"] = changes; fake.Keys.Add("base-profile");
            var result = await engine.ApplyAsync(await engine.PreviewAsync("dlss-rec", true, default), default);
            Check(result.Status == ConfigurationResultStatus.Completed && result.Steps.Count == 2, "Dois IDs não foram aplicados.");
            Check((await engine.RestoreAsync(result.Snapshot, default)).Status == ConfigurationResultStatus.Reverted && changes.All(x => !fake.Read(x.Target).Exists), "Defaults foram transformados em overrides.");
        });
        test("Configuração: catálogo inclui pagefile serviços OBS DNS e resolução", () =>
        {
            Check(OptimizationCatalog.All.Count == 56, "Quantidade inesperada de ajustes.");
            foreach (var id in new[] { "pagefile-auto", "unused-services-off", "obs-priority", "dns-cloudflare", "dns-google", "dns-quad9", "dns-adguard", "dns-auto", "resolution-720p" }) Check(OptimizationCatalog.Find(id)?.SourceEvidence.Length > 0, "Contrato/fonte ausente: " + id);
            return Task.CompletedTask;
        });
        test("Pagefile: automático preserva estado manual e tipo MultiString para undo", async () =>
        {
            var fake = new ConfigurationFake(); var engine = new ConfigurationEngine(fake); var target = new ConfigurationTarget(ConfigurationTargetKind.Pagefile, "system");
            var manual = ConfigurationValue.State(new ConfigurationPagefileState(false, new(true, (int)RegistryValueKind.MultiString, JsonSerializer.Serialize(new[] { @"C:\pagefile.sys 4096 8192", @"D:\pagefile.sys 2048 4096" }))));
            var automatic = ConfigurationValue.State(new ConfigurationPagefileState(true, null)); fake.Put(target, manual);
            fake.Dynamic["pagefile-auto"] = [new(target, automatic, automatic)];
            var applied = await engine.ApplyAsync(await engine.PreviewAsync("pagefile-auto", true, default), default);
            Check(applied.Status == ConfigurationResultStatus.Completed && fake.Read(target) == automatic, "Pagefile automático não confirmado.");
            Check((await engine.RestoreAsync(applied.Snapshot, default)).Status == ConfigurationResultStatus.Reverted && fake.Read(target) == manual, "Estado manual do pagefile não foi restaurado.");
            Check(!(await engine.PreviewAsync("pagefile-auto", false, default)).CanApply, "Desativação manual sem tamanho foi oferecida.");
        });
        test("DNS: provedores usam o mesmo recurso por GUID e recuperam modo anterior", async () =>
        {
            var fake = new ConfigurationFake(); var engine = new ConfigurationEngine(fake); var target = new ConfigurationTarget(ConfigurationTargetKind.DnsIpv4, "9a86dc75-e3b7-4bb8-a6d3-0e6171b5b3a1");
            var old = ConfigurationValue.State(new ConfigurationDnsState(false, new[] { "10.0.0.53" })); var auto = ConfigurationValue.State(new ConfigurationDnsState(true, []));
            fake.Put(target, old); fake.Dynamic["dns-cloudflare"] = [new(target, ConfigurationValue.State(new ConfigurationDnsState(false, new[] { "1.1.1.1", "1.0.0.1" })), auto)];
            fake.Dynamic["dns-google"] = [new(target, ConfigurationValue.State(new ConfigurationDnsState(false, new[] { "8.8.8.8", "8.8.4.4" })), auto)];
            var p = await engine.PreviewAsync("dns-cloudflare", true, default); var google = await engine.PreviewAsync("dns-google", true, default);
            Check(p.Steps[0].Target.ResourceId == google.Steps[0].Target.ResourceId, "Provedores não compartilham recurso para conflito.");
            var applied = await engine.ApplyAsync(p, default);
            Check((await engine.RestoreAsync(applied.Snapshot, default)).Status == ConfigurationResultStatus.Reverted && fake.Read(target) == old, "DNS estático anterior foi substituído por DHCP no undo.");
        });
        test("Vídeo: codec preserva frequência posição negativa rotação e profundidade", () =>
        {
            var before = new ConfigurationDisplayMode(2560, 1440, 165, 32, 1, -2560, 120, 0);
            Check(ConfigurationDisplayCodec.Decode(ConfigurationDisplayCodec.Encode(before)) == before, "Modo original perdeu informação.");
            var after = before with { Width = 1280, Height = 720 };
            Check(ConfigurationDisplayCodec.Decode(ConfigurationDisplayCodec.Encode(after)) == after, "Modo 720p alterou outros campos.");
            bool rejected = false;
            try { ConfigurationDisplayCodec.Encode(before with { Orientation = 9 }); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "Rotação inválida aceita.");
            return Task.CompletedTask;
        });
        test("Vídeo: 720p permite usuário comum e restaura modo original exato", async () =>
        {
            var fake = new ConfigurationFake { Environment = new(true, false, 19045) }; var engine = new ConfigurationEngine(fake);
            var target = new ConfigurationTarget(ConfigurationTargetKind.DisplayMode, @"\\.\DISPLAY1");
            var original = new ConfigurationDisplayMode(1920, 1080, 144, 32, 0, 0, 0, 0); var before = ConfigurationValue.State(original); var desired = ConfigurationValue.State(original with { Width = 1280, Height = 720 });
            fake.Put(target, before); fake.Dynamic["resolution-720p"] = [new(target, desired, before)];
            var applied = await engine.ApplyAsync(await engine.PreviewAsync("resolution-720p", true, default), default);
            Check(applied.Status == ConfigurationResultStatus.Completed && !applied.Steps[0].RequiresAdmin, "Resolução exigiu admin ou falhou.");
            Check((await engine.RestoreAsync(applied.Snapshot, default)).Status == ConfigurationResultStatus.Reverted && fake.Read(target) == before, "Modo original não restaurado.");
        });
        test("Serviço: alteração intermediária observada permite restauração parcial sem sobrescrever terceiros", async () =>
        {
            var fake = new ConfigurationFake(); var engine = new ConfigurationEngine(fake); var target = OptimizationCatalog.Find("sysmain-off")!.Changes[0].Target;
            var before = ConfigurationValue.State(new ConfigurationServiceState(2, true, true)); var intermediate = ConfigurationValue.State(new ConfigurationServiceState(4, true, false));
            fake.Put(target, before); fake.Intermediate[target.ResourceId] = intermediate;
            var result = await engine.ApplyAsync(await engine.PreviewAsync("sysmain-off", true, default), default);
            Check(result.Status == ConfigurationResultStatus.Failed && result.Steps[0].ObservedAfter == intermediate, "Estado intermediário foi perdido.");
            fake.Intermediate.Clear();
            Check((await engine.RestoreAsync(result.Snapshot, default)).Status == ConfigurationResultStatus.Reverted && fake.Read(target) == before, "Não restaurou o estado intermediário da própria ação.");
            fake.Put(target, ConfigurationValue.State(new ConfigurationServiceState(3, false)));
            Check((await engine.RestoreAsync(result.Snapshot, default)).Status == ConfigurationResultStatus.Conflict, "Mudança de terceiro foi sobrescrita.");
        });
        test("Configuração: cancelamento entre alvos preserva antes e depois para recuperação", async () =>
        {
            using var cancel = new CancellationTokenSource(); var fake = new ConfigurationFake(); var engine = new ConfigurationEngine(fake);
            var p = await engine.PreviewAsync("game-mode", true, default); fake.OnWrite = () => cancel.Cancel();
            var result = await engine.ApplyAsync(p, cancel.Token);
            Check(result.Status == ConfigurationResultStatus.Partial && fake.Writes == 1 && result.CanRestore && result.Steps[1].Status == ConfigurationStepStatus.Pending, "Cancelamento perdeu estado parcial.");
            fake.OnWrite = null;
            Check((await engine.RestoreAsync(result.Snapshot, default)).Status == ConfigurationResultStatus.Reverted, "Cancelamento não permitiu recuperar o primeiro alvo.");
        });
        test("Configuração: snapshot adulterado com estado composto ou ObservedAfter inconsistente é bloqueado", async () =>
        {
            var fake = new ConfigurationFake(); var engine = new ConfigurationEngine(fake); var target = OptimizationCatalog.Find("sysmain-off")!.Changes[0].Target;
            fake.Put(target, ConfigurationValue.State(new ConfigurationServiceState(2, true)));
            var p = await engine.PreviewAsync("sysmain-off", true, default); var step = p.Steps[0];
            ConfigurationStep[] invalid = [step with { Before = ConfigurationValue.State(new ConfigurationServiceState(0, false)) },
                step with { Status = ConfigurationStepStatus.Applied, ObservedAfter = ConfigurationValue.State(new ConfigurationServiceState(3, false)) },
                step with { Status = ConfigurationStepStatus.Pending, ObservedAfter = step.Desired }];
            foreach (var bad in invalid) Check((await engine.RestoreAsync(p.Snapshot with { Steps = [bad] }, default)).Status == ConfigurationResultStatus.Blocked, "Snapshot composto adulterado foi aceito.");
            Check(fake.Writes == 0, "Validação adulterada iniciou escrita.");
        });
    }

    private sealed class ConfigurationFake : IConfigurationSystemAccess
    {
        public ConfigurationEnvironment Environment = new(true, true, 26100);
        public Dictionary<string, ConfigurationValue> Values = new(StringComparer.Ordinal);
        public HashSet<string> Keys = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> FailBefore = [], FailAfter = [];
        public Dictionary<string, IReadOnlyList<ConfigurationChange>> Dynamic = new();
        public Dictionary<string, ConfigurationValue> Intermediate = new();
        public Action? OnWrite;
        public bool IgnoreWrites, DenyRead, FailRefresh;
        public int Writes, Refreshes;
        private readonly Dictionary<string, string> paths = new();
        public ConfigurationEnvironment InspectEnvironment() => Environment;
        public IReadOnlyList<ConfigurationChange> ResolveDynamicChanges(string capability) => Dynamic.GetValueOrDefault(capability) ?? [];
        public ConfigurationValue Read(ConfigurationTarget target)
        {
            if (DenyRead) throw new UnauthorizedAccessException("Consulta negada.");
            return Values.TryGetValue(target.ResourceId, out var value) ? value : ConfigurationValue.Missing(Keys.Contains(target.Path));
        }
        public void Put(ConfigurationTarget target, ConfigurationValue value)
        {
            Values[target.ResourceId] = value with { KeyExists = true }; paths[target.ResourceId] = target.Path; Keys.Add(target.Path);
        }
        public void Write(ConfigurationTarget target, ConfigurationValue value)
        {
            Writes++;
            if (FailBefore.Contains(target.ResourceId)) throw new IOException("Escrita recusada.");
            if (Intermediate.TryGetValue(target.ResourceId, out var partial)) { Put(target, partial); throw new IOException("Falha após mudança parcial do recurso composto."); }
            if (!IgnoreWrites)
            {
                if (value.Exists) Put(target, value);
                else
                {
                    Values.Remove(target.ResourceId); paths.Remove(target.ResourceId);
                    if (!value.KeyExists && !paths.Values.Any(x => x.Equals(target.Path, StringComparison.OrdinalIgnoreCase))) Keys.Remove(target.Path);
                }
            }
            if (FailAfter.Contains(target.ResourceId)) throw new IOException("Falha após a escrita.");
            OnWrite?.Invoke();
        }
        public void Refresh(ConfigurationRefresh refresh) { Refreshes++; if (FailRefresh) throw new IOException("Sessão indisponível."); }
    }
}
