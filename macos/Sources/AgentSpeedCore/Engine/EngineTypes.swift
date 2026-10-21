import Foundation

/// The engine's view of one agent (one transcript file).
struct EngineAgent: Equatable, Sendable {
    var id: String
    var source: SourceKind
    var project: String
    var model: String?
    var isSubagent: Bool
    var phase: AgentPhase
    var inFlightStartMs: Int64?
    var lastLineMs: Int64?

    init(
        id: String,
        source: SourceKind,
        project: String,
        model: String? = nil,
        isSubagent: Bool = false,
        phase: AgentPhase = .idle,
        inFlightStartMs: Int64? = nil,
        lastLineMs: Int64? = nil
    ) {
        self.id = id
        self.source = source
        self.project = project
        self.model = model
        self.isSubagent = isSubagent
        self.phase = phase
        self.inFlightStartMs = inFlightStartMs
        self.lastLineMs = lastLineMs
    }

    init(id: String, state: AgentState) {
        self.init(
            id: id,
            source: state.source,
            project: state.project ?? Self.fallbackProject(for: id),
            model: state.model,
            isSubagent: state.isSubagent,
            phase: state.phase,
            inFlightStartMs: state.inFlightStartMs,
            lastLineMs: state.lastLineMs
        )
    }

    /// Project name from the transcript location when the transcript never named a working directory.
    static func fallbackProject(for path: String) -> String {
        let parent = (path as NSString).deletingLastPathComponent
        let name = (parent as NSString).lastPathComponent
        return name.isEmpty ? (path as NSString).lastPathComponent : name
    }
}

/// One row of the agents list (METRICS 6.7).
struct EngineAgentStatus: Equatable, Sendable {
    var agent: EngineAgent
    /// Phase after the 300-second staleness rule.
    var phase: AgentPhase
    var live: Rate
    var recentSpeed: Double?
}

/// Everything the engine computes for one instant.
struct EngineOutput: Sendable {
    var nowMs: Int64
    var live: Rate
    var liveEstimatedShare: Double
    var sourceLive: [SourceKind: Rate]
    var agents: [EngineAgentStatus]
    var history: [HistoryPoint]
    var today: TokenTotals
    var todayBySource: [SourceKind: TokenTotals]
    var averageToday: Double?
    /// Today's responses whose final usage was never written.
    var unfinalizedToday: Int
    var peakToday: Rate
    var peakTodayAtMs: Int64?
    var lastResponse: ResponseSummary?
    var events: [SpeedEvent]
}
