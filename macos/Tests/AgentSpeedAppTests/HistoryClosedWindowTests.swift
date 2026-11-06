import AgentSpeedCore
import Foundation
import Observation
import Testing
@testable import AgentSpeed

/// macOS issue #28: the history the window follows changes every few seconds (traffic every 10 s). A closed window
/// keeps its filter but rebuilds nothing until it opens again, so none of it is laid out in the background.
@Suite("History window while closed")
@MainActor
struct HistoryClosedWindowTests {
    let today = DayKey(Date(), calendar: HistoryDates.calendar)
    let suite = "HistoryClosedWindowTests.\(UUID().uuidString)"
    let folder = FileManager.default.temporaryDirectory.appendingPathComponent("HistoryClosedWindowTests-\(UUID().uuidString)")

    private func cleanUp() {
        UserDefaults(suiteName: suite)?.removePersistentDomain(forName: suite)
        try? FileManager.default.removeItem(at: folder)
    }

    private final class Changes: @unchecked Sendable {
        private let lock = NSLock()
        private var count = 0
        func insert() { lock.withLock { count += 1 } }
        var total: Int { lock.withLock { count } }
    }

    private func makeModel(store: HistoryStore) -> HistoryViewModel {
        let settings = AppSettings(defaults: UserDefaults(suiteName: suite)!)
        let l10n = Localizer(preference: .en)
        let cost = CostDisplay(
            settings: settings, l10n: l10n,
            rates: ExchangeRateController(userAgent: "tests", appDataDirectory: folder),
            prices: PriceSyncController(book: PriceBook(), userAgent: "tests", appDataDirectory: folder)
        )
        return HistoryViewModel(store: store, l10n: l10n, accessibility: AccessibilityState(), cost: cost, traffic: TrafficStore())
    }

    /// The days with today's output scaled, so each publish changes the content.
    private func days(_ factor: Double) -> [DailyUsage] {
        PreviewHistory.days(endingAt: Date(), count: 40, calendar: HistoryDates.calendar).map { usage in
            guard usage.day == today.description else { return usage }
            var scaled = usage
            scaled.totals.output = max(1, Int(Double(usage.totals.output) * factor))
            return scaled
        }
    }

    private func todayOutput(_ model: HistoryViewModel) -> Int? {
        model.content.days.first { $0.day == today }?.output
    }

    /// The data loop applies a publish on a later main-actor turn.
    private func settle() async {
        for _ in 0..<8 { await Task.yield() }
    }

    @Test func anOpenWindowFollowsTheHistory() async {
        defer { cleanUp() }
        let store = HistoryStore()
        store.publish(state: .ready(updatedAt: Date()), days: days(1))
        let model = makeModel(store: store)
        let before = todayOutput(model)
        store.publish(state: .ready(updatedAt: Date()), days: days(3))
        await settle()
        #expect(todayOutput(model) != before)
    }

    @Test func aClosedWindowRebuildsNothingUntilItOpens() async {
        defer { cleanUp() }
        let store = HistoryStore()
        store.publish(state: .ready(updatedAt: Date()), days: days(1))
        let model = makeModel(store: store)
        let shown = todayOutput(model)
        model.isOpen = false
        let changes = Changes()
        withObservationTracking { _ = model.content } onChange: { changes.insert() }
        for factor in [2.0, 3.0, 4.0] {
            store.publish(state: .ready(updatedAt: Date()), days: days(factor))
            await settle()
        }
        #expect(changes.total == 0)
        #expect(todayOutput(model) == shown)

        model.isOpen = true
        let expected = makeModel(store: store)
        #expect(todayOutput(model) == todayOutput(expected))
        #expect(todayOutput(model) != shown)
    }

    @Test func reopeningWithoutNewHistoryRebuildsNothing() {
        defer { cleanUp() }
        let store = HistoryStore()
        store.publish(state: .ready(updatedAt: Date()), days: days(1))
        let model = makeModel(store: store)
        model.isOpen = false
        let changes = Changes()
        withObservationTracking { _ = model.content } onChange: { changes.insert() }
        model.isOpen = true
        #expect(changes.total == 0)
    }
}
