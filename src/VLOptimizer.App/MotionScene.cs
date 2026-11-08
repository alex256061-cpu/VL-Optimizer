using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace VLOptimizer.App;

/// <summary>One switch controls every ambient clock, including opacity and path motion.</summary>
internal interface IMotionVisual
{
    void SetMotionEnabled(bool enabled);
    bool IsMotionRunning { get; }
    string MotionDiagnostic { get; }
}

/// <summary>
/// Retained WPF clocks: no CompositionTarget.Rendering subscription or per-frame geometry rebuild.
/// Pause preserves the animation's position across reduced-motion, minimize and page navigation.
/// </summary>
internal sealed class SceneClocks
{
    private readonly FrameworkElement _owner;
    private readonly List<(Animatable Target, DependencyProperty Property, AnimationClock Clock, double Phase)> _clocks = [];
    private bool _requested = true;
    private bool _started;
    private bool _attached;
    private bool _presentationActive;
    public bool IsRunning => _presentationActive && _clocks.Count > 0 && _clocks.All(item =>
        !item.Clock.IsPaused && item.Clock.CurrentState == ClockState.Active &&
        item.Clock.CurrentGlobalSpeed is { } speed && Math.Abs(speed) > 0);
    public string DiagnosticSnapshot => string.Join("; ", _clocks.Select(item =>
        $"{item.Property.Name}:time={item.Clock.CurrentTime?.TotalSeconds:F3},paused={item.Clock.IsPaused},state={item.Clock.CurrentState},speed={item.Clock.CurrentGlobalSpeed},value={item.Target.GetValue(item.Property)}"));

    public SceneClocks(FrameworkElement owner)
    {
        _owner = owner;
        owner.Loaded += (_, _) => { _attached = true; Synchronize(); };
        owner.Unloaded += (_, _) => { _attached = false; Synchronize(); };
        owner.IsVisibleChanged += (_, _) => Synchronize();
    }

    public void Add(Animatable target, DependencyProperty property, AnimationTimeline animation, double phase = 0)
    {
        // Ambient clocks keep the reference's 60 Hz cadence on high-refresh desktops.
        // This changes sampling only; loop duration and travel speed stay identical.
        Timeline.SetDesiredFrameRate(animation, 60);
        var clock = (AnimationClock)animation.CreateClock(true);
        target.ApplyAnimationClock(property, clock, HandoffBehavior.SnapshotAndReplace);
        // Clocks are created during construction, before any presentation source is attached.
        clock.Controller!.Pause();
        _clocks.Add((target, property, clock, phase));
    }

    public void SetEnabled(bool enabled)
    {
        _requested = enabled;
        Synchronize();
    }

    private void Synchronize()
    {
        bool running = _requested && _attached && _owner.IsVisible;
        if (running == _presentationActive) return;
        foreach (var (_, _, clock, phase) in _clocks)
        {
            if (running)
            {
                if (!_started)
                {
                    // CreateClock already starts the root timeline. Begin would preserve a pending
                    // interactive Pause and overwrite this initial phase on its next timing tick.
                    clock.Controller!.Resume();
                    clock.Controller.SeekAlignedToLastTick(TimeSpan.FromSeconds(phase), TimeSeekOrigin.BeginTime);
                }
                else clock.Controller!.Resume();
            }
            else clock.Controller!.Pause();
        }
        if (running) _started = true;
        _presentationActive = running;
    }
}

/// <summary>The mark and graduated dial stay fixed; three separate ellipses precess around them.</summary>
internal sealed class AnimatedOrbitVisual : Grid, IMotionVisual
{
    private readonly SceneClocks _clocks;
    public bool IsMotionRunning => _clocks.IsRunning;
    public string MotionDiagnostic => _clocks.DiagnosticSnapshot;
    public void SetMotionEnabled(bool enabled) => _clocks.SetEnabled(enabled);

    public AnimatedOrbitVisual()
    {
        Width = 282;
        Height = 260;
        VerticalAlignment = VerticalAlignment.Top;
        IsHitTestVisible = false;
        ClipToBounds = false;
        _clocks = new SceneClocks(this);

        var scene = new RetainedDrawingElement(282, 260);
        Children.Add(scene);
        const double cx = 141, cy = 119;

        var dial = scene.NewVisual();
        using (var dc = dial.RenderOpen())
        {
            var halo = new RadialGradientBrush();
            halo.GradientStops.Add(new GradientStop(SceneInk.Color(Palette.AccentDeep, 0.20), 0));
            halo.GradientStops.Add(new GradientStop(SceneInk.Color(Palette.AccentDeep, 0.06), 0.54));
            halo.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            halo.Freeze();
            dc.DrawEllipse(halo, null, new Point(cx, cy), 111, 111);
            dc.DrawEllipse(null, SceneInk.Pen(Palette.Line, 0.8, 0.46), new Point(cx, cy), 111, 111);
            for (int i = 0; i < 44; i++)
            {
                double angle = (-90 + i * (360.0 / 44)) * Math.PI / 180;
                double inner = i % 4 == 0 ? 96 : 100;
                bool lit = i < 31;
                var pen = SceneInk.Pen(lit ? Palette.Accent : Palette.Line, i % 4 == 0 ? 2 : 1.6, lit ? 0.9 : 0.8);
                dc.DrawLine(pen, new Point(cx + Math.Cos(angle) * inner, cy + Math.Sin(angle) * inner),
                    new Point(cx + Math.Cos(angle) * 107, cy + Math.Sin(angle) * 107));
            }
        }

        AddOrbit(scene, 0, Palette.Accent, 2.6, 86, 0.26, 0.93, -18, 35, 12, 24, 1.6);
        AddOrbit(scene, 1, Palette.Text, 1.8, 78, 0.35, 0.75, 57, 112, 15, 31, 5.2);
        AddOrbit(scene, 2, Palette.AccentBright, 1.5, 70, 0.38, 0.86, -69, -126, 10, 27, 2.9);

        var logo = new Image
        {
            Width = 70, Height = 70, Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(cx - 35, cy - 35, 0, 0)
        };
        try
        {
            var bitmap = new BitmapImage(new Uri("pack://application:,,,/Assets/logo.png"));
            bitmap.Freeze();
            logo.Source = bitmap;
        }
        catch (System.IO.IOException) { /* The scene remains valid in a resource-free test host. */ }
        AutomationProperties.SetName(logo, "Monograma VL vermelho");
        Children.Add(logo);
    }

    private void AddOrbit(RetainedDrawingElement scene, int index, string color, double thickness,
        double radius, double narrow, double wide, double startAngle, double endAngle,
        double breathingSeconds, double turnSeconds, double phase)
    {
        var visual = scene.NewVisual();
        using (var dc = visual.RenderOpen())
        {
            // Two close rims give the same thin, physical band seen in the reference video.
            dc.DrawEllipse(null, SceneInk.Pen(color, thickness + 4, 0.06), new Point(), radius, radius);
            dc.DrawEllipse(null, SceneInk.Pen(color, thickness, index == 0 ? 0.9 : 0.75), new Point(), radius, radius);
            dc.DrawEllipse(null, SceneInk.Pen(color, 0.7, 0.42), new Point(), radius - 3.2, radius - 3.2);
            var arc = new StreamGeometry();
            using (var context = arc.Open())
            {
                context.BeginFigure(new Point(-radius * 0.88, -radius * 0.47), false, false);
                context.ArcTo(new Point(radius * 0.08, -radius * 0.997), new Size(radius, radius),
                    0, false, SweepDirection.Clockwise, true, false);
            }
            arc.Freeze();
            dc.DrawGeometry(null, SceneInk.Pen(Palette.Text, thickness * 0.66, 0.76), arc);
        }
        var squash = new ScaleTransform(narrow, 1);
        var angle = new RotateTransform(startAngle);
        var transform = new TransformGroup();
        transform.Children.Add(squash);
        transform.Children.Add(angle);
        transform.Children.Add(new TranslateTransform(141, 119));
        visual.Transform = transform;
        _clocks.Add(squash, ScaleTransform.ScaleXProperty,
            new DoubleAnimation(narrow, wide, TimeSpan.FromSeconds(breathingSeconds))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } }, phase);
        _clocks.Add(angle, RotateTransform.AngleProperty,
            new DoubleAnimation(startAngle, endAngle, TimeSpan.FromSeconds(turnSeconds))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } }, phase);
    }
}

/// <summary>Cached motherboard drawing with small light packets travelling along fixed traces.</summary>
internal sealed class AnimatedCircuitVisual : FrameworkElement, IMotionVisual
{
    private readonly VisualCollection _visuals;
    private readonly ContainerVisual _scene = new();
    private readonly SceneClocks _clocks;
    private readonly ScaleTransform _fit = new();
    public bool IsMotionRunning => _clocks.IsRunning;
    public string MotionDiagnostic => _clocks.DiagnosticSnapshot;
    public void SetMotionEnabled(bool enabled) => _clocks.SetEnabled(enabled);

    public AnimatedCircuitVisual()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        _visuals = new VisualCollection(this) { _scene };
        _scene.Transform = _fit;
        _clocks = new SceneClocks(this);
        var board = new DrawingVisual();
        _scene.Children.Add(board);
        Point[][] routes =
        [
            [new(42, 810), new(42, 670), new(160, 552), new(160, 220), new(255, 125), new(255, 0)],
            [new(390, 820), new(390, 540), new(470, 460), new(470, 360), new(585, 245), new(585, 0)],
            [new(620, 820), new(620, 645), new(700, 565), new(700, 345), new(760, 285), new(760, 0)],
            [new(1440, 225), new(1285, 225), new(1195, 315), new(1195, 490), new(1105, 580), new(1105, 820)],
            [new(1440, 640), new(1340, 640), new(1255, 555), new(1255, 395), new(1350, 300), new(1440, 300)],
            [new(790, 820), new(790, 650), new(875, 565), new(875, 485), new(960, 400), new(1060, 400)]
        ];
        using (var dc = board.RenderOpen())
        {
            dc.DrawRectangle(Palette.BrushOf(Palette.Canvas), null, new Rect(0, 0, 1440, 820));
            var gridPen = SceneInk.Pen(Palette.Circuit, 0.65, 0.42);
            var diagonalPen = SceneInk.Pen(Palette.Circuit, 0.65, 0.52);
            for (double x = 0; x <= 1440; x += 54) dc.DrawLine(gridPen, new Point(x, 0), new Point(x, 820));
            for (double y = 0; y <= 820; y += 54) dc.DrawLine(gridPen, new Point(0, y), new Point(1440, y));
            for (double x = -820; x < 1440; x += 32)
                dc.DrawLine(diagonalPen, new Point(x, 820), new Point(x + 820, 0));

            foreach (var points in routes)
            {
                dc.DrawGeometry(null, SceneInk.Pen(Palette.AccentDeep, 1.0, 0.48), Path(points));
                dc.DrawEllipse(Palette.BrushOf(Palette.Canvas), SceneInk.Pen(Palette.AccentDeep, 1, 0.8), points[^1], 3, 3);
            }
            DrawChip(dc, new Point(340, 660), 62);
            DrawChip(dc, new Point(940, 225), 58);
            DrawChip(dc, new Point(1285, 655), 62);
        }

        for (int i = 0; i < routes.Length; i++)
        {
            var pulse = new DrawingVisual();
            _scene.Children.Add(pulse);
            using (var dc = pulse.RenderOpen())
            {
                dc.DrawLine(SceneInk.Pen(Palette.Accent, 2, 0.10), new Point(-27, 0), new Point(-12, 0));
                dc.DrawLine(SceneInk.Pen(Palette.Accent, 2, 0.25), new Point(-17, 0), new Point(-6, 0));
                dc.DrawLine(SceneInk.Pen(Palette.AccentBright, 2, 0.72), new Point(-9, 0), new Point());
                dc.DrawEllipse(SceneInk.Brush(Palette.Accent, 0.08), null, new Point(), 7, 7);
                dc.DrawEllipse(SceneInk.Brush(Palette.Accent, 0.17), null, new Point(), 4.5, 4.5);
                dc.DrawEllipse(SceneInk.Brush(Palette.AccentBright, 0.94), null, new Point(), 2.1, 2.1);
                dc.DrawEllipse(SceneInk.Brush(Palette.Text, 0.88), null, new Point(), 0.8, 0.8);
            }
            var first = routes[i][0];
            var transform = new MatrixTransform(new Matrix(1, 0, 0, 1, first.X, first.Y));
            pulse.Transform = transform;
            _clocks.Add(transform, MatrixTransform.MatrixProperty,
                new MatrixAnimationUsingPath
                {
                    PathGeometry = Path(routes[i]),
                    DoesRotateWithTangent = true,
                    Duration = TimeSpan.FromSeconds(11 + i * 1.7),
                    RepeatBehavior = RepeatBehavior.Forever
                }, 1.1 + i * 1.45);
        }
    }

    private static PathGeometry Path(IReadOnlyList<Point> points)
    {
        var segment = new PolyLineSegment(points.Skip(1), true);
        var figure = new PathFigure(points[0], [segment], false);
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }

    private static void DrawChip(DrawingContext dc, Point center, double size)
    {
        var outline = SceneInk.Pen(Palette.AccentDeep, 1, 0.65);
        double half = size / 2;
        dc.DrawRoundedRectangle(Palette.BrushOf(Palette.Canvas), outline,
            new Rect(center.X - half, center.Y - half, size, size), 3, 3);
        dc.DrawRectangle(null, SceneInk.Pen(Palette.Line, 1, 0.85),
            new Rect(center.X - half + 12, center.Y - half + 12, size - 24, size - 24));
        for (double offset = -half + 8; offset < half - 3; offset += 7)
        {
            dc.DrawLine(outline, new Point(center.X + offset, center.Y - half - 10), new Point(center.X + offset, center.Y - half));
            dc.DrawLine(outline, new Point(center.X + offset, center.Y + half), new Point(center.X + offset, center.Y + half + 10));
            dc.DrawLine(outline, new Point(center.X - half - 10, center.Y + offset), new Point(center.X - half, center.Y + offset));
            dc.DrawLine(outline, new Point(center.X + half, center.Y + offset), new Point(center.X + half + 10, center.Y + offset));
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _fit.ScaleX = finalSize.Width / 1440;
        _fit.ScaleY = finalSize.Height / 820;
        return finalSize;
    }
    protected override int VisualChildrenCount => _visuals?.Count ?? 0;
    protected override Visual GetVisualChild(int index) => _visuals[index];
}

/// <summary>Small cached visual host used by the dial. Only animated transforms change per frame.</summary>
internal sealed class RetainedDrawingElement : FrameworkElement
{
    private readonly VisualCollection _visuals;
    public RetainedDrawingElement(double width, double height)
    {
        Width = width;
        Height = height;
        _visuals = new VisualCollection(this);
        IsHitTestVisible = false;
    }
    public DrawingVisual NewVisual()
    {
        var visual = new DrawingVisual();
        _visuals.Add(visual);
        return visual;
    }
    protected override int VisualChildrenCount => _visuals.Count;
    protected override Visual GetVisualChild(int index) => _visuals[index];
}

internal static class SceneInk
{
    public static Color Color(string hex, double opacity)
    {
        var color = Palette.ColorOf(hex);
        color.A = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
        return color;
    }
    public static SolidColorBrush Brush(string hex, double opacity)
    {
        var brush = new SolidColorBrush(Color(hex, opacity));
        brush.Freeze();
        return brush;
    }
    public static Pen Pen(string hex, double width, double opacity)
    {
        var pen = new Pen(Brush(hex, opacity), width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        return pen;
    }
}

/// <summary>Pointer feedback retargets from the current animated value. Keyboard input stays immediate.</summary>
internal static class MotionFeedback
{
    private static readonly ConditionalWeakTable<UIElement, FeedbackState> States = new();
    public static void Attach(UIElement element, Func<bool> allowed)
    {
        if (States.TryGetValue(element, out _)) return;
        var state = new FeedbackState(element, allowed);
        States.Add(element, state);
    }

    private sealed class FeedbackState
    {
        private readonly UIElement _element;
        private readonly Func<bool> _allowed;
        private readonly ScaleTransform _scale = new(1, 1);
        private bool _pressed;
        private bool _finePointer;
        public FeedbackState(UIElement element, Func<bool> allowed)
        {
            _element = element;
            _allowed = allowed;
            var group = new TransformGroup();
            if (element.RenderTransform is { } original && original != Transform.Identity) group.Children.Add(original);
            group.Children.Add(_scale);
            element.RenderTransform = group;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.MouseEnter += (_, e) => { _finePointer = e.StylusDevice == null; Retarget(); };
            element.MouseLeave += (_, _) => { _finePointer = false; _pressed = false; Retarget(); };
            element.PreviewMouseLeftButtonDown += (_, e) => { _pressed = e.StylusDevice == null; Retarget(); };
            element.PreviewMouseLeftButtonUp += (_, _) => { _pressed = false; Retarget(); };
            element.LostMouseCapture += (_, _) => { _pressed = false; Retarget(); };
            element.IsEnabledChanged += (_, _) => Retarget();
            element.IsVisibleChanged += (_, _) => { if (!element.IsVisible) Reset(); };
        }

        private void Reset()
        {
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _scale.ScaleX = _scale.ScaleY = 1;
        }

        private void Retarget()
        {
            if (!_allowed() || !_element.IsEnabled || !_element.IsVisible || !SystemParameters.ClientAreaAnimation)
            {
                Reset();
                return;
            }
            double target = _pressed ? 0.97 : _element.IsMouseOver && _finePointer ? 1.012 : 1;
            MotionTransitions.Animate(_scale, ScaleTransform.ScaleXProperty, _scale.ScaleX, target, 160);
            MotionTransitions.Animate(_scale, ScaleTransform.ScaleYProperty, _scale.ScaleY, target, 160);
        }
    }
}

internal static class MotionTransitions
{
    private sealed class TransitionState
    {
        public readonly TranslateTransform Offset = new();
        public long EndsAt;
    }
    private static readonly ConditionalWeakTable<UIElement, TransitionState> States = new();
    public static void Show(UIElement element, bool enabled)
    {
        var state = States.GetValue(element, item =>
        {
            var value = new TransitionState();
            var group = new TransformGroup();
            if (item.RenderTransform is { } original && original != Transform.Identity) group.Children.Add(original);
            group.Children.Add(value.Offset);
            item.RenderTransform = group;
            return value;
        });
        var offset = state.Offset;
        if (!enabled || !SystemParameters.ClientAreaAnimation)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            offset.BeginAnimation(TranslateTransform.YProperty, null);
            element.Opacity = 1;
            offset.Y = 0;
            state.EndsAt = 0;
            return;
        }
        bool interrupted = Environment.TickCount64 < state.EndsAt;
        Animate(element, UIElement.OpacityProperty, interrupted ? element.Opacity : 0.72, 1, 200);
        Animate(offset, TranslateTransform.YProperty, interrupted ? offset.Y : 6, 0, 200);
        state.EndsAt = Environment.TickCount64 + 200;
    }

    internal static void Animate(Animatable target, DependencyProperty property, double from, double to, double milliseconds)
        => target.BeginAnimation(property, Transition(from, to, milliseconds), HandoffBehavior.SnapshotAndReplace);

    internal static void Animate(UIElement target, DependencyProperty property, double from, double to, double milliseconds)
        => target.BeginAnimation(property, Transition(from, to, milliseconds), HandoffBehavior.SnapshotAndReplace);

    private static DoubleAnimationUsingKeyFrames Transition(double from, double to, double milliseconds)
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(milliseconds) };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        // Exact cubic-bezier(.23,1,.32,1) from the existing motion skill's ease-out token.
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(milliseconds)),
            new KeySpline(0.23, 1, 0.32, 1)));
        return animation;
    }
}
