using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SelectiveVpn.Models;

namespace SelectiveVpn.Controls;

/// <summary>
/// Live graph of the VPN channel: download as a filled curve, upload as a line, scrolling
/// smoothly from right to left over the last minute. The scale follows the busiest moment on
/// screen and eases toward it, so a burst does not make the graph jump.
/// </summary>
public sealed class TrafficGraph : FrameworkElement
{
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples), typeof(IReadOnlyList<TrafficSample>), typeof(TrafficGraph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((TrafficGraph)d).UpdateClock()));

    public static readonly DependencyProperty DownBrushProperty = DependencyProperty.Register(
        nameof(DownBrush), typeof(Brush), typeof(TrafficGraph),
        new FrameworkPropertyMetadata(Brushes.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((TrafficGraph)d).ResetPens()));

    public static readonly DependencyProperty UpBrushProperty = DependencyProperty.Register(
        nameof(UpBrush), typeof(Brush), typeof(TrafficGraph),
        new FrameworkPropertyMetadata(Brushes.MediumPurple, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((TrafficGraph)d).ResetPens()));

    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush), typeof(Brush), typeof(TrafficGraph),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF)), FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((TrafficGraph)d).ResetPens()));

    /// <summary>Smallest top of the scale, so an idle channel draws as a calm line near the bottom.</summary>
    private const double MinimumScale = 16 * 1024;
    private const double WindowSeconds = 60;
    /// <summary>The newest point slides in from the edge instead of appearing on it.</summary>
    private const double LagSeconds = 1.0;

    private readonly DispatcherTimer _clock;
    private double _scale = MinimumScale;
    private Pen? _downPen, _upPen, _gridPen, _dashPen;
    private Brush? _downFill, _upFill, _dotGlow;

    public TrafficGraph()
    {
        SnapsToDevicePixels = false;
        ClipToBounds = true;
        _clock = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
        _clock.Tick += (_, _) => InvalidateVisual();
        IsVisibleChanged += (_, _) => UpdateClock();
        Unloaded += (_, _) => _clock.Stop();
        Loaded += (_, _) => UpdateClock();
    }

    public IReadOnlyList<TrafficSample>? Samples
    {
        get => (IReadOnlyList<TrafficSample>?)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public Brush DownBrush
    {
        get => (Brush)GetValue(DownBrushProperty);
        set => SetValue(DownBrushProperty, value);
    }

    public Brush UpBrush
    {
        get => (Brush)GetValue(UpBrushProperty);
        set => SetValue(UpBrushProperty, value);
    }

    public Brush GridBrush
    {
        get => (Brush)GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    /// <summary>True while the graph redraws itself to scroll; checks read it.</summary>
    internal bool IsAnimating => _clock.IsEnabled;

    /// <summary>Top of the scale in bytes per second, eased toward the busiest visible sample.</summary>
    internal double Scale => _scale;

    private void UpdateClock()
    {
        var run = IsLoaded && IsVisible && Samples is { Count: > 0 };
        if (run && !_clock.IsEnabled)
            _clock.Start();
        else if (!run && _clock.IsEnabled)
            _clock.Stop();
        if (Samples is not { Count: > 0 })
            _scale = MinimumScale;
        InvalidateVisual();
    }

    private void ResetPens()
    {
        _downPen = _upPen = _gridPen = _dashPen = null;
        _downFill = _upFill = _dotGlow = null;
    }

    private void EnsurePens()
    {
        if (_downPen is not null)
            return;

        var down = ColorOf(DownBrush, Colors.DeepSkyBlue);
        var up = ColorOf(UpBrush, Colors.MediumPurple);
        _downPen = Frozen(new Pen(new SolidColorBrush(down), 1.8) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        _upPen = Frozen(new Pen(new SolidColorBrush(up), 1.4) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round });
        _gridPen = Frozen(new Pen(GridBrush, 1));
        _dashPen = Frozen(new Pen(GridBrush, 1) { DashStyle = new DashStyle([3, 4], 0) });
        _downFill = Frozen(new LinearGradientBrush(
            [new GradientStop(WithAlpha(down, 0x70), 0), new GradientStop(WithAlpha(down, 0x00), 1)], new Point(0, 0), new Point(0, 1)));
        _upFill = Frozen(new LinearGradientBrush(
            [new GradientStop(WithAlpha(up, 0x38), 0), new GradientStop(WithAlpha(up, 0x00), 1)], new Point(0, 0), new Point(0, 1)));
        _dotGlow = Frozen(new RadialGradientBrush(
            [new GradientStop(WithAlpha(down, 0x90), 0), new GradientStop(WithAlpha(down, 0x00), 1)]));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 4 || height < 4)
            return;

        EnsurePens();
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
        for (var line = 1; line <= 2; line++)
        {
            var y = Math.Round(height * line / 3.0) + 0.5;
            dc.DrawLine(_gridPen!, new Point(0, y), new Point(width, y));
        }

        var samples = Samples;
        var baseline = height - 1.5;
        if (samples is not { Count: > 0 })
        {
            dc.DrawLine(_dashPen!, new Point(0, baseline), new Point(width, baseline));
            return;
        }

        var now = TrafficSample.Now - LagSeconds;
        double X(double seconds) => width * (1 - (now - seconds) / WindowSeconds);

        var visible = samples.Where(sample => X(sample.Seconds) >= -width * 0.1).ToList();
        if (visible.Count == 0)
            visible.Add(samples[^1]);
        var peak = visible.Max(sample => Math.Max(sample.DownPerSecond, sample.UpPerSecond));
        var target = Math.Max(MinimumScale, peak * 1.2);
        // Grow quickly so a burst stays on screen, shrink slowly so the graph settles calmly.
        _scale += (target - _scale) * (target > _scale ? 0.25 : 0.04);

        var top = 4.0;
        double Y(double value) => baseline - (baseline - top) * Math.Clamp(value / _scale, 0, 1.05);

        var down = visible.Select(sample => new Point(X(sample.Seconds), Y(sample.DownPerSecond))).ToList();
        var up = visible.Select(sample => new Point(X(sample.Seconds), Y(sample.UpPerSecond))).ToList();
        // Before the first sample the channel was quiet: start every curve on the baseline.
        down.Insert(0, new Point(Math.Min(down[0].X, 0) - 2, baseline));
        up.Insert(0, new Point(Math.Min(up[0].X, 0) - 2, baseline));

        dc.DrawGeometry(_upFill, null, Area(up, baseline));
        dc.DrawGeometry(_downFill, null, Area(down, baseline));
        dc.DrawGeometry(null, _upPen, Curve(up));
        dc.DrawGeometry(null, _downPen, Curve(down));

        var head = down[^1];
        dc.DrawEllipse(_dotGlow, null, head, 9, 9);
        dc.DrawEllipse(_downPen!.Brush, null, head, 2.6, 2.6);
    }

    private static Geometry Curve(IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(points[0], false, false);
            AddSmooth(context, points);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Geometry Area(IReadOnlyList<Point> points, double baseline)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(points[0].X, baseline), true, true);
            context.LineTo(points[0], false, false);
            AddSmooth(context, points);
            context.LineTo(new Point(points[^1].X, baseline), false, false);
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>Catmull-Rom through the samples, kept from overshooting above or below its neighbours.</summary>
    private static void AddSmooth(StreamGeometryContext context, IReadOnlyList<Point> points)
    {
        for (var i = 0; i < points.Count - 1; i++)
        {
            var p0 = points[Math.Max(i - 1, 0)];
            var p1 = points[i];
            var p2 = points[i + 1];
            var p3 = points[Math.Min(i + 2, points.Count - 1)];
            var low = Math.Min(p1.Y, p2.Y);
            var high = Math.Max(p1.Y, p2.Y);
            var c1 = new Point(p1.X + (p2.X - p0.X) / 6, Math.Clamp(p1.Y + (p2.Y - p0.Y) / 6, low, high));
            var c2 = new Point(p2.X - (p3.X - p1.X) / 6, Math.Clamp(p2.Y - (p3.Y - p1.Y) / 6, low, high));
            context.BezierTo(c1, c2, p2, true, true);
        }
    }

    private static Color ColorOf(Brush brush, Color fallback) => brush switch
    {
        SolidColorBrush solid => solid.Color,
        GradientBrush { GradientStops.Count: > 0 } gradient => gradient.GradientStops[0].Color,
        _ => fallback
    };

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        if (freezable.CanFreeze)
            freezable.Freeze();
        return freezable;
    }
}
