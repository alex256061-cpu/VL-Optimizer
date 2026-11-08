using System.Text.Json;
using VLOptimizer.Core;

public static class GameGraphicsTests
{
    public static void Register(Action<string, Func<Task>> test, string root)
    {
        Directory.CreateDirectory(root); int index = 0;
        (GameGraphicsService Service, GraphicsFake Fake, string Exe, string Root) New()
        {
            string dir = Path.Combine(root, (++index).ToString("D3")); Directory.CreateDirectory(dir); string exe = Path.Combine(dir, "game.exe");
            byte[] bytes = new byte[256]; bytes[0] = 0x4d; bytes[1] = 0x5a; BitConverter.GetBytes(128).CopyTo(bytes, 0x3c); bytes[128] = 0x50; bytes[129] = 0x45; File.WriteAllBytes(exe, bytes);
            var fake = new GraphicsFake(); return (new(dir, fake), fake, exe, dir);
        }
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        test("GPU jogo: mescla preferência preservando tokens alheios", () =>
        {
            Check(GameGraphicsService.MergeHighPerformance("VRROptimizeEnable=0;GpuPreference=1;Other=a;") == "VRROptimizeEnable=0;Other=a;GpuPreference=2;", "Tokens alheios perdidos.");
            Check(GameGraphicsService.MergeHighPerformance("GpuPreference=2;") == "GpuPreference=2;", "Não idempotente."); return Task.CompletedTask;
        });
        test("GPU jogo: prévia só lê e journal precede primeira escrita", async () =>
        {
            var x = New(); bool saved = false; x.Fake.BeforeWrite = () => saved = File.Exists(Path.Combine(x.Root, "game-graphics", "journal.json"));
            var p = await x.Service.PreviewAsync(x.Exe); Check(x.Fake.Writes == 0, "Prévia escreveu.");
            var result = await x.Service.ApplyAsync(p); Check(saved && result.Status == OperationStatus.Completed && result.Backup?.Verified == true, "Journal/verificação ausente.");
            var restored = await new GameGraphicsService(x.Root, x.Fake).RestoreAsync(p.Id);
            Check(restored.Status == OperationStatus.Reverted && !x.Fake.Value.Exists, "Não recuperou ausência após reabrir.");
        });
        test("GPU jogo: undo restaura texto exato e preserva edições externas", async () =>
        {
            var x = New(); var original = ConfigurationValue.String("GpuPreference=1;Other=original;"); x.Fake.Value = original;
            var p = await x.Service.PreviewAsync(x.Exe); await x.Service.ApplyAsync(p); await x.Service.RestoreAsync(p.Id); Check(x.Fake.Value == original, "Texto anterior não restaurado.");
            p = await x.Service.PreviewAsync(x.Exe); await x.Service.ApplyAsync(p); var changed = ConfigurationValue.String("Other=novo;"); x.Fake.Value = changed;
            Check((await x.Service.RestoreAsync(p.Id)).Status == OperationStatus.Conflict && x.Fake.Value == changed, "Conflito externo sobrescrito.");
        });
        test("GPU jogo: alteração do PE ou do Registro invalida prévia", async () =>
        {
            var x = New(); var p = await x.Service.PreviewAsync(x.Exe); x.Fake.Value = ConfigurationValue.String("Other=changed;");
            Check((await x.Service.ApplyAsync(p)).Status == OperationStatus.Conflict && x.Fake.Writes == 0, "Estado divergente aplicado.");
            var y = New(); p = await y.Service.PreviewAsync(y.Exe); using (var file = new FileStream(y.Exe, FileMode.Append)) file.WriteByte(1);
            Check((await y.Service.ApplyAsync(p)).Status == OperationStatus.Conflict && y.Fake.Writes == 0, "PE divergente aplicado.");
        });
        test("GPU jogo: cópia e replay não autorizam escrita", async () =>
        {
            var x = New(); var p = await x.Service.PreviewAsync(x.Exe);
            Check((await x.Service.ApplyAsync(p with { })).Status == OperationStatus.Conflict, "Cópia autorizada."); await x.Service.ApplyAsync(p); int writes = x.Fake.Writes;
            Check((await x.Service.ApplyAsync(p)).Status == OperationStatus.Conflict && x.Fake.Writes == writes, "Replay autorizado.");
        });
        test("GPU jogo: falha após escrita mantém recuperação e falso sucesso é recusado", async () =>
        {
            var x = New(); x.Fake.FailAfter = true; var p = await x.Service.PreviewAsync(x.Exe); var result = await x.Service.ApplyAsync(p);
            Check(result.Status == OperationStatus.Unverified && (await x.Service.GetBackupsAsync()).Count == 1, "Falha sem backup."); x.Fake.FailAfter = false;
            Check((await x.Service.RestoreAsync(p.Id)).Status == OperationStatus.Reverted, "Falha parcial sem recuperação.");
            var y = New(); y.Fake.Ignore = true; result = await y.Service.ApplyAsync(await y.Service.PreviewAsync(y.Exe)); Check(result.Status == OperationStatus.Unverified, "Escrita ignorada concluída.");
        });
        test("GPU jogo: journal inválido impede primeira escrita", async () =>
        {
            var x = New(); var p = await x.Service.PreviewAsync(x.Exe); Directory.CreateDirectory(Path.Combine(x.Root, "game-graphics")); File.WriteAllText(Path.Combine(x.Root, "game-graphics", "journal.json"), "{inválido");
            try { await x.Service.ApplyAsync(p); throw new InvalidOperationException("Journal inválido aceito."); } catch (JsonException) { Check(x.Fake.Writes == 0, "Escreveu sem journal."); }
        });
    }
    private sealed class GraphicsFake : IConfigurationSystemAccess
    {
        public ConfigurationValue Value = ConfigurationValue.Missing(); public int Writes; public Action? BeforeWrite; public bool FailAfter, Ignore;
        public ConfigurationEnvironment InspectEnvironment() => new(true, false, 19045);
        public IReadOnlyList<ConfigurationChange> ResolveDynamicChanges(string capability) => [];
        public ConfigurationValue Read(ConfigurationTarget target) => Value;
        public void Write(ConfigurationTarget target, ConfigurationValue value)
        { BeforeWrite?.Invoke(); Writes++; if (!Ignore) Value = value; if (FailAfter) throw new IOException("Falha simulada após escrita."); }
        public void Refresh(ConfigurationRefresh refresh) { }
    }
}
