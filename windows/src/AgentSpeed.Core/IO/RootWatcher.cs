namespace AgentSpeed.Core.IO;

/// <summary>
/// File system watcher for one transcript root. Callbacks only record paths; all work happens on the monitor thread.
/// </summary>
internal sealed class RootWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;

    private RootWatcher(FileSystemWatcher watcher)
    {
        _watcher = watcher;
    }

    public static RootWatcher? TryCreate(string root, Action<string> onPath, Action onOverflow)
    {
        FileSystemWatcher? watcher = null;
        try
        {
            watcher = new FileSystemWatcher(root, "*.jsonl")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                InternalBufferSize = 64 * 1024,
            };
            watcher.Changed += (_, e) => onPath(e.FullPath);
            watcher.Created += (_, e) => onPath(e.FullPath);
            watcher.Deleted += (_, e) => onPath(e.FullPath);
            watcher.Renamed += (_, e) =>
            {
                onPath(e.OldFullPath);
                onPath(e.FullPath);
            };
            watcher.Error += (_, _) => onOverflow();
            watcher.EnableRaisingEvents = true;
            return new RootWatcher(watcher);
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException
            or PlatformNotSupportedException or InvalidOperationException)
        {
            watcher?.Dispose();
            return null;
        }
    }

    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
    }
}
