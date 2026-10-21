namespace AgentSpeed.Core;

/// <summary>Reads local agent transcripts on background threads and publishes snapshots to a <see cref="SpeedHub"/>.</summary>
public sealed class LiveMonitor : IDisposable
{
    private readonly SpeedHub _hub;

    public LiveMonitor(SpeedHub hub, MonitorConfiguration? configuration = null)
    {
        _hub = hub;
        Configuration = configuration ?? MonitorConfiguration.Default;
    }

    public MonitorConfiguration Configuration { get; private set; }

    public static string DefaultCodexRoot
    {
        get
        {
            var home = Environment.GetEnvironmentVariable("CODEX_HOME");
            return string.IsNullOrEmpty(home)
                ? Path.Combine(UserHome, ".codex", "sessions")
                : Path.Combine(home, "sessions");
        }
    }

    public static string DefaultClaudeRoot
    {
        get
        {
            var dir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            return string.IsNullOrEmpty(dir)
                ? Path.Combine(UserHome, ".claude", "projects")
                : Path.Combine(dir, "projects");
        }
    }

    private static string UserHome => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public void Start()
    {
    }

    public void Stop()
    {
    }

    public void Update(MonitorConfiguration configuration)
    {
        Configuration = configuration;
    }

    public void Dispose() => Stop();
}
