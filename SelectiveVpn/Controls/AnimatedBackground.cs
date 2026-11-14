using System.ComponentModel;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using SelectiveVpn.Services;
using ShapePath = System.Windows.Shapes.Path;

namespace SelectiveVpn.Controls;

/// <summary>
/// Decorative, retained WPF scene. Only transforms and opacity receive animation clocks;
/// all geometry and gradient brushes stay frozen for the life of the selected theme.
/// </summary>
public sealed class AnimatedBackground : Grid
{
    private readonly Canvas _scene = new() { Width = 1140, Height = 830, IsHitTestVisible = false };
    private Storyboard? _storyboard;
    private Window? _owner;
    private bool _subscribed;
    private bool _clockStarted;
    private bool _clockPaused;
    private bool _rebuilding;

    public AnimatedBackground()
    {
        IsHitTestVisible = false;
        Focusable = false;
        ClipToBounds = true;
        Children.Add(new Viewbox { Stretch = Stretch.Fill, Child = _scene, IsHitTestVisible = false });
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += (_, _) => RefreshAnimationState();
        SetTheme(AppTheme.CurrentTheme);
    }

    internal string ThemeId { get; private set; } = AppTheme.Orbit;
    internal int LayerCount => _scene.Children.Count;
    internal bool IsAnimationRunning { get; private set; }
    internal bool HasActiveClocks => _clockStarted;
    internal bool IsAnimationSupported => SystemParameters.ClientAreaAnimation && (RenderCapability.Tier >> 16) > 0;

    protected override AutomationPeer? OnCreateAutomationPeer() => null;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            AppTheme.ThemeChanged += OnThemeChanged;
            SystemParameters.StaticPropertyChanged += OnSystemPropertyChanged;
            RenderCapability.TierChanged += OnRenderTierChanged;
            _subscribed = true;
        }
        AttachOwner(Window.GetWindow(this));
        if (ThemeId != AppTheme.CurrentTheme)
            SetTheme(AppTheme.CurrentTheme);
        RefreshAnimationState();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => Deactivate();

    private void Deactivate()
    {
        StopClocks();
        AttachOwner(null);
        if (!_subscribed)
            return;
        AppTheme.ThemeChanged -= OnThemeChanged;
        SystemParameters.StaticPropertyChanged -= OnSystemPropertyChanged;
        RenderCapability.TierChanged -= OnRenderTierChanged;
        _subscribed = false;
    }

    private void AttachOwner(Window? window)
    {
        if (ReferenceEquals(_owner, window))
            return;
        if (_owner is not null)
        {
            _owner.StateChanged -= OnOwnerStateChanged;
            _owner.IsVisibleChanged -= OnOwnerVisibleChanged;
            _owner.Closed -= OnOwnerClosed;
        }
        _owner = window;
        if (_owner is null)
            return;
        _owner.StateChanged += OnOwnerStateChanged;
        _owner.IsVisibleChanged += OnOwnerVisibleChanged;
        _owner.Closed += OnOwnerClosed;
    }

    private void OnOwnerStateChanged(object? sender, EventArgs e) => RefreshAnimationState();
    private void OnOwnerVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => RefreshAnimationState();
    private void OnOwnerClosed(object? sender, EventArgs e) => Deactivate();

    private void OnThemeChanged(object? sender, string theme) => Dispatch(() => SetTheme(AppTheme.CurrentTheme));
    private void OnRenderTierChanged(object? sender, EventArgs e) => Dispatch(RefreshAnimationState);
    private void OnSystemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
            Dispatch(RefreshAnimationState);
    }

    private void Dispatch(Action action)
    {
        if (Dispatcher.CheckAccess()) action();
        else if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(action);
    }

    internal void RefreshAnimationState()
    {
        if (_rebuilding || _storyboard is null)
            return;
        var shouldRun = IsLoaded && IsVisible && _owner is { IsVisible: true }
            && _owner.WindowState != WindowState.Minimized && IsAnimationSupported;
        if (shouldRun)
        {
            if (!_clockStarted)
            {
                _storyboard.Begin(this, HandoffBehavior.SnapshotAndReplace, isControllable: true);
                _clockStarted = true;
            }
            else if (_clockPaused)
                _storyboard.Resume(this);
            _clockPaused = false;
        }
        else if (_clockStarted && !_clockPaused)
        {
            _storyboard.Pause(this);
            _clockPaused = true;
        }
        IsAnimationRunning = shouldRun && _clockStarted;
    }

    private void StopClocks()
    {
        if (_clockStarted)
            _storyboard?.Remove(this);
        _clockStarted = false;
        _clockPaused = false;
        IsAnimationRunning = false;
    }

    private void SetTheme(string theme)
    {
        var normalized = AppTheme.Normalize(theme);
        if (ThemeId == normalized && _scene.Children.Count > 0)
            return;
        _rebuilding = true;
        try
        {
            StopClocks();
            _scene.Children.Clear();
            _storyboard = null;
            ThemeId = normalized;
            Visibility = Visibility.Visible;
            _storyboard = new Storyboard();
            Timeline.SetDesiredFrameRate(_storyboard, 30);
            CreateOrbit();
            AddDitherNoise();
            AddReadingScrim(normalized);
        }
        finally { _rebuilding = false; }
        RefreshAnimationState();
    }

    private void CreateOrbit()
    {
        var palette = AppTheme.ColorsFor(ThemeId);
        AddGlow(520, -260, 1000, 900, palette["BackgroundGlow1"], 0.42, 46, -50, 60, -20, 50);
        AddGlow(-380, 280, 1000, 860, palette["BackgroundGlow2"], 0.38, 58, 30, -60, 40, -30);
        AddStars(palette["BackgroundStar"], 64, 0.6);
    }

    private void AddStars(string color, int count, double peak)
    {
        for (var i = 0; i < count; i++)
        {
            var size = i % 4 == 0 ? 3 : i % 3 == 0 ? 1.5 : 2;
            var dot = new Ellipse { Width = size, Height = size, Fill = FrozenSolid(color), Opacity = 0 };
            Canvas.SetLeft(dot, (i * 337 + 93) % 1130);
            Canvas.SetTop(dot, (i * 197 + 41) % 820);
            _scene.Children.Add(dot);
            var seconds = 4 + (i % 7) * 1.3;
            var fade = new DoubleAnimationUsingKeyFrames
            {
                Duration = TimeSpan.FromSeconds(seconds), RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromSeconds(-(i % 5) * 0.9)
            };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.05, KeyTime.FromPercent(0)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(peak, KeyTime.FromPercent(0.5)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.05, KeyTime.FromPercent(1)));
            Storyboard.SetTarget(fade, dot);
            Storyboard.SetTargetProperty(fade, new PropertyPath(OpacityProperty));
            _storyboard!.Children.Add(fade);
        }
    }

    private void AddGlow(double left, double top, double width, double height, string color,
        double opacity, double seconds, double fromX, double toX, double fromY, double toY)
    {
        var fill = new RadialGradientBrush
        {
            GradientStops = new GradientStopCollection
            {
                new(WithAlpha(color, 180), 0), new(WithAlpha(color, 65), 0.38), new(WithAlpha(color, 0), 1)
            }
        };
        fill.Freeze();
        var glow = new Ellipse { Width = width, Height = height, Fill = fill, Opacity = opacity };
        Canvas.SetLeft(glow, left);
        Canvas.SetTop(glow, top);
        _scene.Children.Add(glow);
        AddDrift(glow, seconds, fromX, toX, fromY, toY, -4, 4);
    }

    /// <summary>
    /// A static film of very faint grain. Large dark gradients are quantised to 8 bits per
    /// channel and show visible bands; the grain breaks those bands up.
    /// </summary>
    private void AddDitherNoise()
    {
        const int size = 128;
        var pixels = new byte[size * size * 4];
        var random = new Random(7919);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = (byte)random.Next(0, 9);
            var value = random.Next(2) == 0 ? (byte)0 : alpha; // premultiplied black or white
            pixels[i] = pixels[i + 1] = pixels[i + 2] = value;
            pixels[i + 3] = alpha;
        }
        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, pixels, size * 4);
        bitmap.Freeze();
        var brush = new ImageBrush(bitmap)
        {
            TileMode = TileMode.Tile, Stretch = Stretch.None,
            Viewport = new Rect(0, 0, size, size), ViewportUnits = BrushMappingMode.Absolute
        };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);
        brush.Freeze();
        _scene.Children.Add(new Rectangle { Width = 1140, Height = 830, Fill = brush });
    }

    private void AddReadingScrim(string theme)
    {
        var background = AppTheme.ColorsFor(theme)["WindowBackground"];
        var brush = new RadialGradientBrush
        {
            Center = new Point(0.32, 0.48), GradientOrigin = new Point(0.32, 0.48), RadiusX = 0.58, RadiusY = 0.7,
            GradientStops = new GradientStopCollection
            {
                new(WithAlpha(background, 115), 0), new(WithAlpha(background, 45), 0.5), new(WithAlpha(background, 0), 1)
            }
        };
        brush.Freeze();
        _scene.Children.Add(new Rectangle { Width = 1140, Height = 830, Fill = brush });
    }

    private void AddDrift(UIElement element, double seconds, double fromX, double toX,
        double fromY, double toY, double fromAngle, double toAngle)
    {
        var translate = new TranslateTransform(fromX, fromY);
        var rotate = new RotateTransform(fromAngle, 570, 415);
        element.RenderTransform = new TransformGroup { Children = new TransformCollection { rotate, translate } };
        // Storyboard target references to Freezable transforms are cloned with its timeline.
        // Target the visual and walk its transform instead, keeping clocks on the drawn scene.
        AddAnimation(element, new PropertyPath("(0).(1)[1].(2)", RenderTransformProperty, TransformGroup.ChildrenProperty, TranslateTransform.XProperty), fromX, toX, seconds);
        AddAnimation(element, new PropertyPath("(0).(1)[1].(2)", RenderTransformProperty, TransformGroup.ChildrenProperty, TranslateTransform.YProperty), fromY, toY, seconds);
        AddAnimation(element, new PropertyPath("(0).(1)[0].(2)", RenderTransformProperty, TransformGroup.ChildrenProperty, RotateTransform.AngleProperty), fromAngle, toAngle, seconds);
    }

    private void AddAnimation(UIElement target, PropertyPath property, double from, double to, double seconds, bool reverse = true)
    {
        var animation = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        {
            RepeatBehavior = RepeatBehavior.Forever, AutoReverse = reverse,
            EasingFunction = reverse ? new SineEase { EasingMode = EasingMode.EaseInOut } : null
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        _storyboard!.Children.Add(animation);
    }

    private static Geometry FrozenGeometry(string data)
    {
        var geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }

    private static SolidColorBrush FrozenSolid(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush FrozenGradient(string color, byte alpha)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
            GradientStops = new GradientStopCollection
            {
                new(WithAlpha(color, 0), 0), new(WithAlpha(color, alpha), 0.42),
                new(WithAlpha(color, alpha), 0.64), new(WithAlpha(color, 0), 1)
            }
        };
        brush.Freeze();
        return brush;
    }

    private static Color WithAlpha(string color, byte alpha)
    {
        var value = (Color)ColorConverter.ConvertFromString(color);
        value.A = alpha;
        return value;
    }
}
