import AgentSpeedCore
import Foundation

/// Checks of the speed-title path the app itself takes (`LiveGauge.ingest`, hysteresis, the 0.8 s hold and the
/// decision timer), run by `--render-previews`. Results land in `gauge-checks.txt`; failures also go to stderr.
@MainActor
enum GaugeChecks {
    static func run(into directory: URL) {
        var results: [(String, Bool)] = []
        func check(_ name: String, _ condition: Bool) { results.append((name, condition)) }

        // Ladder positions at the default redline.
        check("32 tok/s is a sports car at R = 400", SpeedTitle.step(for: 32, redline: 400) == .sportsCar)
        check("31.9 tok/s is still a car", SpeedTitle.step(for: 31.9, redline: 400) == .car)
        check("400 tok/s is a rocket", SpeedTitle.step(for: 400, redline: 400) == .rocket)
        check("0.5 tok/s walks", SpeedTitle.step(for: 0.5, redline: 400) == .walk)

        // The tracker climbs straight to the right title, but only after the hold.
        var tracker = TitleTracker()
        let early = tracker.update(value: 32, redline: 400, now: 0) || tracker.update(value: 32, redline: 400, now: 0.5)
        check("no title before the 0.8 s hold", !early && tracker.shown == .idle)
        check("sports car after the hold", tracker.update(value: 32, redline: 400, now: 0.81) && tracker.shown == .sportsCar)
        check("hysteresis keeps the title above 90 %", !tracker.update(value: 29, redline: 400, now: 2) && tracker.shown == .sportsCar)
        _ = tracker.update(value: 28, redline: 400, now: 3)
        check("leaves below 90 % after the hold", tracker.update(value: 28, redline: 400, now: 3.81) && tracker.shown == .car)

        // The app path: a steady 32 tok/s ingested once reaches the title through the gauge's own decision timer.
        let gauge = LiveGauge()
        gauge.redline = 400
        let start = Clock.now
        gauge.ingest(32, now: start)
        check("gauge holds the title at first", gauge.title == .idle)
        RunLoop.main.run(until: Date().addingTimeInterval(1.1))
        check("gauge reaches sports car on its own after the hold", gauge.title == .sportsCar)
        check("effect tier stays cruise", gauge.tier == .cruise)
        gauge.ingest(32, now: Clock.now)
        check("steady value keeps the title", gauge.title == .sportsCar)

        let report = results.map { "\($0.1 ? "PASS" : "FAIL")  \($0.0)" }.joined(separator: "\n")
        try? (report + "\n").write(to: directory.appendingPathComponent("gauge-checks.txt"), atomically: true, encoding: .utf8)
        for (name, passed) in results where !passed {
            FileHandle.standardError.write(Data("gauge check failed: \(name)\n".utf8))
        }
    }
}
