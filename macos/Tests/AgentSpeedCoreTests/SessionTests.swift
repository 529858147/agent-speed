import Foundation
import Testing
@testable import AgentSpeedCore

@Suite("Transcript session")
struct SessionTests {
    /// Copies the shared fixtures into a scratch root with modification times that follow the fixture order.
    static func makeFixtureRoots() throws -> (TempDirectory, MonitorConfiguration) {
        let directory = try TempDirectory()
        let order = try #require(try Fixtures.json("parser-expected.json")["order"] as? [String])
        let start = Date().addingTimeInterval(-60)
        for (index, relative) in order.enumerated() {
            let text = try String(contentsOf: Fixtures.root.appendingPathComponent(relative), encoding: .utf8)
            try directory.write(relative, text, modified: start.addingTimeInterval(TimeInterval(index)))
        }
        let configuration = MonitorConfiguration(
            codexRoot: directory.file("codex/sessions"),
            claudeRoot: directory.file("claude/projects")
        )
        return (directory, configuration)
    }

    static func startOfToday() -> Int64 { Clock.milliseconds(Calendar.current.startOfDay(for: Date())) }

    @Test func seedsFixturesWithoutLiveEvents() throws {
        let (directory, configuration) = try Self.makeFixtureRoots()
        let session = TranscriptSession(configuration: configuration)
        let summary = session.seed(todayStartMs: Self.startOfToday())
        #expect(summary.filesRead == 6)
        #expect(summary.filesDiscovered == 6)
        #expect(Set(session.responses.map(\.key)) == [
            "resp-1", "resp-2", "resp-3", "resp-4", "codex-legacy:thread-legacy:20213", "codex-legacy:thread-legacy:41000",
            "msg_A", "msg_B", "msg_C", "msg_D", "msg_E",
        ])
        #expect(session.responses.first { $0.key == "msg_B" }?.agentId.hasSuffix("s-cli.jsonl") == true)
        #expect(session.malformedLines == 1)
        #expect(session.responses.first { $0.key == "msg_E" }?.isFinal == false)
        let (output, events) = session.advance(nowMs: Clock.milliseconds(Date()), todayStartMs: Self.startOfToday(), emitsEvents: true)
        #expect(events.isEmpty)
        let context = SnapshotContext(session: session, todayStartMs: Self.startOfToday())
        #expect(context.warnings(for: output) == [.malformedLines(1), .unfinalizedUsage(1)])
        _ = directory
    }

    @Test func disabledSourcesAreExcluded() throws {
        let (directory, base) = try Self.makeFixtureRoots()
        var configuration = base
        configuration.enabledSources = [.claudeDesktop]
        let session = TranscriptSession(configuration: configuration)
        _ = session.seed(todayStartMs: Self.startOfToday())
        #expect(session.responses.map(\.key) == ["msg_C"])
        _ = directory
    }

    @Test func liveAppendsProduceCompletionsAndTruncationDoesNotDoubleCount() throws {
        let (directory, configuration) = try Self.makeFixtureRoots()
        let session = TranscriptSession(configuration: configuration)
        _ = session.seed(todayStartMs: Self.startOfToday())
        let relative = "codex/sessions/2026/10/20/rollout-2026-10-20T10-00-00-modern.jsonl"
        try directory.append(relative, #"{"timestamp":"2026-10-20T10:00:20.000Z","type":"event_msg","payload":{"type":"user_message","message":"x"}}"# + "\n")
        try directory.append(relative, #"{"timestamp":"2026-10-20T10:00:24.000Z","type":"token_usage_record","payload":{"response_id":"resp-live","usage":{"input_tokens":100,"output_tokens":80}}}"# + "\n")

        let canonical = try #require(realpath(directory.file(relative).path, nil))
        let canonicalPath = String(cString: canonical)
        free(canonical)
        #expect(session.handleChanges(atCanonicalPaths: [canonicalPath]))
        let now = Clock.milliseconds(Date())
        let (_, events) = session.advance(nowMs: now, todayStartMs: Self.startOfToday(), emitsEvents: true)
        #expect(events.count == 1)
        if case .responseCompleted(let summary)? = events.first {
            #expect(summary.outputTokens == 80)
            #expect(summary.duration == 4)
            #expect(summary.project == "rocket")
        } else {
            Issue.record("expected a completion")
        }
        let requestsBefore = session.responses.count

        // Rewrite the file shorter: the parser restarts, already-counted keys are not counted again.
        let original = try String(contentsOf: Fixtures.root.appendingPathComponent(relative), encoding: .utf8)
        let firstLines = original.split(separator: "\n").prefix(8).joined(separator: "\n") + "\n"
        try Data(firstLines.utf8).write(to: directory.file(relative))
        #expect(session.handleChanges(atCanonicalPaths: [canonicalPath]))
        let (_, afterReset) = session.advance(nowMs: now + 10, todayStartMs: Self.startOfToday(), emitsEvents: true)
        #expect(afterReset.isEmpty)
        #expect(session.responses.count == requestsBefore)
    }

    @Test func newFilesAreDiscoveredAndReadFromTheStart() throws {
        let (directory, configuration) = try Self.makeFixtureRoots()
        let session = TranscriptSession(configuration: configuration)
        _ = session.seed(todayStartMs: Self.startOfToday())
        try directory.write("claude/projects/-w-new/s-new.jsonl", [
            #"{"type":"user","entrypoint":"cli","cwd":"/w/new","message":{"role":"user","content":"hi"},"timestamp":"2026-10-20T10:05:00.000Z"}"#,
            #"{"type":"assistant","entrypoint":"cli","cwd":"/w/new","message":{"id":"msg_new","model":"m","stop_reason":"end_turn","usage":{"input_tokens":3,"output_tokens":30}},"timestamp":"2026-10-20T10:05:03.000Z"}"#,
        ].joined(separator: "\n") + "\n")
        #expect(session.rediscover(todayStartMs: Self.startOfToday()))
        let record = session.responses.first { $0.key == "msg_new" }
        #expect(record?.startMs == Timestamp.parse("2026-10-20T10:05:00.000Z"))
        #expect(record?.source == .claudeCode)
    }

    @Test func oldFilesStartAtTheirEnd() throws {
        let directory = try TempDirectory()
        let relative = "2026/10/01/rollout-old.jsonl"
        try directory.write(relative, #"{"timestamp":"2026-10-01T10:00:00Z","type":"token_usage_record","payload":{"response_id":"old","usage":{"output_tokens":5}}}"# + "\n",
                            modified: Date().addingTimeInterval(-3 * 86_400))
        let session = TranscriptSession(configuration: MonitorConfiguration(enabledSources: [.codex], codexRoot: directory.url))
        let summary = session.seed(todayStartMs: Self.startOfToday())
        #expect(summary.filesRead == 0)
        #expect(session.responses.isEmpty)
        try directory.append(relative, #"{"timestamp":"2026-10-01T10:00:09Z","type":"token_usage_record","payload":{"response_id":"new","usage":{"output_tokens":5}}}"# + "\n")
        // Neither hot nor active today, so only a watcher event picks the change up.
        #expect(!session.pollRecentFiles(nowMs: Clock.milliseconds(Date())))
        #expect(!session.rediscover(todayStartMs: Self.startOfToday()))
        let canonical = try #require(realpath(directory.file(relative).path, nil))
        defer { free(canonical) }
        #expect(session.handleChanges(atCanonicalPaths: [String(cString: canonical)]))
        #expect(session.responses.map(\.key) == ["new"])
    }

    @Test func largeFilesAreSeededFromTheirTailWithAWarning() throws {
        let directory = try TempDirectory()
        var lines: [String] = []
        for index in 0..<40 {
            let second = String(format: "%02d", index)
            lines.append(#"{"timestamp":"2026-10-20T10:00:\#(second).000Z","type":"token_usage_record","payload":{"response_id":"r\#(index)","usage":{"output_tokens":1}}}"#)
        }
        let text = lines.joined(separator: "\n") + "\n"
        try directory.write("2026/10/20/rollout-big.jsonl", text)
        let lineBytes = lines[39].utf8.count + 1
        let limits = TranscriptSession.ReadingLimits(largeFileBytes: Int64(text.utf8.count - 1), tailWindowBytes: Int64(lineBytes * 5 + 3))
        let session = TranscriptSession(configuration: MonitorConfiguration(enabledSources: [.codex], codexRoot: directory.url), limits: limits)
        let summary = session.seed(todayStartMs: Self.startOfToday())
        #expect(summary.partialHistory)
        #expect(Set(session.responses.map(\.key)) == ["r35", "r36", "r37", "r38", "r39"])
        #expect(SnapshotContext(session: session, todayStartMs: Self.startOfToday()).warnings == [.partialHistory])
    }
}
