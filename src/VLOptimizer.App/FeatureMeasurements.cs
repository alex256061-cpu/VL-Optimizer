using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using VLOptimizer.Core;

namespace VLOptimizer.App;

public sealed partial class MainWindow
{
    private string _measurementTab = "FPS";
    private IReadOnlyList<ToolkitProcess> _observedProcesses = [];
    private IReadOnlyList<ToolkitInstalledApp>? _installedApps;
    private ToolkitDiskScan? _diskScan;
    private ToolkitMemoryScheduler? _memoryScheduler;
    private int _memorySchedulerMinutes = 10;

    private UIElement BuildMeasurementsPage()
    {
        var root = Stack();
        root.Children.Add(FeatureTabBar(["FPS", "Diagnóstico", "Memória", "Disco", "Programas", "Manutenção", "Hardware"], _measurementTab, tab => _measurementTab = tab));
        root.Children.Add(_measurementTab switch { "Diagnóstico" => BuildDiagnosticTool(), "Memória" => BuildMemoryTool(), "Disco" => BuildDiskTool(), "Programas" => BuildInstalledAppsTool(), "Manutenção" => BuildMaintenanceTool(), "Hardware" => BuildHardwareTool(), _ => BuildFpsTool() });
        return root;
    }

    /// <summary>Liga ou desliga a medição ao vivo. Nada aqui lança: uma falha vira texto no painel.</summary>
    private async Task ToggleLiveFpsAsync()
    {
        if (_busy || _smoke) return;
        if (_liveFpsStatus?.Running == true)
        {
            await StopLiveFpsAsync("Medição encerrada.");
            RenderPage();
            return;
        }
        string path = ResolvePresentMonPath();
        if (path.Length == 0)
        {
            _liveFpsMessage = "PresentMon não encontrado. Inclua-o no pacote ou selecione uma cópia local.";
            RenderPage();
            return;
        }
        await RunFeatureAsync("Iniciando medição de FPS…", async token =>
        {
            int? pinned = _capturePid > 0 ? _capturePid : null;
            (bool ok, string message) = LiveFps.Start(path, pinned);
            _liveFpsMessage = message;
            if (!ok) throw new InvalidOperationException(message);
            StartLiveFpsPolling();
            await Task.CompletedTask;
        });
        RenderPage();
    }

    private Task PinLiveFpsAsync(int processId)
    {
        try
        {
            LiveFps.Statistics.Pin(processId);
            LiveFps.Statistics.RegisterGameProcess(processId);
            _capturePid = processId;
            PollLiveFps();
            Notice("Alvo fixado em PID " + processId + ".");
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException) { Notice("Não foi possível fixar o alvo.", true); }
        return Task.CompletedTask;
    }

    private UIElement BuildFpsTool()
    {
        var root = Stack(); root.Children.Add(InlineNote("FPS precisa de quadros capturados.", "Medição ao vivo usa o PresentMon em fluxo contínuo, como a referência. A captura por duração gera um relatório com lows. Medições só são comparáveis com jogo, cena e condições equivalentes.", "\uE9D9"));
        RegisterFpsHotkey();
        AddLiveFpsPanel(root);

        // Medição ao vivo: o mesmo fluxo contínuo da referência, sem duração fixa.
        var live = Stack(); live.Children.Add(Text("Medição ao vivo", 17, weight: FontWeights.SemiBold));
        live.Children.Add(Text(_presentMonPath.Length == 0 ? "PresentMon não encontrado. Rode scripts\\publish.ps1 para incluí-lo no pacote, ou selecione uma cópia local." : "PresentMon: " + _presentMonPath, 12, Muted, margin: new Thickness(0, 9, 0, 12)));
        var liveActions = new WrapPanel();
        bool liveRunning = _liveFpsStatus?.Running == true;
        liveActions.Children.Add(FeatureButton(liveRunning ? "Parar medição" : "Iniciar medição ao vivo", () => _ = ToggleLiveFpsAsync(), !liveRunning));
        if (liveRunning) liveActions.Children.Add(FeatureButton("Atualizar agora", () => PollLiveFps()));
        if (liveRunning && _liveFpsStatus?.Target is { } liveTarget) liveActions.Children.Add(FeatureButton("Fixar " + liveTarget.Application, () => _ = PinLiveFpsAsync(liveTarget.ProcessId)));
        liveActions.Children.Add(FeatureButton("Selecionar PresentMon…", PickPresentMon));
        live.Children.Add(liveActions);
        live.Children.Add(Text("Lê a janela de 10 segundos e mostra médias e lows em tempo real. O jogo precisa apresentar quadros; tela cheia exclusiva bloqueia a leitura.", 12, Muted, margin: new Thickness(0, 10, 0, 0)));
        root.Children.Add(Card(live));

        AddFpsOverlayControls(root);
        var actions = new WrapPanel(); actions.Children.Add(FeatureButton("Importar CSV de frames…", () => _ = ImportFpsAsync(), true)); actions.Children.Add(FeatureButton("Atualizar processos", () => _ = RefreshObservedProcessesAsync())); root.Children.Add(actions);
        var capture = Stack(); capture.Children.Add(Text("Captura por processo", 17, weight: FontWeights.SemiBold));
        capture.Children.Add(Text(_presentMonPath.Length == 0 ? "PresentMon não selecionado. A importação CSV funciona sem esse executável." : _presentMonPath, 12, Muted, margin: new Thickness(0, 9, 0, 12)));
        var fields = new WrapPanel(); fields.Children.Add(PidField());
        var duration = NewChoice("Duração da captura", new[] { 15, 30, 60, 120 }.Select(n => (n.ToString(), n + " segundos")).ToArray(), _captureSeconds.ToString(), value => _captureSeconds = int.Parse(value, CultureInfo.InvariantCulture)); duration.Margin = new Thickness(0, 0, 12, 12); fields.Children.Add(duration);
        var start = FeatureButton("Capturar FPS", () => _ = CaptureFpsAsync(), true); start.IsEnabled = !_busy && _presentMonPath.Length > 0; start.VerticalAlignment = VerticalAlignment.Bottom; fields.Children.Add(start); capture.Children.Add(fields);
        capture.Children.Add(Text("O processo deve permanecer aberto. A captura inicia ao confirmar; uma sessão não comprova ganho e pode ter linhas rejeitadas ou dados parciais.", 12, Muted)); root.Children.Add(Card(capture));
        if (_fpsReport != null)
        {
            root.Children.Add(SectionTitle("Frames analisados", $"{_fpsReport.Processes.Count} processos · {_fpsReport.RejectedRows} linhas rejeitadas"));
            foreach (var process in _fpsReport.Processes)
            {
                var stats = process.Statistics; var content = Stack(); content.Children.Add(Text(process.Application + " · PID " + process.ProcessId, 16, weight: FontWeights.SemiBold));
                content.Children.Add(FpsMetrics(stats));
                content.Children.Add(Text($"{stats.Frames:N0} frames · {stats.FrameDurationSeconds:0.##} segundos de tempo de frames. Não é a duração de relógio da sessão.", 11, Muted, margin: new Thickness(0, 9, 0, 0)));
                var baseline = _baselineFps?.Processes.FirstOrDefault(p => p.Application.Equals(process.Application, StringComparison.OrdinalIgnoreCase));
                if (baseline != null)
                {
                    var comparison = ToolkitFpsService.Compare(baseline.Statistics, stats);
                    content.Children.Add(Text($"Variação em relação à referência: FPS {comparison.FpsChangePercent:+0.0;-0.0;0}% · 1% low {comparison.Low1ChangePercent:+0.0;-0.0;0}%", 13, AccentSoft, margin: new Thickness(0, 14, 0, 0)));
                    content.Children.Add(Text("A diferença descreve estas duas amostras. Ela não atribui causalidade a um ajuste.", 11, Muted, margin: new Thickness(0, 6, 0, 0)));
                }
                root.Children.Add(Card(content));
            }
            var reference = new WrapPanel(); reference.Children.Add(FeatureButton("Usar esta sessão como referência", () => { _baselineFps = _fpsReport; Notice("Referência guardada nesta sessão. Importe ou capture novamente sob condições equivalentes."); RenderPage(); }));
            reference.Children.Add(FeatureButton("Exportar estatísticas…", ExportFpsReport)); if (_baselineFps != null) reference.Children.Add(FeatureButton("Limpar referência", () => { _baselineFps = null; RenderPage(); })); root.Children.Add(reference);
            foreach (var notice in _fpsReport.Notices) root.Children.Add(Text(notice, 12, Muted, margin: new Thickness(0, 8, 0, 0)));
        }
        return root;
    }

    private static UIElement FpsMetrics(ToolkitFpsStatistics stats)
    {
        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 14, 0, 0) };
        foreach (var (label, value) in new[] { ("FPS MÉDIO", stats.Fps.ToString("0.0")), ("1% LOW", stats.Low1.ToString("0.0")), ("0,1% LOW", stats.Low01.ToString("0.0")), ("FRAMETIME MÉDIO", stats.FrameTimeMs.ToString("0.00") + " ms") })
        { var content = Stack(); content.Margin = new Thickness(0, 0, 15, 0); content.Children.Add(Text(label, 9, Muted, FontWeights.SemiBold)); content.Children.Add(Text(value, 23, AccentSoft, FontWeights.Bold, new Thickness(0, 6, 0, 0))); grid.Children.Add(content); }
        return grid;
    }

    private UIElement PidField()
    {
        var panel = Stack(); panel.Margin = new Thickness(0, 0, 12, 12); panel.Children.Add(Text("PID do jogo ou aplicativo", 12, Muted, margin: new Thickness(0, 0, 0, 5)));
        var entry = new TextBox { Text = _capturePid > 0 ? _capturePid.ToString() : "", Width = 210, IsEnabled = !_busy, ToolTip = "Identificador numérico do processo" }; AutomationProperties.SetName(entry, "PID para medição");
        entry.TextChanged += (_, _) => _capturePid = int.TryParse(entry.Text, out var id) && id > 0 ? id : 0; panel.Children.Add(entry);
        if (_observedProcesses.Count > 0)
        {
            var choose = new ComboBox { MinHeight = 34, Width = 210, Margin = new Thickness(0, 7, 0, 0), ItemsSource = _observedProcesses.Where(p => p.HasWindow).Select(p => new PidOption(p.Id, p.Name + " · " + p.Id)).ToArray(), DisplayMemberPath = "Label", SelectedValuePath = "Id", IsEnabled = !_busy };
            AutomationProperties.SetName(choose, "Escolher processo com janela"); choose.SelectionChanged += (_, _) => { if (choose.SelectedValue is int id) { _capturePid = id; entry.Text = id.ToString(); } }; panel.Children.Add(choose);
        }
        return panel;
    }
    private record PidOption(int Id, string Label) { public override string ToString() => Label; }

    private static StackPanel NewChoice(string label, IReadOnlyList<(string Id, string Title)> options, string selected, Action<string> changed)
    {
        var panel = Stack(); panel.Children.Add(Text(label, 12, Muted, margin: new Thickness(0, 0, 0, 5)));
        var combo = new ComboBox { MinHeight = 44, MinWidth = 145, ItemsSource = options.Select(o => new Choice(o.Id, o.Title)).ToArray(), DisplayMemberPath = "Title", SelectedValuePath = "Id", SelectedValue = selected };
        AutomationProperties.SetName(combo, label); combo.SelectionChanged += (_, _) => { if (combo.SelectedValue is string id) changed(id); }; panel.Children.Add(combo); return panel;
    }
    private record Choice(string Id, string Title) { public override string ToString() => Title; }

    private void PickPresentMon()
    {
        if (_smoke) return; var picker = new OpenFileDialog { Title = "Selecionar cópia local do PresentMon", Filter = "Executável (*.exe)|*.exe", CheckFileExists = true };
        if (picker.ShowDialog(this) == true) { _presentMonPath = picker.FileName; RenderPage(); }
    }

    /// <summary>Resolves the capture tool without user interaction: a copy beside the app first, then
    /// one the user picked, then the local build output. Returns an empty string when nothing works so
    /// the page says so instead of offering a dead button.</summary>
    private string ResolvePresentMonPath()
    {
        if (_presentMonPath.Length > 0 && File.Exists(_presentMonPath)) return _presentMonPath;
        foreach (var candidate in new[]
        {
            Path.Combine(AppContext.BaseDirectory, "PresentMon.exe"),
            Path.Combine(AppContext.BaseDirectory, "tools", "PresentMon.exe"),
        })
        {
            if (File.Exists(candidate)) { _presentMonPath = candidate; return candidate; }
        }
        return "";
    }

    private async Task RefreshObservedProcessesAsync() => await RunFeatureAsync("Lendo processos com janela…", async token => _observedProcesses = await Toolkit.Inventory.ReadProcessesAsync(token));

    private async Task ImportFpsAsync()
    {
        if (_busy || _smoke) return; var picker = new OpenFileDialog { Title = "Importar captura CSV do PresentMon", Filter = "Captura CSV (*.csv)|*.csv", CheckFileExists = true };
        if (picker.ShowDialog(this) != true) return;
        await RunFeatureAsync("Analisando frametimes…", async token => { _fpsReport = await Toolkit.Fps.ImportCsvAsync(picker.FileName, token); Notice("Frames analisados. As métricas refletem apenas linhas válidas da captura importada."); });
    }

    /// <summary>Encerra a captura por duração. A medição ao vivo substitui este modo no fluxo normal,
    /// mas o relatório final com lows continua disponível a partir do CSV importado.</summary>
    private async Task CaptureFpsAsync()
    {
        if (_busy || _smoke) return;
        if (_capturePid <= 0) { Notice("Informe um PID válido ou escolha um processo com janela.", true); return; }
        if (!ConfirmFeature("Capturar FPS por duração?", $"O PresentMon observará o PID {_capturePid} por {_captureSeconds} segundos para gerar o relatório final. Prepare a cena antes de continuar.\n\nExecutável: {_presentMonPath}", "Iniciar captura")) return;
        int pid = _capturePid, seconds = _captureSeconds;
        _fpsReport = null;
        await RunFeatureAsync("Capturando frames…", async token =>
        {
            var result = await Toolkit.Fps.CaptureAsync(_presentMonPath, pid, seconds, null, token);
            _fpsReport = result.Report;
            await RecordFeatureActivityAsync("fps-capture", "Captura FPS · PID " + pid, result.CompletedDuration ? "Captura concluída" : "Captura parcial", $"Código de saída {result.ExitCode}. {_fpsReport.Processes.Count} processos com frames válidos. {result.StandardError}", result.ExitCode == 0 && result.CompletedDuration ? OperationStatus.Completed : OperationStatus.PartiallyCompleted);
            Notice(result.CompletedDuration ? "Captura finalizada. Consulte frames aceitos, linhas rejeitadas e observações." : "A captura terminou antes do prazo. Consulte os dados parciais e as observações.");
        });
    }

    private void ExportFpsReport()
    {
        if (_fpsReport == null || _smoke) return;
        var picker = new SaveFileDialog { FileName = "VL-FPS-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".json", Filter = "Estatísticas JSON (*.json)|*.json" };
        if (picker.ShowDialog(this) != true) return;
        try { File.WriteAllText(picker.FileName, System.Text.Json.JsonSerializer.Serialize(new { Current = _fpsReport, Reference = _baselineFps }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })); Notice("Estatísticas exportadas. O arquivo inclui nomes de aplicativos e identificadores de processos."); }
        catch (Exception ex) { Notice("Não foi possível exportar: " + ex.Message, true); }
    }

    private UIElement BuildDiagnosticTool()
    {
        var root = Stack(); root.Children.Add(InlineNote("Observe sob a carga que interessa.", "A coleta reúne CPU, núcleo mais ocupado, GPU quando disponível e pico de RAM. O diagnóstico é uma indicação da amostra; ele não prova a causa de uma queda de desempenho.", "\uE9D9"));
        var controls = new WrapPanel(); controls.Children.Add(PidField()); controls.Children.Add(FeatureButton("Atualizar processos", () => _ = RefreshObservedProcessesAsync())); controls.Children.Add(FeatureButton("Observar 30 segundos", () => _ = CollectDiagnosticsAsync(), true)); root.Children.Add(controls);
        root.Children.Add(Text("PID opcional: sem seleção, a coleta observa o sistema. Quando um processo é escolhido, intervalos fora de foco são identificados pelo coletor.", 12, Muted, margin: new Thickness(0, 0, 0, 16)));
        if (_bottleneckReport != null)
        {
            var report = _bottleneckReport; var content = Stack(); content.Children.Add(Chip(report.Partial ? "AMOSTRA PARCIAL" : "AMOSTRA CONCLUÍDA", AccentSoft));
            content.Children.Add(Text(report.Assessment, 22, weight: FontWeights.Bold, margin: new Thickness(0, 14, 0, 12)));
            content.Children.Add(Text($"CPU: {Percent(report.CpuPercent)} · núcleo mais ocupado: {Percent(report.BusiestCorePercent)} · GPU: {Percent(report.GpuPercent)} · pico de RAM: {report.PeakRamPercent:0.#}%", 14));
            content.Children.Add(Text($"{report.Samples.Count} amostras · {report.MeasuredSeconds:0.#} s medidos · {report.AwaySeconds:0.#} s fora de foco" + (report.ProcessClosed ? " · processo encerrado durante a coleta" : ""), 12, Muted, margin: new Thickness(0, 12, 0, 0)));
            foreach (var notice in report.Notices) content.Children.Add(Text(notice, 12, Muted, margin: new Thickness(0, 9, 0, 0))); root.Children.Add(Card(content, true));
        }
        return root;
    }

    private async Task CollectDiagnosticsAsync()
    {
        var pid = _capturePid > 0 ? (int?)_capturePid : null;
        await RunFeatureAsync("Observando recursos por 30 segundos…", async token => { var progress = new Progress<ToolkitDiagnosticSample>(sample => _analysisStatus.Text = $"◌  Amostra de {sample.At:HH:mm:ss} · CPU {Percent(sample.CpuPercent)} · RAM {sample.RamPercent:0.#}%"); _bottleneckReport = await Toolkit.Diagnostics.CollectAsync(30, pid, progress, token); });
    }

    private UIElement BuildMemoryTool()
    {
        var root = Stack(); root.Children.Add(InlineNote("Memória disponível é uma leitura, não uma meta.", "A lista standby contém cache reutilizável pelo Windows. Esvaziá-la pode aumentar leituras de disco e não demonstra ganho de FPS. Consulte os números e os privilégios antes de decidir.", "\uE964"));
        var actions = new WrapPanel(); actions.Children.Add(FeatureButton("Consultar memória", () => _ = ReadMemoryAsync(), true)); actions.Children.Add(FeatureButton("Revisar esvaziamento de standby", () => _ = PurgeMemoryAsync())); root.Children.Add(actions);
        var schedule = Stack(); schedule.Children.Add(Text(_memoryScheduler?.IsRunning == true ? "Agendamento ligado nesta sessão" : "Agendamento desligado", 14, AccentSoft, FontWeights.SemiBold));
        schedule.Children.Add(Text("Opcional: esvazia standby no intervalo escolhido enquanto o VL estiver aberto, inclusive minimizado. Não inicia com o Windows e não persiste após fechar o aplicativo. Cache reutilizável pode precisar ser lido do disco novamente.", 12, Muted, margin: new Thickness(0, 9, 0, 12)));
        var interval = NewChoice("Intervalo", new[] { 5, 10, 15, 30, 60 }.Select(n => (n.ToString(), n + " minutos")).ToArray(), _memorySchedulerMinutes.ToString(), value => _memorySchedulerMinutes = int.Parse(value, CultureInfo.InvariantCulture)); interval.IsEnabled = _memoryScheduler?.IsRunning != true && !_busy; schedule.Children.Add(interval);
        schedule.Children.Add(FeatureButton(_memoryScheduler?.IsRunning == true ? "Parar agendamento" : "Revisar agendamento de standby", () => _ = _memoryScheduler?.IsRunning == true ? StopMemorySchedulerAsync() : StartMemorySchedulerAsync()));
        root.Children.Add(new Expander { Header = "Agendamento opcional de standby", Content = schedule, Foreground = Muted, FontSize = 13, Margin = new Thickness(0, 7, 0, 18) });
        if (_memoryRead != null)
        {
            var memory = _memoryRead; var content = Stack(); content.Children.Add(Text($"{memory.UsedPercent:0.#}% em uso", 28, weight: FontWeights.Bold));
            content.Children.Add(Text($"{Bytes(memory.AvailableBytes)} disponíveis de {Bytes(memory.TotalBytes)}", 14, Muted, margin: new Thickness(0, 8, 0, 12))); content.Children.Add(Bar(memory.UsedPercent));
            content.Children.Add(Text("Standby: " + (memory.StandbyBytes.HasValue ? Bytes(memory.StandbyBytes.Value) : "indisponível") + " · comprometida: " + (memory.CommittedBytes.HasValue ? Bytes(memory.CommittedBytes.Value) : "indisponível"), 13, Muted, margin: new Thickness(0, 14, 0, 0)));
            if (memory.StandbyNotice != null) content.Children.Add(Text(memory.StandbyNotice, 12, Muted, margin: new Thickness(0, 8, 0, 0))); root.Children.Add(Card(content));
        }
        return root;
    }
    private async Task ReadMemoryAsync() => await RunFeatureAsync("Consultando memória…", async token => _memoryRead = await Toolkit.Memory.ReadAsync(token));
    private async Task PurgeMemoryAsync()
    {
        if (_busy || _smoke || !ConfirmFeature("Esvaziar a lista standby?", "Esta ação requer privilégios de administrador e remove páginas de cache da lista standby. Não fecha aplicativos e não possui desfazer. O Windows voltará a construir o cache conforme o uso. Memória liberada não equivale a ganho de FPS.", "Executar uma vez")) return;
        await RunFeatureAsync("Esvaziando standby e verificando…", async token => { var result = await Toolkit.Memory.PurgeStandbyAsync(token); await RecordFeatureActivityAsync("standby-purge", "Esvaziamento de standby", result.Success ? "Concluído" : "Não concluído", result.Message, result.Success ? OperationStatus.Completed : OperationStatus.Failed); _memoryRead = await Toolkit.Memory.ReadAsync(CancellationToken.None); Notice(result.Message, !result.Success); });
    }
    private async Task StartMemorySchedulerAsync()
    {
        if (_busy || _smoke || _memoryScheduler?.IsRunning == true) return;
        if (_snapshot?.IsAdministrator != true) { Notice("O agendamento de purga exige uma sessão com privilégios de administrador. Consulte o diagnóstico e reabra o aplicativo elevado se desejar ativá-lo."); return; }
        if (!ConfirmFeature("Ativar purga periódica de standby?", $"A cada {_memorySchedulerMinutes} minutos, enquanto este VL estiver aberto, o Windows receberá uma solicitação de esvaziamento de standby. A primeira ocorre após o intervalo.\n\nO cache pode precisar ser reconstruído, elevando leituras de disco. Não há desfazer e não foi demonstrado ganho de FPS. Você pode parar nesta tela; fechar o VL também para o agendamento.", "Ativar neste intervalo")) return;
        await StopMemorySchedulerAsync(false);
        var results = new Progress<ToolkitMemoryPurgeResult>(result => _ = ObserveMemorySchedulerAsync(result));
        _memoryScheduler = Toolkit.Memory.CreateScheduler(_memorySchedulerMinutes, results); _memoryScheduler.Start(); RenderPage(); Notice("Agendamento ligado apenas nesta execução. A primeira purga ocorre após o intervalo escolhido.");
    }
    private async Task ObserveMemorySchedulerAsync(ToolkitMemoryPurgeResult result)
    {
        await RecordFeatureActivityAsync("standby-schedule", "Purga periódica de standby", result.Success ? "Solicitação aceita" : "Solicitação recusada", result.Message, result.Success ? OperationStatus.Completed : OperationStatus.Failed);
        if (!result.Success) Notice("Purga agendada não concluída: " + result.Message, true);
    }
    private async Task StopMemorySchedulerAsync(bool notify = true)
    {
        var scheduler = _memoryScheduler; _memoryScheduler = null;
        if (scheduler != null) await scheduler.DisposeAsync();
        if (notify) { RenderPage(); Notice("Agendamento de standby parado."); }
    }

    private UIElement BuildDiskTool()
    {
        var root = Stack(); root.Children.Add(InlineNote("Encontre o que ocupa espaço.", "Escolha uma pasta para calcular os tamanhos e localizar arquivos grandes. A varredura é somente leitura, ignora links e pode ficar parcial quando faltarem permissões.", "\uEDA2")); root.Children.Add(FeatureButton("Escolher pasta e verificar…", () => _ = ScanDiskAsync(), true));
        if (_diskScan == null) return root;
        var scan = _diskScan; root.Children.Add(Text($"{Bytes(scan.TotalBytes)} · {scan.FileCount:N0} arquivos" + (scan.Complete ? "" : " · varredura parcial"), 23, weight: FontWeights.Bold, margin: new Thickness(0, 12, 0, 6))); root.Children.Add(Text(scan.Root, 12, Muted, margin: new Thickness(0, 0, 0, 16)));
        foreach (var directory in scan.Directories.OrderByDescending(d => d.TotalBytes).Take(30)) root.Children.Add(SearchResult("PASTA", directory.Path, $"{Bytes(directory.TotalBytes)} · {directory.FileCount:N0} arquivos", () => OpenExternal(directory.Path)));
        var files = Stack(); foreach (var file in scan.LargestFiles.Take(30)) files.Children.Add(Text($"{Bytes(file.Bytes)} · {file.Path}", 12, Muted, margin: new Thickness(0, 8, 0, 0)));
        root.Children.Add(new Expander { Header = "Maiores arquivos encontrados", Content = files, Foreground = Muted, FontSize = 12 }); AddIssueDetails(root, scan.Issues); return root;
    }
    private async Task ScanDiskAsync()
    {
        if (_busy || _smoke) return; var picker = new OpenFolderDialog { Title = "Pasta para varredura de tamanho" }; if (picker.ShowDialog(this) != true) return;
        await RunFeatureAsync("Calculando tamanhos de pastas…", async token => { var progress = new Progress<ToolkitScanProgress>(p => _analysisStatus.Text = $"◌  {p.Directories:N0} pastas · {p.Files:N0} arquivos · {Bytes(p.Bytes)} observados"); _diskScan = await Toolkit.Disk.ScanAsync(picker.FolderName, progress, token); });
    }

    private UIElement BuildInstalledAppsTool()
    {
        var root = Stack(); root.Children.Add(InlineNote("Programas registrados neste Windows.", "O inventário lê as entradas de instalação. Nomes e estimativas vêm dos registros de cada programa. A desinstalação abre o assistente registrado, após conferir o executável e revisar o pedido.", "\uE8A5"));
        var actions = new WrapPanel(); actions.Children.Add(FeatureButton("Ler programas instalados", () => _ = RefreshInstalledAppsAsync(), true)); actions.Children.Add(FeatureButton("Abrir aplicativos do Windows", () => OpenExternal("ms-settings:appsfeatures"))); root.Children.Add(actions);
        AddUninstallHistory(root);
        root.Children.Add(BuildStoreAppsTool());
        if (_installedApps == null) return root;
        root.Children.Add(Text(_installedApps.Count + " registros de instalação", 12, Muted, margin: new Thickness(0, 8, 0, 14)));
        foreach (var app in _installedApps.Where(a => Matches(a.Name + " " + a.Publisher)).Take(200))
        {
            var content = Stack(); content.Children.Add(Text(app.Name, 15, weight: FontWeights.SemiBold)); content.Children.Add(Text(string.Join(" · ", new[] { app.Version, app.Publisher, app.EstimatedBytes.HasValue ? Bytes(app.EstimatedBytes.Value) + " estimados" : "Tamanho não informado" }.Where(x => !string.IsNullOrWhiteSpace(x))), 12, Muted, margin: new Thickness(0, 6, 0, 0)));
            if (app.InstallLocation != null) content.Children.Add(Text(app.InstallLocation, 11, Muted, margin: new Thickness(0, 5, 0, 0)));
            content.Children.Add(FeatureButton("Revisar desinstalação…", () => _ = ReviewUninstallAsync(app.Id))); root.Children.Add(Card(content));
        }
        if (_installedApps.Count > 200) root.Children.Add(Text("A lista exibe até 200 itens por busca. Use o campo superior para filtrar.", 12, Muted)); return root;
    }
    private static string Percent(double? value) => value.HasValue ? value.Value.ToString("0.#") + "%" : "indisponível";
}
