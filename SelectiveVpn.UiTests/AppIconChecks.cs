using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SelectiveVpn.Services;

namespace SelectiveVpn.UiTests;

internal static class AppIconChecks
{
    public static IReadOnlyList<(string Name, Func<Task> Run)> All { get; } =
    [
        ("tray icon renders the embedded transparent Orbit artwork rather than the process host icon", EmbeddedOrbitTrayAsync)
    ];

    private static Task EmbeddedOrbitTrayAsync()
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/SelectiveVpn;component/Assets/SelectiveVpn.ico"));
        Check.True(resource is not null, "The application assembly must contain its icon independently of the executable host.");
        using var stream = resource!.Stream;
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var sizes = decoder.Frames.Select(frame => frame.PixelWidth).ToHashSet();
        Check.True(new[] { 16, 32, 48, 256 }.All(sizes.Contains), "The compiled icon must include native tray and Explorer resolutions.");
        foreach (var frame in decoder.Frames)
        {
            Check.Equal(frame.PixelWidth, frame.PixelHeight, "Each icon frame must be square.");
            var pixels = Pixels(frame);
            Check.True(pixels[3] == 0 && pixels[(frame.PixelWidth - 1) * 4 + 3] == 0,
                "Round Orbit artwork must retain transparent corners in every icon size.");
        }

        // The generic UI-test process has its own executable icon. A correct tray loader must
        // nevertheless return the resource owned by SelectiveVpn, with no real tray or app startup.
        using var tray = AppIcon.LoadTrayIcon();
        var native = Imaging.CreateBitmapSourceFromHIcon(tray.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        var expected = decoder.Frames.Single(frame => frame.PixelWidth == native.PixelWidth && frame.PixelHeight == native.PixelHeight);
        var actualPixels = Pixels(native);
        var expectedPixels = Pixels(expected);
        var mismatches = 0;
        var cyanPixels = 0;
        for (var index = 0; index < actualPixels.Length; index += 4)
        {
            if (Enumerable.Range(0, 4).Any(channel => Math.Abs(actualPixels[index + channel] - expectedPixels[index + channel]) > 4))
                mismatches++;
            if (actualPixels[index + 3] > 250 && actualPixels[index] > 180 && actualPixels[index + 1] > 160
                && actualPixels[index + 2] is > 70 and < 160)
                cyanPixels++;
        }
        Check.True(mismatches <= native.PixelWidth * native.PixelHeight / 50,
            "Native tray rendering must match the same-size embedded Orbit frame within alpha-rounding tolerance.");
        Check.True(cyanPixels > native.PixelWidth * native.PixelHeight / 4,
            "The native tray handle must show the cyan Orbit mark, not the old shield or a development-host icon.");
        return Task.CompletedTask;
    }

    private static byte[] Pixels(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Pbgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        return pixels;
    }
}
