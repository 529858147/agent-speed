import AgentSpeedCore
import Foundation

/// Deterministic sample data for `--render-previews`.
enum PreviewData {
    enum AgentSet {
        case standard
        /// Nested groups, a sub-agent whose parent is not listed, and a group cut by the row limit.
        case grouped
        /// The first `count` (up to 24) agents of a busy machine, for the expandable list.
        case crowd(Int)
    }

    static func snapshot(speed: Double, redline: Double, agents set: AgentSet = .standard, now: Date = Date()) -> SpeedSnapshot {
        let second = floor(now.timeIntervalSince1970)
        let history: [HistoryPoint] = (0..<300).map { index in
            let t = Double(index)
            let wave = 0.55 + 0.35 * sin(t / 23) + 0.18 * sin(t / 7.3) + 0.08 * sin(t * 1.7)
            let ramp = Easing.smoothstep(0, 80, t)
            let ending = index > 288 ? speed / max(redline, 1) : wave * ramp
            let base = max(0, ending) * redline * 0.9
            let isTail = index >= 297
            return HistoryPoint(
                time: Date(timeIntervalSince1970: second - Double(299 - index)),
                measured: Rate(output: isTail ? base * 0.4 : base, total: base * 26),
                estimated: Rate(output: isTail ? base * 0.6 : 0, total: isTail ? base * 15 : 0)
            )
        }
        let agents: [AgentStatus]
        switch set {
        case .standard: agents = standardAgents(speed: speed, now: now)
        case .grouped: agents = groupedAgents(speed: speed, now: now)
        case .crowd(let count): agents = Array(crowdAgents(speed: speed, now: now).prefix(count))
        }
        let today = TokenTotals(requests: 1_284, input: 48_200_000, cachedInput: 41_000_000, cacheWriteInput: 2_100_000, output: 1_240_000, reasoning: 380_000)
        let sources: [SourceStatus] = [
            source(.codex, speed * 0.34, TokenTotals(requests: 520, input: 19_000_000, output: 520_000), 1),
            source(.claudeCode, speed * 0.42, TokenTotals(requests: 480, input: 19_400_000, output: 440_000), 2),
            source(.claudeDesktop, speed * 0.10, TokenTotals(requests: 120, input: 4_600_000, output: 120_000), 1),
            source(.deepseek, speed * 0.14, TokenTotals(requests: 164, input: 5_200_000, output: 160_000), 1),
        ]
        return SpeedSnapshot(
            timestamp: now,
            state: .running,
            live: Rate(output: speed, total: speed * 26),
            liveEstimatedShare: 0.32,
            peakToday: Rate(output: redline * 1.08, total: redline * 26),
            peakTodayAt: now.addingTimeInterval(-3_600 * 2.4),
            averageToday: redline * 0.31,
            today: today,
            sources: sources,
            agents: agents,
            generatingCount: agents.filter { $0.phase == .generating }.count,
            toolsCount: agents.filter { $0.phase == .tools }.count,
            history: history,
            lastResponse: nil,
            warnings: []
        )
    }

    private static func standardAgents(speed: Double, now: Date) -> [AgentStatus] {
        [
            agent("rocket", .codex, "rocket", "gpt-6.1-sol", .generating, false, speed * 0.34, now),
            agent("nova", .claudeCode, "nova", "claude-opus-5-5", .generating, false, speed * 0.27, now),
            agent("nova-sub", .claudeCode, "nova", "claude-sonnet-5-5", .generating, true, speed * 0.15, now, parent: "nova"),
            agent("quasar", .deepseek, "quasar", "deepseek-v4-pro", .generating, false, speed * 0.14, now),
            agent("orbit", .claudeDesktop, "orbit", "claude-fable-5-1", .generating, false, speed * 0.10, now),
            agent("lumen", .claudeCode, "lumen", "claude-haiku-5", .idle, false, 0, now.addingTimeInterval(-220)),
        ]
    }

    /// Twenty-four agents in engine order: groups of every source, then tools and idle stragglers.
    private static func crowdAgents(speed: Double, now: Date) -> [AgentStatus] {
        [
            agent("orbit", .claudeDesktop, "orbit", "claude-opus-5-5", .generating, false, speed * 0.16, now),
            agent("orbit-explore", .claudeDesktop, "orbit", "claude-sonnet-5-5", .generating, true, speed * 0.1, now, parent: "orbit"),
            agent("orbit-tests", .claudeDesktop, "orbit", "claude-haiku-5", .tools, true, 0, now.addingTimeInterval(-4), parent: "orbit"),
            agent("quasar", .deepseek, "quasar", "deepseek-v4-pro", .generating, false, speed * 0.14, now),
            agent("quasar-scout", .deepseek, "quasar", "deepseek-v4-flash", .generating, true, speed * 0.08, now, parent: "quasar"),
            agent("rocket", .codex, "rocket", "gpt-6.1-sol", .generating, false, speed * 0.12, now),
            agent("rocket-worker", .codex, "rocket", "gpt-6.1-sol-mini", .generating, true, speed * 0.06, now, parent: "rocket"),
            agent("rocket-review", .codex, "rocket", "gpt-6.1-sol-mini", .generating, true, speed * 0.04, now, parent: "rocket"),
            agent("nova", .claudeCode, "nova", "claude-opus-5-5", .generating, false, speed * 0.1, now),
            agent("nova-sub", .claudeCode, "nova", "claude-sonnet-5-5", .generating, true, speed * 0.05, now, parent: "nova"),
            agent("atlas", .codex, "atlas-web", "gpt-6.1-sol", .generating, false, speed * 0.07, now),
            agent("vega", .deepseek, "vega", "deepseek-v4-flash", .generating, false, speed * 0.05, now),
            agent("lumen", .claudeCode, "lumen", "claude-haiku-5", .tools, false, 0, now.addingTimeInterval(-9)),
            agent("pulse", .claudeDesktop, "pulse", "claude-sonnet-5-5", .idle, false, 0, now.addingTimeInterval(-95)),
            agent("ember", .codex, "ember", "gpt-6.1-sol-mini", .idle, false, 0, now.addingTimeInterval(-240)),
            agent("cinder", .claudeCode, "cinder", "claude-opus-5-5", .idle, false, 0, now.addingTimeInterval(-420)),
            agent("delta", .deepseek, "delta", "deepseek-v4-pro", .idle, false, 0, now.addingTimeInterval(-480)),
            agent("helix", .codex, "helix", "gpt-6.1-sol", .idle, false, 0, now.addingTimeInterval(-540)),
            agent("prism", .claudeDesktop, "prism", "claude-opus-5-5", .idle, false, 0, now.addingTimeInterval(-600)),
            agent("zenith", .claudeCode, "zenith", "claude-sonnet-5-5", .idle, false, 0, now.addingTimeInterval(-660)),
            agent("cobalt", .codex, "cobalt", "gpt-6.1-sol-mini", .idle, false, 0, now.addingTimeInterval(-720)),
            agent("aurora", .deepseek, "aurora", "deepseek-v4-flash", .idle, false, 0, now.addingTimeInterval(-780)),
            agent("ion", .claudeCode, "ion", "claude-haiku-5", .idle, false, 0, now.addingTimeInterval(-840)),
            agent("tide", .claudeDesktop, "tide", "claude-sonnet-5-5", .idle, false, 0, now.addingTimeInterval(-900)),
        ]
    }

    /// Nested groups, a sub-agent whose parent is not listed, and a group cut by the row limit.
    private static func groupedAgents(speed: Double, now: Date) -> [AgentStatus] {
        [
            agent("orbit", .claudeDesktop, "orbit", "claude-opus-5-5", .generating, false, speed * 0.22, now),
            agent("orbit-explore", .claudeDesktop, "orbit", "claude-sonnet-5-5", .generating, true, speed * 0.16, now, parent: "orbit"),
            agent("orbit-review", .claudeDesktop, "orbit", "claude-haiku-5", .generating, true, speed * 0.06, now, parent: "orbit-explore"),
            agent("orbit-tests", .claudeDesktop, "orbit", "claude-sonnet-5-5", .tools, true, 0, now.addingTimeInterval(-6), parent: "orbit"),
            agent("quasar-worker", .deepseek, "quasar", "deepseek-v4-flash", .generating, true, speed * 0.2, now),
            agent("rocket", .codex, "rocket", "gpt-6.1-sol", .generating, false, speed * 0.12, now),
            agent("rocket-worker", .codex, "rocket", "gpt-6.1-sol-mini", .generating, true, speed * 0.06, now, parent: "rocket"),
            agent("lumen", .claudeCode, "lumen", "claude-haiku-5", .idle, false, 0, now.addingTimeInterval(-220)),
        ]
    }

    private static func agent(
        _ id: String, _ source: SourceKind, _ project: String, _ model: String,
        _ phase: AgentPhase, _ isSubagent: Bool, _ live: Double, _ activity: Date, parent: String? = nil
    ) -> AgentStatus {
        AgentStatus(
            id: "/preview/\(id).jsonl",
            source: source,
            project: project,
            model: model,
            phase: phase,
            isSubagent: isSubagent,
            live: Rate(output: live, total: live * 26),
            recentSpeed: live > 0 ? live : nil,
            lastActivity: activity,
            parentId: parent.map { "/preview/\($0).jsonl" }
        )
    }

    /// DeepSeek not installed and Claude Desktop turned off: the dashboard hides the first and dims the second.
    static func withPartialSources(_ snapshot: SpeedSnapshot) -> SpeedSnapshot {
        var snapshot = snapshot
        snapshot.sources = snapshot.sources.map { source in
            var source = source
            switch source.kind {
            case .deepseek: source.available = false
            case .claudeDesktop: source.enabled = false
            case .codex, .claudeCode, .opencode, .gemini: return source
            }
            source.sessionCount = 0
            source.live = .zero
            source.today = TokenTotals()
            source.activeAgents = 0
            return source
        }
        snapshot.agents = snapshot.agents.filter { $0.source != .deepseek && $0.source != .claudeDesktop }
        snapshot.generatingCount = snapshot.agents.filter { $0.phase == .generating }.count
        snapshot.toolsCount = snapshot.agents.filter { $0.phase == .tools }.count
        return snapshot
    }

    private static func source(_ kind: SourceKind, _ live: Double, _ today: TokenTotals, _ active: Int) -> SourceStatus {
        SourceStatus(
            kind: kind,
            enabled: true,
            rootPath: kind.transcriptFolder?.defaultURL.path ?? "",
            available: true,
            sessionCount: kind.transcriptFolder.map(sessionCount) ?? 0,
            live: Rate(output: live, total: live * 26),
            today: today,
            activeAgents: active
        )
    }

    private static func sessionCount(_ folder: TranscriptFolder) -> Int {
        switch folder {
        case .codex: 128
        case .claude: 342
        case .dsh: 46
        }
    }
}
