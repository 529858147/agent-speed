using System.Text.Json;

namespace AgentSpeed.Core.Pi;

/// <summary>
/// The automatic Pi roots (METRICS 2, 4.13): <c>&lt;agent dir&gt;/sessions</c>, then the custom session folder of Pi's own
/// settings (<c>sessionDir</c> in <c>&lt;agent dir&gt;/settings.json</c>) and <c>%PI_CODING_AGENT_SESSION_DIR%</c>.
/// The agent dir is <c>%PI_CODING_AGENT_DIR%</c>, else <c>~/.pi/agent</c>; a leading <c>~</c> means the home folder.
/// </summary>
internal static class PiPaths
{
    /// <summary>Pi's settings are read up to this many bytes; a longer file does not parse and names no folder.</summary>
    public const int MaxSettingsBytes = 1_048_576;

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Roots of this machine, from the process environment and Pi's settings file (read, never written).</summary>
    public static IReadOnlyList<string> AutomaticRoots() => Roots(Environment.GetEnvironmentVariable, UserHome);

    /// <summary>The default root alone, as shown in settings.</summary>
    public static string DefaultRoot() => DefaultRoot(Environment.GetEnvironmentVariable, UserHome);

    public static string DefaultRoot(Func<string, string?> environment, string home) =>
        Path.Combine(AgentDirectory(environment, home), "sessions");

    /// <param name="environment">Reads one environment variable; null or empty means unset.</param>
    public static IReadOnlyList<string> Roots(Func<string, string?> environment, string home)
    {
        var agent = AgentDirectory(environment, home);
        var candidates = new List<string> { Path.Combine(agent, "sessions") };
        if (SessionDirSetting(Path.Combine(agent, "settings.json")) is { } custom)
        {
            candidates.Add(ExpandHome(custom, home));
        }

        if (environment("PI_CODING_AGENT_SESSION_DIR") is { Length: > 0 } sessions)
        {
            candidates.Add(ExpandHome(sessions, home));
        }

        var seen = new HashSet<string>(PathComparer);
        var roots = new List<string>();
        foreach (var candidate in candidates)
        {
            if (Full(candidate) is { } full && seen.Add(full))
            {
                roots.Add(full);
            }
        }

        return roots;
    }

    /// <summary>
    /// The omp roots that are not a Pi root and do not lie inside one (METRICS 4.7): those files are Pi's.
    /// </summary>
    public static IReadOnlyList<string> WithoutPi(IEnumerable<string> ompRoots, IEnumerable<string> piRoots)
    {
        var pis = piRoots.Select(Trim).Where(p => p.Length > 0).ToList();
        return [.. ompRoots.Where(omp => !pis.Any(pi => IsSameOrInside(Trim(omp), pi)))];
    }

    /// <summary>
    /// A sub-agent's session file (METRICS 4.13): <c>&lt;stem&gt;.jsonl</c> for <c>&lt;stem&gt;/&lt;name&gt;.jsonl</c> when it
    /// is a regular file (not a link); null for a main session.
    /// </summary>
    public static string? ParentFileOf(string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(folder))
        {
            return null;
        }

        var candidate = folder + ".jsonl";
        try
        {
            var info = new FileInfo(candidate);
            return info.Exists && (info.Attributes & FileAttributes.ReparsePoint) == 0 ? candidate : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    internal static string ExpandHome(string path, string home) =>
        path == "~" ? home
        : path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal) ? Path.Combine(home, path[2..])
        : path;

    private static string AgentDirectory(Func<string, string?> environment, string home) =>
        environment("PI_CODING_AGENT_DIR") is { Length: > 0 } agent ? ExpandHome(agent, home) : Path.Combine(home, ".pi", "agent");

    /// <summary><c>sessionDir</c> when it is a non-empty string after trimming; null when unset or unreadable.</summary>
    private static string? SessionDirSetting(string settingsPath)
    {
        try
        {
            using var stream = new FileStream(settingsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var buffer = new byte[(int)Math.Clamp(stream.Length, 0, MaxSettingsBytes)];
            var length = 0;
            int read;
            while (length < buffer.Length && (read = stream.Read(buffer, length, buffer.Length - length)) > 0)
            {
                length += read;
            }

            var json = buffer.AsMemory(0, length);
            if (json.Span.StartsWith("﻿"u8))
            {
                json = json[3..];
            }

            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 512 });
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("sessionDir", out var value)
                && value.ValueKind == JsonValueKind.String
                && value.GetString()!.Trim() is { Length: > 0 } custom
                    ? custom
                    : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
            or JsonException)
        {
            return null;
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

    private static bool IsSameOrInside(string path, string root) =>
        path.Equals(root, PathComparison)
        || (path.Length > root.Length && path.StartsWith(root, PathComparison)
            && (path[root.Length] == Path.DirectorySeparatorChar || path[root.Length] == Path.AltDirectorySeparatorChar));

    private static string Trim(string path) => Path.TrimEndingDirectorySeparator(path ?? string.Empty);

    private static string UserHome => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
