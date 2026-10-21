namespace AgentSpeed.Core.Parsing;

/// <summary>One response upsert produced by a parser (METRICS 4 and 4.3).</summary>
/// <param name="Final">False while only a streaming usage snapshot was seen (METRICS 3.3); Codex is always final.</param>
internal sealed record ParsedResponse(
    string Key,
    SourceKind Source,
    string AgentId,
    string? Model,
    long? StartMs,
    long EndMs,
    TokenCounts Tokens,
    bool Final = true);

internal interface IResponseSink
{
    /// <param name="isCompletion">True for the first emission of a key, false for later updates.</param>
    void OnResponse(ParsedResponse response, bool isCompletion);
}

internal enum LineStatus : byte
{
    Valid,
    Malformed,
    Blank,
}
