using System.Globalization;
using AgentSpeed.Core.Droid;
using AgentSpeed.Core.IO;
using AgentSpeed.Core.Parsing;

namespace AgentSpeed.Core.History;

/// <summary>
/// Factory Droid sessions (METRICS 4.16, 8): the history rule makes a session's contributions a function of its
/// transcript, its sidecar and, for a fork or successor, its parent's sidecar, so the stored modification time of its
/// entry is a hash of all three files' sizes and times, taken before the session is read. A session whose own counters
/// are unknown (its parent sidecar unreadable) is not stored, so the next analysis reads it again; the signature of that
/// parent sidecar is remembered in memory from one analysis to the next.
/// </summary>
internal sealed partial class HistoryAnalyzer
{
    private const int DroidHeaderProbeBytes = 64 * 1024;

    private readonly Dictionary<string, DroidSidecarSignature> _droidUnreadableParents = new(PathComparer);

    /// <summary>The history rule, then the session's leftover growth, with the remembered unreadable parent signature.</summary>
    private void ApplyDroid(string path, DroidParser droid, ILineBatch batch, IResponseSink sink)
    {
        droid.UnreadableParent = _droidUnreadableParents.TryGetValue(path, out var remembered) ? remembered : null;
        batch.ApplyTo(sink);
        if (droid.UnreadableParent is { } signature)
        {
            _droidUnreadableParents[path] = signature;
        }
        else
        {
            _droidUnreadableParents.Remove(path);
        }

        if (!droid.CountersUnknown)
        {
            droid.FinishHistory(sink);
        }
    }

    /// <summary>
    /// Leaves a session with unknown own counters out of this analysis: what was stored stays, with its keys, and the keys
    /// it claimed now stay claimed for the files after it, as when it is read.
    /// </summary>
    private void SkipDroid(KeyLedger ledger, StoredFile? previous)
    {
        ledger.FinishFile();
        if (previous is { Id: > 0 })
        {
            _store.LoadKeys(ledger, previous.Id);
        }
    }

    /// <param name="file">A discovered session: the transcript's size and the newer of the two files' times.</param>
    private static long DroidIdentity(DiscoveredFile file)
    {
        var parent = DroidParentOf(file.Path) is { } id ? Stamp(DroidPaths.SiblingSidecar(file.Path, id)) : "-";
        var text = string.Join('|',
            string.Create(CultureInfo.InvariantCulture, $"{file.Length}:{file.LastWriteUtc.Ticks}"),
            Stamp(DroidPaths.SidecarOf(file.Path)),
            parent);
        return unchecked((long)KeyHash.Fnv1a(text));

        static string Stamp(string path)
        {
            try
            {
                var info = new FileInfo(path);
                return info.Exists && (info.Attributes & FileAttributes.ReparsePoint) == 0
                    ? string.Create(CultureInfo.InvariantCulture, $"{info.Length}:{info.LastWriteTimeUtc.Ticks}")
                    : "-";
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return "-";
            }
        }
    }

    /// <summary><c>parent</c> of the first <c>session_start</c> within the transcript's first 64 KiB; null when none.</summary>
    private static string? DroidParentOf(string path)
    {
        try
        {
            using var handle = TranscriptTailer.Open(path);
            var buffer = new byte[(int)Math.Min(RandomAccess.GetLength(handle), DroidHeaderProbeBytes)];
            var span = buffer.AsSpan(0, RandomAccess.Read(handle, buffer, 0));
            while (!span.IsEmpty)
            {
                var newline = span.IndexOf((byte)'\n');
                if (newline < 0)
                {
                    return null;
                }

                var line = DroidLineReader.Read(span[..newline]);
                if (line is { Status: LineStatus.Valid, Kind: DroidLineKind.SessionStart })
                {
                    return line.Parent;
                }

                span = span[(newline + 1)..];
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        return null;
    }
}
