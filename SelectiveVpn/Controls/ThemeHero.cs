using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using SelectiveVpn.Services;

namespace SelectiveVpn.Controls;

/// <summary>
/// The animated 3D centrepiece of the connection panel. Each theme draws its own scene with
/// real 3D projection: a wireframe globe with an orbiting satellite (Orbit), rendered with real 3D projection.
/// The scene reacts to the connection state: idle, connecting and connected.
/// </summary>
public sealed class ThemeHero : FrameworkElement
{
    public static readonly DependencyProperty StatusKindProperty = DependencyProperty.Register(
        nameof(StatusKind), typeof(string), typeof(ThemeHero),
        new FrameworkPropertyMetadata("idle", FrameworkPropertyMetadataOptions.AffectsRender,
            (d, _) => ((ThemeHero)d).SnapWhenStatic()));

    private Window? _owner;
    private bool _subscribed;
    private bool _rendering;
    private TimeSpan? _lastFrame;
    private double _time;
    private double _phase;
    private double _energy;
    private double _busy;

    public ThemeHero()
    {
        Focusable = false;
        IsHitTestVisible = false;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += (_, _) => RefreshRendering();
    }

    public string StatusKind
    {
        get => (string)GetValue(StatusKindProperty);
        set => SetValue(StatusKindProperty, value);
    }

    internal bool IsAnimating => _rendering;

    protected override AutomationPeer? OnCreateAutomationPeer() => null;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            AppTheme.ThemeChanged += OnThemeChanged;
            _subscribed = true;
        }
        AttachOwner(Window.GetWindow(this));
        // Start from the current state instead of animating in from idle.
        _energy = IsConnected ? 1 : 0;
        _busy = IsConnecting ? 1 : 0;
        RefreshRendering();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed)
        {
            AppTheme.ThemeChanged -= OnThemeChanged;
            _subscribed = false;
        }
        AttachOwner(null);
        StopRendering();
    }

    private void OnThemeChanged(object? sender, string theme)
    {
        if (Dispatcher.CheckAccess()) InvalidateVisual();
        else Dispatcher.BeginInvoke(InvalidateVisual);
    }

    private void AttachOwner(Window? window)
    {
        if (ReferenceEquals(_owner, window))
            return;
        if (_owner is not null)
        {
            _owner.StateChanged -= OnOwnerChanged;
            _owner.IsVisibleChanged -= OnOwnerVisibleChanged;
        }
        _owner = window;
        if (_owner is null)
            return;
        _owner.StateChanged += OnOwnerChanged;
        _owner.IsVisibleChanged += OnOwnerVisibleChanged;
    }

    private void OnOwnerChanged(object? sender, EventArgs e) => RefreshRendering();
    private void OnOwnerVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => RefreshRendering();

    private void RefreshRendering()
    {
        var shouldRun = IsLoaded && IsVisible && _owner is { IsVisible: true }
            && _owner.WindowState != WindowState.Minimized && SystemParameters.ClientAreaAnimation;
        if (shouldRun && !_rendering)
        {
            _lastFrame = null;
            CompositionTarget.Rendering += OnFrame;
            _rendering = true;
        }
        else if (!shouldRun)
            StopRendering();
    }

    private void StopRendering()
    {
        if (!_rendering)
            return;
        CompositionTarget.Rendering -= OnFrame;
        _rendering = false;
    }

    private bool IsConnected => string.Equals(StatusKind, "connected", StringComparison.OrdinalIgnoreCase);
    private bool IsConnecting => string.Equals(StatusKind, "connecting", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// With system animations off there are no frames to ease the colours, so a status change
    /// has to land on its final look at once instead of staying on the previous one.
    /// </summary>
    private void SnapWhenStatic()
    {
        if (_rendering)
            return;
        _energy = IsConnected ? 1 : 0;
        _busy = IsConnecting ? 1 : 0;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = e is RenderingEventArgs args ? args.RenderingTime : TimeSpan.FromTicks(Environment.TickCount64 * TimeSpan.TicksPerMillisecond);
        if (_lastFrame == now)
            return;
        var dt = _lastFrame is { } last ? (now - last).TotalSeconds : 0;
        _lastFrame = now;
        Advance(Math.Clamp(dt, 0, 0.1));
        InvalidateVisual();
    }

    private void Advance(double dt)
    {
        _time += dt;
        var follow = Math.Min(1, dt * 2.6);
        _energy += ((IsConnected ? 1 : 0) - _energy) * follow;
        _busy += ((IsConnecting ? 1 : 0) - _busy) * follow;

        const double idle = 0.22, active = 0.55, busy = 2.6;
        _phase += (Lerp(idle, active, _energy) + busy * _busy) * dt;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 4 || h < 4)
            return;
        RenderOrbit(dc, w, h);
    }

    // ---------------------------------------------------------------- colours

    private Color MainColor()
    {
        var color = Mix(AppTheme.GetColor("HeroIdle"), AppTheme.GetColor("HeroActive"), _energy);
        return Mix(color, AppTheme.GetColor("HeroBusy"), _busy * 0.9);
    }

    private Color SecondColor()
    {
        var color = Mix(AppTheme.GetColor("HeroIdle"), AppTheme.GetColor("HeroActive2"), _energy);
        return Mix(color, AppTheme.GetColor("HeroBusy"), _busy * 0.9);
    }

    // ---------------------------------------------------------------- Orbit

    private const double Tilt = -0.42;
    private const int CurveSteps = 120;
    private const double Camera = 560;

    private void RenderOrbit(DrawingContext dc, double w, double h)
    {
        var c = new Point(w / 2, h / 2);
        var r = Math.Min(w, h) * 0.34;
        var main = MainColor();
        var second = SecondColor();
        var bright = Mix(main, Colors.White, 0.45);
        var lit = Math.Max(_energy, _busy);
        var rot = _phase;
        // Under perspective the visible cap starts slightly in front of the centre plane and the
        // silhouette is slightly wider than the radius; both keep the grid exactly inside the rim.
        var horizon = r * r / Camera;
        var limb = r * Camera / Math.Sqrt(Camera * Camera - r * r);

        // Ambient glow, atmosphere halo just outside the limb, and pulse rings.
        dc.DrawEllipse(Radial((0, WithAlpha(main, A(46 + 80 * lit))), (0.45, WithAlpha(main, A(16 + 30 * lit))), (1, WithAlpha(main, 0))),
            null, c, r * 1.75, r * 1.75);
        const double halo = 1.22;
        dc.DrawEllipse(Radial((0.78, WithAlpha(main, 0)), (1 / halo, WithAlpha(main, A(70 + 90 * lit))),
            (0.9, WithAlpha(main, A(20 + 30 * lit))), (1, WithAlpha(main, 0))), null, c, limb * halo, limb * halo);
        if (_energy > 0.03)
        {
            for (var k = 0; k < 3; k++)
            {
                var p = (_time / 3.6 + k / 3.0) % 1;
                var rr = r * (1.03 + 0.55 * p);
                var fade = (1 - p) * (1 - p) * Math.Min(1, p * 8);
                dc.DrawEllipse(null, Stroke(Solid(WithAlpha(main, A(150 * fade * _energy))), 1.8 - p), c, rr, rr);
            }
        }

        // Satellite orbit, split into the part behind and in front of the globe.
        var ringBack = new StreamGeometry();
        var ringFront = new StreamGeometry();
        var ringR = r * 1.38;
        using (var back = ringBack.Open())
        using (var front = ringFront.Open())
            TraceCurve(front, back, CurveSteps, s => RingPoint(c, ringR, s * Math.PI * 2));
        ringBack.Freeze();
        ringFront.Freeze();
        var dashes = new DashStyle([3, 3.5], -_time * 1.5);
        dashes.Freeze();
        dc.DrawGeometry(null, Stroke(Solid(WithAlpha(main, A(44 + 40 * lit))), 1.2, dashes), ringBack);

        var satU = _phase * 2.1;
        var (sat, satDepth) = RingPoint(c, ringR, satU);
        DrawSatelliteTrail(dc, c, ringR, satU, main, 0.35 + 0.25 * lit, front: false);
        if (satDepth <= 0)
            DrawGlowDot(dc, sat, main, 4, 0.55);

        // Body of the sphere: a dark glass core lit from the top left.
        dc.DrawEllipse(Solid(WithAlpha(AppTheme.GetColor("WindowBackground"), 150)), null, c, limb, limb);
        var body = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.32, 0.26), Center = new Point(0.4, 0.36), RadiusX = 0.78, RadiusY = 0.78,
            GradientStops =
            [
                new(WithAlpha(Mix(main, Colors.White, 0.3), A(70 + 70 * lit)), 0),
                new(WithAlpha(main, A(30 + 34 * lit)), 0.5),
                new(WithAlpha(second, A(10 + 16 * lit)), 1)
            ]
        };
        body.Freeze();
        dc.DrawEllipse(body, null, c, limb, limb);

        // Globe wireframe. Curves are continuous and cut exactly where they cross the limb.
        var frontLines = new StreamGeometry();
        var backLines = new StreamGeometry();
        using (var front = frontLines.Open())
        using (var back = backLines.Open())
        {
            for (var k = 0; k < 8; k++)
            {
                var lam = k * Math.PI / 8 + rot;
                var (sinLam, cosLam) = Math.SinCos(lam);
                TraceCurve(front, back, CurveSteps, s =>
                {
                    var (sinT, cosT) = Math.SinCos(s * Math.PI * 2);
                    var (point, depth) = Project(c, r * cosT * sinLam, r * sinT, r * cosT * cosLam, Tilt);
                    return (point, depth - horizon);
                });
            }
            foreach (var beta in new[] { -1.05, -0.52, 0.0, 0.52, 1.05 })
            {
                var (sinB, cosB) = Math.SinCos(beta);
                TraceCurve(front, back, CurveSteps, s =>
                {
                    var (sinL, cosL) = Math.SinCos(s * Math.PI * 2 + rot);
                    var (point, depth) = Project(c, r * cosB * sinL, r * sinB, r * cosB * cosL, Tilt);
                    return (point, depth - horizon);
                });
            }
        }
        frontLines.Freeze();
        backLines.Freeze();
        dc.DrawGeometry(null, Stroke(Solid(WithAlpha(main, A(30 + 26 * lit))), 1), backLines);

        // Night side: darkens the far lower right so the sphere reads as a solid.
        var shade = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.3, 0.25), Center = new Point(0.36, 0.32), RadiusX = 0.9, RadiusY = 0.9,
            GradientStops = [new(Color.FromArgb(0, 0, 0, 0), 0.5), new(Color.FromArgb(96, 0, 0, 0), 1)]
        };
        shade.Freeze();
        dc.DrawEllipse(shade, null, c, limb, limb);

        // Front lines fade with distance from the light.
        var light = new Point(c.X - r * 0.3, c.Y - r * 0.35);
        var lineBrush = new RadialGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute, Center = light, GradientOrigin = light, RadiusX = r * 1.5, RadiusY = r * 1.5,
            GradientStops =
            [
                new(WithAlpha(bright, A(215 + 40 * lit)), 0),
                new(WithAlpha(main, A(140 + 90 * lit)), 0.55),
                new(WithAlpha(main, A(60 + 50 * lit)), 1)
            ]
        };
        lineBrush.Freeze();
        dc.DrawGeometry(null, Stroke(lineBrush, 1.3), frontLines);

        // Inner atmosphere, rim light and specular highlight.
        dc.DrawEllipse(Radial((0.7, WithAlpha(main, 0)), (0.93, WithAlpha(main, A(26 + 44 * lit))), (1, WithAlpha(main, A(80 + 90 * lit)))),
            null, c, limb, limb);
        var rim = new LinearGradientBrush(WithAlpha(bright, A(200 + 55 * lit)), WithAlpha(main, A(70 + 60 * lit)), new Point(0.15, 0.1), new Point(0.9, 0.95));
        rim.Freeze();
        dc.DrawEllipse(null, Stroke(rim, 1.5), c, limb, limb);
        dc.DrawEllipse(Radial((0, Color.FromArgb(A(46 + 40 * lit), 255, 255, 255)), (1, Color.FromArgb(0, 255, 255, 255))),
            null, new Point(c.X - r * 0.36, c.Y - r * 0.44), r * 0.46, r * 0.3);

        // Home and server locations plus the tunnel arc between them.
        var home = SpherePoint(-0.65 + rot, 0.42);
        var server = SpherePoint(0.95 + rot, -0.38);
        Point ArcPoint(double s)
        {
            var v = Slerp(home, server, s);
            var lift = r * (1 + 0.42 * Math.Sin(Math.PI * s));
            return Project(c, v.x * lift, v.y * lift, v.z * lift, Tilt).point;
        }
        if (_energy > 0.02 || _busy > 0.02)
        {
            var arc = new StreamGeometry();
            using (var ctx = arc.Open())
            {
                ctx.BeginFigure(ArcPoint(0), false, false);
                for (var i = 1; i <= 64; i++)
                    ctx.LineTo(ArcPoint(i / 64.0), true, true);
            }
            arc.Freeze();
            var arcStrength = Math.Max(_energy, _busy * 0.7);
            dc.DrawGeometry(null, Stroke(Solid(WithAlpha(second, A(34 * arcStrength))), 9), arc);
            dc.DrawGeometry(null, Stroke(Solid(WithAlpha(second, A(70 * arcStrength))), 5), arc);
            dc.DrawGeometry(null, Stroke(Solid(WithAlpha(Mix(second, Colors.White, 0.25), A(235 * arcStrength))), 2), arc);

            // Data packet with a short comet tail.
            var ps = (_time * (0.55 + _busy)) % 1;
            for (var k = 5; k >= 1; k--)
            {
                var ts = ps - k * 0.03;
                if (ts < 0)
                    continue;
                var tail = 1 - k / 6.0;
                dc.DrawEllipse(Solid(Color.FromArgb(A(170 * tail * lit), 255, 255, 255)), null, ArcPoint(ts), 2.2 * tail, 2.2 * tail);
            }
            DrawGlowDot(dc, ArcPoint(ps), Colors.White, 2.6, lit);
        }
        var marker = 0;
        foreach (var v in new[] { home, server })
        {
            var (p, depth) = Project(c, v.x * r, v.y * r, v.z * r, Tilt);
            if (depth > horizon && _energy > 0.05)
            {
                var pulse = (_time / 1.8 + marker * 0.5) % 1;
                var pr = 4 + 11 * pulse;
                dc.DrawEllipse(null, Stroke(Solid(WithAlpha(main, A(170 * (1 - pulse) * _energy))), 1.2), p, pr, pr);
            }
            DrawGlowDot(dc, p, depth > horizon ? main : WithAlpha(main, 120), depth > horizon ? 4.2 : 3, depth > horizon ? 0.6 + 0.4 * lit : 0.25);
            marker++;
        }

        dc.DrawGeometry(null, Stroke(Solid(WithAlpha(main, A(100 + 130 * lit))), 1.6, dashes), ringFront);
        DrawSatelliteTrail(dc, c, ringR, satU, main, 0.6 + 0.4 * lit, front: true);
        if (satDepth > 0)
            DrawGlowDot(dc, sat, main, 5, 0.75 + 0.25 * lit);
    }

    private static void DrawSatelliteTrail(DrawingContext dc, Point c, double ringR, double satU, Color color, double strength, bool front)
    {
        const int segments = 20;
        const double span = 1.15;
        var (prev, prevDepth) = RingPoint(c, ringR, satU - span);
        for (var i = 1; i <= segments; i++)
        {
            var s = i / (double)segments;
            var (p, depth) = RingPoint(c, ringR, satU - span * (1 - s));
            if ((depth + prevDepth) / 2 > 0 == front)
            {
                var pen = new Pen(Solid(WithAlpha(color, A(215 * s * s * strength))), 1 + 2.4 * s);
                pen.Freeze();
                dc.DrawLine(pen, prev, p);
            }
            prev = p;
            prevDepth = depth;
        }
    }

    private static (Point point, double depth) RingPoint(Point c, double radius, double u)
    {
        var x = radius * Math.Cos(u);
        var z = radius * Math.Sin(u);
        // Incline the orbit plane, then roll it a little for a dynamic look.
        const double incline = 1.18;
        var y = -z * Math.Sin(incline);
        z *= Math.Cos(incline);
        const double roll = 0.32;
        var xr = x * Math.Cos(roll) - y * Math.Sin(roll);
        var yr = x * Math.Sin(roll) + y * Math.Cos(roll);
        return Project(c, xr, yr, z, 0);
    }

    private static (double x, double y, double z) SpherePoint(double lam, double beta)
        => (Math.Cos(beta) * Math.Sin(lam), Math.Sin(beta), Math.Cos(beta) * Math.Cos(lam));

    private static (double x, double y, double z) Slerp((double x, double y, double z) a, (double x, double y, double z) b, double t)
    {
        var dot = Math.Clamp(a.x * b.x + a.y * b.y + a.z * b.z, -1, 1);
        var omega = Math.Acos(dot);
        if (omega < 1e-4)
            return a;
        var so = Math.Sin(omega);
        var wa = Math.Sin((1 - t) * omega) / so;
        var wb = Math.Sin(t * omega) / so;
        return (a.x * wa + b.x * wb, a.y * wa + b.y * wb, a.z * wa + b.z * wb);
    }

    /// <summary>
    /// Traces a curve sampled over 0..1 as continuous polylines, sending the near half to
    /// <paramref name="front"/> and the far half to <paramref name="back"/>. A run is cut at the
    /// interpolated point where depth changes sign, so the two halves meet without a step.
    /// </summary>
    private static void TraceCurve(StreamGeometryContext front, StreamGeometryContext back, int steps,
        Func<double, (Point point, double depth)> sample)
    {
        var (prev, prevDepth) = sample(0);
        var ctx = prevDepth > 0 ? front : back;
        ctx.BeginFigure(prev, false, false);
        for (var i = 1; i <= steps; i++)
        {
            var (p, depth) = sample(i / (double)steps);
            var next = depth > 0 ? front : back;
            if (!ReferenceEquals(next, ctx))
            {
                var t = prevDepth / (prevDepth - depth);
                var cross = new Point(prev.X + (p.X - prev.X) * t, prev.Y + (p.Y - prev.Y) * t);
                ctx.LineTo(cross, true, true);
                ctx = next;
                ctx.BeginFigure(cross, false, false);
            }
            ctx.LineTo(p, true, true);
            prev = p;
            prevDepth = depth;
        }
    }

    private static (Point point, double depth) Project(Point c, double x, double y, double z, double tilt)
    {
        var yt = y * Math.Cos(tilt) - z * Math.Sin(tilt);
        var zt = y * Math.Sin(tilt) + z * Math.Cos(tilt);
        var f = Camera / (Camera - zt);
        return (new Point(c.X + x * f, c.Y + yt * f), zt);
    }

    // ---------------------------------------------------------------- helpers

    private static void DrawGlowDot(DrawingContext dc, Point p, Color color, double radius, double strength)
    {
        strength = Math.Clamp(strength, 0, 1);
        dc.DrawEllipse(Radial((0, WithAlpha(color, A(170 * strength))), (0.35, WithAlpha(color, A(60 * strength))), (1, WithAlpha(color, 0))),
            null, p, radius * 3.6, radius * 3.6);
        dc.DrawEllipse(Solid(WithAlpha(Mix(color, Colors.White, 0.45), A(255 * strength))), null, p, radius, radius);
    }

    private static RadialGradientBrush Radial(params (double offset, Color color)[] stops)
    {
        var brush = new RadialGradientBrush();
        foreach (var (offset, color) in stops)
            brush.GradientStops.Add(new GradientStop(color, offset));
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush Solid(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen Stroke(Brush brush, double thickness, DashStyle? dash = null)
    {
        var pen = new Pen(brush, Math.Max(0.1, thickness)) { LineJoin = PenLineJoin.Round };
        if (dash is null)
            pen.StartLineCap = pen.EndLineCap = PenLineCap.Round;
        else
            pen.DashStyle = dash;
        pen.Freeze();
        return pen;
    }

    private static byte A(double value) => (byte)Math.Clamp(value, 0, 255);

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t),
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
