using System.Security.Cryptography;
using System.Text;

namespace SelectiveVpn.Services;

/// <summary>
/// Один экземпляр приложения: повторный запуск активирует уже открытое окно.
/// </summary>
internal static class AppInstance
{
    private static Mutex? _mutex;

    public static string MutexName => @"Local\SelectiveVpn.Single." + PathHash;

    public static string ActivateEventName => @"Local\SelectiveVpn.Activate." + PathHash;

    private static string PathHash
    {
        get
        {
            var path = (Environment.ProcessPath ?? "").ToLowerInvariant();
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
            return hash[..16];
        }
    }

    public static bool TryOwn(TimeSpan wait)
    {
        var mutex = new Mutex(false, MutexName, out _);
        try
        {
            if (!mutex.WaitOne(wait))
            {
                mutex.Dispose();
                return false;
            }
        }
        catch (AbandonedMutexException)
        {
            // Предыдущий процесс завершился, не отпустив mutex — владение перешло нам.
        }

        _mutex = mutex;
        return true;
    }

    public static void Release()
    {
        var mutex = _mutex;
        _mutex = null;
        if (mutex is null)
            return;

        try
        {
            mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }

        mutex.Dispose();
    }

    public static EventWaitHandle CreateActivateListener()
        => new(false, EventResetMode.AutoReset, ActivateEventName, out _);

    public static void SignalActivate()
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(ActivateEventName);
            signal.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
    }
}
