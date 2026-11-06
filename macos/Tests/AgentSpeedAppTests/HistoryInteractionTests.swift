import AgentSpeedCore
import Foundation
import Testing
@testable import AgentSpeed

@Suite("History table hover")
@MainActor
struct HistoryInteractionTests {
    let first = DayKey("2026-10-25")!
    let second = DayKey("2026-10-26")!

    private func flushHover() async {
        await withCheckedContinuation { continuation in
            DispatchQueue.main.async { continuation.resume() }
        }
    }

    @Test func responderReentryDoesNotClearTheHoveredRow() async {
        let interaction = HistoryInteraction()
        interaction.hover(first, from: .table)
        interaction.trackTableHover(first, inside: false)
        #expect(interaction.hoveredDay == first)
        interaction.trackTableHover(first, inside: true)
        await flushHover()
        #expect(interaction.hoveredDay == first)
        #expect(interaction.hovered?.origin == .table)
    }

    @Test func lateExitFromPreviousRowKeepsTheNewRow() async {
        let interaction = HistoryInteraction()
        interaction.hover(first, from: .table)
        interaction.trackTableHover(second, inside: true)
        interaction.trackTableHover(first, inside: false)
        await flushHover()
        #expect(interaction.hoveredDay == second)
    }

    @Test func enteringAndLeavingAnotherRowClearsTheOldHighlight() async {
        let interaction = HistoryInteraction()
        interaction.hover(first, from: .table)
        interaction.trackTableHover(second, inside: true)
        interaction.trackTableHover(second, inside: false)
        await flushHover()
        #expect(interaction.hoveredDay == nil)
    }

    @Test func leavingTheTableClearsItsHover() async {
        let interaction = HistoryInteraction()
        interaction.hover(first, from: .table)
        interaction.trackTableHover(first, inside: false)
        await flushHover()
        #expect(interaction.hoveredDay == nil)
    }

    @Test func enteringTheChartSupersedesAQueuedTableEntry() async {
        let interaction = HistoryInteraction()
        interaction.trackTableHover(first, inside: true)
        interaction.hover(second, from: .chart)
        await flushHover()
        #expect(interaction.hoveredDay == second)
        #expect(interaction.hovered?.origin == .chart)
    }

    @Test func lateChartExitKeepsAQueuedTableEntry() async {
        let interaction = HistoryInteraction()
        interaction.hover(first, from: .chart)
        interaction.trackTableHover(second, inside: true)
        interaction.hover(nil, from: .chart)
        await flushHover()
        #expect(interaction.hoveredDay == second)
        #expect(interaction.hovered?.origin == .table)
    }

    @Test func resetCancelsAQueuedTableEntry() async {
        let interaction = HistoryInteraction()
        interaction.trackTableHover(first, inside: true)
        interaction.reset()
        await flushHover()
        #expect(interaction.hoveredDay == nil)
    }
}
