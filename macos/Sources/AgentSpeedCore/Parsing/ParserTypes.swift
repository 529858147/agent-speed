import Foundation

/// One model response as produced by a transcript parser (METRICS 4).
public struct ResponseRecord: Equatable, Hashable, Sendable {
    public var key: String
    public var source: SourceKind
    /// Absolute transcript path.
    public var agentId: String
    public var model: String?
    /// When the request was sent; nil when unknown.
    public var startMs: Int64?
    public var endMs: Int64
    public var usage: TokenUsage
    /// False while only a streaming snapshot of the usage was written (METRICS 3.3). Codex usage is always final.
    public var isFinal: Bool

    /// A final response with a valid interval contributes measured speed.
    public var isMeasured: Bool { isFinal && ResponseTiming.effectiveStart(startMs: startMs, endMs: endMs) != nil }

    public init(
        key: String,
        source: SourceKind,
        agentId: String,
        model: String?,
        startMs: Int64?,
        endMs: Int64,
        usage: TokenUsage,
        isFinal: Bool = true
    ) {
        self.key = key
        self.source = source
        self.agentId = agentId
        self.model = model
        self.startMs = startMs
        self.endMs = endMs
        self.usage = usage
        self.isFinal = isFinal
    }
}

/// A parser emission. The first emission of a key is a completion; later ones are updates (METRICS 4.3).
struct ResponseUpsert: Equatable, Sendable {
    var record: ResponseRecord
    var isCompletion: Bool
}

/// A Codex thread or a DeepSeek Harness session as named by the first `session_meta` or header of its file
/// (METRICS 4.2, 4.3, 6.7).
struct SessionThread: Equatable, Sendable {
    var id: String?
    var parentId: String?
    var createdMs: Int64
}

/// Per-file agent state maintained by a parser.
struct AgentState: Equatable, Sendable {
    var source: SourceKind
    var project: String?
    var model: String?
    var isSubagent: Bool
    /// Codex and DeepSeek only; set once by the first `session_meta` or header.
    var thread: SessionThread?
    var phase: AgentPhase = .idle
    var phaseSinceMs: Int64?
    var inFlightStartMs: Int64?
    var lastLineMs: Int64?
    var malformedLines = 0

    init(source: SourceKind, isSubagent: Bool = false) {
        self.source = source
        self.isSubagent = isSubagent
    }

    mutating func setPhase(_ newPhase: AgentPhase, at timestampMs: Int64, inFlightStartMs start: Int64? = nil) {
        if phase != newPhase {
            phase = newPhase
            phaseSinceMs = timestampMs
        }
        inFlightStartMs = newPhase == .generating ? start : nil
    }

    mutating func noteLine(at timestampMs: Int64) {
        lastLineMs = max(lastLineMs ?? timestampMs, timestampMs)
    }

    mutating func setProject(fromWorkingDirectory cwd: String) {
        let name = (cwd as NSString).lastPathComponent
        if !name.isEmpty { project = name }
    }
}
