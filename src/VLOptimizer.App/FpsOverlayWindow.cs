using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using VLOptimizer.Core;

namespace VLOptimizer.App;

internal sealed class FpsOverlayWindow : Window
{
    private readonly TextBlock content;
    private int frames = -1;
    private DateTimeOffset changedAt = DateTimeOffset.UtcNow;
    public bool ClickThroughVerified { get; private set; }
    public FpsOverlayWindow()
    {
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false;
        ShowActivated = false; Topmost = true; ResizeMode = ResizeMode.NoResize; Width = 330; Height = 138; IsHitTestVisible = false;
        content = new TextBlock { Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI"), FontSize = 14, TextWrapping = TextWrapping.Wrap, Text = "VL · aguardando frames válidos…" };
        Content = new Border { Background = new SolidColorBrush(Color.FromArgb(224, 16, 12, 14)), BorderBrush = new SolidColorBrush(Color.FromRgb(255, 53, 66)), BorderThickness = new Thickness(1), Padding = new Thickness(14), CornerRadius = new CornerRadius(9), Child = content };
        SourceInitialized += (_, _) =>
        {
            IntPtr handle = new WindowInteropHelper(this).Handle; long style = GetWindowLongPtrW(handle, -20).ToInt64();
            SetWindowLongPtrW(handle, -20, new IntPtr(style | 0x20L | 0x80L | 0x08000000L));
            ClickThroughVerified = (GetWindowLongPtrW(handle, -20).ToInt64() & 0x080000A0L) == 0x080000A0L;
        };
    }
    public void Place(int corner)
    {
        Rect work = SystemParameters.WorkArea; Left = corner is 1 or 3 ? work.Right - Width - 16 : work.Left + 16;
        Top = corner is 2 or 3 ? work.Bottom - Height - 16 : work.Top + 16;
    }
    public void Update(ToolkitFpsLiveSample sample)
    {
        var process = sample.Processes.FirstOrDefault();
        if (process == null) { content.Text = $"VL · {sample.ElapsedSeconds:0.0} s\nAguardando frames válidos…\nPresentMon · média acumulada"; return; }
        if (process.Frames != frames) { frames = process.Frames; changedAt = DateTimeOffset.UtcNow; }
        string state = DateTimeOffset.UtcNow - changedAt > TimeSpan.FromSeconds(3) ? "Sem novos frames · " : "";
        content.Text = $"VL · {process.Application}\n{process.AverageFps:0.0} FPS médios · {process.AverageFrameTimeMs:0.00} ms\n{process.Frames:N0} frames · {sample.ElapsedSeconds:0.0} s\n{state}média acumulada da captura";
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetWindowLongPtrW(IntPtr handle, int index);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowLongPtrW(IntPtr handle, int index, IntPtr value);
}
