using System.IO.Enumeration;
using AgentSpeed.Core.IO;

namespace AgentSpeed.Core.Droid;

/// <summary>
/// Factory Droid sessions (METRICS 4.16): regular <c>*.jsonl</c> files directly in the root (older droid versions) or in a
/// folder directly in it (one per working directory); names starting with <c>.</c>, reparse points and anything deeper
/// are skipped. A session's time is the newer of its transcript's and its sidecar's (startup order and window).
/// </summary>
internal static class DroidDiscovery
{
    private const int MaxScannedEntries = 200_000;

    private static readonly StringComparer NameComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Sessions under <paramref name="root"/>; when more than <paramref name="limit"/>, the newest ones.</summary>
    public static List<DiscoveredFile> Enumerate(
        string root, int limit = TranscriptDiscovery.MaxFilesPerRoot, CancellationToken cancellationToken = default)
    {
        var scanCap = limit == int.MaxValue ? int.MaxValue : MaxScannedEntries;
        var files = new List<DiscoveredFile>();
        var scanned = 0;
        var folders = List(root, files, ref scanned, scanCap, cancellationToken);
        foreach (var folder in folders)
        {
            if (scanned >= scanCap)
            {
                break;
            }

            List(folder, files, ref scanned, scanCap, cancellationToken);
        }

        limit = Math.Max(0, limit);
        if (files.Count > limit)
        {
            files.Sort(static (a, b) => b.LastWriteUtc.CompareTo(a.LastWriteUtc));
            files.RemoveRange(limit, files.Count - limit);
        }

        return files;
    }

    /// <summary>Validates a watcher-reported transcript path with the rules of <see cref="Enumerate"/>.</summary>
    public static bool IsSessionPath(string root, string path) =>
        DroidPaths.IsTranscriptName(Path.GetFileName(path.AsSpan())) && IsInLayout(root, path);

    /// <summary>The transcript of a watcher-reported sidecar path inside the layout; null for any other path.</summary>
    public static string? TranscriptOfSidecarPath(string root, string path) =>
        DroidPaths.TranscriptOfSidecar(path) is { } transcript && IsInLayout(root, path) ? transcript : null;

    // Directly in the root or in a folder directly in it; no component starts with '.', no reparse point on the way.
    private static bool IsInLayout(string root, string path)
    {
        string relative;
        try
        {
            relative = Path.GetRelativePath(root, path);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (Path.IsPathRooted(relative))
        {
            return false;
        }

        var parts = relative.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 2 || parts.Any(p => p.StartsWith('.')))
        {
            return false;
        }

        try
        {
            var current = Path.GetFullPath(path);
            var stop = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            while (current.Length > stop.Length)
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    return false;
                }

                current = Path.GetDirectoryName(current) ?? stop;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }

        return true;
    }

    /// <summary>Adds the sessions of one folder; returns its subfolders that may hold sessions.</summary>
    private static List<string> List(string folder, List<DiscoveredFile> files, ref int scanned, int scanCap, CancellationToken cancellationToken)
    {
        var folders = new List<string>();
        var transcripts = new List<Entry>();
        var sidecars = new Dictionary<string, DateTime>(NameComparer);
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = false,
                IgnoreInaccessible = true,
                AttributesToSkip = 0,
                ReturnSpecialDirectories = false,
            };
            var entries = new FileSystemEnumerable<Entry>(
                folder,
                static (ref FileSystemEntry entry) => new Entry(
                    entry.IsDirectory,
                    entry.FileName.ToString(),
                    entry.ToFullPath(),
                    entry.Length,
                    entry.LastWriteTimeUtc.UtcDateTime),
                options)
            {
                ShouldIncludePredicate = static (ref FileSystemEntry entry) =>
                    (entry.Attributes & FileAttributes.ReparsePoint) == 0 && !entry.FileName.StartsWith('.'),
            };

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++scanned > scanCap)
                {
                    break;
                }

                if (entry.IsDirectory)
                {
                    folders.Add(entry.Path);
                }
                else if (DroidPaths.IsTranscriptName(entry.Name))
                {
                    transcripts.Add(entry);
                }
                else if (entry.Name.EndsWith(DroidPaths.SidecarSuffix, StringComparison.Ordinal))
                {
                    sidecars[entry.Name] = entry.WriteUtc;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        foreach (var transcript in transcripts)
        {
            var sidecar = transcript.Name[..^DroidPaths.TranscriptExtension.Length] + DroidPaths.SidecarSuffix;
            var time = sidecars.TryGetValue(sidecar, out var sidecarTime) && sidecarTime > transcript.WriteUtc
                ? sidecarTime
                : transcript.WriteUtc;
            files.Add(new DiscoveredFile(transcript.Path, transcript.Length, time));
        }

        folders.Sort(StringComparer.Ordinal);
        return folders;
    }

    private readonly record struct Entry(bool IsDirectory, string Name, string Path, long Length, DateTime WriteUtc);
}
