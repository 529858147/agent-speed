import Foundation

/// Which family of transcript a file belongs to.
enum TranscriptFamily: Sendable {
    case codex
    case claude
}

enum DecodedLine: Sendable {
    case codex(LineExtraction<CodexLine>)
    case claude(LineExtraction<ClaudeLine>)
}

/// Lines decoded ahead of time (off the state-machine thread) so seeding can extract in parallel.
enum DecodedLines: Sendable {
    case codex([LineExtraction<CodexLine>])
    case claude([LineExtraction<ClaudeLine>])
}

/// A family-agnostic per-file parser. Exactly one of the two machines is set.
struct TranscriptParser: Sendable {
    let family: TranscriptFamily
    private var codex: CodexParser?
    private var claude: ClaudeParser?

    init(family: TranscriptFamily, agentId: String, fileID: FileID) {
        self.family = family
        switch family {
        case .codex: codex = CodexParser(agentId: agentId, fileID: fileID)
        case .claude: claude = ClaudeParser(agentId: agentId, fileID: fileID)
        }
    }

    var state: AgentState {
        codex?.state ?? claude!.state
    }

    static func decode(_ bytes: UnsafeRawBufferPointer, family: TranscriptFamily) -> DecodedLine {
        switch family {
        case .codex: .codex(CodexLineExtractor.extract(bytes))
        case .claude: .claude(ClaudeLineExtractor.extract(bytes))
        }
    }

    mutating func consume(_ line: DecodedLine, deduper: inout Deduper, emit: (ResponseUpsert) -> Void) {
        switch line {
        case .codex(let extraction): codex?.consume(extraction, deduper: &deduper, emit: emit)
        case .claude(let extraction): claude?.consume(extraction, deduper: &deduper, emit: emit)
        }
    }

    mutating func consume(rawLine bytes: UnsafeRawBufferPointer, deduper: inout Deduper, emit: (ResponseUpsert) -> Void) {
        consume(Self.decode(bytes, family: family), deduper: &deduper, emit: emit)
    }

    mutating func consume(_ lines: DecodedLines, deduper: inout Deduper, emit: (ResponseUpsert) -> Void) {
        switch lines {
        case .codex(let extractions):
            for extraction in extractions { codex?.consume(extraction, deduper: &deduper, emit: emit) }
        case .claude(let extractions):
            for extraction in extractions { claude?.consume(extraction, deduper: &deduper, emit: emit) }
        }
    }
}
