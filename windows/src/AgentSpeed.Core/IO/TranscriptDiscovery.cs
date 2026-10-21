using System.IO.Enumeration;

namespace AgentSpeed.Core.IO;

internal readonly record struct DiscoveredFile(string Path, long Length, DateTime LastWriteUtc);

/// <summary>Which file names a root's transcripts have (METRICS 2).</summary>
internal enum TranscriptNaming
{
    /// <summary><c>*.jsonl</c> (Codex, Claude).</summary>
    Jsonl,

    /// <summary><c>session.v&lt;N&gt;.jsonl.zstd</c> or <c>session.v&lt;N&gt;.jsonl</c> (DeepSeek Harness).</summary>
    DshSession,
}

/// <summary>Recursive transcript enumeration that never follows reparse points or symlinks (METRICS 2).</summary>
internal static class TranscriptDiscovery
{
    /// <summary>Directories are listed down to this many levels below the root, so files sit at most one deeper.</summary>
    public const int MaxDepth = 6;

    public const int MaxFilesPerRoot = 20_000;

    private const int MaxScannedEntries = 200_000;

    public static bool IsTranscriptName(ReadOnlySpan<char> fileName, TranscriptNaming naming = TranscriptNaming.Jsonl) =>
        naming == TranscriptNaming.DshSession
            ? IsDshSessionName(fileName)
            : fileName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase);

    public static bool IsDshSessionName(ReadOnlySpan<char> fileName)
    {
        const string Prefix = "session.v";
        if (!fileName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = fileName[Prefix.Length..];
        var digits = 0;
        while (digits < rest.Length && char.IsAsciiDigit(rest[digits]))
        {
            digits++;
        }

        rest = rest[digits..];
        return digits > 0
            && (rest.Equals(".jsonl", StringComparison.OrdinalIgnoreCase)
                || rest.Equals(".jsonl.zstd", StringComparison.OrdinalIgnoreCase));
    }

    public static List<DiscoveredFile> Enumerate(
        string root, TranscriptNaming naming = TranscriptNaming.Jsonl, CancellationToken cancellationToken = default)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = MaxDepth,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false,
        };

        var files = new List<DiscoveredFile>();
        var scanned = 0;
        try
        {
            var enumerable = new FileSystemEnumerable<DiscoveredFile>(
                root,
                static (ref FileSystemEntry entry) => new DiscoveredFile(
                    entry.ToFullPath(), entry.Length, entry.LastWriteTimeUtc.UtcDateTime),
                options)
            {
                ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                    !entry.IsDirectory
                    && (entry.Attributes & FileAttributes.ReparsePoint) == 0
                    && IsTranscriptName(entry.FileName, naming),
                ShouldRecursePredicate = static (ref FileSystemEntry entry) =>
                    (entry.Attributes & FileAttributes.ReparsePoint) == 0,
            };

            foreach (var file in enumerable)
            {
                cancellationToken.ThrowIfCancellationRequested();
                files.Add(file);
                if (++scanned >= MaxScannedEntries)
                {
                    break;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        if (files.Count > MaxFilesPerRoot)
        {
            files.Sort(static (a, b) => b.LastWriteUtc.CompareTo(a.LastWriteUtc));
            files.RemoveRange(MaxFilesPerRoot, files.Count - MaxFilesPerRoot);
        }

        return files;
    }

    /// <summary>
    /// Validates a path reported by a watcher: inside <paramref name="root"/>, within the depth limit, a transcript
    /// name, and no reparse point on the file or on any directory between root and file.
    /// </summary>
    public static bool IsAcceptablePath(string root, string path, TranscriptNaming naming = TranscriptNaming.Jsonl)
    {
        if (!IsTranscriptName(System.IO.Path.GetFileName(path.AsSpan()), naming))
        {
            return false;
        }

        string relative;
        try
        {
            relative = System.IO.Path.GetRelativePath(root, path);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (System.IO.Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith("../", StringComparison.Ordinal))
        {
            return false;
        }

        if (Parsing.PathText.ComponentCount(relative) > MaxDepth + 1)
        {
            return false;
        }

        try
        {
            var current = System.IO.Path.GetFullPath(path);
            var stop = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(root));
            while (current.Length > stop.Length)
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }

                current = System.IO.Path.GetDirectoryName(current) ?? stop;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }

        return true;
    }
}
