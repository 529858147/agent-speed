import AgentSpeedCore
import Foundation

/// Deterministic sample data for `--render-previews`.
enum PreviewData {
    static func snapshot(speed: Double, redline: Double, grouped: Bool = false, now: Date = Date()) -> SpeedSnapshot {
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
        let agents = grouped ? groupedAgents(speed: speed, now: now) : [
            agent("rocket", .codex, "rocket", "gpt-6.1-sol", .generating, false, speed * 0.38, now),
            agent("nova", .claudeCode, "nova", "claude-opus-5-5", .generating, false, speed * 0.31, now),
            agent("nova-sub", .claudeCode, "nova", "claude-sonnet-5-5", .generating, true, speed * 0.18, now, parent: "nova"),
            agent("orbit", .claudeDesktop, "orbit", "claude-fable-5-1", .generating, false, speed * 0.13, now),
            agent("atlas", .codex, "atlas-web", "gpt-6.1-sol-mini", .tools, false, 0, now.addingTimeInterval(-14)),
            agent("lumen", .claudeCode, "lumen", "claude-haiku-5", .idle, false, 0, now.addingTimeInterval(-220)),
        ]
        let today = TokenTotals(requests: 1_284, input: 48_200_000, cachedInput: 41_000_000, cacheWriteInput: 2_100_000, output: 1_240_000, reasoning: 380_000)
        let sources: [SourceStatus] = [
            source(.codex, speed * 0.38, TokenTotals(requests: 610, input: 22_000_000, output: 620_000), 2),
            source(.claudeCode, speed * 0.49, TokenTotals(requests: 540, input: 21_000_000, output: 480_000), 3),
            source(.claudeDesktop, speed * 0.13, TokenTotals(requests: 134, input: 5_200_000, output: 140_000), 1),
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

    /// Nested groups, a sub-agent whose parent is not listed, and a group cut by the row limit.
    private static func groupedAgents(speed: Double, now: Date) -> [AgentStatus] {
        [
            agent("orbit", .claudeDesktop, "orbit", "claude-opus-5-5", .generating, false, speed * 0.22, now),
            agent("orbit-explore", .claudeDesktop, "orbit", "claude-sonnet-5-5", .generating, true, speed * 0.16, now, parent: "orbit"),
            agent("orbit-review", .claudeDesktop, "orbit", "claude-haiku-5", .generating, true, speed * 0.06, now, parent: "orbit-explore"),
            agent("orbit-tests", .claudeDesktop, "orbit", "claude-sonnet-5-5", .tools, true, 0, now.addingTimeInterval(-6), parent: "orbit"),
            agent("atlas-worker", .codex, "atlas-web", "gpt-6.1-sol-mini", .generating, true, speed * 0.2, now),
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

    private static func source(_ kind: SourceKind, _ live: Double, _ today: TokenTotals, _ active: Int) -> SourceStatus {
        SourceStatus(
            kind: kind,
            enabled: true,
            rootPath: kind == .codex ? LiveMonitor.defaultCodexRoot.path : LiveMonitor.defaultClaudeRoot.path,
            available: true,
            sessionCount: kind == .codex ? 128 : 342,
            live: Rate(output: live, total: live * 26),
            today: today,
            activeAgents: active
        )
    }
}
