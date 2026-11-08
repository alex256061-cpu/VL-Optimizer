using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace VLOptimizer.App;

public sealed partial class MainWindow
{
    private readonly List<WeakReference<IMotionVisual>> _sceneMotions = [];

    private void RegisterSceneMotion(IMotionVisual scene)
    {
        _sceneMotions.Add(new WeakReference<IMotionVisual>(scene));
        scene.SetMotionEnabled(MotionAllowed);
    }
    private void AttachMotionFeedback(UIElement element) => MotionFeedback.Attach(element, () => MotionAllowed);
    private List<IMotionVisual> SceneMotions()
    {
        _sceneMotions.RemoveAll(item => !item.TryGetTarget(out _));
        return _sceneMotions.Select(item => item.TryGetTarget(out var scene) ? scene : null).OfType<IMotionVisual>().ToList();
    }
    private void UpdateSceneMotion()
    {
        foreach (var scene in SceneMotions()) scene.SetMotionEnabled(MotionAllowed);
    }
    private void OnMotionSystemPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation)) Dispatcher.BeginInvoke(UpdateSpinners);
    }
    private void StopSceneMotion()
    {
        SystemParameters.StaticPropertyChanged -= OnMotionSystemPreferenceChanged;
        foreach (var scene in SceneMotions()) scene.SetMotionEnabled(false);
    }

    private async Task CaptureVisualAsync(FrameworkElement visual, string file)
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        visual.UpdateLayout();
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(visual.ActualWidth)), Math.Max(1, (int)Math.Ceiling(visual.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(file); encoder.Save(output);
    }

    private async Task RunMotionSmokeAsync()
    {
        var index = Array.IndexOf(_args, "--screenshot-dir");
        var directory = index >= 0 && index + 1 < _args.Length ? Path.GetFullPath(_args[index + 1]) : Path.Combine(AppContext.BaseDirectory, "private", "motion-smoke");
        Directory.CreateDirectory(directory);
        try
        {
            // This mode validates the SAME live scenes as normal use. No setting is persisted,
            // no external resource is opened, and Apply/Undo remain blocked by _smoke.
            _shell.IsHitTestVisible = false;
            Width = 1536; Height = 884; WindowState = WindowState.Normal;
            _reduceMotion = false; _economy = false;
            Navigate("power", false);
            await CaptureAsync(Path.Combine(directory, "energy-before-analysis.png"));
            await AnalyzeAsync();
            Navigate("power", false); await CaptureAsync(Path.Combine(directory, "energy-after-analysis.png"));
            Navigate("overview", false); UpdateSceneMotion(); await Task.Delay(400);
            if (!SystemParameters.ClientAreaAnimation) throw new InvalidOperationException("O Windows solicita movimento reduzido. O teste não altera essa preferência.");
            var orbit = SceneMotions().OfType<AnimatedOrbitVisual>().Last(scene => scene.IsVisible);
            var circuit = SceneMotions().OfType<AnimatedCircuitVisual>().Single(scene => scene.IsVisible);
            var orbitPosition = orbit.TranslatePoint(new Point(), _shell);
            var watch = Stopwatch.StartNew(); var timestamps = new List<double>();
            for (int frame = 0; frame < 30; frame++)
            {
                var remaining = frame * 100 - watch.Elapsed.TotalMilliseconds;
                if (remaining > 1) await Task.Delay((int)remaining);
                timestamps.Add(watch.Elapsed.TotalMilliseconds);
                await CaptureAsync(Path.Combine(directory, $"motion-{frame:000}.png"));
                await CaptureVisualAsync(circuit, Path.Combine(directory, $"circuit-{frame:000}.png"));
            }
            int activeSceneCount = SceneMotions().Count(scene => scene.IsMotionRunning);
            bool orbitPixelsMove = PixelsDiffer(Path.Combine(directory, "motion-000.png"), Path.Combine(directory, "motion-029.png"),
                new Int32Rect((int)orbitPosition.X, (int)orbitPosition.Y, (int)orbit.ActualWidth, (int)orbit.ActualHeight));
            bool circuitPixelsMove = PixelsDiffer(Path.Combine(directory, "circuit-000.png"), Path.Combine(directory, "circuit-029.png"));
            foreach (var scene in SceneMotions()) scene.SetMotionEnabled(false);
            await Task.Delay(220);
            await CaptureAsync(Path.Combine(directory, "paused-0.png")); await Task.Delay(550); await CaptureAsync(Path.Combine(directory, "paused-1.png"));
            bool paused = SceneMotions().All(scene => !scene.IsMotionRunning)
                && !PixelsDiffer(Path.Combine(directory, "paused-0.png"), Path.Combine(directory, "paused-1.png"));
            _reduceMotion = true; UpdateSpinners(); await Task.Delay(220);
            await CaptureAsync(Path.Combine(directory, "reduced-0.png")); await Task.Delay(550); await CaptureAsync(Path.Combine(directory, "reduced-1.png"));
            bool reduced = SceneMotions().All(scene => !scene.IsMotionRunning)
                && !PixelsDiffer(Path.Combine(directory, "reduced-0.png"), Path.Combine(directory, "reduced-1.png"));
            _reduceMotion = false; _economy = true; UpdateSpinners(); await Task.Delay(220);
            await CaptureAsync(Path.Combine(directory, "economy-0.png")); await Task.Delay(550); await CaptureAsync(Path.Combine(directory, "economy-1.png"));
            bool economy = SceneMotions().All(scene => !scene.IsMotionRunning)
                && !PixelsDiffer(Path.Combine(directory, "economy-0.png"), Path.Combine(directory, "economy-1.png"));
            _economy = false; UpdateSpinners(); await Task.Delay(150);
            WindowState = WindowState.Minimized; await Task.Delay(250);
            bool minimized = SceneMotions().All(scene => !scene.IsMotionRunning);
            WindowState = WindowState.Normal; UpdateSpinners(); await Task.Delay(250);
            bool resumed = SceneMotions().Any(scene => scene.IsMotionRunning);
            _reduceMotion = true; UpdateSpinners(); await Task.Delay(220);

            ExpandRail(true); await Task.Delay(250); UpdateLayout();
            await CaptureAsync(Path.Combine(directory, "sidebar-expanded.png"));
            bool labelsFit = _navLabels.All(label => label.Opacity >= .99 && label.TranslatePoint(new Point(label.ActualWidth, 0), _sidebar).X <= ExpandedRailWidth - 3);
            bool overlayAllocated = _sidebar.ActualWidth >= ExpandedRailWidth && System.Windows.Controls.Grid.GetColumnSpan(_sidebar) == 2 && Math.Abs(_railClip.Rect.Width - ExpandedRailWidth) < 1;
            ExpandRail(false); await Task.Delay(250); await CaptureAsync(Path.Combine(directory, "sidebar-collapsed.png"));
            bool collapsed = Math.Abs(_railClip.Rect.Width - RailWidth) < 1;
            Width = 683; Height = 384; Navigate("power", false); ExpandRail(true); await Task.Delay(100); await CaptureAsync(Path.Combine(directory, "sidebar-compact-expanded.png"));
            ExpandRail(false); Width = 1536; Height = 884; Navigate("overview", false);
            _reduceMotion = false; UpdateSpinners(); await Task.Delay(250);
            var activeProfile = await MeasureIdleAsync();
            WindowState = WindowState.Minimized; var minimizedProfile = await MeasureIdleAsync(); WindowState = WindowState.Normal;
            bool success = activeSceneCount >= 2 && orbitPixelsMove && circuitPixelsMove && paused && reduced && economy && minimized && resumed && labelsFit && overlayAllocated && collapsed;
            var report = new { Success = success, ReadOnly = true, SystemAnimations = SystemParameters.ClientAreaAnimation, RenderingTier = RenderCapability.Tier >> 16, ActiveSceneCount = activeSceneCount,
                OrbitPixelsMove = orbitPixelsMove, CircuitPixelsMove = circuitPixelsMove,
                PauseStopsAll = paused, ReducedStopsAll = reduced, EconomyStopsAll = economy, MinimizeStopsAll = minimized, RestoreResumes = resumed,
                SidebarLabelsFit = labelsFit, SidebarOverlayAllocated = overlayAllocated, SidebarCollapsed = collapsed,
                OrbitBounds = new { X = orbitPosition.X, Y = orbitPosition.Y, Width = orbit.ActualWidth, Height = orbit.ActualHeight },
                CapturedFrameCount = timestamps.Count, CaptureTimestampsMilliseconds = timestamps,
                ActiveProfile = activeProfile, MinimizedProfile = minimizedProfile,
                Note = "Sequência visual amostrada para verificar movimento e pausas; não mede 60 FPS nem ganho de desempenho do computador." };
            await File.WriteAllTextAsync(Path.Combine(directory, "motion-smoke-result.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Application.Current.Shutdown(success ? 0 : 4);
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "motion-error.txt"), ex.ToString());
            Application.Current.Shutdown(3);
        }
    }

    private static bool PixelsDiffer(string first, string second, Int32Rect? region = null)
    {
        static BitmapSource Read(string path)
        {
            using var stream = File.OpenRead(path);
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource source = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            source.Freeze(); return source;
        }
        var left = Read(first); var right = Read(second);
        if (left.PixelWidth != right.PixelWidth || left.PixelHeight != right.PixelHeight) return true;
        var rectangle = region ?? new Int32Rect(0, 0, left.PixelWidth, left.PixelHeight);
        int stride = rectangle.Width * 4;
        var a = new byte[stride * rectangle.Height]; var b = new byte[a.Length];
        left.CopyPixels(rectangle, a, stride, 0); right.CopyPixels(rectangle, b, stride, 0);
        return !a.AsSpan().SequenceEqual(b);
    }
}
