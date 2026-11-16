using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace SelectiveVpn.Desktop.Controls;

/// <summary>
/// Simplified Orbit wireframe globe for Avalonia (WPF ThemeHero stand-in).
/// Draws lat/lon arcs, glow, dashed orbit, and a satellite; reacts to StatusKind.
/// No full 3D projection, pulse particles, or tunnel packet trail.
/// </summary>
public sealed class OrbitGlobeHero : Control
{
    public static readonly StyledProperty<string> StatusKindProperty =
        AvaloniaProperty.Register<OrbitGlobeHero, string>(nameof(StatusKind), "disconnected");

    private readonly DispatcherTimer _timer;
    private double _phase;
    private double _energy;
    private double _busy;
    private bool _attached;

    public OrbitGlobeHero()
    {
        IsHitTestVisible = false;
        ClipToBounds = false;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) =>
        {
            Advance(0.033);
            InvalidateVisual();
        };
    }

    public string StatusKind
    {
        get => GetValue(StatusKindProperty);
        set => SetValue(StatusKindProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Snap();
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        _attached = false;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StatusKindProperty && !_attached)
            Snap();
    }

    private bool IsConnected => string.Equals(StatusKind, "connected", StringComparison.OrdinalIgnoreCase);
    private bool IsConnecting => string.Equals(StatusKind, "connecting", StringComparison.OrdinalIgnoreCase);

    private void Snap()
    {
        _energy = IsConnected ? 1 : 0;
        _busy = IsConnecting ? 1 : 0;
    }

    private void Advance(double dt)
    {
        var follow = Math.Min(1, dt * 2.6);
        _energy += ((IsConnected ? 1 : 0) - _energy) * follow;
        _busy += ((IsConnecting ? 1 : 0) - _busy) * follow;
        var speed = 0.22 + 0.33 * _energy + 2.2 * _busy;
        _phase += speed * dt;
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w < 8 || h < 8)
            return;

        var c = new Point(w / 2, h / 2);
        var r = Math.Min(w, h) * 0.34;
        var main = MixColor(GetBrushColor("HeroIdle"), GetBrushColor("HeroActive"), _energy);
        main = MixColor(main, GetBrushColor("HeroBusy"), _busy * 0.9);
        var lit = Math.Max(_energy, _busy);
        var alpha = (byte)(40 + 90 * lit);

        // Ambient glow
        context.DrawEllipse(
            new RadialGradientBrush
            {
                GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
                GradientStops =
                [
                    new GradientStop(WithAlpha(main, (byte)(30 + 50 * lit)), 0),
                    new GradientStop(WithAlpha(main, 0), 1)
                ]
            },
            null, c, r * 1.7, r * 1.7);

        // Dashed satellite orbit
        var ringR = r * 1.38;
        var orbitPen = new Pen(new SolidColorBrush(WithAlpha(main, (byte)(50 + 40 * lit))), 1.2)
        {
            DashStyle = new DashStyle([3, 3.5], -_phase * 8)
        };
        context.DrawEllipse(null, orbitPen, c, ringR, ringR * 0.42);

        // Globe body
        var bg = GetBrushColor("WindowBackground");
        context.DrawEllipse(new SolidColorBrush(WithAlpha(bg, 180)), null, c, r, r);
        context.DrawEllipse(
            new RadialGradientBrush
            {
                GradientOrigin = new RelativePoint(0.32, 0.26, RelativeUnit.Relative),
                Center = new RelativePoint(0.4, 0.36, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.78, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.78, RelativeUnit.Relative),
                GradientStops =
                [
                    new GradientStop(WithAlpha(MixColor(main, Colors.White, 0.3), (byte)(50 + 60 * lit)), 0),
                    new GradientStop(WithAlpha(main, (byte)(24 + 30 * lit)), 0.55),
                    new GradientStop(WithAlpha(main, (byte)(8 + 12 * lit)), 1)
                ]
            },
            null, c, r, r);

        // Wireframe meridians / parallels (2D ellipse approximation of tilted globe)
        var wire = new Pen(new SolidColorBrush(WithAlpha(main, (byte)(70 + 80 * lit))), 1.15);
        var back = new Pen(new SolidColorBrush(WithAlpha(main, (byte)(28 + 24 * lit))), 1);
        for (var i = -2; i <= 2; i++)
        {
            var t = i / 2.5;
            var rr = r * Math.Sqrt(Math.Max(0.08, 1 - t * t));
            context.DrawEllipse(null, back, new Point(c.X, c.Y + r * t * 0.55), rr * 0.92, rr * 0.28);
        }

        for (var k = 0; k < 6; k++)
        {
            var lam = k * Math.PI / 6 + _phase * 0.35;
            var squash = 0.22 + 0.78 * Math.Abs(Math.Cos(lam));
            var shift = Math.Sin(lam) * r * 0.55;
            context.DrawEllipse(null, wire,
                new Point(c.X + shift * 0.15, c.Y),
                r * squash * 0.35 + r * 0.12,
                r * 0.96);
        }

        // Rim
        context.DrawEllipse(null,
            new Pen(new SolidColorBrush(WithAlpha(MixColor(main, Colors.White, 0.35), (byte)(140 + 60 * lit))), 1.6),
            c, r, r);

        // Specular
        context.DrawEllipse(
            new RadialGradientBrush
            {
                GradientStops =
                [
                    new GradientStop(Color.FromArgb((byte)(40 + 35 * lit), 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 1)
                ]
            },
            null,
            new Point(c.X - r * 0.36, c.Y - r * 0.44),
            r * 0.42, r * 0.28);

        // Satellite
        var satU = _phase * 2.1;
        var sat = new Point(
            c.X + Math.Cos(satU) * ringR,
            c.Y + Math.Sin(satU) * ringR * 0.42);
        context.DrawEllipse(new SolidColorBrush(WithAlpha(main, (byte)(160 + 60 * lit))), null, sat, 3.2, 3.2);
        context.DrawEllipse(new SolidColorBrush(WithAlpha(main, alpha)), null, sat, 7, 7);
    }

    private static Color GetBrushColor(string key)
    {
        if (Avalonia.Application.Current?.Resources.TryGetResource(key, null, out var value) == true
            && value is ISolidColorBrush brush)
            return brush.Color;
        return key switch
        {
            "HeroActive" => Color.Parse("#7FE3FF"),
            "HeroBusy" => Color.Parse("#FFC46E"),
            "WindowBackground" => Color.Parse("#0B0E1C"),
            _ => Color.Parse("#8A93B5")
        };
    }

    private static Color MixColor(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t),
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
}
