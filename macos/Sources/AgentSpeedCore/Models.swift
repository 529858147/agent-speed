import Foundation

public enum SourceKind: String, CaseIterable, Codable, Sendable, Identifiable {
    case codex
    case claudeCode
    case claudeDesktop

    public var id: String { rawValue }
}

public enum AgentPhase: String, Codable, Sendable {
    case generating
    case tools
    case idle
}

public enum SpeedMetric: String, CaseIterable, Codable, Sendable {
    case output
    case total
}

public struct TokenTotals: Codable, Equatable, Sendable {
    public var requests: Int
    public var input: Int
    public var cachedInput: Int
    public var cacheWriteInput: Int
    public var output: Int
    public var reasoning: Int

    public var total: Int { input + output }

    public init(
        requests: Int = 0,
        input: Int = 0,
        cachedInput: Int = 0,
        cacheWriteInput: Int = 0,
        output: Int = 0,
        reasoning: Int = 0
    ) {
        self.requests = requests
        self.input = input
        self.cachedInput = cachedInput
        self.cacheWriteInput = cacheWriteInput
        self.output = output
        self.reasoning = reasoning
    }

    public static let zero = TokenTotals()
}

public struct Rate: Equatable, Sendable {
    public var output: Double
    public var total: Double

    public init(output: Double = 0, total: Double = 0) {
        self.output = output
        self.total = total
    }

    public func value(for metric: SpeedMetric) -> Double {
        metric == .output ? output : total
    }

    public static let zero = Rate()
}

public struct HistoryPoint: Equatable, Sendable {
    /// Start of the one-second bucket.
    public var time: Date
    public var measured: Rate
    public var estimated: Rate

    public init(time: Date, measured: Rate, estimated: Rate) {
        self.time = time
        self.measured = measured
        self.estimated = estimated
    }
}

public struct AgentStatus: Identifiable, Equatable, Sendable {
    /// Absolute transcript path.
    public var id: String
    public var source: SourceKind
    public var project: String
    public var model: String?
    public var phase: AgentPhase
    public var isSubagent: Bool
    public var live: Rate
    /// Output tok/s of the agent's recent responses (METRICS 6.2).
    public var recentSpeed: Double?
    public var lastActivity: Date

    public init(
        id: String,
        source: SourceKind,
        project: String,
        model: String?,
        phase: AgentPhase,
        isSubagent: Bool,
        live: Rate,
        recentSpeed: Double?,
        lastActivity: Date
    ) {
        self.id = id
        self.source = source
        self.project = project
        self.model = model
        self.phase = phase
        self.isSubagent = isSubagent
        self.live = live
        self.recentSpeed = recentSpeed
        self.lastActivity = lastActivity
    }
}

public struct SourceStatus: Identifiable, Equatable, Sendable {
    public var id: SourceKind { kind }
    public var kind: SourceKind
    public var enabled: Bool
    public var rootPath: String
    /// The root folder exists and is readable.
    public var available: Bool
    public var sessionCount: Int
    public var live: Rate
    public var today: TokenTotals
    public var activeAgents: Int

    public init(
        kind: SourceKind,
        enabled: Bool,
        rootPath: String,
        available: Bool,
        sessionCount: Int,
        live: Rate,
        today: TokenTotals,
        activeAgents: Int
    ) {
        self.kind = kind
        self.enabled = enabled
        self.rootPath = rootPath
        self.available = available
        self.sessionCount = sessionCount
        self.live = live
        self.today = today
        self.activeAgents = activeAgents
    }
}

public struct ResponseSummary: Equatable, Sendable {
    public var source: SourceKind
    public var project: String
    public var model: String?
    public var outputTokens: Int
    public var totalTokens: Int
    /// Nil when the request start is unknown.
    public var duration: TimeInterval?
    /// Output tok/s, nil when the duration is unknown.
    public var speed: Double?
    public var endedAt: Date

    public init(
        source: SourceKind,
        project: String,
        model: String?,
        outputTokens: Int,
        totalTokens: Int,
        duration: TimeInterval?,
        speed: Double?,
        endedAt: Date
    ) {
        self.source = source
        self.project = project
        self.model = model
        self.outputTokens = outputTokens
        self.totalTokens = totalTokens
        self.duration = duration
        self.speed = speed
        self.endedAt = endedAt
    }
}

public enum MonitorState: Equatable, Sendable {
    case starting(progress: Double)
    case running
    case noSources
}

public enum MonitorWarning: Equatable, Sendable {
    case partialHistory
    case unreadableFiles(Int)
    case malformedLines(Int)
}

public struct SpeedSnapshot: Equatable, Sendable {
    public var timestamp: Date
    public var state: MonitorState
    public var live: Rate
    /// Share of `live` (0...1) that is an in-flight estimate.
    public var liveEstimatedShare: Double
    public var peakToday: Rate
    public var peakTodayAt: Date?
    /// Output tok/s over today's valid responses.
    public var averageToday: Double?
    public var today: TokenTotals
    public var sources: [SourceStatus]
    public var agents: [AgentStatus]
    public var generatingCount: Int
    public var toolsCount: Int
    /// Last 300 one-second buckets, oldest first.
    public var history: [HistoryPoint]
    public var lastResponse: ResponseSummary?
    public var warnings: [MonitorWarning]

    public init(
        timestamp: Date,
        state: MonitorState,
        live: Rate,
        liveEstimatedShare: Double,
        peakToday: Rate,
        peakTodayAt: Date?,
        averageToday: Double?,
        today: TokenTotals,
        sources: [SourceStatus],
        agents: [AgentStatus],
        generatingCount: Int,
        toolsCount: Int,
        history: [HistoryPoint],
        lastResponse: ResponseSummary?,
        warnings: [MonitorWarning]
    ) {
        self.timestamp = timestamp
        self.state = state
        self.live = live
        self.liveEstimatedShare = liveEstimatedShare
        self.peakToday = peakToday
        self.peakTodayAt = peakTodayAt
        self.averageToday = averageToday
        self.today = today
        self.sources = sources
        self.agents = agents
        self.generatingCount = generatingCount
        self.toolsCount = toolsCount
        self.history = history
        self.lastResponse = lastResponse
        self.warnings = warnings
    }

    public static let empty = SpeedSnapshot(
        timestamp: .distantPast,
        state: .starting(progress: 0),
        live: .zero,
        liveEstimatedShare: 0,
        peakToday: .zero,
        peakTodayAt: nil,
        averageToday: nil,
        today: .zero,
        sources: [],
        agents: [],
        generatingCount: 0,
        toolsCount: 0,
        history: [],
        lastResponse: nil,
        warnings: []
    )
}

public enum SpeedEvent: Equatable, Sendable {
    case responseCompleted(ResponseSummary)
    case newPeak(Double)
}

public struct MonitorConfiguration: Equatable, Sendable {
    public var enabledSources: Set<SourceKind>
    /// Nil uses the default Codex sessions folder.
    public var codexRoot: URL?
    /// Nil uses the default Claude projects folder (shared by Claude Code and Claude Desktop).
    public var claudeRoot: URL?

    public init(
        enabledSources: Set<SourceKind> = Set(SourceKind.allCases),
        codexRoot: URL? = nil,
        claudeRoot: URL? = nil
    ) {
        self.enabledSources = enabledSources
        self.codexRoot = codexRoot
        self.claudeRoot = claudeRoot
    }
}
