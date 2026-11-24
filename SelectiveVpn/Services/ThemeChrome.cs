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

    /// <summary>Set on filled buttons when the look draws them raised; text and icon buttons turn it off.</summary>
    public static readonly DependencyProperty IsVolumetricProperty = DependencyProperty.RegisterAttached(
        "IsVolumetric", typeof(bool), typeof(ThemeChrome), new FrameworkPropertyMetadata(false));

    public static bool GetIsVolumetric(DependencyObject element) => (bool)element.GetValue(IsVolumetricProperty);
    public static void SetIsVolumetric(DependencyObject element, bool value) => element.SetValue(IsVolumetricProperty, value);
}
