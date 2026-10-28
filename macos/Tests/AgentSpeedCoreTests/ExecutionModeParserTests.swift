import Foundation
import Testing
@testable import AgentSpeedCore

@Suite("Native execution modes")
struct ExecutionModeParserTests {
    @Test func claudeUpsertsUpgradeEvidenceWithoutChangingRequestIdentity() throws {
        var deduper = Deduper()
        let user = ParserBehaviorTests.claudeUser("2026-10-24T10:00:00Z", content: #""go""#)
        let first = ParserBehaviorTests.claudeAssistant("2026-10-24T10:00:01Z", id: "m1", output: 10)
        let last = ParserBehaviorTests.claudeAssistant("2026-10-24T10:00:02Z", id: "m1", output: 40, stop: #""end_turn""#)
            .replacingOccurrences(of: #""output_tokens":40"#, with: #""output_tokens":40,"speed":"standard""#)
        let (parser, records) = ParserBehaviorTests.run(.claude, [user, first, last], deduper: &deduper)
        #expect(records.count == 2)
        #expect(records.first?.record.modes.facts.isEmpty == true)
        #expect(records.last?.record.modes.attribution(for: .serviceTier).mode == "standard")
        #expect(records.map(\.isCompletion) == [true, false])
        #expect(records.first?.record.key == records.last?.record.key)
        #expect(parser.state.modes.attribution(for: .serviceTier).evidence == "served")
        #expect(records.last?.record.usage.output == 40)
    }

    @Test func kimiExactModelSelectionClearsWithoutChangingTokenMath() {
        let selected = #"{"type":"llm.request","time":"2026-10-24T10:00:00Z","model":"k3.1-pro-highspeed"}"#
        let first = #"{"type":"context.append_message","time":"2026-10-24T10:00:01Z","message":{"message":{"id":"one","role":"assistant","usage":{"inputOther":2,"output":20}}}}"#
        let clear = #"{"type":"llm.request","time":"2026-10-24T10:00:02Z","model":null}"#
        let second = first.replacingOccurrences(of: "one", with: "two").replacingOccurrences(of: "10:00:01", with: "10:00:03")
        var deduper = Deduper()
        let (_, records) = ParserBehaviorTests.run(.kimi, [selected, first, clear, second], deduper: &deduper)
        #expect(records.count == 2)
        #expect(records.first?.record.modes.attribution(for: .modelVariant).mode == "highspeed")
        #expect(records.last?.record.modes.facts.isEmpty == true)
        #expect(records.map { $0.record.usage.output } == [20, 20])
    }

    @Test func opencodeUsesVerifiedProviderIDsAndResponseNativeField() throws {
        func parse(_ provider: String, tier: String = "priority") throws -> OpencodeMessageData {
            let json = #"{"model":{"id":"m","providerID":"\#(provider)"},"native":{"serviceTier":"\#(tier)"},"tokens":{"input":1,"output":12}}"#
            return try #require(Data(json.utf8).withUnsafeBytes(OpencodeDataExtractor.extract))
        }
        for provider in ["openai", "azure"] {
            let row = try parse(provider)
            let result = row.modes.attribution(for: .serviceTier)
            #expect(result.mode == "fast" && result.evidence == "served" && result.provider == "openai")
            #expect(row.tokens?.output == 12)
        }
        for provider in ["azure-openai", "openai-codex", "anthropic", "openai-compatible", "my-router"] {
            #expect(try parse(provider).modes.facts.isEmpty)
        }
        #expect(try parse("azure", tier: "warp-2").modes.attribution(for: .serviceTier).mode == "warp-2")
    }

    @Test func codexExplicitClearAndParallelSessions() throws {
        let context = #"{"type":"turn_context","timestamp":"2026-10-24T10:00:00Z","payload":{"model":"m","service_tier":"priority"}}"#
        let usage = #"{"type":"token_usage_record","timestamp":"2026-10-24T10:00:01Z","payload":{"response_id":"r1","usage":{"input_tokens":3,"output_tokens":40,"total_tokens":43}}}"#
        let clear = #"{"type":"turn_context","timestamp":"2026-10-24T10:00:02Z","payload":{"model":"m","service_tier":null}}"#
        let second = usage.replacingOccurrences(of: "r1", with: "r2").replacingOccurrences(of: "10:00:01", with: "10:00:03")
        var deduper = Deduper()
        let (_, records) = ParserBehaviorTests.run(.codex, [context, usage, clear, second], deduper: &deduper)
        #expect(records.count == 2)
        #expect(records.first?.record.modes.attribution(for: .serviceTier).mode == "fast")
        #expect(records.first?.record.modes.attribution(for: .serviceTier).evidence == "selected")
        #expect(records.last?.record.modes.facts.isEmpty == true)
        var otherDeduper = Deduper()
        let (_, other) = ParserBehaviorTests.run(.codex, [second], deduper: &otherDeduper, fileID: 2)
        #expect(other.first?.record.modes.facts.isEmpty == true)
    }

    @Test func codexAppliedSessionSettingsPreserveTimingAndEarlierResponses() {
        let meta = #"{"type":"session_meta","timestamp":"2026-10-28T10:00:00Z","payload":{"id":"one","model_provider":"openai"}}"#
        func settings(_ second: String, _ tier: String, thread: String = "one") -> String {
            #"{"type":"event_msg","timestamp":"2026-10-28T10:00:\#(second)Z","payload":{"type":"thread_settings_applied","thread_id":"\#(thread)","thread_settings":{"service_tier":\#(tier),"model_provider_id":"openai"}}}"#
        }
        func usage(_ second: String, _ id: String) -> String {
            #"{"type":"token_usage_record","timestamp":"2026-10-28T10:00:\#(second)Z","payload":{"response_id":"\#(id)","usage":{"input_tokens":3,"output_tokens":40}}}"#
        }
        let start = #"{"type":"event_msg","timestamp":"2026-10-28T10:00:01Z","payload":{"type":"task_started"}}"#
        var deduper = Deduper()
        let (parser, records) = ParserBehaviorTests.run(.codex, [meta, settings("00", #""priority""#),
            start, usage("03", "r1"), settings("04", #""default""#), usage("06", "r2"),
            settings("07", "null"), usage("09", "r3")], deduper: &deduper)
        #expect(records.map { $0.record.modes.attribution(for: .serviceTier).mode } == ["fast", "standard", "unknown"])
        #expect(records.first?.record.modes.facts.first?.scope == .session)
        #expect(records.first?.record.modes.facts.first?.evidence == .selected)
        #expect(records.first?.record.startMs == 1_793_181_601_000)
        #expect(records.allSatisfy { $0.record.usage.output == 40 })
        #expect(parser.state.modes.facts.isEmpty)

        var childDeduper = Deduper()
        let (other, _) = ParserBehaviorTests.run(.codex, [meta, settings("00", #""priority""#, thread: "parent")], deduper: &childDeduper)
        #expect(other.state.modes.facts.isEmpty)
    }

    @Test func codexAppliedSettingsRequireExplicitTierAndVerifiedProvider() {
        let selected = #"{"type":"event_msg","timestamp":"2026-10-28T10:00:00Z","payload":{"type":"thread_settings_applied","thread_settings":{"service_tier":"priority","model_provider_id":"openai"}}}"#
        let missing = #"{"type":"event_msg","timestamp":"2026-10-28T10:00:01Z","payload":{"type":"thread_settings_applied","thread_settings":{"model_provider_id":"openai"}}}"#
        var deduper = Deduper()
        let (parser, _) = ParserBehaviorTests.run(.codex, [selected, missing], deduper: &deduper)
        #expect(parser.state.modes.attribution(for: .serviceTier).mode == "fast")
        for value in ["null", "true", "42", "[]", "{}", #""bad mode""#] {
            var invalidDeduper = Deduper()
            let invalid = selected.replacingOccurrences(of: #""priority""#, with: value)
            let (invalidParser, _) = ParserBehaviorTests.run(.codex, [selected, invalid], deduper: &invalidDeduper)
            #expect(invalidParser.state.modes.facts.isEmpty)
        }
        var customDeduper = Deduper()
        let (custom, _) = ParserBehaviorTests.run(.codex, [selected.replacingOccurrences(of: "openai", with: "my-router")], deduper: &customDeduper)
        #expect(custom.state.modes.facts.isEmpty)
    }

    @Test func ompSelectionsFollowJournalBranchesAndDoNotInheritRecordedEchoes() throws {
        let header = #"{"type":"session","id":"session","timestamp":"2026-10-24T10:00:00Z"}"#
        func change(_ id: String, parent: String?, tier: String) -> String {
            #"{"type":"service_tier_change","id":"\#(id)","parentId":\#(parent.map { "\"" + $0 + "\"" } ?? "null"),"timestamp":"2026-10-24T10:00:01Z","serviceTier":{"openai":"\#(tier)"}}"#
        }
        func call(_ id: String, parent: String, tier: String?) -> String {
            let field = tier.map { ",\"serviceTier\":\"" + $0 + "\"" } ?? ""
            return #"{"type":"message","id":"\#(id)","parentId":"\#(parent)","timestamp":"2026-10-24T10:00:02Z","message":{"role":"assistant","model":"m","provider":"openai","api":"responses","timestamp":1792836001000,"duration":1000,"stopReason":"stop","usage":{"input":1,"output":10}\#(field)}}"#
        }
        var deduper = Deduper()
        let (_, records) = ParserBehaviorTests.run(.omp, [header,
            change("base", parent: nil, tier: "priority"), call("fast", parent: "base", tier: "default"),
            call("next", parent: "fast", tier: nil), change("fork", parent: "base", tier: "ultrafast"),
            call("ultra", parent: "fork", tier: nil), call("branch", parent: "base", tier: nil),
        ], deduper: &deduper)
        #expect(records.count == 4)
        #expect(records.map { $0.record.modes.attribution(for: .serviceTier).mode } == ["standard", "fast", "ultrafast", "fast"])
        #expect(records.map { $0.record.modes.attribution(for: .serviceTier).evidence } == ["recorded", "selected", "selected", "selected"])
        #expect(records.allSatisfy { $0.record.modes.facts.allSatisfy { $0.evidence != .served } })
        var childDeduper = Deduper()
        let (_, child) = ParserBehaviorTests.run(.omp, [header, call("child", parent: "base", tier: nil)], deduper: &childDeduper)
        #expect(child.first?.record.modes.facts.isEmpty == true)
    }
}
