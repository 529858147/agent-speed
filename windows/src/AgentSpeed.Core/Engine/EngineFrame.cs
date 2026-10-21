namespace AgentSpeed.Core.Engine;

/// <param name="ParentId">The parent's id when the parent is listed too (METRICS 6.7).</param>
internal sealed record AgentFrame(AgentInfo Info, AgentPhase Phase, Rate Live, double? RecentSpeed, string? ParentId = null);

/// <summary>Everything the engine computes for one instant (METRICS 6).</summary>
internal sealed record EngineFrame(
    long NowMs,
    Rate Live,
    double EstimatedShare,
    Rate[] SourceLive,
    Rate PeakToday,
    long? PeakTodayAtMs,
    double? AverageToday,
    TokenTotals Today,
    TokenTotals[] SourceToday,
    IReadOnlyList<AgentFrame> Agents,
    int GeneratingCount,
    int ToolsCount,
    IReadOnlyList<HistoryPoint> History,
    ResponseSummary? LastResponse,
    IReadOnlyList<SpeedEvent> Events,
    int UnfinalizedToday);
