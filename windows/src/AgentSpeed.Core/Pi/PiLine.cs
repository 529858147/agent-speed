using AgentSpeed.Core.Parsing;

namespace AgentSpeed.Core.Pi;

/// <summary>Entry classification by <c>type</c> (METRICS 4.13).</summary>
internal enum PiEntryKind : byte
{
    Other,

    /// <summary>The session header, always the file's first line.</summary>
    Session,
    Message,

    /// <summary>A <c>usage</c>, <c>compaction</c> or <c>branch_summary</c> entry carrying usage: totals only.</summary>
    SideUsage,
}

internal enum PiRole : byte
{
    Other,
    User,
    Assistant,
    ToolResult,
}

/// <summary><c>message.stopReason</c>; a missing or non-string value counts as <see cref="Aborted"/>.</summary>
internal enum PiStop : byte
{
    Aborted,
    Error,
    Stop,
    ToolUse,
    Length,

    /// <summary>The provider deferred the response; the completed record still counts.</summary>
    Deferred,
    Other,
}

/// <summary>
/// The model call fields shared by assistant messages and side-usage entries. <see cref="Guarded"/> means <c>model</c>,
/// <c>provider</c> and <c>api</c> are strings and <c>usage</c> is an object: a complete model-call record.
/// </summary>
internal struct PiCall
{
    public bool Guarded;
    public string? Model;

    /// <summary>The <c>provider</c> string; null when it is empty (METRICS 3.2).</summary>
    public string? Provider;
    public bool HasUsageObject;
    public TokenCounts Tokens;

    /// <summary>One of <c>input</c>, <c>output</c>, <c>cacheRead</c>, <c>cacheWrite</c> is above 0.</summary>
    public bool HasUsage;
}

/// <summary>One extracted pi entry. Conversation text is never kept.</summary>
internal struct PiLine
{
    public LineStatus Status;
    public PiEntryKind Kind;

    /// <summary>The entry <c>id</c> when it is a string.</summary>
    public string? Id;

    /// <summary>The entry (or header) <c>timestamp</c>, ISO 8601.</summary>
    public long? TimestampMs;

    /// <summary>Header <c>cwd</c>.</summary>
    public string? Cwd;

    public PiRole Role;

    /// <summary>Assistant message or a side-usage entry's model call fields.</summary>
    public PiCall Call;

    /// <summary><c>message.timestamp</c> truncated, when it is a finite number above 0.</summary>
    public long? StartedMs;

    /// <summary><c>message.durationMs</c> truncated, when it is a finite number of at least 0.</summary>
    public long? DurationMs;

    public PiStop Stop;

    /// <summary>Distinct string ids of the <c>toolCall</c> content blocks.</summary>
    public string[]? ToolCallIds;

    /// <summary>Tool result <c>toolCallId</c> when it is a string.</summary>
    public string? ToolCallId;
}
