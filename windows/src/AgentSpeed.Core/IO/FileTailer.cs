using System.Buffers;
using AgentSpeed.Core.Parsing;
using Microsoft.Win32.SafeHandles;

namespace AgentSpeed.Core.IO;

internal readonly record struct TailReadResult(bool WasReset, long Length, DateTime LastWriteUtc, long BytesRead);

/// <summary>How a tailer that starts inside a file treats the bytes before the first newline (METRICS 5).</summary>
internal enum StartAlignment
{
    None,

    /// <summary>Skip the fragment only when the byte before the start offset is not a newline.</summary>
    SkipPartialLine,

    /// <summary>Always skip to the next newline (the large-file tail rule).</summary>
    SkipToNextLine,
}

/// <summary>
/// Reads appended bytes of one transcript and splits them into complete lines (METRICS 5). Files are opened only
/// for the duration of a read with full sharing so agents are never blocked. Not thread-safe.
/// </summary>
internal sealed class FileTailer
{
    public const int MaxLineBytes = 32 * 1024 * 1024;
    private const int ChunkBytes = 1024 * 1024;

    private byte[]? _partial;
    private int _partialLength;
    private bool _skippingOversized;
    private StartAlignment _alignment;
    private bool _alignToNextLine;

    public FileTailer(string path)
    {
        Path = path;
    }

    public string Path { get; }

    /// <summary>Bytes consumed so far, including a buffered incomplete line.</summary>
    public long Offset { get; private set; }

    public FileIdentity? Identity { get; private set; }

    public long OversizedLines { get; private set; }

    public long TotalBytesRead { get; private set; }

    /// <summary>Positions the tailer before the first read.</summary>
    public void StartAt(long offset, StartAlignment alignment = StartAlignment.None)
    {
        Offset = Math.Max(0, offset);
        _alignment = Offset > 0 ? alignment : StartAlignment.None;
        _alignToNextLine = _alignment != StartAlignment.None;
        ClearPartial();
    }

    /// <summary>Records identity and size without reading (files that start at their current size).</summary>
    public TailReadResult Prime(long offset)
    {
        using var handle = Open(Path);
        var length = RandomAccess.GetLength(handle);
        Identity = FileIdentity.From(handle);
        StartAt(Math.Min(offset, length));
        return new TailReadResult(false, length, File.GetLastWriteTimeUtc(handle), 0);
    }

    public TailReadResult ReadAppended(ILineSink sink, CancellationToken cancellationToken = default)
    {
        using var handle = Open(Path);
        var length = RandomAccess.GetLength(handle);
        var identity = FileIdentity.From(handle);
        var wasReset = false;
        if (length < Offset || (Identity is { } known && identity is { } current && known != current))
        {
            Offset = 0;
            _alignment = StartAlignment.None;
            _alignToNextLine = false;
            ClearPartial();
            wasReset = true;
        }

        Identity = identity;
        if (_alignment == StartAlignment.SkipPartialLine && Offset <= length)
        {
            Span<byte> previous = stackalloc byte[1];
            _alignToNextLine = RandomAccess.Read(handle, previous, Offset - 1) != 1 || previous[0] != (byte)'\n';
        }

        _alignment = StartAlignment.None;
        long bytesRead = 0;
        if (length > Offset)
        {
            var buffer = ArrayPool<byte>.Shared.Rent((int)Math.Min(ChunkBytes, length - Offset));
            try
            {
                while (Offset < length)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var want = (int)Math.Min(buffer.Length, length - Offset);
                    var read = RandomAccess.Read(handle, buffer.AsSpan(0, want), Offset);
                    if (read <= 0)
                    {
                        break;
                    }

                    Offset += read;
                    bytesRead += read;
                    Consume(buffer.AsSpan(0, read), sink);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        TotalBytesRead += bytesRead;
        if (_partialLength == 0)
        {
            _partial = null;
        }

        return new TailReadResult(wasReset, length, File.GetLastWriteTimeUtc(handle), bytesRead);
    }

    /// <summary>Opens for reading without ever locking the writer out (read/write/delete sharing).</summary>
    public static SafeFileHandle Open(string path) => File.OpenHandle(
        path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.SequentialScan);

    private void Consume(ReadOnlySpan<byte> data, ILineSink sink)
    {
        if (_alignToNextLine)
        {
            var first = data.IndexOf((byte)'\n');
            if (first < 0)
            {
                return;
            }

            _alignToNextLine = false;
            data = data[(first + 1)..];
        }

        while (!data.IsEmpty)
        {
            var newline = data.IndexOf((byte)'\n');
            if (newline < 0)
            {
                AppendPartial(data);
                return;
            }

            var segment = data[..newline];
            data = data[(newline + 1)..];
            if (_skippingOversized)
            {
                _skippingOversized = false;
                OversizedLines++;
                continue;
            }

            if (_partialLength == 0)
            {
                Emit(segment, sink);
                continue;
            }

            if ((long)_partialLength + segment.Length > MaxLineBytes)
            {
                OversizedLines++;
            }
            else
            {
                AppendPartial(segment);
                Emit(_partial.AsSpan(0, _partialLength), sink);
            }

            _partialLength = 0;
        }
    }

    private void Emit(ReadOnlySpan<byte> line, ILineSink sink)
    {
        if (line.Length > MaxLineBytes)
        {
            OversizedLines++;
            return;
        }

        if (!line.IsEmpty && line[^1] == '\r')
        {
            line = line[..^1];
        }

        sink.OnLine(line);
    }

    private void AppendPartial(ReadOnlySpan<byte> data)
    {
        if (_skippingOversized)
        {
            return;
        }

        var needed = (long)_partialLength + data.Length;
        if (needed > MaxLineBytes)
        {
            _skippingOversized = true;
            _partialLength = 0;
            return;
        }

        if (_partial is null || _partial.Length < needed)
        {
            var grown = new byte[(int)Math.Min(MaxLineBytes, Math.Max(needed, Math.Max(64 * 1024, (_partial?.Length ?? 0) * 2L)))];
            _partial.AsSpan(0, _partialLength).CopyTo(grown);
            _partial = grown;
        }

        data.CopyTo(_partial.AsSpan(_partialLength));
        _partialLength = (int)needed;
    }

    private void ClearPartial()
    {
        _partial = null;
        _partialLength = 0;
        _skippingOversized = false;
    }
}
