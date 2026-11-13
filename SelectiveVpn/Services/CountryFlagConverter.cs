using System.Globalization;
using System.Windows.Data;

namespace SelectiveVpn.Services;

public sealed class CountryFlagConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => CountryFlags.GetImage(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
