import Foundation

/// Which family of transcript file a file belongs to. A new file-based source adds a case here, a line extractor and
/// parser (`DecodedLine`, `TranscriptParser`, `TranscriptDecoder`) and its location (`TranscriptLocations`).
enum TranscriptFamily: String, CaseIterable, Sendable {
    case codex
    case claude
    /// DeepSeek Harness (dsh).
    case dsh
    /// Gemini CLI.
    case gemini

    /// Nil for sources that are not read from transcript files: OpenCode is a database (`OpencodeSource`).
    init?(_ source: SourceKind) {
        switch source {
        case .codex: self = .codex
        case .claudeCode, .claudeDesktop: self = .claude
        case .deepseek: self = .dsh
        case .gemini: self = .gemini
        case .opencode: return nil
        }
    }

    /// Sources whose transcripts belong to this family.
    var sources: [SourceKind] {
        switch self {
        case .codex: [.codex]
        case .claude: [.claudeCode, .claudeDesktop]
        case .dsh: [.deepseek]
        case .gemini: [.gemini]
        }
    }

    /// File names (and for Gemini CLI, folder layout) of this family's transcripts (METRICS 2, 4.6).
    var names: TranscriptNames {
        switch self {
        case .codex: .codexSession
        case .claude: .jsonl
        case .dsh: .dshSession
        case .gemini: .geminiSession
        }
    }
}

enum DecodedLine: Sendable {
    case codex(LineExtraction<CodexLine>)
    case claude(LineExtraction<ClaudeLine>)
    case dsh(LineExtraction<DshLine>)
    case gemini(LineExtraction<GeminiLine>)
}

/// Lines decoded ahead of time (off the state-machine thread) so seeding can extract in parallel.
enum DecodedLines: Sendable {
    case codex([LineExtraction<CodexLine>])
    case claude([LineExtraction<ClaudeLine>])
    case dsh([LineExtraction<DshLine>])
    case gemini([LineExtraction<GeminiLine>])
}

/// A family-agnostic per-file parser. Exactly one of the machines is set.
struct TranscriptParser: Sendable {
    let family: TranscriptFamily
    private var codex: CodexParser?
    private var claude: ClaudeParser?
    private var dsh: DshParser?
    private var gemini: GeminiParser?

    init(family: TranscriptFamily, agentId: String, fileID: FileID) {
        self.family = family
        switch family {
        case .codex: codex = CodexParser(agentId: agentId, fileID: fileID)
        case .claude: claude = ClaudeParser(agentId: agentId, fileID: fileID)
        case .dsh: dsh = DshParser(agentId: agentId, fileID: fileID)
        case .gemini: gemini = GeminiParser(agentId: agentId, fileID: fileID)
        }
    }

    var state: AgentState {
        codex?.state ?? claude?.state ?? dsh?.state ?? gemini!.state
    }

    /// Decodes one unit of input: a line, or a whole document for Gemini CLI legacy `.json` files.
    func decode(_ bytes: UnsafeRawBufferPointer) -> DecodedLine {
        switch family {
        case .codex: .codex(CodexLineExtractor.extract(bytes))
        case .claude: .claude(ClaudeLineExtractor.extract(bytes))
        case .dsh: .dsh(DshLineExtractor.extract(bytes))
        case .gemini:
            .gemini(gemini?.isDocument == true ? GeminiLineExtractor.extractDocument(bytes) : GeminiLineExtractor.extract(bytes))
        }
    }

    /// Counts a unit of input that could not be decoded at all, such as a corrupt zstd frame (METRICS 5.1).
    mutating func noteMalformedLine() {
        codex?.noteMalformedLine()
        claude?.noteMalformedLine()
        dsh?.noteMalformedLine()
        gemini?.noteMalformedLine()
    }

    mutating func consume(_ line: DecodedLine, deduper: inout Deduper, emit: (ResponseUpsert) -> Void) {
        switch line {
        case .codex(let extraction): codex?.consume(extraction, deduper: &deduper, emit: emit)
        case .claude(let extraction): claude?.consume(extraction, deduper: &deduper, emit: emit)
        case .dsh(let extraction): dsh?.consume(extraction, deduper: &deduper, emit: emit)
        case .gemini(let extraction): gemini?.consume(extraction, deduper: &deduper, emit: emit)
        }
    }

    mutating func consume(rawLine bytes: UnsafeRawBufferPointer, deduper: inout Deduper, emit: (ResponseUpsert) -> Void) {
        consume(decode(bytes), deduper: &deduper, emit: emit)
    }

    mutating func consume(_ lines: DecodedLines, deduper: inout Deduper, emit: (ResponseUpsert) -> Void) {
        switch lines {
        case .codex(let extractions):
            for extraction in extractions { codex?.consume(extraction, deduper: &deduper, emit: emit) }
        case .claude(let extractions):
            for extraction in extractions { claude?.consume(extraction, deduper: &deduper, emit: emit) }
        case .dsh(let extractions):
            for extraction in extractions { dsh?.consume(extraction, deduper: &deduper, emit: emit) }
        case .gemini(let extractions):
            for extraction in extractions { gemini?.consume(extraction, deduper: &deduper, emit: emit) }
        }
    }
}

/// Reads a whole transcript (from the reader's position) and decodes its lines, off the state-machine thread.
enum TranscriptDecoder {
    static func decode(_ family: TranscriptFamily, reader: inout TranscriptReader) -> (lines: DecodedLines, read: FileTailer.ReadResult) {
        switch family {
        case .codex:
            let (lines, read) = extract(CodexLineExtractor.extract, reader: &reader)
            return (.codex(lines), read)
        case .claude:
            let (lines, read) = extract(ClaudeLineExtractor.extract, reader: &reader)
            return (.claude(lines), read)
        case .dsh:
            let (lines, read) = extract(DshLineExtractor.extract, reader: &reader)
            return (.dsh(lines), read)
        case .gemini:
            let extractor = reader.isDocument ? GeminiLineExtractor.extractDocument : GeminiLineExtractor.extract
            let (lines, read) = extract(extractor, reader: &reader)
            return (.gemini(lines), read)
        }
    }

    private static func extract<Line: Sendable>(
        _ extractor: (UnsafeRawBufferPointer) -> LineExtraction<Line>,
        reader: inout TranscriptReader
    ) -> ([LineExtraction<Line>], FileTailer.ReadResult) {
        var lines: [LineExtraction<Line>] = []
        let read = reader.read(onMalformed: { lines.append(.malformed) }) { bytes in
            let extraction = extractor(bytes)
            if case .blank = extraction { return }
            lines.append(extraction)
        }
        return (lines, read)
    }
}
