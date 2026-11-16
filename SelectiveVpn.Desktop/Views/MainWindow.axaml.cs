using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace SelectiveVpn.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpenedForShot;
    }

    private void OnOpenedForShot(object? sender, EventArgs e)
    {
        var path = Environment.GetEnvironmentVariable("SELECTIVE_VPN_UI_SHOT");
        if (string.IsNullOrWhiteSpace(path))
            return;

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await Task.Delay(700).ConfigureAwait(true);
                var size = PixelSize.FromSize(Bounds.Size, 1);
                if (size.Width < 8 || size.Height < 8)
                    return;

                using var bitmap = new RenderTargetBitmap(size);
                bitmap.Render(this);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                bitmap.Save(path);
            }
            catch (Exception ex)
            {
                await File.WriteAllTextAsync(path + ".error.txt", ex.ToString()).ConfigureAwait(true);
            }
        }, DispatcherPriority.Background);
    }
}
