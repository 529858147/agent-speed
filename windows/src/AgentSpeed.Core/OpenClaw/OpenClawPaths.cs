using System.Text.RegularExpressions;

namespace AgentSpeed.Core.OpenClaw;

/// <summary>
/// OpenClaw's state folders and agent databases (METRICS 2, 4.14). A state folder is <c>%OPENCLAW_STATE_DIR%</c> alone, else
/// <c>&lt;base&gt;/.openclaw</c> and every named-profile folder <c>&lt;base&gt;/.openclaw-&lt;name&gt;</c>, with <c>&lt;base&gt;</c> =
/// <c>%OPENCLAW_HOME%</c>, else the home folder; a leading <c>~</c> means the home folder. Read, never written.
/// </summary>
internal static partial class OpenClawPaths
{
    public const string DatabaseName = "openclaw-agent.sqlite";

    private const string ProfilePrefix = ".openclaw-";

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>The automatic state folders of this machine, from the process environment.</summary>
    public static IReadOnlyList<string> AutomaticStateFolders() => StateFolders(Environment.GetEnvironmentVariable, UserHome);

    /// <summary>The first automatic state folder (<c>~/.openclaw</c> unless overridden), as shown in settings.</summary>
    public static string DefaultStateFolder() => AutomaticStateFolders() is { Count: > 0 } folders ? folders[0] : string.Empty;

    /// <param name="environment">Reads one environment variable; null or blank means unset.</param>
    public static IReadOnlyList<string> StateFolders(Func<string, string?> environment, string home)
    {
        if (environment("OPENCLAW_STATE_DIR")?.Trim() is { Length: > 0 } state)
        {
            return Full(Pi.PiPaths.ExpandHome(state, home)) is { } folder ? [folder] : [];
        }

        var baseFolder = environment("OPENCLAW_HOME")?.Trim() is { Length: > 0 } custom ? Pi.PiPaths.ExpandHome(custom, home) : home;
        var candidates = new List<string> { Path.Combine(baseFolder, ".openclaw") };
        candidates.AddRange(ProfileNames(baseFolder).Select(name => Path.Combine(baseFolder, name)));
        var seen = new HashSet<string>(PathComparer);
        return [.. candidates.Select(Full).OfType<string>().Where(seen.Add)];
    }

    /// <summary>
    /// The agent databases under one root (METRICS 4.14): a database file named <c>openclaw-agent.sqlite</c> itself, an agent
    /// folder that holds one, or a state folder whose <c>agents/&lt;id&gt;/agent/openclaw-agent.sqlite</c> files are regular
    /// files. Agent folders starting with <c>.</c> and links are skipped; ordinal name order.
    /// </summary>
    public static IReadOnlyList<string> Databases(string root)
    {
        if (root.Length == 0)
        {
            return [];
        }

        if (IsRegularFile(root))
        {
            return Path.GetFileName(root) == DatabaseName ? [root] : [];
        }

        var direct = Path.Combine(root, DatabaseName);
        if (IsRegularFile(direct))
        {
            return [direct];
        }

        var found = new List<string>();
        foreach (var folder in AgentFolders(Path.Combine(root, "agents")))
        {
            var database = Path.Combine(folder, "agent", DatabaseName);
            if (IsRegularFile(database))
            {
                found.Add(database);
            }
        }

        return found;
    }

    /// <summary>Every database of several roots, each path once.</summary>
    public static IReadOnlyList<string> Databases(IEnumerable<string> roots)
    {
        var seen = new HashSet<string>(PathComparer);
        return [.. roots.SelectMany(Databases).Where(seen.Add)];
    }

    /// <summary>The agent-scoped Codex home next to every agent database: a Codex root (METRICS 2, 4.14).</summary>
    public static IReadOnlyList<string> CodexRoots(IEnumerable<string> databases)
    {
        var seen = new HashSet<string>(PathComparer);
        return [.. databases
            .Select(database => Path.Combine(Path.GetDirectoryName(database) ?? string.Empty, "codex-home", "sessions"))
            .Where(seen.Add)];
    }

    /// <summary>The root exists: a state folder, an agent folder or the database itself (METRICS 2, detection).</summary>
    public static bool Exists(string root)
    {
        try
        {
            return root.Length > 0 && (Directory.Exists(root) || File.Exists(root));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static IEnumerable<string> ProfileNames(string baseFolder)
    {
        List<string> names;
        try
        {
            names = [.. new DirectoryInfo(baseFolder).EnumerateDirectories(ProfilePrefix + "*").Select(d => d.Name)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            return [];
        }

        names.Sort(StringComparer.Ordinal);
        return names.Where(name => name.StartsWith(ProfilePrefix, StringComparison.Ordinal)
            && ProfileName().IsMatch(name[ProfilePrefix.Length..])
            && !name[ProfilePrefix.Length..].Equals("default", StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> AgentFolders(string agents)
    {
        try
        {
            var folders = new DirectoryInfo(agents).EnumerateDirectories()
                .Where(d => !d.Name.StartsWith('.') && (d.Attributes & FileAttributes.ReparsePoint) == 0)
                .Select(d => d.FullName)
                .ToList();
            folders.Sort(StringComparer.Ordinal);
            return folders;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            return [];
        }
    }

    private static bool IsRegularFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && (info.Attributes & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static string? Full(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) is { Length: > 0 } full ? full : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProfileName();

    private static string UserHome => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
