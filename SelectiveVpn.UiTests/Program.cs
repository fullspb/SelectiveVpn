using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using SelectiveVpn.Services;

namespace SelectiveVpn.UiTests;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // Never create the production App/MainWindow or call Application.Run: they own user settings,
        // single-instance integration, update checks, elevation, and real Mihomo startup.
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/SelectiveVpn;component/Resources/OrbitStyles.xaml")
        });
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/SelectiveVpn;component/Localization/Lang.ru.xaml")
        });
        AppTheme.Apply(AppTheme.Orbit);
        var owner = new Window
        {
            Width = 1, Height = 1, Opacity = 0, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000
        };
        application.MainWindow = owner;
        owner.Show();
        var dispatcher = Dispatcher.CurrentDispatcher;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));

        var completion = RunChecksAsync();
        if (!completion.IsCompleted)
        {
            var frame = new DispatcherFrame();
            _ = completion.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }

        var failures = completion.GetAwaiter().GetResult();
        foreach (Window window in application.Windows.Cast<Window>().ToArray())
            window.Close();
        application.Shutdown();
        return failures == 0 ? 0 : 1;
    }

    private static async Task<int> RunChecksAsync()
    {
        var checks = ViewModelChecks.All.Concat(RoutingModeChecks.All).Concat(ConnectionDurationChecks.All)
            .Concat(PopupChecks.All)
            .Concat(MainWindowLayoutChecks.All).Concat(AppIconChecks.All).Concat(IpCountryChecks.All)
            .Concat(LocalizationChecks.All).ToArray();
        var failed = 0;
        foreach (var (name, check) in checks)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                await check().WaitAsync(TimeSpan.FromSeconds(15));
                Console.WriteLine($"PASS {name} ({watch.ElapsedMilliseconds} ms)");
            }
            catch (Exception exception)
            {
                failed++;
                while (exception is TargetInvocationException { InnerException: { } inner })
                    exception = inner;
                Console.Error.WriteLine($"FAIL {name}: {exception}");
            }
        }

        Console.WriteLine($"RESULT {checks.Length - failed}/{checks.Length} passed; {failed} failed. No real VPN or user settings were used.");
        return failed;
    }
}

internal static class Check
{
    public static void True(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message} Expected: {expected}; actual: {actual}.");
    }

    public static async Task UntilAsync(Func<bool> condition, string message, int milliseconds = 3000)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.ElapsedMilliseconds > milliseconds)
                throw new TimeoutException(message);
            await Task.Delay(5);
        }
    }
}
