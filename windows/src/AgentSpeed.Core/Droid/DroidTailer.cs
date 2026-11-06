using AgentSpeed.Core.IO;
using AgentSpeed.Core.Parsing;

namespace AgentSpeed.Core.Droid;

/// <summary>
/// Reads a Factory Droid transcript's appended lines and then its sidecar, whole, unless its signature (size,
/// modification time and identity) is the one the parser last applied; a reset reads it again (METRICS 4.16 Read cycle).
/// The read's time is the newer of the two files', so change detection notices a sidecar rewrite as it does a transcript
/// append.
/// </summary>
internal sealed class DroidTailer(string path) : FileTailer(path)
{
    public string SidecarPath { get; } = DroidPaths.SidecarOf(path);

    public override TailReadResult ReadAppended(ILineSink sink, CancellationToken cancellationToken = default)
    {
        var result = base.ReadAppended(sink, cancellationToken);
        var batch = sink as DroidBatch;
        var read = DroidSidecar.Read(SidecarPath, result.WasReset ? null : batch?.KnownSidecar);
        if (batch is not null)
        {
            batch.Sidecar = read;
        }

        var sidecarWrite = read.Signature is { } signature ? new DateTime(signature.WriteTicks, DateTimeKind.Utc) : (DateTime?)null;
        return sidecarWrite is { } written && written > result.LastWriteUtc && !read.Link
            ? result with { LastWriteUtc = written }
            : result;
    }
}
