import Foundation
import Testing
@testable import AgentSpeedCore

@Suite("Compressed Codex sessions")
struct CompressedStreamReaderTests {
    static let name = "rollout-2026-10-20T10-01-20-x.jsonl.zst"

    static func read(_ reader: inout CompressedStreamReader, into collector: ZstdFrameTailerTests.Collector) -> FileTailer.ReadResult {
        reader.read(onReset: { collector.resets += 1 }, onMalformed: { collector.malformed += 1 }) { bytes in
            collector.lines.append(String(decoding: bytes, as: UTF8.self))
        }
    }

    @Test func decodesEveryFrameAndSkipsSkippableFrames() throws {
        let directory = try TempDirectory()
        let url = try directory.write(Self.name, bytes: ZstdFrames.raw("a\nb") + ZstdFrames.skippable([1, 2, 3]) + ZstdFrames.raw("c\nd\npartial"))
        var reader = CompressedStreamReader(path: url.path)
        let collector = ZstdFrameTailerTests.Collector()
        let result = Self.read(&reader, into: collector)
        #expect(collector.lines == ["a", "bc", "d"], "a line split across frames is joined; the unterminated tail is dropped")
        #expect(collector.malformed == 0)
        #expect(result.bytesRead == Int(try FileManager.default.attributesOfItem(atPath: url.path)[.size] as? Int ?? -1))
        #expect(Self.read(&reader, into: collector).bytesRead == 0, "an unchanged file is not read again")
    }

    @Test func framesHaveNoOutputLimit() throws {
        let directory = try TempDirectory()
        let line = String(repeating: "x", count: 1023) + "\n"
        let count = (ZstdFrameTailer.maxFrameOutputBytes / line.utf8.count) + 512
        let url = try directory.write(Self.name, bytes: ZstdFrames.raw(String(repeating: line, count: count)))
        var reader = CompressedStreamReader(path: url.path)
        var lines = 0
        var malformed = 0
        _ = reader.read(onReset: {}, onMalformed: { malformed += 1 }) { bytes in
            if bytes.count == 1023 { lines += 1 }
        }
        #expect(lines == count)
        #expect(malformed == 0)
    }

    @Test func aDecodingErrorStopsTheFileWithOneMalformedLine() throws {
        let directory = try TempDirectory()
        let url = try directory.write(Self.name, bytes: ZstdFrames.raw("x\ny\n") + ZstdFrames.corruptBlock() + ZstdFrames.raw("z\n"))
        var reader = CompressedStreamReader(path: url.path)
        let collector = ZstdFrameTailerTests.Collector()
        _ = Self.read(&reader, into: collector)
        #expect(collector.lines == ["x", "y"])
        #expect(collector.malformed == 1)
    }

    @Test func aTruncatedStreamIsOneMalformedLine() throws {
        let directory = try TempDirectory()
        let url = try directory.write(Self.name, bytes: ZstdFrames.raw("one\ntwo\n") + Array(ZstdFrames.raw("three\n").dropLast(3)))
        var reader = CompressedStreamReader(path: url.path)
        let collector = ZstdFrameTailerTests.Collector()
        _ = Self.read(&reader, into: collector)
        #expect(collector.lines == ["one", "two"])
        #expect(collector.malformed == 1)
    }

    @Test func aChangedFileIsReadAgainFromTheStart() throws {
        let directory = try TempDirectory()
        let url = try directory.write(Self.name, bytes: ZstdFrames.raw("first\n"))
        var reader = CompressedStreamReader(path: url.path)
        let collector = ZstdFrameTailerTests.Collector()
        _ = Self.read(&reader, into: collector)
        try directory.write(Self.name, bytes: ZstdFrames.raw("first\nsecond\n"))
        let result = Self.read(&reader, into: collector)
        #expect(result.didReset)
        #expect(collector.resets == 1)
        #expect(collector.lines == ["first", "first", "second"])
    }

    @Test func aFileNotReadAtStartupWaitsForAChange() throws {
        let directory = try TempDirectory()
        let url = try directory.write(Self.name, bytes: ZstdFrames.raw("old\n"))
        var reader = CompressedStreamReader(path: url.path, start: .end(4))
        let collector = ZstdFrameTailerTests.Collector()
        _ = Self.read(&reader, into: collector)
        #expect(collector.lines.isEmpty)
        try directory.write(Self.name, bytes: ZstdFrames.raw("old\nnew\n"))
        _ = Self.read(&reader, into: collector)
        #expect(collector.lines == ["old", "new"])
    }

    @Test func compressionWhileTrackedCountsNothingTwice() throws {
        let (directory, configuration) = try SessionTests.makeFixtureRoots()
        let session = TranscriptSession(configuration: configuration, clock: { 1_792_490_500_000 })
        let fixtureDay = Clock.milliseconds(Calendar.current.startOfDay(for: Date(timeIntervalSince1970: 1_792_490_400)))
        _ = session.seed(todayStartMs: fixtureDay)
        let before = session.responses.count
        // Codex compresses a finished rollout: the `.jsonl.zst` copy owns nothing new.
        let relative = "codex/sessions/2026/10/20/rollout-2026-10-20T10-00-00-modern.jsonl"
        let text = try String(contentsOf: directory.file(relative), encoding: .utf8)
        try directory.write(relative + ".zst", bytes: ZstdFrames.raw(text))
        try FileManager.default.removeItem(at: directory.file(relative))
        #expect(session.rediscover(todayStartMs: fixtureDay))
        #expect(session.responses.count == before)
        let (_, events) = session.advance(nowMs: 1_792_490_500_000, todayStartMs: fixtureDay, emitsEvents: true)
        #expect(!events.contains { if case .responseCompleted = $0 { true } else { false } })
    }

    @Test func oldResponsesReadLateDoNotAnnounceCompletion() throws {
        var engine = SpeedEngine()
        let record = { (key: String, end: Int64) in
            ResponseRecord(key: key, source: .codex, agentId: "/a", model: "m", startMs: end - 2000, endMs: end,
                           usage: TokenUsage(input: 1, output: 50))
        }
        let now: Int64 = 1_792_490_500_000
        #expect(engine.upsert(record("old", now - 120_001), project: "p", isCompletion: true, emitsEvents: true, nowMs: now) == nil)
        #expect(engine.upsert(record("recent", now - 120_000), project: "p", isCompletion: true, emitsEvents: true, nowMs: now) != nil)
        let output = engine.advance(nowMs: now, todayStartMs: now - 3_600_000, emitsEvents: false)
        #expect(output.today.requests == 2, "late responses still count in totals")
    }
}
