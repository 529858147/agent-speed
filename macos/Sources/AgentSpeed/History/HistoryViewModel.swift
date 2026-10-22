import AgentSpeedCore
import AppKit
import Observation
import SwiftUI

/// The filter and derived content of the Usage History window. The content is rebuilt once per change of the stored
/// days, the filter, the language or the date, never per frame.
@MainActor
@Observable
final class HistoryViewModel {
    let store: HistoryStore
    let l10n: Localizer
    let accessibility: AccessibilityState
    /// Pointer, keyboard and selection state, kept apart so hovering never rebuilds the content.
    let interaction = HistoryInteraction()

    private(set) var source: SourceKind?
    private(set) var rangeKind: HistoryRangeKind = .days30
    private(set) var customRange: DayRange?
    private(set) var sort = HistorySort()
    private(set) var content: HistoryContent

    @ObservationIgnored private var dataLoop: ObservationLoop?
    @ObservationIgnored private var observers: [NSObjectProtocol] = []

    init(store: HistoryStore, l10n: Localizer, accessibility: AccessibilityState) {
        self.store = store
        self.l10n = l10n
        self.accessibility = accessibility
        let today = DayKey(Date(), calendar: HistoryDates.calendar)
        content = HistoryContent.build(
            usage: store.days, source: nil, rangeKind: .days30, customRange: nil, sort: HistorySort(), today: today, l10n: l10n
        )
        dataLoop = ObservationLoop { [weak self] in
            guard let self else { return }
            _ = self.store.days
            _ = self.l10n.language
            self.rebuild()
        }
        let center = NotificationCenter.default
        for name in [Notification.Name.NSCalendarDayChanged, .NSSystemTimeZoneDidChange, .NSSystemClockDidChange] {
            observers.append(center.addObserver(forName: name, object: nil, queue: .main) { [weak self] _ in
                MainActor.assumeIsolated { self?.rebuild() }
            })
        }
    }

    var motion: Animation? { accessibility.reduceMotion ? nil : HistoryMotion.chart }

    func select(source: SourceKind?) {
        guard source != self.source else { return }
        self.source = source
        interaction.reset()
        rebuild()
    }

    func select(range kind: HistoryRangeKind) {
        guard kind != .custom, kind != rangeKind else { return }
        rangeKind = kind
        interaction.reset()
        rebuild()
        // Today is still being written: bring in what the logs hold now, the view follows as the store publishes.
        if kind == .today { refreshToday() }
    }

    func applyCustom(from: DayKey, to: DayKey) {
        customRange = HistoryRanges.clamp(from: from, to: to, within: content.selectableBounds)
        rangeKind = .custom
        interaction.reset()
        rebuild()
    }

    func toggleSort(_ column: HistoryColumn) {
        sort = content.sort.toggled(column)
        content = content.sorted(by: sort)
    }

    func refresh() {
        store.refresh()
    }

    /// The range is the single day that is still being written (DESIGN 4b: today's figures update live).
    var showsLiveDay: Bool {
        content.range.isSingleDay && content.range.start == content.today
    }

    /// Re-reads today's logs without the analysing progress, so the live day updates in place.
    func refreshToday() {
        store.refresh(showsProgress: false)
    }

    private func rebuild() {
        let today = DayKey(Date(), calendar: HistoryDates.calendar)
        let next = HistoryContent.build(
            usage: store.days, source: source, rangeKind: rangeKind, customRange: customRange, sort: sort,
            today: today, l10n: l10n
        )
        if next != content { content = next }
    }
}

/// Hover, keyboard focus day, row selection and the reveal flash.
@MainActor
@Observable
final class HistoryInteraction {
    enum Origin: Equatable { case chart, table }

    /// The day under the pointer, and where the pointer is.
    private(set) var hovered: (day: DayKey, origin: Origin)?
    /// The day the chart's keyboard cursor sits on while the chart has focus.
    var keyboardDay: DayKey?
    var selection: Set<DayKey> = []
    /// Anchor of shift-extended selections.
    var anchor: DayKey?
    /// A request to scroll the table to a day and flash it; the id makes every request unique.
    private(set) var reveal: (day: DayKey, id: Int)?
    private(set) var flash: (day: DayKey, id: Int)?
    @ObservationIgnored private var revealCount = 0

    var hoveredDay: DayKey? { hovered?.day }

    func hover(_ day: DayKey?, from origin: Origin) {
        if let day {
            if hovered?.day != day || hovered?.origin != origin { hovered = (day, origin) }
        } else if hovered?.origin == origin {
            hovered = nil
        }
    }

    /// The pointer left `day`'s row; a row entered in the meantime keeps its hover.
    func unhover(_ day: DayKey, from origin: Origin) {
        if hovered?.day == day, hovered?.origin == origin { hovered = nil }
    }

    func revealInTable(_ day: DayKey) {
        revealCount += 1
        reveal = (day, revealCount)
        let id = revealCount
        // The flash starts once the scroll has brought the row on screen.
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.32) { [weak self] in
            guard let self, self.reveal?.id == id else { return }
            self.flash = (day, id)
        }
    }

    func reset() {
        hovered = nil
        keyboardDay = nil
        selection = []
        anchor = nil
    }
}

enum HistoryMotion {
    /// Bar heights between ranges and sources (DESIGN 4b: spring, at most 0.45 s).
    static let chart = Animation.spring(response: 0.42, dampingFraction: 0.86)
    static let fade = Animation.easeOut(duration: 0.2)
    static let scroll = Animation.easeInOut(duration: 0.35)
}
