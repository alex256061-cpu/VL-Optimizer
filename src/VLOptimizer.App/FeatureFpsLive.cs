using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VLOptimizer.Core;

namespace VLOptimizer.App;

/// <summary>Live FPS surface: a running PresentMon stream polled once per second, showing the same
/// numbers the reference tool shows (average, 1% low, 0.1% low, frametime) plus the optional overlay.
///
/// <para>Every failure path is non-fatal: a missing PresentMon, a refused trace session or a stopped
/// process all become a message in the panel, never an unhandled exception or a frozen window.</para></summary>
public sealed partial class MainWindow
{
    private LiveFpsService? _liveFps;
    private DispatcherTimer? _liveFpsTimer;
    private LiveFpsStatus? _liveFpsStatus;
    private TextBlock? _liveFpsText;
    private TextBlock? _liveFpsBaseText;
    private string _liveFpsMessage = "";
    private bool _fpsOverlayEnabled;
    private int _fpsOverlayCorner;
    private FpsOverlayWindow? _fpsOverlayWindow;
    private GlobalHotkey? _fpsHotkey;

    private LiveFpsService LiveFps => _liveFps ??= new LiveFpsService();

    /// <summary>Registers Ctrl+Alt+B exactly like the reference tool: with no meter running it starts
    /// one; with a meter running it marks the baseline. Failure to register is reported once and never
    /// blocks the rest of the page.</summary>
    private void RegisterFpsHotkey()
    {
        if (_fpsHotkey is not null) return;
        var hotkey = new GlobalHotkey();
        bool ok = hotkey.TryRegister(GlobalHotkey.Control | GlobalHotkey.Alt, GlobalHotkey.KeyB, OnFpsHotkey);
        if (!ok) { hotkey.Dispose(); Notice("Ctrl+Alt+B já está em uso por outro programa; use o botão na página de FPS.", true); return; }
        _fpsHotkey = hotkey;
    }

    private void OnFpsHotkey()
    {
        // Chamado pelo message loop nativo: qualquer trabalho de UI tem de voltar para o dispatcher.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                if (_liveFpsStatus?.Running != true) { _ = ToggleLiveFpsAsync(); return; }
                var (baseline, error) = LiveFps.Statistics.MarkBaseline();
                _liveFpsMessage = error ?? "Base marcada em " + baseline!.Fps.ToString("0.0") + " FPS. Ligue o ajuste e compare.";
                PollLiveFps();
                RenderPage();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { Notice("O atalho falhou: " + ex.Message, true); }
        }));
    }

    private void AddFpsOverlayControls(StackPanel root)
    {
        var panel = Stack();
        var toggle = new CheckBox { Content = "Mostrar sobreposição durante a medição", IsChecked = _fpsOverlayEnabled, Margin = new Thickness(0, 5, 0, 10) };
        toggle.Checked += (_, _) => { _fpsOverlayEnabled = true; UpdateFpsOverlay(); };
        toggle.Unchecked += (_, _) => { _fpsOverlayEnabled = false; UpdateFpsOverlay(); };
        panel.Children.Add(toggle);
        panel.Children.Add(NewChoice("Canto da tela principal", [("0", "Superior esquerdo"), ("1", "Superior direito"), ("2", "Inferior esquerdo"), ("3", "Inferior direito")], _fpsOverlayCorner.ToString(), value =>
        {
            _fpsOverlayCorner = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
            _fpsOverlayWindow?.Place(_fpsOverlayCorner);
        }));
        panel.Children.Add(Text("Janela externa que deixa os cliques passarem. Use jogo em janela ou sem bordas; tela cheia exclusiva pode ocultá-la.", 12, Muted));
        root.Children.Add(new Expander { Header = "FPS na tela · opcional", Content = panel, Foreground = Muted, Margin = new Thickness(0, 9, 0, 16) });
    }

    /// <summary>Starts polling when a measurement begins, so the page repaints without a manual refresh.</summary>
    private void StartLiveFpsPolling()
    {
        StopLiveFpsPolling();
        _liveFpsTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _liveFpsTimer.Tick += (_, _) => PollLiveFps();
        _liveFpsTimer.Start();
        PollLiveFps();
    }

    private void StopLiveFpsPolling()
    {
        _liveFpsTimer?.Stop();
        _liveFpsTimer = null;
    }

    /// <summary>Reads one snapshot and updates the panel in place. Rethrows nothing: a poll that fails
    /// stops the timer and leaves a readable message instead of killing the dispatcher.</summary>
    private void PollLiveFps()
    {
        try
        {
            if (_liveFps is null) return;
            _liveFpsStatus = _liveFps.Snapshot();
            if (_liveFpsText is not null)
            {
                _liveFpsText.Text = _liveFpsStatus.Target is { } target
                    ? DescribeLiveTarget(target, _liveFpsStatus.Notice)
                    : _liveFpsStatus.Running
                        ? "Aguardando quadros válidos do processo alvo…"
                        : _liveFpsStatus.Error ?? _liveFpsMessage;
            }
            if (_liveFpsBaseText is not null)
            {
                _liveFpsBaseText.Text = DescribeBaseline(_liveFpsStatus.Target, _liveFps.Statistics.CurrentBaseline);
            }
            if (!_liveFpsStatus.Running) StopLiveFpsPolling();
            UpdateFpsOverlay();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or System.ComponentModel.Win32Exception)
        {
            StopLiveFpsPolling();
            if (_liveFpsText is not null) _liveFpsText.Text = "A leitura parou: " + ex.Message;
        }
    }

    private static string DescribeLiveTarget(LiveFrameStats stats, string? notice)
    {
        var text = $"{stats.Application} · PID {stats.ProcessId}\n"
                 + $"{stats.Fps:0.0} FPS · {stats.FrameTimeMs:0.00} ms\n"
                 + $"1% low {stats.Low1:0.0} · 0.1% low {stats.Low01:0.0} · {stats.Frames:N0} quadros";
        if (stats.IndependentFlip) text += "\nApresentação direta (Independent Flip)";
        if (!string.IsNullOrEmpty(notice)) text += "\n" + notice;
        return text;
    }

    /// <summary>Live value next to a marked baseline, with the gain the user actually cares about.
    /// A negative or tiny difference is reported as a difference, never rounded up into a win.</summary>
    private static string DescribeBaseline(LiveFrameStats? target, LiveFrameStatistics.Baseline? baseline)
    {
        if (baseline is null) return "Base: ainda não marcada. Pressione Ctrl+Alt+B durante o jogo.";
        if (target is null) return "Base: " + baseline.Fps.ToString("0.0") + " FPS · " + baseline.Application;
        if (!string.Equals(target.Application, baseline.Application, StringComparison.OrdinalIgnoreCase))
            return "Base é de " + baseline.Application + "; o alvo atual é " + target.Application + ".";
        double gain = baseline.Fps > 0 ? (target.Fps - baseline.Fps) / baseline.Fps * 100 : 0;
        string verdict = Math.Abs(gain) < 1 ? "praticamente igual" : gain > 0 ? "+" + gain.ToString("0.0") + "%" : gain.ToString("0.0") + "%";
        return $"Base {baseline.Fps:0.0} FPS → agora {target.Fps:0.0} FPS · {verdict}";
    }

    private void UpdateFpsOverlay()
    {
        var running = _liveFpsStatus?.Running == true;
        if (_smoke || !_fpsOverlayEnabled || !running) { _fpsOverlayWindow?.Close(); _fpsOverlayWindow = null; return; }
        try
        {
            if (_fpsOverlayWindow == null)
            {
                _fpsOverlayWindow = new FpsOverlayWindow();
                _fpsOverlayWindow.Place(_fpsOverlayCorner);
                _fpsOverlayWindow.Show();
                if (!_fpsOverlayWindow.ClickThroughVerified)
                {
                    _fpsOverlayWindow.Close(); _fpsOverlayWindow = null; _fpsOverlayEnabled = false;
                    Notice("A sobreposição não confirmou o modo que deixa os cliques passarem; ela foi fechada.", true);
                    return;
                }
            }
            if (_liveFpsStatus?.Target is { } target)
            {
                _fpsOverlayWindow.Update(new ToolkitFpsLiveSample(DateTimeOffset.UtcNow, 0,
                    [new ToolkitFpsLiveProcess(target.ProcessId, target.Application, target.Fps, target.FrameTimeMs, target.Frames, 0)],
                    0, []));
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _fpsOverlayWindow?.Close(); _fpsOverlayWindow = null; _fpsOverlayEnabled = false;
        }
    }

    private void AddLiveFpsPanel(StackPanel root)
    {
        if (_liveFpsStatus is null && !_liveFpsMessage.Contains("andamento", StringComparison.Ordinal)) return;
        var panel = Stack();
        panel.Children.Add(Text("Medição ao vivo", 17, weight: FontWeights.SemiBold));
        _liveFpsText = Text(_liveFpsStatus?.Target is { } target ? DescribeLiveTarget(target, _liveFpsStatus.Notice) : _liveFpsMessage, 15, AccentSoft, margin: new Thickness(0, 10, 0, 8));
        panel.Children.Add(_liveFpsText);
        _liveFpsBaseText = Text(DescribeBaseline(_liveFpsStatus?.Target, _liveFps?.Statistics.CurrentBaseline), 13, Muted, margin: new Thickness(0, 6, 0, 6));
        panel.Children.Add(_liveFpsBaseText);
        panel.Children.Add(Text("Janela de 10 segundos, igual à referência. 1% e 0,1% low são os quadros mais lentos da janela. Ctrl+Alt+B inicia a medição ou marca a base.", 11, Muted));
        root.Children.Add(Card(panel, true));
    }

    /// <summary>Stops the meter and the poll. Called when the page is left or the window closes, so no
    /// timer or child process outlives the view.</summary>
    private async Task StopLiveFpsAsync(string? message = null)
    {
        StopLiveFpsPolling();
        try { if (_liveFps is not null) await _liveFps.StopAsync(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
        _liveFpsStatus = null;
        _liveFpsMessage = message ?? "";
        _fpsOverlayWindow?.Close();
        _fpsOverlayWindow = null;
    }

    /// <summary>Releases the global hotkey. Must run on exit: a registered hotkey survives the window
    /// and would keep the combination held until the process ends.</summary>
    private void ReleaseFpsHotkey()
    {
        _fpsHotkey?.Dispose();
        _fpsHotkey = null;
    }
}
