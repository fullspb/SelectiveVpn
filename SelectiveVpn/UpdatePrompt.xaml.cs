using System.Diagnostics;
using System.IO;
using System.Windows;
using SelectiveVpn.Services;

namespace SelectiveVpn;

public partial class UpdatePrompt : Window
{
    private readonly UpdateInfo _update;
    private readonly Func<Task> _prepareExit;
    private readonly Action _requestExit;
    private CancellationTokenSource? _downloadCts;
    private string? _setupPath;
    private bool _busy;

    public UpdatePrompt(UpdateInfo update, Func<Task> prepareExit, Action requestExit)
    {
        _update = update;
        _prepareExit = prepareExit;
        _requestExit = requestExit;
        InitializeComponent();
        HeadingText.Text = Loc.Tf("Update.Heading", update.Version);
        VersionText.Text = Loc.Tf("Update.VersionLine", AppVersion.Current, update.Version);
        StatusText.Text = Loc.T("Update.Ready");
        SourceInitialized += (_, _) => TitleBarTheme.ApplyFromResources(this);
        Loaded += (_, _) => TitleBarTheme.ApplyFromResources(this);
        Closed += (_, _) =>
        {
            try
            {
                _downloadCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            _downloadCts?.Dispose();
        };
    }

    private async void DownloadClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        _busy = true;
        DownloadButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        ProgressBar.Visibility = Visibility.Visible;
        PercentText.Visibility = Visibility.Visible;
        ProgressBar.Value = 0;
        PercentText.Text = "0%";
        StatusText.Text = Loc.T("Update.Downloading");
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource("MutedForeground");

        _downloadCts?.Dispose();
        _downloadCts = new CancellationTokenSource();
        var dest = AppUpdateChecker.LocalSetupPath(_update.Version);
        try
        {
            var progress = new Progress<double>(value =>
            {
                ProgressBar.Value = value;
                PercentText.Text = ((int)Math.Round(value)).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
            });
            await AppUpdateChecker.DownloadAsync(_update.DownloadUrl, dest, progress, _downloadCts.Token)
                .ConfigureAwait(true);
            _setupPath = dest;
            StatusText.Text = Loc.T("Update.Downloaded");
            DownloadButton.Visibility = Visibility.Collapsed;
            InstallButton.Visibility = Visibility.Visible;
            InstallButton.IsDefault = true;
            CancelButton.IsEnabled = true;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = Loc.T("Update.Canceled");
            DownloadButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
        }
        catch (Exception)
        {
            StatusText.Text = Loc.T("Update.DownloadFail");
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("DangerForeground");
            DownloadButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
        }
        finally
        {
            _busy = false;
        }
    }

    private async void InstallClick(object sender, RoutedEventArgs e)
    {
        if (_busy || string.IsNullOrWhiteSpace(_setupPath) || !File.Exists(_setupPath))
            return;

        _busy = true;
        InstallButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        StatusText.Text = Loc.T("Update.StartingInstaller");
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _setupPath,
                UseShellExecute = true
            });
            await _prepareExit().ConfigureAwait(true);
            DialogResult = true;
            _requestExit();
        }
        catch (Exception)
        {
            StatusText.Text = Loc.T("Update.InstallFail");
            StatusText.Foreground = (System.Windows.Media.Brush)FindResource("DangerForeground");
            InstallButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
            _busy = false;
        }
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            try
            {
                _downloadCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            return;
        }

        DialogResult = false;
    }
}
