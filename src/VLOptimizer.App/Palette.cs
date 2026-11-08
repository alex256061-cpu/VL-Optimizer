using System.Windows.Media;

namespace VLOptimizer.App;

internal static class Palette
{
    public const string Canvas = "#080809";
    public const string Panel = "#141113";
    public const string Raised = "#21171B";
    public const string Accent = "#FF3542";
    public const string AccentBright = "#FF7179";
    public const string AccentDeep = "#971D2B";
    public const string Text = "#F4F1F2";
    public const string Muted = "#B1A6AD";
    public const string Line = "#38282F";
    public const string Circuit = "#332028";
    public static Color ColorOf(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    public static SolidColorBrush BrushOf(string hex) { var brush = new SolidColorBrush(ColorOf(hex)); brush.Freeze(); return brush; }
}
