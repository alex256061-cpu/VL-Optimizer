using VLOptimizer.Core;

public static class HardwareInventoryTests
{
    public static void Register(Action<string, Func<Task>> test)
    {
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        test("Hardware: falha de uma fonte preserva demais grupos e proveniência", async () =>
        {
            var fake = new FakeHardware { Failure = HardwareQuery.Graphics };
            var report = await new HardwareInventoryService(fake).ReadAsync();
            Check(report.Sections.Count == 7 && fake.Calls.Count == 7, "Inventário interrompeu os demais grupos.");
            Check(report.Sections.Single(x => x.Id == "Graphics").Error is not null && report.Sections.Single(x => x.Id == "Processor").Rows.Count == 1, "Erro convertido em ausência ou perdeu dado válido.");
            Check(report.Sections.All(x => x.Source.StartsWith("WMI Win32_")) && report.Findings.Any(x => x.Contains("parcial")), "Fonte e parcialidade ausentes.");
        });
        test("Hardware: dados ausentes não viram prova de perfil de memória desligado", async () =>
        {
            var report = await new HardwareInventoryService(new FakeHardware()).ReadAsync();
            Check(!report.Findings.Any(x => x.Contains("configuração ")), "Inventou comparação sem velocidades.");
            Check(report.Findings.Any(x => x.Contains("não são inferidos")), "Limites de origem não expostos.");
        });
        test("Hardware: comparação de velocidades informa limite da inferência", async () =>
        {
            var row = new HardwareRow("DIMM A", new Dictionary<string, string?> { ["Speed"] = "6000", ["ConfiguredClockSpeed"] = "4800" });
            var findings = HardwareInventoryService.Analyze([new("MemoryModules", "RAM", "WMI Win32_PhysicalMemory", [row], null)]);
            Check(findings.Any(x => x.Contains("4800") && x.Contains("6000") && x.Contains("não comprova")), "Comparação perdeu limite ou valores.");
            var zero = HardwareInventoryService.Analyze([new("MemoryModules", "RAM", "WMI", [row with { Values = new Dictionary<string, string?> { ["Speed"] = "6000", ["ConfiguredClockSpeed"] = "0" } }], null)]);
            Check(!zero.Any(x => x.Contains("configuração ")), "Zero interpretado como clock válido.");
            await Task.CompletedTask;
        });
        test("Hardware: cancelamento anterior evita consultas", async () =>
        {
            var fake = new FakeHardware(); using var cts = new CancellationTokenSource(); cts.Cancel();
            try { await new HardwareInventoryService(fake).ReadAsync(null, cts.Token); throw new InvalidOperationException("Cancelamento ignorado."); }
            catch (OperationCanceledException) { Check(fake.Calls.Count == 0, "Consultou após cancelamento."); }
        });
        test("Mouse: estima eventos da janela sem converter pouca amostra em taxa", async () =>
        {
            var value = MouseEventStatistics.Analyze(Enumerable.Range(0, 101).Select(x => x * 8d));
            Check(value is { Samples: 100, MedianIntervalMs: 8, EstimatedEventsPerSecond: 125, P95IntervalMs: 8 }, "Intervalos/taxa incorretos.");
            Check(MouseEventStatistics.Analyze([0d, 8d, double.NaN, double.PositiveInfinity]) is null, "Amostra insuficiente produziu taxa.");
            Check(MouseEventStatistics.Analyze(Enumerable.Range(0, 20).Select(x => -x * 8d)) is null, "Tempo regressivo produziu taxa.");
            await Task.CompletedTask;
        });
    }
    private sealed class FakeHardware : IHardwareInventoryAccess
    {
        public HardwareQuery? Failure { get; init; }
        public List<HardwareQuery> Calls { get; } = [];
        public IReadOnlyList<HardwareRow> Read(HardwareQuery query)
        {
            Calls.Add(query); if (Failure == query) throw new IOException("Fonte de teste indisponível.");
            return [new(query.ToString(), new Dictionary<string, string?> { ["Name"] = "Item de teste" })];
        }
    }
}
