import AgentSpeedCore
import Foundation

/// `--dump-unpriced`: the copied diagnostics text of METRICS 9.10 over every log, as `reference.py --dump-unpriced`.
/// The history goes through the real analysis into a store (in memory, or `--history-store`) and is read back through
/// *Reading*; `--live` takes today's engine. Only the `app` line differs from the reference.
enum DumpUnpriced {
    static func run(configuration: MonitorConfiguration, options: ProbeArguments.UnpricedOptions, storePath: String?,
                    table: PriceTable) {
        let range = UnpricedRange.probe(days: options.days, first: options.first, last: options.last, live: options.live,
                                        source: options.source)
        let parts: [UnpricedPart]
        let started = Date()
        if options.live {
            let report = TranscriptScan.today(configuration: configuration, pricing: PriceBook(tables: [table]))
            parts = UnpricedPart.live(day: range.first, requests: report.todayBySource.mapValues(\.requests),
                                      facts: report.todayUnpricedFacts)
        } else {
            do {
                let report = try HistoryScan.run(configuration: configuration, storePath: storePath,
                                                 pricing: PriceBook(tables: [table]))
                parts = report.days.map(\.unpricedPart)
            } catch {
                FileHandle.standardError.write(Data("history analysis failed: \(error)\n".utf8))
                exit(1)
            }
        }
        let view = UnpricedView(parts: parts, table: table, range: range)
        let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0.0.0"
        FileHandle.standardOutput.write(Data(UnpricedText.render(view, app: .current(version: version)).utf8))
        let elapsed = Int(Date().timeIntervalSince(started) * 1000)
        FileHandle.standardError.write(Data("unpriced: \(parts.count) parts, \(view.unpriced) unpriced requests in \(elapsed) ms\n".utf8))
    }
}
