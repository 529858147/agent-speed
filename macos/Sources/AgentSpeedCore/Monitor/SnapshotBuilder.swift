import Foundation

/// Facts about the roots and files that the engine does not know.
struct SnapshotContext: Sendable {
    struct SourceInfo: Sendable {
        var enabled: Bool
        var rootPath: String
        var available: Bool
        var sessionCount: Int
    }

    var sources: [SourceKind: SourceInfo]
    var warnings: [MonitorWarning]

    init(session: TranscriptSession, todayStartMs: Int64) {
        let counts = session.sessionCounts(todayStartMs: todayStartMs)
        var sources: [SourceKind: SourceInfo] = [:]
        for kind in SourceKind.allCases {
            guard let family = TranscriptFamily(kind) else {
                sources[kind] = SourceInfo(enabled: session.enabledSources.contains(kind), rootPath: "", available: false, sessionCount: 0)
                continue
            }
            let root = session.root(for: family)
            sources[kind] = SourceInfo(
                enabled: session.enabledSources.contains(kind),
                rootPath: root.path,
                available: root.available,
                sessionCount: counts[kind] ?? 0
            )
        }
        self.sources = sources
        var warnings: [MonitorWarning] = []
        if session.partialHistory { warnings.append(.partialHistory) }
        if session.unreadableFileCount > 0 { warnings.append(.unreadableFiles(session.unreadableFileCount)) }
        let malformed = session.malformedLines
        if malformed > 0 { warnings.append(.malformedLines(malformed)) }
        self.warnings = warnings
    }

    /// Warnings that depend on the engine's view of today.
    func warnings(for output: EngineOutput) -> [MonitorWarning] {
        output.unfinalizedToday > 0 ? warnings + [.unfinalizedUsage(output.unfinalizedToday)] : warnings
    }
}

/// Maps engine output onto the public `SpeedSnapshot`.
enum SnapshotBuilder {
    static func running(_ output: EngineOutput, context: SnapshotContext) -> SpeedSnapshot {
        let agents = output.agents.map { row in
            AgentStatus(
                id: row.agent.id,
                source: row.agent.source,
                project: row.agent.project,
                model: row.agent.model,
                phase: row.phase,
                isSubagent: row.agent.isSubagent,
                live: row.live,
                recentSpeed: row.recentSpeed,
                lastActivity: date(row.agent.lastLineMs ?? 0),
                parentId: row.parentId
            )
        }
        let sources = SourceKind.allCases.map { kind in
            let info = context.sources[kind]
            return SourceStatus(
                kind: kind,
                enabled: info?.enabled ?? false,
                rootPath: info?.rootPath ?? "",
                available: info?.available ?? false,
                sessionCount: info?.sessionCount ?? 0,
                live: output.sourceLive[kind] ?? .zero,
                today: output.todayBySource[kind] ?? TokenTotals(),
                activeAgents: agents.filter { $0.source == kind && $0.phase == .generating }.count
            )
        }
        return SpeedSnapshot(
            timestamp: date(output.nowMs),
            state: .running,
            live: output.live,
            liveEstimatedShare: output.liveEstimatedShare,
            peakToday: output.peakToday,
            peakTodayAt: output.peakTodayAtMs.map(date),
            averageToday: output.averageToday,
            today: output.today,
            sources: sources,
            agents: agents,
            generatingCount: agents.filter { $0.phase == .generating }.count,
            toolsCount: agents.filter { $0.phase == .tools }.count,
            history: output.history,
            lastResponse: output.lastResponse,
            warnings: context.warnings(for: output)
        )
    }

    /// A snapshot without data, for `.starting` and `.noSources`.
    static func placeholder(state: MonitorState, context: SnapshotContext, now: Date) -> SpeedSnapshot {
        var snapshot = SpeedSnapshot.empty
        snapshot.timestamp = now
        snapshot.state = state
        snapshot.sources = SourceKind.allCases.map { kind in
            let info = context.sources[kind]
            return SourceStatus(
                kind: kind,
                enabled: info?.enabled ?? false,
                rootPath: info?.rootPath ?? "",
                available: info?.available ?? false,
                sessionCount: 0,
                live: .zero,
                today: TokenTotals(),
                activeAgents: 0
            )
        }
        snapshot.warnings = context.warnings
        return snapshot
    }

    private static func date(_ ms: Int64) -> Date {
        Date(timeIntervalSince1970: TimeInterval(ms) / 1000)
    }
}
