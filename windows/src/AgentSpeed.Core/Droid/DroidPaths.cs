namespace AgentSpeed.Core.Droid;

/// <summary>
/// Factory Droid's files (METRICS 2, 4.16): the sessions root <c>~/.factory/sessions</c>, transcripts
/// <c>&lt;session id&gt;.jsonl</c> and their settings files (<i>sidecars</i>) <c>&lt;session id&gt;.settings.json</c> in the
/// same folder. Nothing else below <c>~/.factory</c> is opened: its <c>settings.json</c> holds the user's API keys.
/// </summary>
internal static class DroidPaths
{
    public const string SessionsFolder = "sessions";
    public const string TranscriptExtension = ".jsonl";
    public const string SidecarSuffix = ".settings.json";
    public const int MaxSessionIdLength = 128;

    /// <summary>The automatic root below a home folder; no environment variable moves it.</summary>
    public static string DefaultRoot(string home) => Path.Combine(home, ".factory", SessionsFolder);

    /// <summary>
    /// The root a chosen folder stands for: its <c>sessions</c> folder when it has one (the <c>.factory</c> folder; the file
    /// system decides case, so Windows compares it case-insensitively), else the folder itself.
    /// </summary>
    public static string RootOf(string setting)
    {
        var nested = Path.Combine(setting, SessionsFolder);
        try
        {
            var info = new DirectoryInfo(nested);
            return info.Exists && (info.Attributes & FileAttributes.ReparsePoint) == 0 ? nested : setting;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return setting;
        }
    }

    /// <summary>A session id (<c>^[A-Za-z0-9][A-Za-z0-9_-]{0,127}$</c>): no other file name can be formed from it.</summary>
    public static bool IsSessionId(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty || value.Length > MaxSessionIdLength || !char.IsAsciiLetterOrDigit(value[0]))
        {
            return false;
        }

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('_' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A transcript's file name: ends in <c>.jsonl</c> and does not start with <c>.</c>.</summary>
    public static bool IsTranscriptName(ReadOnlySpan<char> fileName) =>
        fileName.EndsWith(TranscriptExtension, StringComparison.Ordinal) && !fileName.StartsWith('.');

    /// <summary>The session id of a transcript: its file name without <c>.jsonl</c>.</summary>
    public static string SessionIdOf(string transcript)
    {
        var name = Path.GetFileName(transcript);
        return name.EndsWith(TranscriptExtension, StringComparison.Ordinal) ? name[..^TranscriptExtension.Length] : name;
    }

    /// <summary><c>&lt;session id&gt;.settings.json</c> next to a transcript.</summary>
    public static string SidecarOf(string transcript) =>
        transcript.EndsWith(TranscriptExtension, StringComparison.Ordinal)
            ? string.Concat(transcript.AsSpan(0, transcript.Length - TranscriptExtension.Length), SidecarSuffix)
            : transcript + SidecarSuffix;

    /// <summary>The transcript whose sidecar this is; null for any other file name.</summary>
    public static string? TranscriptOfSidecar(string sidecar)
    {
        var name = Path.GetFileName(sidecar.AsSpan());
        return name.EndsWith(SidecarSuffix, StringComparison.Ordinal) && name.Length > SidecarSuffix.Length && !name.StartsWith('.')
            ? string.Concat(sidecar.AsSpan(0, sidecar.Length - SidecarSuffix.Length), TranscriptExtension)
            : null;
    }

    /// <summary><c>&lt;folder&gt;/&lt;session id&gt;.jsonl</c> in a transcript's own folder (a Task worker's parent, METRICS 6.7).</summary>
    public static string SiblingTranscript(string transcript, string sessionId) =>
        Path.Combine(Path.GetDirectoryName(transcript) ?? string.Empty, sessionId + TranscriptExtension);

    /// <summary><c>&lt;folder&gt;/&lt;session id&gt;.settings.json</c> in a transcript's own folder (a fork's parent counters).</summary>
    public static string SiblingSidecar(string transcript, string sessionId) =>
        Path.Combine(Path.GetDirectoryName(transcript) ?? string.Empty, sessionId + SidecarSuffix);

    /// <summary>
    /// A session's time for the startup order, the startup window and change detection: the newer of the transcript's and
    /// its sidecar's modification times (METRICS 4.16 Reading). A sidecar that is missing or a link does not count.
    /// </summary>
    public static DateTime Modified(string transcript, DateTime transcriptWriteUtc) =>
        SidecarWriteUtc(transcript) is { } sidecar && sidecar > transcriptWriteUtc ? sidecar : transcriptWriteUtc;

    /// <summary>The sidecar's modification time; null when it is missing, not a regular file or unreadable.</summary>
    public static DateTime? SidecarWriteUtc(string transcript)
    {
        try
        {
            var info = new FileInfo(SidecarOf(transcript));
            return info.Exists && (info.Attributes & FileAttributes.ReparsePoint) == 0 ? info.LastWriteTimeUtc : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
