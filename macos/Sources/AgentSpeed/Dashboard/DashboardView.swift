import AgentSpeedCore
import AppKit
import SwiftUI

/// The menu bar dashboard (DESIGN section 4): hero, live chart, sources, agents, today and footer.
struct DashboardView: View {
    let model: AppModel
    /// Tallest the popover may be on its screen; beyond it the sections under the hero scroll.
    var maxHeight: CGFloat?

    private static let topPadding = DashboardLayout.padding
    private static let bottomPadding = Spacing.s

    var body: some View {
        DashboardColumn(maxHeight: maxHeight.map { $0 - Self.topPadding - Self.bottomPadding }) {
            if let version = model.updates.justUpdatedTo {
                UpdatedBanner(model: model, version: version)
                    .padding(.bottom, DashboardLayout.sectionSpacing)
                    .transition(.opacity.combined(with: .move(edge: .top)))
            }
            if model.isPreviewing {
                PreviewBanner(model: model)
                    .padding(.bottom, DashboardLayout.sectionSpacing)
                    .transition(.opacity.combined(with: .move(edge: .top)))
            }
            DashboardContent(model: model)
                .dashboardFlexible()
            Hairline()
                .padding(.top, DashboardLayout.sectionSpacing)
                .padding(.bottom, Spacing.xs)
            DashboardFooter(model: model)
                .padding(.trailing, -6)
        }
        .padding(.horizontal, DashboardLayout.padding)
        .padding(.top, Self.topPadding)
        .padding(.bottom, Self.bottomPadding)
        .frame(width: DashboardController.width, alignment: .top)
        .animation(model.dashboardMotion, value: model.isPreviewing)
        .animation(model.dashboardMotion, value: model.updates.justUpdatedTo)
    }
}

/// Switches between startup, missing-sources and live layouts.
private struct DashboardContent: View {
    let model: AppModel

    var body: some View {
        Group {
            switch model.snapshot.state {
            case .starting(let progress):
                LoadingState(model: model, progress: progress)
                    .transition(.opacity)
            case .noSources:
                NoSourcesState(model: model)
                    .transition(.opacity)
            case .running:
                DashboardSections(model: model)
                    .transition(.opacity)
            }
        }
        .animation(Motion.fade, value: StateKind(model.snapshot.state))
    }

    private enum StateKind: Equatable {
        case starting, noSources, running

        init(_ state: MonitorState) {
            switch state {
            case .starting: self = .starting
            case .noSources: self = .noSources
            case .running: self = .running
            }
        }
    }
}

private struct DashboardSections: View {
    let model: AppModel

    @State private var contentHeight: CGFloat = 0
    @State private var viewportHeight: CGFloat = 0

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HeroSection(model: model)
            SectionDivider()
            ScrollView(.vertical) {
                DashboardDetails(model: model)
                    .padding(.leading, DashboardLayout.padding)
                    .padding(.trailing, trailingInset)
                    .onGeometryChange(for: CGFloat.self, of: { $0.size.height }) { contentHeight = $0 }
            }
            .scrollBounceBehavior(.basedOnSize)
            .onGeometryChange(for: CGFloat.self, of: { $0.size.height }) { viewportHeight = $0 }
            .padding(.horizontal, -DashboardLayout.padding)
        }
    }

    /// Legacy scroll bars take layout width; give it back from the margin so columns stay aligned with the hero.
    private var trailingInset: CGFloat {
        let overflows = contentHeight > viewportHeight + 0.5
        guard overflows, NSScroller.preferredScrollerStyle == .legacy else { return DashboardLayout.padding }
        let scroller = NSScroller.scrollerWidth(for: .regular, scrollerStyle: .legacy)
        return max(Spacing.xxs, DashboardLayout.padding - scroller)
    }
}

/// Everything under the hero; the part that scrolls on short screens.
private struct DashboardDetails: View {
    let model: AppModel

    var body: some View {
        let warnings = model.snapshot.warnings
        VStack(alignment: .leading, spacing: 0) {
            LiveChartSection(model: model)
            SectionDivider()
            SourcesSection(model: model)
            SectionDivider()
            AgentsSection(model: model)
            SectionDivider()
            TodaySection(model: model)
            if !warnings.isEmpty {
                WarningsFootnote(model: model, warnings: warnings)
                    .padding(.top, DashboardLayout.sectionSpacing)
                    .transition(.opacity)
            }
        }
        .animation(model.dashboardMotion, value: warnings.isEmpty)
    }
}
