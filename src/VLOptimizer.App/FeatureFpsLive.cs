using System.Windows;
using System.Windows.Controls;
using VLOptimizer.Core;

namespace VLOptimizer.App;

public sealed partial class MainWindow
{
    private ToolkitFpsLiveSample? _liveFpsSample;
    private TextBlock? _liveFpsText;
    private bool _capturingFps;

    private void AddLiveFpsPanel(StackPanel root)
    {
        if (!_capturingFps && _liveFpsSample == null) return;
        var panel = Stack(); panel.Children.Add(Text(_capturingFps ? "Captura em andamento" : "Última leitura durante a captura", 17, weight: FontWeights.SemiBold));
        _liveFpsText = Text(_liveFpsSample == null ? "Aguardando frames válidos do processo escolhido…" : DescribeLiveFps(_liveFpsSample), 15, AccentSoft, margin: new Thickness(0, 10, 0, 8)); panel.Children.Add(_liveFpsText);
        panel.Children.Add(Text("Médias acumuladas desde o início. Os lows são calculados no relatório final. Uma leitura vazia significa que ainda não chegaram frames válidos.", 11, Muted)); root.Children.Add(Card(panel, true));
    }
    private static string DescribeLiveFps(ToolkitFpsLiveSample sample)
    {
        var summary = $"{sample.ElapsedSeconds:0.0} s de captura · {sample.RejectedRows} linhas rejeitadas";
        if (sample.Processes.Count == 0) return summary + "\nAguardando frames válidos…";
        return summary + "\n" + string.Join("\n", sample.Processes.Take(4).Select(p => $"{p.Application} · PID {p.ProcessId} · {p.AverageFps:0.0} FPS médios · {p.AverageFrameTimeMs:0.00} ms · {p.Frames:N0} frames"));
    }
}
