import AgentSpeedCore
import Foundation

/// CSV export of the rows the daily table shows. Columns are locale independent: English snake_case headers, ISO
/// dates, plain integers, a dot as the decimal separator, RFC 4180 quoting and CRLF line ends (same as Windows).
enum HistoryCSV {
    static let columns = [
        "date", "source", "requests", "input_tokens", "cached_input_tokens", "cache_write_input_tokens", "output_tokens",
        "reasoning_tokens", "cache_hit_percent", "measured_output_tokens", "measured_ms", "avg_speed_tok_s", "peak_tok_s",
        "agents", "unfinalized", "models",
    ]

    /// Stable identifier of a source (or `all`) for the `source` column and file names.
    static func sourceID(_ source: SourceKind?) -> String {
        switch source {
        case nil: "all"
        case .codex: "codex"
        case .claudeCode: "claude-code"
        case .claudeDesktop: "claude-desktop"
        case .deepseek: "deepseek"
        case .opencode: "opencode"
        case .gemini: "gemini"
        case .omp: "omp"
        case .antigravity: "antigravity"
        }
    }

    static func build(_ rows: [DaySummary], source: SourceKind?) -> String {
        var text = columns.joined(separator: ",") + "\r\n"
        let id = sourceID(source)
        for row in rows {
            let fields = [
                row.day.description, id,
                integer(row.requests), integer(row.input), integer(row.cachedInput), integer(row.cacheWriteInput),
                integer(row.output), integer(row.reasoning), decimal(row.cacheHitRatio.map { $0 * 100 }),
                integer(row.measuredOutput), String(row.measuredMs), decimal(row.averageSpeed), decimal(row.peak),
                integer(row.agents), integer(row.unfinalized), row.models.map(\.model).joined(separator: "; "),
            ]
            text += fields.map(field).joined(separator: ",") + "\r\n"
        }
        return text
    }

    /// UTF-8 with a byte order mark, so spreadsheet apps open non-ASCII model names correctly.
    static func encode(_ csv: String) -> Data {
        Data([0xEF, 0xBB, 0xBF]) + Data(csv.utf8)
    }

    /// `agent-speed-history-all-2026-09-22-2026-10-21.csv`
    static func fileName(_ range: DayRange, source: SourceKind?) -> String {
        "agent-speed-history-\(sourceID(source))-\(range.start)-\(range.end).csv"
    }

    private static func integer(_ value: Int) -> String { String(value) }

    private static func decimal(_ value: Double?) -> String {
        guard let value, value.isFinite else { return "" }
        return String(format: "%.1f", locale: Locale(identifier: "en_US_POSIX"), (value * 10).rounded() / 10)
    }

    private static func field(_ raw: String) -> String {
        var value = raw
        // Text a spreadsheet would read as a formula is prefixed with an apostrophe.
        if let first = value.first, "=+-@\t\r".contains(first) { value = "'" + value }
        guard value.contains(where: { ",\"\r\n".contains($0) }) else { return value }
        return "\"" + value.replacingOccurrences(of: "\"", with: "\"\"") + "\""
    }
}
