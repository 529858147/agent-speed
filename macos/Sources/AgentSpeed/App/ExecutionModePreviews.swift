import AgentSpeedCore
import SwiftUI

@MainActor
enum ExecutionModePreviews {
    static func render(model: AppModel, into directory: URL) {
        let saved = model.settings.language
        defer { model.settings.language = saved; model.l10n.apply(saved); model.report.profile.reset() }
        let snapshot = previewSnapshot()
        PreviewRenderer.publish(model, snapshot: snapshot)
        model.history.publish(state: .ready(updatedAt: Date()), days: previewDays())
        let history = HistoryViewModel(store: model.history, l10n: model.l10n, accessibility: model.accessibility,
                                       cost: model.cost, traffic: model.traffic)
        let storage = model.settings.storage
        for (name, language, _) in ReportPreviews.languages {
            model.settings.language = language; model.l10n.apply(language)
            let checks = ExecutionModeLogicChecks.run(l10n: model.l10n).map { ($0.1 ? "PASS " : "FAIL ") + $0.0 }
            try? (checks.joined(separator: "\n") + "\n").write(to: directory.appendingPathComponent("modes-checks-\(name).txt"), atomically: true, encoding: .utf8)
            for dark in [false, true] {
                let scheme = dark ? "dark" : "light"
                for (suffix, width) in [("", DashboardController.width - 2 * DashboardLayout.padding), ("-narrow", 236)] {
                    PreviewSnapshot.writeHosted(
                        AgentsSection(model: model, rowWidth: width).padding(20).frame(width: width + 40).appEnvironment(model),
                        dark: dark, to: directory.appendingPathComponent("modes-agents\(suffix)-\(name)-\(scheme).png"))
                }
                for kind in [ExecutionModeKind.effort, .speed] {
                    storage.set(kind.rawValue, forKey: "historyModeKind")
                    PreviewSnapshot.writeHosted(
                        EffortHistorySection(model: history).padding(24).frame(width: 760).defaultAppStorage(storage).appEnvironment(model),
                        dark: dark, to: directory.appendingPathComponent("modes-history-\(kind.rawValue)-\(name)-\(scheme).png"))
                }
                model.report.preview(.reporting, username: "Profile Group")
                var document = model.report.discovery!
                document.extensions = .init(profileAvatar: true, sharedIdentity: true)
                model.report.serviceState = .ready(document)
                model.report.profile.snapshot = ReportProfileSnapshot.parse(Data(#"{"sharedUserId":"11111111-1111-4111-8111-111111111111","displayName":"Profile Group","avatar":null,"devices":[{"deviceId":"22222222-2222-4222-8222-222222222222","platform":"macOS","isCurrent":true,"online":true},{"deviceId":"44444444-4444-4444-8444-444444444444","platform":"Windows","isCurrent":false,"online":true}]}"#.utf8))
                PreviewSnapshot.writeHosted(
                    Form { ReportProfileSection(model: model, loadsProfileOnAppearance: false) }.settingsForm().frame(width: 680, height: 400).appEnvironment(model),
                    dark: dark, to: directory.appendingPathComponent("profile-\(name)-\(scheme).png"))
                model.report.profile.pairingIsInvitation = true
                let expires = ReportInstant.format(unixMs: Int64(Date().addingTimeInterval(300).timeIntervalSince1970 * 1000))
                model.report.profile.pairing = ReportPairingSnapshot.parse(Data("""
                    {"id":"33333333-3333-4333-8333-333333333333","state":"pending","expiresAt":"\(expires)",
                     "claimant":{"deviceId":"44444444-4444-4444-8444-444444444444","platform":"Windows"}}
                    """.utf8))
                PreviewSnapshot.writeHosted(ReportPairingSheet(model: model).appEnvironment(model), dark: dark,
                                            to: directory.appendingPathComponent("pairing-\(name)-\(scheme).png"))
            }
        }
    }

    /// Every capsule case: max, ultra with two bolts, ultracode, xhigh with one bolt, bolts alone, unknown, sub-agents.
    private static func previewSnapshot() -> SpeedSnapshot {
        var snapshot = PreviewData.snapshot(speed: 420, redline: 2000, agents: .crowd(0))
        let now = snapshot.timestamp
        func agent(_ id: String, _ source: SourceKind, _ model: String, _ phase: AgentPhase, _ live: Double,
                   parent: String? = nil, effort: String?, tier: String? = nil) -> AgentStatus {
            var agent = AgentStatus(id: "/preview/\(id).jsonl", source: source, project: id, model: model, phase: phase,
                                    isSubagent: parent != nil, live: Rate(output: live, total: live * 26),
                                    recentSpeed: live > 0 ? live : nil, lastActivity: now.addingTimeInterval(live > 0 ? 0 : -90),
                                    parentId: parent.map { "/preview/\($0).jsonl" })
            agent.modes = modes(effort, tier: tier)
            agent.intelligence = PreviewData.intelligence(output: live, score: PreviewData.previewScore(model, source: source),
                                                          id: agent.id, idle: phase == .idle, thinkingShare: 0.3,
                                                          source: source)
            return agent
        }
        snapshot.agents = [
            agent("aurora", .claudeCode, "claude-opus-5-5", .generating, 142, effort: "max"),
            agent("aurora-review", .claudeCode, "claude-sonnet-5-5", .generating, 96, parent: "aurora", effort: "xhigh", tier: "fast"),
            agent("rocket", .codex, "gpt-6.1-sol", .generating, 88, effort: "ultra", tier: "ultrafast"),
            agent("rocket-worker", .codex, "gpt-6.1-sol-mini", .tools, 0, parent: "rocket", effort: "high"),
            agent("nebula", .claudeCode, "claude-opus-5-5", .generating, 61, effort: "ultracode"),
            agent("lumen", .opencode, "kimi-k3", .idle, 0, effort: nil),
        ]
        return snapshot
    }

    private static func modes(_ effort: String?, tier: String?) -> ExecutionModes {
        var facts: [ExecutionModeFact] = []
        if let effort, let fact = ExecutionModeFact(kind: .effort, rawValue: effort, evidence: .recorded, provider: nil, scope: .turn) {
            facts.append(fact)
        }
        if let tier, let fact = ExecutionModeFact(kind: .serviceTier, rawValue: tier, evidence: .served, provider: "openai", scope: .response) {
            facts.append(fact)
        }
        return ExecutionModes(facts)
    }

    private static func previewDays() -> [DailyUsage] {
        let today = DayKey(Date(), calendar: HistoryDates.calendar).description
        let opus = "claude-opus-5-5", sol = "gpt-6.1-sol"
        func usage(_ model: String, _ kind: ExecutionModeKind, _ mode: String, _ requests: Int, _ output: Int, _ seconds: Int64) -> ExecutionModeUsage {
            ExecutionModeUsage(model: model, attribution: .init(kind: kind, mode: mode, evidence: "recorded"), requests: requests,
                               output: output, measuredOutput: output, measuredMs: seconds * 1000)
        }
        return [DailyUsage(
            day: today, source: .claudeCode, totals: .init(requests: 60, output: 96_000), measuredOutput: 96_000, measuredMs: 1_200_000,
            unfinalized: 0, agents: 4, peak: nil,
            models: [.init(model: opus, requests: 40, output: 70_000, measuredOutput: 70_000, measuredMs: 900_000),
                     .init(model: sol, requests: 20, output: 26_000, measuredOutput: 26_000, measuredMs: 300_000)],
            modeUsage: [
                usage(opus, .effort, "ultracode", 6, 14_000, 160), usage(opus, .effort, "max", 14, 26_000, 330),
                usage(opus, .effort, "xhigh", 12, 18_000, 240), usage(sol, .effort, "ultra", 4, 7_000, 70),
                usage(sol, .effort, "high", 10, 12_000, 150), usage(sol, .effort, "low", 4, 3_000, 40),
                usage(opus, .speed, "fast", 8, 12_000, 90), usage(sol, .speed, "ultrafast", 5, 8_000, 40),
                usage(sol, .speed, "standard", 12, 16_000, 220), usage(opus, .speed, "standard", 20, 30_000, 420),
            ]
        )]
    }
}
