using System.Security.Principal;
using AgentSpeed.Interop;

namespace AgentSpeed.App;

/// <summary>
/// One instance per user session. A second launch signals the first (which opens the dashboard) and exits.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private RegisteredWaitHandle? _registration;

    private SingleInstance(Mutex mutex, EventWaitHandle activate)
    {
        _mutex = mutex;
        _activate = activate;
    }

    public static SingleInstance? TryAcquire()
    {
        var suffix = UserSuffix();
        var mutex = new Mutex(initiallyOwned: true, $@"Local\AgentSpeed.Instance.{suffix}", out var created);
        var activate = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\AgentSpeed.Activate.{suffix}");
        if (created)
        {
            return new SingleInstance(mutex, activate);
        }

        try
        {
            User32.AllowSetForegroundWindow(-1);
            activate.Set();
        }
        finally
        {
            activate.Dispose();
            mutex.Dispose();
        }

        return null;
    }

    /// <summary>Invokes <paramref name="callback"/> on a thread-pool thread whenever another launch signals.</summary>
    public void Listen(Action callback)
    {
        _registration ??= ThreadPool.RegisterWaitForSingleObject(
            _activate,
            (_, timedOut) =>
            {
                if (!timedOut)
                {
                    callback();
                }
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _registration = null;
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
        }

        _mutex.Dispose();
        _activate.Dispose();
    }

    private static string UserSuffix()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User?.Value ?? Environment.UserName;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return Environment.UserName;
        }
    }
}
