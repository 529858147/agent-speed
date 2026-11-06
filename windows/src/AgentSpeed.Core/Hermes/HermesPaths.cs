using System.Text;

namespace AgentSpeed.Core.Hermes;

/// <summary>
/// Hermes Agent's home, log folders and log files (METRICS 2, 4.15). The home is <c>%HERMES_HOME%</c> (a profile's
/// home resolves to the root it belongs to), else macOS <c>~/.hermes</c>, Windows <c>%LOCALAPPDATA%\hermes</c>. Read,
/// never written; <c>state.db</c> and everything else of the home is never opened.
/// </summary>
internal static class HermesPaths
{
    public const string LogsFolderName = "logs";
    public const string LiveLogName = "agent.log";
    public const string ConfigName = "config.yaml";

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>The automatic Hermes home of this computer, from the process environment.</summary>
    public static string AutomaticHome() =>
        Home(Environment.GetEnvironmentVariable, OperatingSystem.IsWindows(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    /// <summary>
    /// The Hermes home (METRICS 4.15): <c>HERMES_HOME</c> trimmed, after a leading <c>~</c> becomes <paramref name="home"/>,
    /// when absolute — the default home itself or a folder inside it means the default home, a value whose parent is named
    /// <c>profiles</c> means that parent's parent — else the platform default. Pure string rules of the given platform.
    /// </summary>
    /// <param name="environment">Reads one environment variable; null means unset.</param>
    public static string Home(Func<string, string?> environment, bool windows, string home)
    {
        string fallback;
        if (windows)
        {
            var local = (environment("LOCALAPPDATA") ?? string.Empty).Trim();
            fallback = Join(windows, local.Length > 0 ? local : Join(windows, Join(windows, home, "AppData"), "Local"), "hermes");
        }
        else
        {
            fallback = Join(windows, home, ".hermes");
        }

        var raw = (environment("HERMES_HOME") ?? string.Empty).Trim();
        if (raw == "~" || raw.StartsWith("~/", StringComparison.Ordinal) || raw.StartsWith("~\\", StringComparison.Ordinal))
        {
            raw = home + raw[1..];
        }

        if (raw.Length == 0 || !IsAbsolute(raw, windows))
        {
            return fallback;
        }

        var value = NormalizePath(raw, windows);
        var folded = windows ? FoldCase(value) : value;
        var basis = NormalizePath(fallback, windows);
        basis = windows ? FoldCase(basis) : basis;
        var separator = windows ? '\\' : '/';
        if (folded == basis || folded.StartsWith(basis.TrimEnd(separator) + separator, StringComparison.Ordinal))
        {
            return fallback;
        }

        var parent = DirectoryName(value, windows);
        return BaseName(parent, windows) == "profiles" ? DirectoryName(parent, windows) : value;
    }

    /// <summary>
    /// The log folders of a home: <c>&lt;home&gt;/logs</c>, then <c>&lt;home&gt;/profiles/&lt;name&gt;/logs</c> for every profile
    /// folder (not a link, not starting with <c>.</c>) in ordinal order; folders that do not exist are left out.
    /// </summary>
    public static IReadOnlyList<string> LogFolders(string home)
    {
        if (home.Length == 0)
        {
            return [];
        }

        var folders = new List<string> { Path.Combine(home, LogsFolderName) };
        foreach (var profile in ProfileFolders(Path.Combine(home, "profiles")))
        {
            folders.Add(Path.Combine(profile, LogsFolderName));
        }

        var seen = new HashSet<string>(PathComparer);
        return [.. folders.Where(f => seen.Add(f) && FolderExists(f))];
    }

    /// <summary>The user setting: a Hermes home (it has a <c>logs</c> folder) or one log folder (any other folder).</summary>
    public static IReadOnlyList<string> SettingFolders(string setting)
    {
        if (setting.Length == 0)
        {
            return [];
        }

        if (FolderExists(Path.Combine(setting, LogsFolderName)))
        {
            return LogFolders(setting);
        }

        return FolderExists(setting) ? [setting] : [];
    }

    /// <summary>The log folders of a root: the setting's folders when chosen, else the home's.</summary>
    public static IReadOnlyList<string> FoldersOf(string root, bool chosen) => chosen ? SettingFolders(root) : LogFolders(root);

    /// <summary>
    /// <c>agent.log</c> and <c>agent.log.&lt;digits&gt;</c> directly in a log folder, regular files only (never links), in
    /// ordinal name order.
    /// </summary>
    public static IReadOnlyList<string> LogFiles(string folder)
    {
        List<string> found;
        try
        {
            found = [.. new DirectoryInfo(folder).EnumerateFiles(LiveLogName + "*")
                .Where(f => IsLogName(f.Name) && (f.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) == 0)
                .Select(f => f.FullName)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            return [];
        }

        found.Sort(StringComparer.Ordinal);
        return found;
    }

    /// <summary><c>agent.log</c> or <c>agent.log.</c> followed by one or more ASCII digits.</summary>
    public static bool IsLogName(ReadOnlySpan<char> name)
    {
        if (!name.StartsWith(LiveLogName, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = name[LiveLogName.Length..];
        if (rest.IsEmpty)
        {
            return true;
        }

        if (rest.Length < 2 || rest[0] != '.')
        {
            return false;
        }

        foreach (var c in rest[1..])
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The profile's <c>config.yaml</c> for a log folder named <c>logs</c> (case-insensitively on Windows): the file in the
    /// folder's parent. Null for a log folder with another name, which has no switch file (METRICS 4.15 Codex runtime).
    /// </summary>
    public static string? ConfigPath(string folder) => ConfigPath(folder, OperatingSystem.IsWindows());

    public static string? ConfigPath(string folder, bool windows)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(folder);
        var name = Path.GetFileName(trimmed);
        var isLogs = windows ? name.Equals(LogsFolderName, StringComparison.OrdinalIgnoreCase) : name == LogsFolderName;
        var parent = isLogs ? Path.GetDirectoryName(trimmed) : null;
        return string.IsNullOrEmpty(parent) ? null : Path.Combine(parent, ConfigName);
    }

    /// <summary>The root exists: the Hermes home (installed, maybe never run) or the chosen folder (METRICS 2, detection).</summary>
    public static bool Exists(string root) => FolderExists(root);

    private static List<string> ProfileFolders(string profiles)
    {
        try
        {
            var folders = new DirectoryInfo(profiles).EnumerateDirectories()
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

    private static bool FolderExists(string path)
    {
        try
        {
            return path.Length > 0 && Directory.Exists(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    // Python's posixpath / ntpath rules (isabs, normpath, normcase, dirname, basename, join), host independent.

    private static bool IsSeparator(char c, bool windows) => c == '/' || (windows && c == '\\');

    private static bool IsAbsolute(string path, bool windows)
    {
        if (!windows)
        {
            return path.StartsWith('/');
        }

        var p = path.Replace('/', '\\');
        return p.StartsWith(@"\\", StringComparison.Ordinal)
            || (p.Length >= 3 && char.IsAsciiLetter(p[0]) && p[1] == ':' && p[2] == '\\');
    }

    private static string FoldCase(string path) => path.Replace('/', '\\').ToLowerInvariant();

    private static (string Prefix, string Tail) SplitRoot(string path, bool windows)
    {
        if (!windows)
        {
            var slashes = 0;
            while (slashes < path.Length && path[slashes] == '/')
            {
                slashes++;
            }

            // POSIX keeps exactly two leading slashes; one or three and more become one.
            var prefix = slashes == 0 ? string.Empty : slashes == 2 ? "//" : "/";
            return (prefix, path[slashes..]);
        }

        var p = path.Replace('/', '\\');
        if (p.StartsWith(@"\\", StringComparison.Ordinal))
        {
            // \\server\share\rest
            var server = p.IndexOf('\\', 2);
            if (server < 0)
            {
                return (p, string.Empty);
            }

            var share = p.IndexOf('\\', server + 1);
            return share < 0 ? (p, string.Empty) : (p[..share], p[share..]);
        }

        var drive = p.Length >= 2 && p[1] == ':' ? p[..2] : string.Empty;
        var rest = p[drive.Length..];
        return rest.StartsWith('\\') ? (drive + "\\", rest.TrimStart('\\')) : (drive, rest);
    }

    private static string NormalizePath(string path, bool windows)
    {
        var separator = windows ? '\\' : '/';
        var (prefix, rest) = SplitRoot(path, windows);
        var rooted = prefix.Length > 0 && IsSeparator(prefix[^1], windows);
        var parts = new List<string>();
        foreach (var part in rest.Split(windows ? ['\\', '/'] : ['/']))
        {
            if (part.Length == 0 || part == ".")
            {
                continue;
            }

            if (part == ".." && (parts.Count > 0 ? parts[^1] != ".." : rooted))
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }

                continue;
            }

            parts.Add(part);
        }

        var joined = string.Join(separator, parts);
        var result = new StringBuilder(prefix).Append(joined).ToString();
        return result.Length == 0 ? "." : result;
    }

    private static string DirectoryName(string path, bool windows)
    {
        var (prefix, rest) = SplitRoot(path, windows);
        var cut = rest.Length;
        while (cut > 0 && !IsSeparator(rest[cut - 1], windows))
        {
            cut--;
        }

        var head = rest[..cut].TrimEnd(windows ? ['\\', '/'] : ['/']);
        return prefix + head;
    }

    private static string BaseName(string path, bool windows)
    {
        var (_, rest) = SplitRoot(path, windows);
        var cut = rest.Length;
        while (cut > 0 && !IsSeparator(rest[cut - 1], windows))
        {
            cut--;
        }

        return rest[cut..];
    }

    private static string Join(bool windows, string left, string right)
    {
        if (left.Length == 0)
        {
            return right;
        }

        return IsSeparator(left[^1], windows) ? left + right : left + (windows ? '\\' : '/') + right;
    }
}
