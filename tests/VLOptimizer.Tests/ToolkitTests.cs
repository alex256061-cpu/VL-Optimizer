using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using System.Collections.Concurrent;
using System.Diagnostics;
using VLOptimizer.Core;

public static class ToolkitTests
{
    public static void Register(Action<string, Func<Task>> test, string root)
    {
        Directory.CreateDirectory(root); int sequence = 0;
        string Case() { var path = Path.Combine(root, (++sequence).ToString("D3")); Directory.CreateDirectory(path); return path; }
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        ToolkitCleanupService Cleaner(string path, int age = 1) => new(new[] { new ToolkitCleanupCategory("fixture", "Fixture", "Somente arquivos deste teste", new[] { path }, age) });
        string FileAt(string directory, string name, string content, int days = 3)
        { Directory.CreateDirectory(directory); var path = Path.Combine(directory, name); File.WriteAllText(path, content); File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-days)); return path; }
        string Xml(string directory, string extra = "", string name = "gta5_settings.xml")
        {
            Directory.CreateDirectory(directory); var path = Path.Combine(directory, name);
            File.WriteAllText(path, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<Settings><graphics><MSAA note=\"preservar\" value=\"4\"/><TXAA_Enabled value=\"true\"/>" + extra + "</graphics><video><Windowed value=\"0\"/><SamplingMode value=\"0\"/></video><other value=\"intacto\"/></Settings>", new UTF8Encoding(true)); return path;
        }
        ToolkitFiveMService Five(string directory) => new(Path.Combine(directory, "backups"), () => Array.Empty<string>());
        async Task Expect(Func<Task> action, string reason)
        { try { await action(); } catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or InvalidDataException or System.Xml.XmlException or OperationCanceledException) { return; } throw new InvalidOperationException(reason); }

        test("Toolkit FPS usa inverso da média de frametimes e lows lentos", () =>
        { var x = ToolkitFpsService.AnalyzeFrametimes(new[] { 10d, 10, 20, 40 })!; Check(x.Fps == 50 && x.Low1 == 25 && x.Low01 == 25 && x.FrameTimeMs == 20 && x.Frames == 4, "Fórmula FPS divergente"); return Task.CompletedTask; });
        test("Toolkit FPS 100 frames não calcula percentil como low", () =>
        { var values = Enumerable.Repeat(10d, 99).Append(100); var x = ToolkitFpsService.AnalyzeFrametimes(values)!; Check(x.Fps == 91.7 && x.Low1 == 10 && x.FrameTimeMs == 10.9, "Low ou arredondamento divergente"); return Task.CompletedTask; });
        test("Toolkit FPS descarta NaN infinito zero e durações fora do limite", () =>
        { Check(ToolkitFpsService.AnalyzeFrametimes(new[] { double.NaN, double.PositiveInfinity, -1d, 0, 1000 }) is null, "Frames inválidos aceitos"); Check(ToolkitFpsService.AnalyzeFrametimes(new[] { 999.9 })?.Frames == 1, "999,9 deve ser válido"); return Task.CompletedTask; });
        test("Toolkit CSV respeita aspas separa PID e sinaliza múltiplas swapchains", async () =>
        {
            var csv = "Application,ProcessID,MsBetweenPresents,SwapChainAddress\n\"Jogo, edição.exe\",42,10,0x1\n\"Jogo, edição.exe\",42,20,0x2\nOther.exe,43,40,0x3\nOther.exe,43,NaN,0x3\nOther.exe,43,1000,0x3\n";
            var report = await new ToolkitFpsService().ImportCsvAsync(new StringReader(csv));
            Check(report.Processes.Count == 2 && report.Processes[0].Application == "Jogo, edição.exe" && report.RejectedRows == 2 && report.Notices.Any(n => n.Contains("swapchain")), "CSV interpretado incorretamente");
        });
        test("Toolkit CSV CPUFrameTime é aceito com sua procedência", async () =>
        { var r = await new ToolkitFpsService().ImportCsvAsync(new StringReader("Application,ProcessID,CPUFrameTime\ngame.exe,42,10\n")); Check(r.Processes[0].Statistics.Fps == 100 && r.Notices[0].Contains("CPUFrameTime"), "Schema moderno ignorado"); });
        test("Toolkit CSV sem coluna de frame é recusado", async () =>
        { await Expect(async () => await new ToolkitFpsService().ImportCsvAsync(new StringReader("Application,ProcessID,GPUTime\ngame.exe,42,10")), "CSV sem frametimes aceito"); });
        test("Toolkit CSV descarta linha gigante sem acumulá-la em memória", async () =>
        { var r = await new ToolkitFpsService().ImportCsvAsync(new StringReader("Application,ProcessID,MsBetweenPresents\n" + new string('x', 100000) + "\ngame.exe,42,10\n")); Check(r.RejectedRows == 1 && r.Processes.Single().Statistics.Frames == 1, "Linha gigante não foi isolada"); });
        test("Toolkit comparação A/B preserva percentuais e não inventa zero", () =>
        { var a = ToolkitFpsService.AnalyzeFrametimes(Enumerable.Repeat(10d, 1000))!; var b = ToolkitFpsService.AnalyzeFrametimes(Enumerable.Repeat(8d, 1000))!; Check(ToolkitFpsService.Compare(a, b).FpsChangePercent == 25, "Delta A/B errado"); return Task.CompletedTask; });
        test("Toolkit FPS incremental aguarda cabeçalho e linha completos", () =>
        {
            var parser = new ToolkitFpsIncrementalParser(); parser.Append("Applic"); Check(!parser.HasHeader && parser.Snapshot(0).Processes.Count == 0, "Cabeçalho parcial criou medição");
            parser.Append("ation,ProcessID,MsBetweenPresents\r\n\"Jogo, edição.exe\",42,1"); Check(parser.HasHeader && parser.Snapshot(.1).Processes.Count == 0, "Linha parcial criou frame");
            parser.Append("0\r\n\"Jogo, edição.exe\",42,20\n"); var p = parser.Snapshot(1).Processes.Single(); Check(p.Application == "Jogo, edição.exe" && p.Frames == 2 && p.AverageFps == 66.7 && p.AverageFrameTimeMs == 15, "Incrementos mudaram a fórmula"); return Task.CompletedTask;
        });
        test("Toolkit FPS incremental aceita CPUFrameTime e filtra PID escolhido", () =>
        {
            var parser = new ToolkitFpsIncrementalParser(42); parser.Append("\uFEFFApplication,CPUFrameTime,ProcessID\nother.exe,1,43\ngame.exe,8,42\ngame.exe,NaN,42\n"); var sample = parser.Snapshot(2);
            Check(sample.Processes.Single().ProcessId == 42 && sample.Processes[0].AverageFps == 125 && sample.RejectedRows == 1 && sample.Notices.Any(n => n.Contains("CPUFrameTime")), "Schema/filtro incremental incorreto"); return Task.CompletedTask;
        });
        test("Toolkit FPS incremental só finaliza última linha após writer terminar", () =>
        {
            var parser = new ToolkitFpsIncrementalParser(); parser.Append("Application,ProcessID,MsBetweenPresents\ngame.exe,42,10"); Check(parser.Snapshot(1).Processes.Count == 0, "Última linha foi presumida completa"); parser.Complete(); parser.Complete(); Check(parser.Snapshot(1).Processes.Single().Frames == 1, "Complete duplicou ou perdeu frame"); return Task.CompletedTask;
        });
        test("Toolkit FPS incremental limita linha e rejeita cabeçalho inválido", async () =>
        {
            var parser = new ToolkitFpsIncrementalParser(); parser.Append("Application,ProcessID,MsBetweenPresents\n"); parser.Append(new string('x', 100000)); parser.Append("\ngame.exe,42,10\n"); Check(parser.Snapshot(1).Processes.Single().Frames == 1 && parser.RejectedRows == 1, "Linha longa contaminou próximos frames");
            await Expect(() => { new ToolkitFpsIncrementalParser().Append("Application,ProcessID,GPUTime\n"); return Task.CompletedTask; }, "Header sem frametime aceito");
        });
        test("Toolkit FPS incremental cancelado não incorpora fragmentos", async () =>
        {
            var parser = new ToolkitFpsIncrementalParser(); using var cancel = new CancellationTokenSource(); cancel.Cancel();
            await Expect(() => { parser.Append("Application,ProcessID,MsBetweenPresents\ngame.exe,42,10\n", cancel.Token); return Task.CompletedTask; }, "Cancelamento ignorado"); Check(!parser.HasHeader && parser.Snapshot(0).Processes.Count == 0, "Fluxo cancelado criou medição");
        });
        test("Toolkit FPS tail lê writer compartilhado e mantém UTF8 parcial", async () =>
        {
            var dir = Case(); var path = Path.Combine(dir, "own.csv"); await using var writer = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            byte[] first = Encoding.UTF8.GetBytes("Application,ProcessID,MsBetweenPresents\n\"Jogo, ç.exe\",42,10\n\"Jogo, ç.exe\",42,20\n"); int split = Array.IndexOf(first, (byte)0xc3) + 1;
            await writer.WriteAsync(first.AsMemory(0, split)); await writer.FlushAsync();
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5)); var header = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); var frames = new TaskCompletionSource<ToolkitFpsLiveSample>(TaskCreationOptions.RunContinuationsAsynchronously);
            var progress = new ImmediateToolkitProgress<ToolkitFpsLiveSample>(s => { if (s.Notices.Any(n => n.Contains("Métrica"))) header.TrySetResult(true); if (s.Processes.FirstOrDefault()?.Frames == 2) frames.TrySetResult(s); });
            var follow = ToolkitFpsCsvTail.FollowAsync(path, 42, progress, Stopwatch.StartNew(), cancel.Token, TimeSpan.FromMilliseconds(15));
            await header.Task.WaitAsync(TimeSpan.FromSeconds(3)); await writer.WriteAsync(first.AsMemory(split)); await writer.FlushAsync(); var live = await frames.Task.WaitAsync(TimeSpan.FromSeconds(3));
            cancel.Cancel(); await follow; Check(live.Processes.Single().Application == "Jogo, ç.exe" && live.Processes[0].AverageFps == 66.7, "Decoder/compartilhamento corrompeu linha");
        });
        test("Toolkit FPS tail cancela sem varrer outros CSV e sem publicar após cancel", async () =>
        {
            var dir = Case(); File.WriteAllText(Path.Combine(dir, "outra-sessao.csv"), "Application,ProcessID,MsBetweenPresents\nforeign.exe,42,10\n"); string own = Path.Combine(dir, "own-session.csv");
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5)); var samples = new ConcurrentQueue<ToolkitFpsLiveSample>(); var first = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var progress = new ImmediateToolkitProgress<ToolkitFpsLiveSample>(s => { samples.Enqueue(s); first.TrySetResult(true); }); var follow = ToolkitFpsCsvTail.FollowAsync(own, 42, progress, Stopwatch.StartNew(), cancel.Token, TimeSpan.FromMilliseconds(15));
            await first.Task.WaitAsync(TimeSpan.FromSeconds(3)); cancel.Cancel(); await follow; int count = samples.Count;
            File.WriteAllText(own, "Application,ProcessID,MsBetweenPresents\ngame.exe,42,10\n"); await Task.Delay(40);
            Check(samples.All(s => s.Processes.Count == 0) && samples.Count == count && samples.All(s => s.ElapsedSeconds >= 0), "Leu sessão alheia ou publicou após cancel");
        });
        test("Toolkit FPS tail rejeita CSV inválido de fixture sem executar coletor", async () =>
        {
            var dir = Case(); var path = Path.Combine(dir, "bad.csv"); File.WriteAllText(path, "Application,ProcessID,GPUTime\n"); using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await Expect(async () => await ToolkitFpsCsvTail.FollowAsync(path, 42, null, Stopwatch.StartNew(), cancel.Token, TimeSpan.FromMilliseconds(15)), "CSV inválido não foi recusado");
        });

        test("Toolkit limpeza prévia apenas lê e filtra idade por arquivo", async () =>
        {
            var dir = Case(); var old = FileAt(dir, "antigo.tmp", "antigo"); var sub = Path.Combine(dir, "pasta-antiga"); var recent = FileAt(sub, "novo.tmp", "novo", 0); Directory.SetLastWriteTimeUtc(sub, DateTime.UtcNow.AddDays(-20));
            var p = await Cleaner(dir).PreviewAsync(["fixture"]); Check(p.Files.Count == 1 && p.Files[0].Path == old && File.Exists(recent) && File.Exists(old), "Prévia apagou ou incluiu descendente novo de pasta antiga");
        });
        test("Toolkit limpeza exclui apenas arquivos listados e mantém raiz", async () =>
        {
            var dir = Case(); var old = FileAt(dir, "antigo.tmp", "aaaa"); var service = Cleaner(dir); var p = await service.PreviewAsync(["fixture"]); var fresh = FileAt(dir, "chegou-depois.tmp", "novo");
            var r = await service.ApplyAsync(p); Check(r.DeletedFiles == 1 && r.DeletedBytes == 4 && !File.Exists(old) && File.Exists(fresh) && Directory.Exists(dir), "Exclusão extrapolou prévia");
        });
        test("Toolkit limpeza preserva arquivo alterado depois da prévia", async () =>
        {
            var dir = Case(); var path = FileAt(dir, "mudou.tmp", "old"); var s = Cleaner(dir); var p = await s.PreviewAsync(["fixture"]); File.WriteAllText(path, "novo conteúdo");
            var r = await s.ApplyAsync(p); Check(r.SkippedFiles == 1 && r.DeletedFiles == 0 && File.Exists(path), "Arquivo modificado foi apagado");
        });
        test("Toolkit limpeza valida identidade mesmo com tamanho e datas restaurados", async () =>
        {
            var dir = Case(); var path = FileAt(dir, "recriado.tmp", "old"); var s = Cleaner(dir); var p = await s.PreviewAsync(["fixture"]); var original = p.Files.Single();
            File.Move(path, path + ".preservado"); File.WriteAllText(path, "new"); File.SetLastWriteTimeUtc(path, original.LastWriteUtc); File.SetCreationTimeUtc(path, original.CreationUtc);
            var r = await s.ApplyAsync(p); Check(r.SkippedFiles == 1 && File.ReadAllText(path) == "new", "Identidade FileID não foi conferida");
        });
        test("Toolkit limpeza recusa prévia fabricada e reutilizada", async () =>
        {
            var dir = Case(); FileAt(dir, "a.tmp", "a"); var s = Cleaner(dir); var p = await s.PreviewAsync(["fixture"]);
            await Expect(async () => await s.ApplyAsync(p with { }), "Clone de prévia foi aceito"); Check(File.Exists(Path.Combine(dir, "a.tmp")), "Clone alterou disco");
            p = await s.PreviewAsync(["fixture"]); await s.ApplyAsync(p); await Expect(async () => await s.ApplyAsync(p), "Prévia consumida foi aceita");
        });
        test("Toolkit limpeza cancelada preserva arquivos selecionados", async () =>
        {
            var dir = Case(); var file = FileAt(dir, "a.tmp", "a"); var s = Cleaner(dir); var p = await s.PreviewAsync(["fixture"]); using var c = new CancellationTokenSource(); c.Cancel();
            await Expect(async () => await s.ApplyAsync(p, c.Token), "Cancelamento ignorado"); Check(File.Exists(file), "Cancelamento apagou arquivo");
        });
        test("Toolkit limpeza preserva arquivo somente leitura", async () =>
        {
            var dir = Case(); var file = FileAt(dir, "read-only.tmp", "a"); File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
            try { var p = await Cleaner(dir).PreviewAsync(["fixture"]); Check(p.Files.Count == 0 && File.Exists(file), "Somente leitura entrou na ação"); }
            finally { File.SetAttributes(file, FileAttributes.Normal); }
        });
        test("Toolkit limpeza usa filtro cloud offline também ao aplicar", async () =>
        {
            var dir = Case(); var file = FileAt(dir, "offline.tmp", "data"); var s = Cleaner(dir); var p = await s.PreviewAsync(["fixture"]); File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.Offline);
            try { Check((await s.PreviewAsync(["fixture"])).Files.Count == 0, "Offline entrou na prévia"); var r = await s.ApplyAsync(p); Check(r.DeletedFiles == 0 && File.Exists(file), "Arquivo convertido em offline foi apagado"); }
            finally { File.SetAttributes(file, FileAttributes.Normal); }
        });
        test("Toolkit limpeza preserva arquivo aberto para escrita sem falso sucesso", async () =>
        {
            var dir = Case(); var file = FileAt(dir, "em-uso.tmp", "data"); var s = Cleaner(dir); var p = await s.PreviewAsync(["fixture"]);
            using var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            var r = await s.ApplyAsync(p); Check(r.DeletedFiles == 0 && r.FailedFiles == 1 && File.Exists(file), "Arquivo em uso foi apagado ou falha ocultada");
        });
        test("Toolkit limpeza recusa categoria desconhecida e raiz do volume", async () =>
        {
            await Expect(async () => await Cleaner(Case()).PreviewAsync(["nao-existe"]), "Categoria desconhecida aceita");
            await Expect(() => { _ = Cleaner(Path.GetPathRoot(root)!); return Task.CompletedTask; }, "Raiz de volume aceita");
        });
        test("Toolkit scanner agrega descendentes e maiores arquivos sem exclusão", async () =>
        {
            var dir = Case(); FileAt(dir, "a.bin", "1234"); FileAt(Path.Combine(dir, "sub"), "b.bin", "123456");
            var r = await new ToolkitDiskService().ScanAsync(dir); Check(r.TotalBytes == 10 && r.FileCount == 2 && r.Directories.Single(n => n.Path == dir).TotalBytes == 10 && r.LargestFiles[0].Bytes == 6, "Agregação de disco errada");
        });

        test("Toolkit FiveM prévia XML não escreve e interpreta ordem de atributos", async () =>
        {
            var dir = Case(); var path = Xml(dir); byte[] before = File.ReadAllBytes(path); var p = await Five(dir).PreviewPresetAsync(path, "fps", 4, true);
            Check(File.ReadAllBytes(path).SequenceEqual(before) && p.Changes.Single(x => x.Element == "MSAA").Before == "4" && p.Changes.Single(x => x.Element == "SamplingMode").After == "1" && p.Changes.Any(x => x.WillCreate), "Prévia XML inválida");
        });
        test("Toolkit FiveM aplica verifica e restaura bytes após reabrir serviço", async () =>
        {
            var dir = Case(); var path = Xml(dir); var bytes = File.ReadAllBytes(path); var s = Five(dir); var p = await s.PreviewPresetAsync(path, "fps", 4, true); var r = await s.ApplyAsync(p);
            Check(r.Success && r.Backup is not null, "Perfil não foi aplicado"); var xml = XDocument.Load(path); Check(xml.Descendants("MSAA").Single().Attribute("value")?.Value == "0" && xml.Descendants("MSAA").Single().Attribute("note")?.Value == "preservar" && xml.Descendants("Windowed").Single().Attribute("value")?.Value == "2", "XML/preservação incorretos");
            var restored = await Five(dir).RestoreAsync(r.Backup!.Id); Check(restored.Success && File.ReadAllBytes(path).SequenceEqual(bytes), "Restore não devolveu bytes exatos");
        });
        test("Toolkit FiveM bloqueia mudança concorrente após prévia", async () =>
        {
            var dir = Case(); var path = Xml(dir); var s = Five(dir); var p = await s.PreviewPresetAsync(path, "fps"); File.AppendAllText(path, "\n"); var expected = File.ReadAllBytes(path); var r = await s.ApplyAsync(p);
            Check(!r.Success && r.Conflict && File.ReadAllBytes(path).SequenceEqual(expected), "Alteração concorrente foi sobrescrita");
        });
        test("Toolkit FiveM undo não sobrescreve edição posterior", async () =>
        {
            var dir = Case(); var path = Xml(dir); var s = Five(dir); var r = await s.ApplyAsync(await s.PreviewPresetAsync(path, "fps")); File.AppendAllText(path, "\n<!-- edição manual -->");
            var expected = File.ReadAllBytes(path); var undo = await s.RestoreAsync(r.Backup!.Id); Check(undo.Conflict && !undo.Success && File.ReadAllBytes(path).SequenceEqual(expected), "Conflito FiveM perdido");
        });
        test("Toolkit FiveM backups são vinculados a cada caminho", async () =>
        {
            var dir = Case(); var a = Xml(Path.Combine(dir, "a")); var b = Xml(Path.Combine(dir, "b"), "<FXAA_Enabled value=\"true\"/>"); var aBytes = File.ReadAllBytes(a); var s = Five(dir);
            var first = await s.ApplyAsync(await s.PreviewPresetAsync(a, "fps")); await s.ApplyAsync(await s.PreviewPresetAsync(b, "balanced")); var bBytes = File.ReadAllBytes(b);
            var r = await s.RestoreAsync(first.Backup!.Id); Check(r.Success && File.ReadAllBytes(a).SequenceEqual(aBytes) && File.ReadAllBytes(b).SequenceEqual(bBytes), "Backup restaurou caminho errado");
        });
        test("Toolkit FiveM perfis sobrepostos restauram em ordem sem perder original", async () =>
        {
            var dir = Case(); var path = Xml(dir); var bytes = File.ReadAllBytes(path); var s = Five(dir);
            var a = await s.ApplyAsync(await s.PreviewPresetAsync(path, "fps")); var b = await s.ApplyAsync(await s.PreviewPresetAsync(path, "balanced"));
            Check((await s.RestoreAsync(a.Backup!.Id)).Conflict, "Reversão antiga deveria detectar sobreposição");
            Check((await s.RestoreAsync(b.Backup!.Id)).Success && (await s.RestoreAsync(a.Backup.Id)).Success && File.ReadAllBytes(path).SequenceEqual(bytes), "Pilha de backups perdeu o original");
        });
        test("Toolkit FiveM bloqueia jogo aberto e XML com DTD ou tags ambíguas", async () =>
        {
            var dir = Case(); var path = Xml(dir); var running = new ToolkitFiveMService(Path.Combine(dir, "backup"), () => new[] { "FiveM_b3095_GTAProcess" });
            await Expect(async () => await running.PreviewPresetAsync(path, "fps"), "Jogo aberto aceito");
            path = Xml(dir, "<MSAA value=\"8\"/>"); await Expect(async () => await Five(dir).PreviewPresetAsync(path, "fps"), "Tag duplicada aceita");
            File.WriteAllText(path, "<!DOCTYPE Settings [<!ENTITY x SYSTEM 'file:///C:/Windows/win.ini'>]><Settings><graphics>&x;</graphics></Settings>");
            await Expect(async () => await Five(dir).PreviewPresetAsync(path, "fps"), "DTD externa aceita");
        });
        test("Toolkit FiveM corrupção de backup bloqueia restore", async () =>
        {
            var dir = Case(); var path = Xml(dir); var s = Five(dir); var r = await s.ApplyAsync(await s.PreviewPresetAsync(path, "fps")); var bytes = File.ReadAllBytes(path);
            File.WriteAllText(Path.Combine(dir, "backups", r.Backup!.Id.ToString("N") + ".xml.bak"), "corrompido"); var undo = await s.RestoreAsync(r.Backup.Id);
            Check(!undo.Success && File.ReadAllBytes(path).SequenceEqual(bytes), "Backup corrompido aplicado");
        });

        test("Toolkit Steam e Epic descobrem bibliotecas usando manifests de fixture", () =>
        {
            var dir = Case(); var steam = Path.Combine(dir, "Steam"); var library = Path.Combine(dir, "Biblioteca ç"); Directory.CreateDirectory(Path.Combine(steam, "steamapps")); Directory.CreateDirectory(Path.Combine(library, "steamapps"));
            File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), "\"libraryfolders\" { \"0\" { \"path\" \"" + library.Replace("\\", "\\\\") + "\" } }");
            var gameDir = Path.Combine(library, "steamapps", "common", "Jogo"); FileAt(gameDir, "Game.exe", "fixture sem execução"); FileAt(gameDir, "setup.exe", "fixture sem execução");
            File.WriteAllText(Path.Combine(library, "steamapps", "appmanifest_123.acf"), "\"AppState\" { \"appid\" \"123\" \"name\" \"Jogo ç\" \"installdir\" \"Jogo\" }");
            var epic = Path.Combine(dir, "Epic"); Directory.CreateDirectory(epic); var epicGame = Path.Combine(dir, "Outro"); var launch = FileAt(epicGame, "Other.exe", "fixture sem execução");
            File.WriteAllText(Path.Combine(epic, "other.item"), JsonSerializer.Serialize(new { AppName = "other", DisplayName = "Outro jogo", InstallLocation = epicGame, LaunchExecutable = "Other.exe", AppCategories = new[] { "games" } }));
            var r = new ToolkitGameService().Discover(steam, epic, null, new[] { new ToolkitProcess(42, "Other", launch, 0, null, true) });
            Check(r.Games.Count == 2 && r.Games.Single(g => g.Source == "Steam").Executables.Count == 1 && r.Games.Single(g => g.Source == "Epic Games").IsRunning, "Biblioteca/manifestos incorretos"); return Task.CompletedTask;
        });
        test("Toolkit Epic recusa LaunchExecutable que escapa da instalação", () =>
        {
            var dir = Case(); var manifests = Path.Combine(dir, "Manifests"); var game = Path.Combine(dir, "Game"); Directory.CreateDirectory(manifests); Directory.CreateDirectory(game);
            File.WriteAllText(Path.Combine(manifests, "escape.item"), JsonSerializer.Serialize(new { AppName = "bad", DisplayName = "Bad", InstallLocation = game, LaunchExecutable = "../other.exe" }));
            var r = new ToolkitGameService().Discover(null, manifests, null); Check(r.Games.Count == 0 && r.Issues.Count > 0, "Traversal de manifesto aceito"); return Task.CompletedTask;
        });
        test("Toolkit gargalo usa média de CPU e pico de RAM sem inventar GPU", () =>
        {
            var samples = Enumerable.Range(1, 6).Select(i => new ToolkitDiagnosticSample(DateTimeOffset.UtcNow, i * 10, null, null, i * 10, true));
            var r = ToolkitDiagnosticsService.AnalyzeSamples(samples, 6); Check(r.CpuPercent == 35 && r.PeakRamPercent == 60 && r.GpuPercent is null && r.Assessment.Contains("parcial"), "Agregação/limitação de sensores errada"); return Task.CompletedTask;
        });
        test("Toolkit gargalo ignora amostras sem foco", async () =>
        {
            var samples = Enumerable.Range(1, 10).Select(i => new ToolkitDiagnosticSample(DateTimeOffset.UtcNow, 10, 10, 20, 30, i <= 5));
            await Expect(() => { ToolkitDiagnosticsService.AnalyzeSamples(samples, 5); return Task.CompletedTask; }, "Menos de seis amostras em foco aceitas");
        });
        test("Toolkit scheduler de RAM permanece desligado ao criar", async () =>
        { await using var scheduler = new ToolkitMemoryService().CreateScheduler(10); Check(!scheduler.IsRunning, "Scheduler iniciou sem consentimento"); });
    }
    private sealed class ImmediateToolkitProgress<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
}
