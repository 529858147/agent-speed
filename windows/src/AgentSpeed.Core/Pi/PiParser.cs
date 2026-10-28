using System.Globalization;
using AgentSpeed.Core.IO;
using AgentSpeed.Core.Parsing;

namespace AgentSpeed.Core.Pi;

/// <summary>
/// pi session state machine (METRICS 4.13). Every assistant message is written once, after its stream ended; the
/// request start is <c>message.timestamp</c> and the end is <c>durationMs</c> (recent versions) or the entry timestamp.
/// In-flight state is inferred from neighbouring entries.
/// </summary>
internal sealed class PiParser : TranscriptParser<PiLine>
{
    /// <summary>Entries this much older than the header were copied from another session (forks, clones).</summary>
    public const long CopiedHistoryMs = 2000;

    private const int HeaderProbeLines = 2;

    private readonly HashSet<string> _pendingToolCalls = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ParsedResponse> _responses = new(StringComparer.Ordinal);
    private bool _headerSeen;
    private bool _headerProbed;

    /// <param name="root">The <c>sessions</c> root the file was found in: files three or more levels below it are sub-agent runs.</param>
    public PiParser(string agentId, IDeduper deduper, string root)
        : base(agentId, deduper, SourceKind.Pi)
    {
        PathIsSubagent = PiDiscovery.DepthBelow(root, agentId) >= PiDiscovery.SubagentMinDepth;
        ParentPath = PathIsSubagent ? PiDiscovery.ParentPath(root, agentId) : null;
        RestorePathState();
    }

    /// <summary>The file lives inside a main session's artifacts folder (an extension's run sub-session).</summary>
    public bool PathIsSubagent { get; }

    /// <summary>The main session a sub-agent run attaches to, or null for a main session.</summary>
    public string? ParentPath { get; }

    /// <summary>The header <c>timestamp</c>; null until a header with a parseable timestamp was read.</summary>
    public long? HeaderMs { get; private set; }

    public bool HeaderSeen => _headerSeen;

    public override void Reset()
    {
        base.Reset();
        _pendingToolCalls.Clear();
        _responses.Clear();
        _headerSeen = false;
        _headerProbed = false;
        HeaderMs = null;
        RestorePathState();
    }

    /// <summary>
    /// Reading that starts at an offset above 0 first takes the header from the file's first lines. Runs on the reading
    /// thread; the header is applied before the batch's lines.
    /// </summary>
    public override void PrepareBatch(ILineBatch batch, long startOffset)
    {
        if (startOffset <= 0 || _headerSeen || _headerProbed || batch is not PiBatch pi)
        {
            return;
        }

        _headerProbed = true;
        pi.Header = ReadHeader(AgentId);
    }

    public override ILineBatch NewBatch() => new PiBatch(this);

    public override void Apply(in PiLine line, IResponseSink sink)
    {
        if (line.Status == LineStatus.Malformed)
        {
            CountMalformed();
            return;
        }

        if (line.Status != LineStatus.Valid)
        {
            return;
        }

        if (line.Kind == PiEntryKind.Session)
        {
            ApplyHeader(in line);
            return;
        }

        if (!_headerSeen || line.TimestampMs is not { } ts
            || (HeaderMs is { } header && ts < header - CopiedHistoryMs))
        {
            return;
        }

        Touch(ts);
        switch (line.Kind)
        {
            case PiEntryKind.Message:
                ApplyMessage(in line, ts, sink);
                break;
            case PiEntryKind.SideUsage:
                ApplySideUsage(in line, ts, sink);
                break;
        }
    }

    protected override PiLine Extract(ReadOnlySpan<byte> line) => PiLineReader.Read(line);

    /// <summary>The first <c>session</c> line among the first two lines of the file, else a blank line.</summary>
    internal static PiLine ReadHeader(string path)
    {
        try
        {
            using var handle = TranscriptTailer.Open(path);
            var length = RandomAccess.GetLength(handle);
            var buffer = new byte[(int)Math.Min(length, 64 * 1024)];
            var read = RandomAccess.Read(handle, buffer, 0);
            var span = buffer.AsSpan(0, read);
            for (var i = 0; i < HeaderProbeLines && !span.IsEmpty; i++)
            {
                var newline = span.IndexOf((byte)'\n');
                var raw = newline < 0 ? span : span[..newline];
                var line = PiLineReader.Read(raw);
                if (line is { Status: LineStatus.Valid, Kind: PiEntryKind.Session })
                {
                    return line;
                }

                if (newline < 0)
                {
                    break;
                }

                span = span[(newline + 1)..];
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        return new PiLine { Status = LineStatus.Blank };
    }

    private void ApplyHeader(in PiLine line)
    {
        if (_headerSeen)
        {
            return;
        }

        _headerSeen = true;
        HeaderMs = line.TimestampMs;
        SetProjectFromCwd(line.Cwd);
        MarkChanged();
        if (line.TimestampMs is { } ts)
        {
            Touch(ts);
        }
    }

    private void ApplyMessage(in PiLine line, long ts, IResponseSink sink)
    {
        switch (line.Role)
        {
            case PiRole.User:
                _pendingToolCalls.Clear();
                SetPhase(AgentPhase.Generating, ts, ts);
                break;
            case PiRole.Assistant:
                ApplyAssistant(in line, ts, sink);
                break;
            case PiRole.ToolResult:
                if (line.ToolCallId is { } callId)
                {
                    _pendingToolCalls.Remove(callId);
                }

                // Nested model work a tool performed: totals only, like the other side usages.
                if (line.Call.HasUsage)
                {
                    ApplySideUsage(in line, ts, sink);
                }

                if (Phase == AgentPhase.Tools && _pendingToolCalls.Count == 0)
                {
                    SetPhase(AgentPhase.Generating, ts, ts);
                }

                break;
        }
    }

    private void ApplyAssistant(in PiLine line, long ts, IResponseSink sink)
    {
        var call = line.Call;
        if (!call.Guarded)
        {
            return;
        }

        var key = Key(line.Id, line.StartedMs ?? ts);
        if (Deduper.TryGetOwner(key, out var owner) && !string.Equals(owner, AgentId, StringComparison.Ordinal))
        {
            return;
        }

        var failed = line.Stop is PiStop.Error or PiStop.Aborted;
        var end = line.StartedMs is { } started && line.DurationMs is { } duration
            ? (duration > long.MaxValue - started ? long.MaxValue : started + duration)
            : ts;
        if (call.HasUsage)
        {
            Emit(key, failed ? null : line.StartedMs, end, call.Model, in call, sink);
            SetModel(call.Model);
        }

        if (failed)
        {
            _pendingToolCalls.Clear();
            SetPhase(AgentPhase.Idle, ts, null);
        }
        else if (line.ToolCallIds is { Length: > 0 } calls)
        {
            _pendingToolCalls.Clear();
            _pendingToolCalls.UnionWith(calls);
            SetPhase(AgentPhase.Tools, ts, null);
        }
        else
        {
            SetPhase(AgentPhase.Idle, ts, null);
        }
    }

    // usage, compaction and branch_summary entries: side model work counts in totals only and never sets the agent's model.
    private void ApplySideUsage(in PiLine line, long ts, IResponseSink sink)
    {
        var call = line.Call;
        if (!call.HasUsage)
        {
            return;
        }

        var key = Key(line.Id, ts);
        if (Deduper.TryGetOwner(key, out var owner) && !string.Equals(owner, AgentId, StringComparison.Ordinal))
        {
            return;
        }

        // Compaction, branch summaries and tool results carry no model of their own; they belong to the session's current one.
        Emit(key, null, ts, call.Model ?? Model, in call, sink);
    }

    private void Emit(string key, long? startMs, long endMs, string? model, in PiCall call, IResponseSink sink)
    {
        var isCompletion = Deduper.TryClaim(key, AgentId, out _);
        var response = new ParsedResponse(key, SourceKind.Pi, AgentId, model, startMs, endMs, call.Tokens, Final: true, Provider: call.Provider);
        if (_responses.TryGetValue(key, out var previous) && previous == response)
        {
            return;
        }

        _responses[key] = response;
        sink.OnResponse(response, isCompletion);
    }

    private static string Key(string? id, long stamp) =>
        string.Create(CultureInfo.InvariantCulture, $"pi:{id ?? string.Empty}:{stamp}");

    private void RestorePathState()
    {
        SetSource(SourceKind.Pi);
        SetSubagent(PathIsSubagent);
    }

    /// <summary>Lines of one read, with the header probed from the file start applied first.</summary>
    private sealed class PiBatch(PiParser owner) : ILineBatch
    {
        private readonly ILineBatch _lines = new Lines(owner);

        public PiLine? Header { get; set; }

        public int Count => _lines.Count;

        public void OnLine(ReadOnlySpan<byte> line) => _lines.OnLine(line);

        public void OnMalformed() => _lines.OnMalformed();

        public void ApplyTo(IResponseSink sink)
        {
            if (Header is { } header)
            {
                owner.Apply(in header, sink);
            }

            _lines.ApplyTo(sink);
        }

        public void Clear()
        {
            Header = null;
            _lines.Clear();
        }

        private sealed class Lines(PiParser parser) : ILineBatch
        {
            private readonly List<PiLine> _items = [];
            private int _malformed;

            public int Count => _items.Count;

            public void OnLine(ReadOnlySpan<byte> line) => _items.Add(PiLineReader.Read(line));

            public void OnMalformed() => _malformed++;

            public void ApplyTo(IResponseSink sink)
            {
                for (var i = 0; i < _malformed; i++)
                {
                    parser.Apply(new PiLine { Status = LineStatus.Malformed }, sink);
                }

                foreach (var item in _items)
                {
                    parser.Apply(in item, sink);
                }
            }

            public void Clear()
            {
                _items.Clear();
                _malformed = 0;
            }
        }
    }
}
