import AgentSpeedCore
import Foundation
import Observation
import Testing
@testable import AgentSpeed

/// macOS issue #21: a hover change may redraw the two rows it concerns, never every row of the table.
@Suite("History row hover marks")
@MainActor
struct HistoryRowMarkTests {
    let days = (20...26).map { DayKey("2026-10-\($0)")! }

    /// Counts observation callbacks, which may arrive off the main actor.
    private final class Changes: @unchecked Sendable {
        private let lock = NSLock()
        private var items: [Int] = []
        func insert(_ item: Int) { lock.withLock { items.append(item) } }
        var all: [Int] { lock.withLock { items } }
    }

    private func flushHover() async {
        await withCheckedContinuation { continuation in
            DispatchQueue.main.async { continuation.resume() }
        }
    }

    /// Whether reading `read` subscribes to a change made by `change`.
    private func observes(_ read: () -> Void, _ change: () -> Void) -> Bool {
        let changes = Changes()
        withObservationTracking(read) { changes.insert(0) }
        change()
        return !changes.all.isEmpty
    }

    @Test func eachRowMarkFollowsTheHoveredDay() {
        let interaction = HistoryInteraction()
        let marks = days.map(interaction.mark(for:))
        interaction.hover(days[2], from: .table)
        #expect(marks.map(\.hovered) == days.map { $0 == days[2] })
        interaction.hover(days[4], from: .chart)
        #expect(marks.map(\.hovered) == days.map { $0 == days[4] })
        interaction.hover(nil, from: .chart)
        #expect(marks.allSatisfy { !$0.hovered })
    }

    @Test func aRowShownAfterTheHoverStartsHovered() {
        let interaction = HistoryInteraction()
        interaction.hover(days[3], from: .chart)
        #expect(interaction.mark(for: days[3]).hovered)
        #expect(!interaction.mark(for: days[1]).hovered)
        #expect(interaction.mark(for: days[3]) === interaction.mark(for: days[3]))
    }

    @Test func movingToTheNextRowChangesOnlyTwoMarks() async {
        let interaction = HistoryInteraction()
        let marks = days.map(interaction.mark(for:))
        interaction.trackTableHover(days[1], inside: true)
        await flushHover()
        let changes = Changes()
        for (index, mark) in marks.enumerated() {
            withObservationTracking { _ = mark.hovered } onChange: { changes.insert(index) }
        }
        interaction.trackTableHover(days[1], inside: false)
        interaction.trackTableHover(days[2], inside: true)
        await flushHover()
        #expect(Set(changes.all) == [1, 2])
        #expect(interaction.hoveredDay == days[2])
    }

    /// The loop's trigger: the resting row reports an exit and an entry while it is laid out again.
    @Test func reenteringTheHoveredRowChangesNothing() async {
        let interaction = HistoryInteraction()
        let mark = interaction.mark(for: days[5])
        interaction.trackTableHover(days[5], inside: true)
        await flushHover()
        let changes = Changes()
        withObservationTracking { _ = mark.hovered; _ = interaction.hovered?.day } onChange: { changes.insert(0) }
        for _ in 0..<3 {
            interaction.trackTableHover(days[5], inside: false)
            interaction.trackTableHover(days[5], inside: true)
        }
        await flushHover()
        #expect(changes.all.isEmpty)
        #expect(mark.hovered)
    }

    /// What a row's own body reads (selection and its mark object) is not invalidated by any hover change.
    @Test func aRowBodyDoesNotObserveTheHover() {
        let interaction = HistoryInteraction()
        let day = days[0]
        #expect(!observes({
            _ = interaction.selection.contains(day)
            _ = interaction.mark(for: day)
        }, {
            interaction.hover(day, from: .table)
            interaction.hover(days[1], from: .chart)
        }))
        #expect(observes({ _ = interaction.selection.contains(day) }, { interaction.selection = [day] }))
    }

    @Test func resetClearsTheMark() {
        let interaction = HistoryInteraction()
        let mark = interaction.mark(for: days[6])
        interaction.hover(days[6], from: .table)
        interaction.reset()
        #expect(!mark.hovered)
    }
}
