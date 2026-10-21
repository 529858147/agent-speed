namespace AgentSpeed.Core.Engine;

/// <summary>Agent state as seen by the engine; produced from a parser after each batch of lines.</summary>
internal readonly record struct AgentInfo(
    string Id,
    SourceKind Source,
    string? Project,
    string? Model,
    bool IsSubagent,
    AgentPhase Phase,
    long? InFlightStartMs,
    long? LastLineMs);
