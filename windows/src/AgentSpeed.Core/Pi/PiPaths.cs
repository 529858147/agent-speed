namespace AgentSpeed.Core.Pi;

/// <summary>
/// The automatic pi roots (METRICS 4.13): <c>$PI_CODING_AGENT_SESSION_DIR</c>, else the <c>sessions</c> folder of the
/// agent directory (<c>$PI_CODING_AGENT_DIR</c>, else <c>~/.pi/agent</c>). pi has no named profiles and no XDG rule.
/// </summary>
internal static class PiPaths
{
    /// <summary>The folder pi's own <c>piConfig.configDir</c> names under the home folder.</summary>
    public const string DefaultConfigFolder = ".pi";

    public const string AgentFolder = "agent";

    /// <summary>The root of this machine, from the process environment.</summary>
    public static string DefaultRoot() => Root(Environment.GetEnvironmentVariable, UserHome);

    /// <param name="environment">Reads one environment variable; null or empty means unset.</param>
    public static string Root(Func<string, string?> environment, string home)
    {
        if (environment("PI_CODING_AGENT_SESSION_DIR") is { Length: > 0 } sessions)
        {
            return sessions;
        }

        var agent = environment("PI_CODING_AGENT_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(home, DefaultConfigFolder, AgentFolder);
        return Path.Combine(agent, "sessions");
    }

    private static string UserHome => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
