import Foundation
import Testing
@testable import AgentSpeedCore

@Suite("Engine fixture cases")
struct EngineCaseTests {
    static let tolerance = 1e-6

    static func cases() throws -> [[String: Any]] {
        try #require(Fixtures.json("engine-cases.json")["cases"] as? [[String: Any]])
    }

    static var caseNames: [String] {
        (try? cases().compactMap { $0["name"] as? String }) ?? []
    }

    @Test(arguments: caseNames)
    func engineCase(_ name: String) throws {
        let spec = try #require(try Self.cases().first { $0["name"] as? String == name })
        let todayStartMs = spec.int64("todayStartMs") ?? 0
        var engine = SpeedEngine()
        for agent in try #require(spec["agents"] as? [[String: Any]]) {
            engine.setAgent(EngineAgent(
                id: try #require(agent.string("id")),
                source: try #require(SourceKind(rawValue: agent.string("source") ?? "")),
                project: "fixture",
                model: agent.string("model"),
                phase: try #require(AgentPhase(rawValue: agent.string("phase") ?? "")),
                inFlightStartMs: agent.int64("inFlightStartMs"),
                lastLineMs: agent.int64("lastLineMs")
            ))
        }
        for response in try #require(spec["responses"] as? [[String: Any]]) {
            let record = ResponseRecord(
                key: try #require(response.string("key")),
                source: try #require(SourceKind(rawValue: response.string("source") ?? "")),
                agentId: try #require(response.string("agentId")),
                model: response.string("model"),
                startMs: response.int64("startMs"),
                endMs: try #require(response.int64("endMs")),
                usage: TokenUsage(input: response.int("input"), output: response.int("output")),
                isFinal: response["final"] as? Bool ?? true
            )
            engine.upsert(record, project: "fixture", isCompletion: true, emitsEvents: false)
        }
        for query in try #require(spec["queries"] as? [[String: Any]]) {
            let now = try #require(query.int64("nowMs"))
            let output = engine.advance(nowMs: now, todayStartMs: todayStartMs, emitsEvents: false)
            let label = "\(name) @ \(now)"
            Self.expectClose(output.live.output, query.double("liveOutput"), "\(label) liveOutput")
            Self.expectClose(output.live.total, query.double("liveTotal"), "\(label) liveTotal")
            Self.expectClose(output.liveEstimatedShare, query.double("estimatedShare"), "\(label) estimatedShare")
            if let requests = query["todayRequests"] as? NSNumber {
                #expect(output.today.requests == requests.intValue, "\(label) todayRequests")
            }
            if let value = query["todayOutput"] as? NSNumber {
                #expect(output.today.output == value.intValue, "\(label) todayOutput")
            }
            if let value = query["todayInput"] as? NSNumber {
                #expect(output.today.input == value.intValue, "\(label) todayInput")
            }
            if let value = query["todayUnfinalized"] as? NSNumber {
                #expect(output.unfinalizedToday == value.intValue, "\(label) todayUnfinalized")
            }
            if query.keys.contains("averageToday") {
                Self.expectClose(output.averageToday, query.double("averageToday"), "\(label) averageToday")
            }
            if let peak = query.double("peakTodayOutput") {
                Self.expectClose(output.peakToday.output, peak, "\(label) peakTodayOutput")
            }
            if let buckets = query["historyMeasuredOutput"] as? [String: NSNumber] {
                #expect(output.history.count == SpeedEngine.historySeconds)
                for (startText, value) in buckets {
                    let startMs = try #require(Int64(startText))
                    let point = output.history.first { Int64(($0.time.timeIntervalSince1970 * 1000).rounded()) == startMs }
                    Self.expectClose(point?.measured.output, value.doubleValue, "\(label) bucket \(startText)")
                }
            }
        }
    }

    static func expectClose(
        _ actual: Double?,
        _ expected: Double?,
        _ label: String,
        sourceLocation: SourceLocation = #_sourceLocation
    ) {
        switch (actual, expected) {
        case (nil, nil):
            return
        case let (actual?, expected?):
            #expect(abs(actual - expected) <= tolerance, "\(label): \(actual) != \(expected)", sourceLocation: sourceLocation)
        default:
            Issue.record("\(label): \(String(describing: actual)) != \(String(describing: expected))", sourceLocation: sourceLocation)
        }
    }
}
