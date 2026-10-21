import Foundation
import Testing
@testable import AgentSpeedCore

@MainActor
@Suite("Live monitor", .serialized)
struct LiveMonitorTests {
    static func waitUntil(timeout: TimeInterval = 10, _ condition: () -> Bool) async -> Bool {
        let deadline = Date().addingTimeInterval(timeout)
        while Date() < deadline {
            if condition() { return true }
            try? await Task.sleep(for: .milliseconds(50))
        }
        return condition()
    }

    @Test func reportsNoSourcesWhenRootsAreMissing() async throws {
        let directory = try TempDirectory()
        let store = SpeedStore()
        let monitor = LiveMonitor(store: store, configuration: MonitorConfiguration(
            codexRoot: directory.file("missing-codex"),
            claudeRoot: directory.file("missing-claude"),
            dshRoot: directory.file("missing-dsh"),
            opencodeRoot: directory.file("missing-opencode"),
            geminiRoot: directory.file("missing-gemini")
        ))
        monitor.start()
        defer { monitor.stop() }
        #expect(await Self.waitUntil { store.snapshot.state == .noSources })
        #expect(store.snapshot.sources.map(\.kind) == SourceKind.allCases)
        #expect(store.snapshot.sources.allSatisfy { !$0.available && $0.enabled })
    }

    @Test func seedsThenStreamsLiveCompletions() async throws {
        let (directory, configuration) = try SessionTests.makeFixtureRoots()
        let store = SpeedStore()
        var completions: [ResponseSummary] = []
        store.addEventListener { event in
            if case .responseCompleted(let summary) = event { completions.append(summary) }
        }
        let monitor = LiveMonitor(store: store, configuration: configuration)
        monitor.start()
        defer { monitor.stop() }

        #expect(await Self.waitUntil { store.snapshot.state == .running })
        let snapshot = store.snapshot
        #expect(snapshot.sources.count == SourceKind.allCases.count)
        #expect(snapshot.sources.first { $0.kind == .codex }?.available == true)
        #expect(snapshot.sources.first { $0.kind == .deepseek }?.available == true)
        #expect(snapshot.warnings.contains(.malformedLines(2)))
        #expect(snapshot.history.count == 300)
        #expect(completions.isEmpty, "seeded responses never produce live events")

        let now = Date()
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        let relative = "claude/projects/-Users-dev-projects-orbit/s-desk.jsonl"
        try directory.append(relative, [
            #"{"type":"user","entrypoint":"claude-desktop","cwd":"/w/orbit","message":{"role":"user","content":"go"},"timestamp":"\#(formatter.string(from: now.addingTimeInterval(-2)))"}"#,
            #"{"type":"assistant","entrypoint":"claude-desktop","cwd":"/w/orbit","message":{"id":"msg_live","model":"m","stop_reason":"end_turn","usage":{"input_tokens":5,"output_tokens":200}},"timestamp":"\#(formatter.string(from: now))"}"#,
        ].joined(separator: "\n") + "\n")

        #expect(await Self.waitUntil { !completions.isEmpty })
        #expect(completions.first?.source == .claudeDesktop)
        #expect(completions.first?.outputTokens == 200)
        #expect(await Self.waitUntil { store.snapshot.live.output > 0 })
        #expect(store.snapshot.agents.contains { $0.id.hasSuffix("s-desk.jsonl") })
    }

    @Test func streamsOpencodeCompletionsFromTheDatabase() async throws {
        let db = try OpencodeTests.LiveDatabase()
        let now = Clock.milliseconds(Date())
        try db.addSession("ses_live", at: now - 60_000)
        let store = SpeedStore()
        var completions: [ResponseSummary] = []
        store.addEventListener { event in
            if case .responseCompleted(let summary) = event { completions.append(summary) }
        }
        let monitor = LiveMonitor(store: store, configuration: MonitorConfiguration(
            enabledSources: [.opencode], opencodeRoot: db.directory.url))
        monitor.start()
        defer { monitor.stop() }
        #expect(await Self.waitUntil { store.snapshot.state == .running })
        #expect(store.snapshot.sources.first { $0.kind == .opencode }?.available == true)

        try db.upsertRow("a_live", session: "ses_live", seq: 1, type: "assistant", created: now - 2000, updated: now,
                         data: OpencodeTests.LiveDatabase.assistant(created: now - 2000, streamed: now - 10, output: 300))
        #expect(await Self.waitUntil { !completions.isEmpty })
        #expect(completions.map(\.source) == [.opencode])
        #expect(completions.first?.outputTokens == 300)
        #expect(completions.first?.project == "atlas")
        #expect(await Self.waitUntil { store.snapshot.agents.contains { $0.id == "opencode:ses_live" } })
        #expect(store.snapshot.sources.first { $0.kind == .opencode }?.sessionCount == 1)
    }

    @Test func restartsOnlyWhenRootsOrSourcesChange() throws {
        let store = SpeedStore()
        let monitor = LiveMonitor(store: store)
        var configuration = monitor.configuration
        configuration.codexRoot = LiveMonitor.defaultCodexRoot
        #expect(!configuration.requiresRescan(comparedTo: monitor.configuration))
        configuration.dshRoot = LiveMonitor.defaultDshRoot
        #expect(!configuration.requiresRescan(comparedTo: monitor.configuration))
        configuration.dshRoot = URL(fileURLWithPath: "/elsewhere/sessions", isDirectory: true)
        #expect(configuration.requiresRescan(comparedTo: monitor.configuration))
        configuration.dshRoot = nil
        configuration.enabledSources = [.codex]
        #expect(configuration.requiresRescan(comparedTo: monitor.configuration))
    }
}
