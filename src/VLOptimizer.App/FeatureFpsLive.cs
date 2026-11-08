using System.Windows;
using System.Windows.Controls;
using VLOptimizer.Core;

namespace VLOptimizer.App;

public sealed partial class MainWindow
{
    private ToolkitFpsLiveSample? _liveFpsSample;
    private TextBlock? _liveFpsText;
    private bool _capturingFps;
    private bool _fpsOverlayEnabled;
    private int _fpsOverlayCorner;
    private FpsOverlayWindow? _fpsOverlayWindow;

    private void AddFpsOverlayControls(StackPanel root)
    {
        var panel = Stack(); var toggle = new CheckBox { Content = "Mostrar sobreposição durante a captura", IsChecked = _fpsOverlayEnabled, Margin = new Thickness(0, 5, 0, 10) };
        toggle.Checked += (_, _) => { _fpsOverlayEnabled = true; UpdateFpsOverlay(); };
        toggle.Unchecked += (_, _) => { _fpsOverlayEnabled = false; UpdateFpsOverlay(); }; panel.Children.Add(toggle);
        panel.Children.Add(NewChoice("Canto da tela principal", new[] { ("0", "Superior esquerdo"), ("1", "Superior direito"), ("2", "Inferior esquerdo"), ("3", "Inferior direito") }, _fpsOverlayCorner.ToString(), value => { _fpsOverlayCorner = int.Parse(value); _fpsOverlayWindow?.Place(_fpsOverlayCorner); }));
        panel.Children.Add(Text("Janela externa que deixa os cliques passarem. Use jogo em janela ou sem bordas; tela cheia exclusiva pode ocultá-la. Exibe só frames reais da captura própria, com média acumulada. Lows ficam no relatório final.", 12, Muted));
        root.Children.Add(new Expander { Header = "FPS na tela · opcional", Content = panel, Foreground = Muted, Margin = new Thickness(0, 9, 0, 16) });
    }
    private void UpdateFpsOverlay()
    {
        if (_smoke || !_fpsOverlayEnabled || !_capturingFps) { _fpsOverlayWindow?.Close(); _fpsOverlayWindow = null; return; }
        if (_fpsOverlayWindow == null)
        {
            _fpsOverlayWindow = new FpsOverlayWindow(); _fpsOverlayWindow.Place(_fpsOverlayCorner); _fpsOverlayWindow.Show();
            if (!_fpsOverlayWindow.ClickThroughVerified) { _fpsOverlayWindow.Close(); _fpsOverlayWindow = null; _fpsOverlayEnabled = false; Notice("A sobreposição não confirmou o modo que deixa os cliques passarem; ela foi fechada.", true); return; }
        }
        if (_liveFpsSample != null) _fpsOverlayWindow.Update(_liveFpsSample);
    }

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
