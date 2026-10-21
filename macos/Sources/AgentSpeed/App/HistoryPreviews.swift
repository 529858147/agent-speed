import AgentSpeedCore
import AppKit
import SwiftUI

/// Usage History renders for `--render-previews`: the real window (toolbar included) in both appearances, at the
/// default and the minimum size, for all sources and one source, empty and while analysing, plus the whole
/// scrolling column with the table.
@MainActor
enum HistoryPreviews {
    struct Case {
        let name: String
        var size = HistoryWindowController.defaultSize
        var source: SourceKind?
        var range: HistoryRangeKind = .days30
        var custom: (Int, Int)?
        var days: Days = .full
        var state: State = .ready
        var language: LanguagePreference = .zhHant
        var hoverDaysAgo: Int?
        var selectDaysAgo: [Int] = []
        var sort: HistoryColumn?
        /// Renders the full scrolling column instead of the window.
        var tall = false
    }

    enum Days { case full, none, recent(Int) }
    enum State { case ready, analyzing(Double, Int, Int), notLoaded }

    static let cases: [Case] = [
        Case(name: "all-wide"),
        Case(name: "all-min", size: HistoryWindowController.minimumSize),
        Case(name: "source-wide", source: .claudeCode, range: .days90),
        Case(name: "source-min", size: HistoryWindowController.minimumSize, source: .deepseek, range: .days7),
        Case(name: "year", range: .year),
        Case(name: "all-range", range: .all),
        Case(name: "custom", range: .custom, custom: (52, 38)),
        Case(name: "hover", hoverDaysAgo: 6, selectDaysAgo: [2, 3]),
        Case(name: "hover-today", source: .codex, range: .days7, hoverDaysAgo: 0),
        Case(name: "empty", source: .gemini, days: .none),
        Case(name: "analyzing", days: .recent(9), state: .analyzing(0.42, 128, 304)),
        Case(name: "analyzing-empty", days: .none, state: .analyzing(0.18, 54, 304)),
        Case(name: "en", language: .en),
        Case(name: "ja", size: HistoryWindowController.minimumSize, language: .ja),
        Case(name: "zh-hans", source: .opencode, range: .days90, language: .zhHans),
        Case(name: "table", tall: true),
        Case(name: "table-sorted", source: .claudeDesktop, sort: .speed, tall: true),
    ]

    static func render(model: AppModel, into directory: URL) {
        let only = ProcessInfo.processInfo.environment["AGENT_SPEED_HISTORY_CASES"]?.split(separator: ",").map(String.init)
        let original = model.settings.language
        for item in cases where only?.contains(item.name) ?? true {
            let viewModel = prepare(item, model: model)
            for dark in [false, true] {
                let url = directory.appendingPathComponent("history-\(item.name)-\(dark ? "dark" : "light").png")
                if item.tall {
                    renderColumn(viewModel: viewModel, model: model, dark: dark, to: url)
                } else {
                    renderWindow(viewModel: viewModel, model: model, size: item.size, dark: dark, to: url)
                }
            }
        }
        if only?.contains("popover") ?? true {
            let viewModel = prepare(Case(name: "popover", range: .custom, custom: (40, 12)), model: model)
            let content = viewModel.content
            for dark in [false, true] {
                PreviewSnapshot.writeHosted(
                    CustomRangeView(bounds: content.selectableBounds, initial: content.range, l10n: model.l10n) { _, _ in }
                        .appEnvironment(model),
                    dark: dark,
                    to: directory.appendingPathComponent("history-custom-popover-\(dark ? "dark" : "light").png")
                )
            }
        }
        model.settings.language = original
        model.l10n.apply(original)
        model.history.publish(state: .notLoaded, days: [])
    }

    /// Fills the store and builds a view model in the case's state.
    static func prepare(_ item: Case, model: AppModel) -> HistoryViewModel {
        let calendar = HistoryDates.calendar
        let now = Date()
        let all = PreviewHistory.days(endingAt: now, count: 160, calendar: calendar)
        model.settings.language = item.language
        model.l10n.apply(item.language)
        let days: [DailyUsage] = switch item.days {
        case .full: all
        case .none: []
        case .recent(let count):
            all.filter { DayKey($0.day).map { $0 >= DayKey(now, calendar: calendar).adding(days: -count) } ?? false }
        }
        let state: HistoryState = switch item.state {
        case .ready: .ready(updatedAt: now.addingTimeInterval(-240))
        case .analyzing(let progress, let done, let total): .analyzing(progress: progress, filesDone: done, filesTotal: total)
        case .notLoaded: .notLoaded
        }
        model.history.publish(state: state, days: days)
        let viewModel = HistoryViewModel(store: model.history, l10n: model.l10n, accessibility: model.accessibility)
        viewModel.select(source: item.source)
        if item.range == .custom, let (start, end) = item.custom {
            let today = DayKey(now, calendar: calendar)
            viewModel.applyCustom(from: today.adding(days: -start), to: today.adding(days: -end))
        } else {
            viewModel.select(range: item.range)
        }
        if let sort = item.sort { viewModel.toggleSort(sort) }
        let today = DayKey(now, calendar: calendar)
        if let ago = item.hoverDaysAgo { viewModel.interaction.hover(today.adding(days: -ago), from: .chart) }
        viewModel.interaction.selection = Set(item.selectDaysAgo.map { today.adding(days: -$0) })
        return viewModel
    }


    /// The real window with its toolbar, drawn off screen.
    private static func renderWindow(viewModel: HistoryViewModel, model: AppModel, size: NSSize, dark: Bool, to url: URL) {
        let host = NSHostingController(rootView: HistoryView(model: viewModel).appEnvironment(model))
        host.sizingOptions = []
        let window = HistoryWindowController.makeWindow(content: host)
        let toolbar = HistoryToolbar(model: viewModel) {}
        window.toolbar = toolbar.toolbar
        window.title = model.l10n("history.title")
        window.appearance = NSAppearance(named: dark ? .darkAqua : .aqua)
        window.setContentSize(size)
        window.setFrameOrigin(NSPoint(x: -30_000, y: -30_000))
        window.orderFront(nil)
        RunLoop.main.run(until: Date().addingTimeInterval(0.6))
        defer { window.orderOut(nil) }
        guard let frame = window.contentView?.superview else { return }
        frame.layoutSubtreeIfNeeded()
        frame.displayIfNeeded()
        guard let rep = frame.bitmapImageRepForCachingDisplay(in: frame.bounds) else { return }
        frame.cacheDisplay(in: frame.bounds, to: rep)
        if let image = rep.cgImage { PreviewSnapshot.write(image, to: url) }
        _ = toolbar
    }

    /// The whole scrolling column at its natural height, to review the table.
    private static func renderColumn(viewModel: HistoryViewModel, model: AppModel, dark: Bool, to url: URL) {
        let rows = viewModel.content.rows.count
        let height = 460 + HistoryLayout.chartHeight + CGFloat(min(rows, 40)) * HistoryLayout.rowHeight + 120
        PreviewSnapshot.writeHosted(
            HistoryView(model: viewModel)
                .appEnvironment(model)
                .frame(width: HistoryWindowController.defaultSize.width, height: height),
            dark: dark,
            to: url
        )
    }
}
