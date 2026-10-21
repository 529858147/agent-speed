import Foundation

/// Raw Codex usage object (`usage`, `total_token_usage`, `last_token_usage`).
struct CodexRawUsage: Equatable, Sendable {
    var input = 0
    var cached = 0
    var cacheWrite = 0
    var output = 0
    var reasoning = 0
    var total = 0

    static let zero = CodexRawUsage()

    var normalized: TokenUsage {
        TokenUsage(input: input, cachedInput: cached, cacheWriteInput: cacheWrite, output: output, reasoning: reasoning)
    }

    /// Field-wise difference and whether any field went negative.
    func delta(since previous: CodexRawUsage) -> (usage: CodexRawUsage, hasNegative: Bool) {
        let usage = CodexRawUsage(
            input: input - previous.input,
            cached: cached - previous.cached,
            cacheWrite: cacheWrite - previous.cacheWrite,
            output: output - previous.output,
            reasoning: reasoning - previous.reasoning,
            total: total - previous.total
        )
        let hasNegative = usage.input < 0 || usage.cached < 0 || usage.cacheWrite < 0 || usage.output < 0
            || usage.reasoning < 0 || usage.total < 0
        return (usage, hasNegative)
    }
}

struct CodexSessionMeta: Equatable, Sendable {
    var id: String?
    var cwd: String?
    var agentPath: String?
    var isSubagent: Bool
}

struct CodexLegacyInfo: Equatable, Sendable {
    var total: CodexRawUsage?
    var last: CodexRawUsage?
}

enum CodexLineKind: Equatable, Sendable {
    case sessionMeta(CodexSessionMeta)
    case turnContext(model: String?, cwd: String?)
    case input
    case output
    case agentMessage(recipient: String?)
    indirect case modernUsage(responseId: String?, threadId: String?, usage: CodexRawUsage)
    /// `event_msg` / `token_count`; `info` is nil when it is null or missing.
    indirect case legacyUsage(threadId: String?, info: CodexLegacyInfo?)
    case turnEnded
    case other
}

struct CodexLine: Equatable, Sendable {
    var timestampMs: Int64?
    var kind: CodexLineKind
}

/// Decodes the fields of a Codex envelope line that METRICS 4.2 needs. Conversation text is never captured.
enum CodexLineExtractor {
    private static let envelopeTypes: [StaticString] = [
        "session_meta", "turn_context", "token_usage_record", "event_msg", "response_item",
    ]

    private struct Payload {
        var type: String?
        var role: String?
        var recipient: String?
        var id: String?
        var cwd: String?
        var agentPath: String?
        var threadSource: String?
        var sourceHasSubagent = false
        var model: String?
        var responseId: String?
        var threadId: String?
        var usage: CodexRawUsage?
        var infoPresent = false
        var info: CodexLegacyInfo?
    }

    static func extract(_ bytes: UnsafeRawBufferPointer) -> LineExtraction<CodexLine> {
        var scanner = JSONScanner(bytes)
        if scanner.isBlank { return .blank }
        do {
            var timestamp: Int64?
            var envelope: Int?
            var payload: Payload?
            try scanner.beginObject()
            while let key = try scanner.nextMember() {
                if scanner.key(key, is: "timestamp") {
                    timestamp = try scanner.readTimestamp()
                } else if scanner.key(key, is: "type") {
                    envelope = try scanner.readStringMatch(envelopeTypes)
                } else if scanner.key(key, is: "payload") {
                    payload = try scanPayload(&scanner)
                } else {
                    try scanner.skipValue()
                }
            }
            try scanner.finish()
            return .line(CodexLine(timestampMs: timestamp, kind: classify(envelope: envelope, payload: payload)))
        } catch {
            return .malformed
        }
    }

    private static func classify(envelope: Int?, payload: Payload?) -> CodexLineKind {
        switch envelope {
        case 0:
            let payload = payload ?? Payload()
            return .sessionMeta(CodexSessionMeta(
                id: payload.id,
                cwd: payload.cwd,
                agentPath: payload.agentPath,
                isSubagent: payload.threadSource == "subagent" || payload.sourceHasSubagent
            ))
        case 1:
            return .turnContext(model: payload?.model, cwd: payload?.cwd)
        case 2:
            guard let payload else { return .modernUsage(responseId: nil, threadId: nil, usage: .zero) }
            return .modernUsage(responseId: payload.responseId, threadId: payload.threadId, usage: payload.usage ?? .zero)
        case 3:
            guard let payload, let type = payload.type else { return .other }
            switch type {
            case "token_count":
                return .legacyUsage(threadId: payload.threadId, info: payload.info)
            case "task_started", "user_message":
                return .input
            case "task_complete", "turn_aborted":
                return .turnEnded
            default:
                return .other
            }
        case 4:
            guard let payload, let type = payload.type else { return .other }
            if type.hasSuffix("_output") { return .input }
            switch type {
            case "message":
                switch payload.role {
                case "user", "developer", "system": return .input
                case "assistant": return .output
                default: return .other
                }
            case "agent_message":
                return .agentMessage(recipient: payload.recipient)
            case "reasoning":
                return .output
            default:
                return type.hasSuffix("_call") ? .output : .other
            }
        default:
            return .other
        }
    }

    private static func scanPayload(_ scanner: inout JSONScanner) throws(JSONScanError) -> Payload? {
        guard try scanner.beginObjectIfPresent() else { return nil }
        var payload = Payload()
        while let key = try scanner.nextMember() {
            if scanner.key(key, is: "type") {
                payload.type = try scanner.readString()
            } else if scanner.key(key, is: "role") {
                payload.role = try scanner.readString()
            } else if scanner.key(key, is: "recipient") {
                payload.recipient = try scanner.readString()
            } else if scanner.key(key, is: "id") {
                payload.id = try scanner.readNonEmptyString()
            } else if scanner.key(key, is: "cwd") {
                payload.cwd = try scanner.readNonEmptyString()
            } else if scanner.key(key, is: "agent_path") {
                payload.agentPath = try scanner.readNonEmptyString()
            } else if scanner.key(key, is: "thread_source") {
                payload.threadSource = try scanner.readString()
            } else if scanner.key(key, is: "source") {
                payload.sourceHasSubagent = try scanSourceHasSubagent(&scanner)
            } else if scanner.key(key, is: "model") {
                payload.model = try scanner.readNonEmptyString()
            } else if scanner.key(key, is: "response_id") {
                payload.responseId = try scanner.readNonEmptyString()
            } else if scanner.key(key, is: "thread_id") {
                payload.threadId = try scanner.readNonEmptyString()
            } else if scanner.key(key, is: "usage") {
                payload.usage = try scanUsage(&scanner)
            } else if scanner.key(key, is: "info") {
                payload.info = try scanInfo(&scanner)
            } else {
                try scanner.skipValue()
            }
        }
        return payload
    }

    private static func scanSourceHasSubagent(_ scanner: inout JSONScanner) throws(JSONScanError) -> Bool {
        guard try scanner.beginObjectIfPresent() else { return false }
        var found = false
        while let key = try scanner.nextMember() {
            if scanner.key(key, is: "subagent") {
                found = try !scanner.readNull()
                if found { try scanner.skipValue() }
            } else {
                try scanner.skipValue()
            }
        }
        return found
    }

    private static func scanInfo(_ scanner: inout JSONScanner) throws(JSONScanError) -> CodexLegacyInfo? {
        guard try scanner.beginObjectIfPresent() else { return nil }
        var info = CodexLegacyInfo()
        while let key = try scanner.nextMember() {
            if scanner.key(key, is: "total_token_usage") {
                info.total = try scanUsage(&scanner)
            } else if scanner.key(key, is: "last_token_usage") {
                info.last = try scanUsage(&scanner)
            } else {
                try scanner.skipValue()
            }
        }
        return info
    }

    static func scanUsage(_ scanner: inout JSONScanner) throws(JSONScanError) -> CodexRawUsage? {
        guard try scanner.beginObjectIfPresent() else { return nil }
        var usage = CodexRawUsage()
        while let key = try scanner.nextMember() {
            if scanner.key(key, is: "input_tokens") {
                usage.input = TokenCount.clamp(try scanner.readInteger())
            } else if scanner.key(key, is: "cached_input_tokens") {
                usage.cached = TokenCount.clamp(try scanner.readInteger())
            } else if scanner.key(key, is: "cache_write_input_tokens") {
                usage.cacheWrite = TokenCount.clamp(try scanner.readInteger())
            } else if scanner.key(key, is: "output_tokens") {
                usage.output = TokenCount.clamp(try scanner.readInteger())
            } else if scanner.key(key, is: "reasoning_output_tokens") {
                usage.reasoning = TokenCount.clamp(try scanner.readInteger())
            } else if scanner.key(key, is: "total_tokens") {
                usage.total = TokenCount.clamp(try scanner.readInteger())
            } else {
                try scanner.skipValue()
            }
        }
        return usage
    }
}
