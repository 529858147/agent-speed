using System.IO.Enumeration;
using AgentSpeed.Core.IO;

namespace AgentSpeed.Core.Pi;

/// <summary>
/// pi session files (METRICS 4.13): main sessions are <c>&lt;bucket&gt;/&lt;name&gt;.jsonl</c> exactly two levels below
/// the root, and sub-agent runs are <c>session.jsonl</c> three or more levels below it
/// (<c>&lt;bucket&gt;/&lt;name&gt;/&lt;uuid&gt;/run-N/session.jsonl</c>). Every file or folder whose name starts with
/// <c>.</c> and every reparse point is skipped.
/// </summary>
internal static class PiDiscovery
{
    /// <summary>Depth of a main session file: the bucket folder and the file itself.</summary>
    public const int MainSessionDepth = 2;

    /// <summary>Depth of the shallowest sub-agent run file.</summary>
    public const int SubagentMinDepth = 3;

    /// <summary>What a sub-agent run file is always named.</summary>
    public const string SubagentFileName = "session.jsonl";

    private const int MaxScannedEntries = 200_000;

    /// <summary>Any <c>*.jsonl</c> without a leading dot; the depth rules refine it (see <see cref="IsSessionFileName"/>).</summary>
    public static bool IsTranscriptName(ReadOnlySpan<char> fileName) =>
        fileName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) && !fileName.StartsWith('.');

    /// <summary>Whether a file this many components below the root (the file itself counted) is a session file.</summary>
    public static bool IsSessionFileName(ReadOnlySpan<char> fileName, int depth) =>
        depth == MainSessionDepth
            ? IsTranscriptName(fileName)
            : depth >= SubagentMinDepth && fileName.SequenceEqual(SubagentFileName);

    /// <summary>Session files under <paramref name="root"/>; when more than <paramref name="limit"/>, the newest ones.</summary>
    public static List<DiscoveredFile> Enumerate(
        string root, int limit = TranscriptDiscovery.MaxFilesPerRoot, CancellationToken cancellationToken = default)
    {
        var scanCap = limit == int.MaxValue ? int.MaxValue : MaxScannedEntries;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = TranscriptDiscovery.MaxDepth,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false,
        };

        var files = new List<DiscoveredFile>();
        try
        {
            var enumerable = new FileSystemEnumerable<DiscoveredFile>(
                root,
                static (ref FileSystemEntry entry) => new DiscoveredFile(
                    entry.ToFullPath(), entry.Length, entry.LastWriteTimeUtc.UtcDateTime),
                options)
            {
                ShouldIncludePredicate = static (ref FileSystemEntry entry) =>
                    !entry.IsDirectory
                    && (entry.Attributes & FileAttributes.ReparsePoint) == 0
                    && IsSessionFileName(entry.FileName, DepthOf(ref entry)),
                ShouldRecursePredicate = static (ref FileSystemEntry entry) =>
                    (entry.Attributes & FileAttributes.ReparsePoint) == 0 && !entry.FileName.StartsWith('.'),
            };

            foreach (var file in enumerable)
            {
                cancellationToken.ThrowIfCancellationRequested();
                files.Add(file);
                if (files.Count >= scanCap)
                {
                    break;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        limit = Math.Max(0, limit);
        if (files.Count > limit)
        {
            files.Sort(static (a, b) => b.LastWriteUtc.CompareTo(a.LastWriteUtc));
            files.RemoveRange(limit, files.Count - limit);
        }

        return files;
    }

    /// <summary>Validates a watcher-reported path with the rules of <see cref="Enumerate"/>.</summary>
    public static bool IsSessionPath(string root, string path)
    {
        // The generic check (inside the root, a `.jsonl` name, the depth limit, no reparse point); not the PISession
        // naming, which would dispatch back here.
        if (!TranscriptDiscovery.IsAcceptablePath(root, path, TranscriptNaming.Jsonl))
        {
            return false;
        }

        var fileName = Path.GetFileName(path.AsSpan());
        var depth = DepthBelow(root, path);
        if (!IsSessionFileName(fileName, depth))
        {
            return false;
        }

        var parts = RelativeParts(root, path);
        return parts is not null && parts.Length >= MainSessionDepth && parts.All(static p => !p.StartsWith('.'));
    }

    /// <summary>Components of <paramref name="path"/> below <paramref name="root"/>; 0 when it is not inside it.</summary>
    public static int DepthBelow(string root, string path)
    {
        var parts = RelativeParts(root, path);
        return parts?.Length ?? 0;
    }

    /// <summary>
    /// The main session a sub-agent run belongs to (METRICS 6.7): a file below
    /// <c>&lt;root&gt;/&lt;bucket&gt;/&lt;name&gt;/</c> attaches to <c>&lt;root&gt;/&lt;bucket&gt;/&lt;name&gt;.jsonl</c>.
    /// Null for main sessions and for paths that are not inside the root.
    /// </summary>
    public static string? ParentPath(string root, string path)
    {
        var parts = RelativeParts(root, path);
        return parts is { Length: >= SubagentMinDepth }
            ? Path.Combine(root, parts[0], parts[1] + ".jsonl")
            : null;
    }

    private static string[]? RelativeParts(string root, string path)
    {
        if (string.IsNullOrEmpty(root))
        {
            return null;
        }

        string relative;
        try
        {
            relative = Path.GetRelativePath(root, path);
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith("..", StringComparison.Ordinal))
        {
            return null;
        }

        var parts = relative.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts : null;
    }

    // Components below the root, the file itself counted: 1 for a file directly in the root.
    private static int DepthOf(ref FileSystemEntry entry)
    {
        var directory = Path.TrimEndingDirectorySeparator(entry.Directory);
        var root = Path.TrimEndingDirectorySeparator(entry.RootDirectory);
        var depth = 1;
        if (directory.Length <= root.Length)
        {
            return depth;
        }

        var relative = directory[root.Length..];
        var start = 0;
        while (start < relative.Length
            && (relative[start] == Path.DirectorySeparatorChar || relative[start] == Path.AltDirectorySeparatorChar))
        {
            start++;
        }

        foreach (var ch in relative[start..])
        {
            if (ch is '/' or '\\')
            {
                depth++;
            }
        }

        return depth + 1;
    }
}
