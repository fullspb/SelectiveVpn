using System.Windows;

namespace SelectiveVpn.Services;

/// <summary>Live theme marker inherited by controls, including separately hosted popups.</summary>
public static class ThemeChrome
{
    public static readonly DependencyProperty IsRetroProperty = DependencyProperty.RegisterAttached(
        "IsRetro", typeof(bool), typeof(ThemeChrome),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetIsRetro(DependencyObject element) => (bool)element.GetValue(IsRetroProperty);
    public static void SetIsRetro(DependencyObject element, bool value) => element.SetValue(IsRetroProperty, value);
}
