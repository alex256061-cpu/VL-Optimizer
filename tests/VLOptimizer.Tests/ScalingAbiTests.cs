using System.Runtime.InteropServices;
using VLOptimizer.Core;

public static class ScalingAbiTests
{
    public static void Register(Action<string, Func<Task>> test, string root)
    {
        Directory.CreateDirectory(root);
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        test("ABI: 32 parâmetros, 4 em registrador + 28 na stack", () =>
        {
            Check(ScalingEngineArguments.ParameterCount == 32, "Contagem de parâmetros errada");
            var fields = typeof(ScalingEngineArguments).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            Check(fields.Length == 32, $"Campos da projeção: {fields.Length}");
            return Task.CompletedTask;
        });
        test("ABI: string vazia não arrasta modo Custom", () =>
        {
            var args = ScalingEngineProjection.FromProfile(new ScalingProfile { ScalingMode = "", FitMode = "" });
            Check(args.Mode == ScalingEngineMode.Auto && args.FitMode == ScalingFitMode.AspectRatio, "Default incorreto");
            return Task.CompletedTask;
        });
        test("ABI: valores fora de faixa são presos", () =>
        {
            var args = ScalingEngineProjection.FromProfile(new ScalingProfile { Sharpness = 999, FlowScale = 1, MaxFrameLatency = 0, QueueTarget = 9, ScaleFactor = 500 });
            Check(args.Sharpness == 10 && args.FlowScale == 25 && args.MaxFrameLatency == 1 && args.QueueTarget == 2 && args.ScaleFactor <= 20f, "Faixas não aplicadas");
            return Task.CompletedTask;
        });
        test("ABI: LS1 performance vira método e subtipo", () =>
        {
            var args = ScalingEngineProjection.FromProfile(new ScalingProfile { ScalingAlgorithm = "LS1", Ls1Performance = true, Ls1Sharpness = 3 });
            Check(args.Method == ScalingEngineMethod.Ls1Performance && args.Subtype == 3, "LS1 mal mapeado");
            return Task.CompletedTask;
        });
        test("ABI: algoritmo desconhecido cai em Off", () =>
        {
            var args = ScalingEngineProjection.FromProfile(new ScalingProfile { ScalingAlgorithm = "Inventado" });
            Check(args.Method == ScalingEngineMethod.Off && args.Subtype == 0, "Fallback errado");
            return Task.CompletedTask;
        });
        test("ABI: bundle não carregado não chama o nativo", () =>
        {
            // Without a successfully resolved export the projection must stay inert.
            Check(!ScalingEngineNative.Bound, "Bundle não deveria estar carregado neste teste");
            Check(!ScalingEngineNative.Apply(ScalingEngineProjection.FromProfile(new ScalingProfile())), "Chamou nativo sem biblioteca");
            return Task.CompletedTask;
        });
        test("ABI: binário declarado bate com o disassembly (se presente)", () =>
        {
            // The parameter table is proven from ApplySettings.asm.json; if that file is updated the
            // expectation below must be re-derived rather than silently drifting.
            string asm = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../vlotimizer/artifacts/private/gdz-analysis/deep/native-deep/ApplySettings.asm.json"));
            if (!File.Exists(asm))
            {
                string alt = @"C:\Users\lucas larado\Desktop\vlotimizer\artifacts\private\gdz-analysis\deep\native-deep\ApplySettings.asm.json";
                if (!File.Exists(alt)) return Task.CompletedTask;
                asm = alt;
            }
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(asm));
            var ops = doc.RootElement.GetProperty("ops");
            int size = doc.RootElement.GetProperty("size").GetInt32();
            var stackOffsets = new List<long>();
            foreach (var op in ops.EnumerateArray())
            {
                var opcode = op.GetProperty("opcode").GetString() ?? "";
                var match = System.Text.RegularExpressions.Regex.Match(opcode, @"\[rsp \+ (0x[0-9a-fA-F]+)\]");
                if (match.Success) stackOffsets.Add(Convert.ToInt64(match.Groups[1].Value, 16));
            }
            Check(size == 402 && ops.GetArrayLength() == 68, $"Disassembly mudou: size={size} ops={ops.GetArrayLength()}");
            Check(stackOffsets.Count == 28, $"Leituras na stack: {stackOffsets.Count}");
            Check(stackOffsets.Min() == 0x28 && stackOffsets.Max() == 0x100, "Faixa da stack mudou");
            // Every reader must sit on an 8-byte slot boundary.
            Check(stackOffsets.All(x => (x - 0x28) % 8 == 0), "Slot desalinhado no disassembly");
            return Task.CompletedTask;
        });
    }
}
