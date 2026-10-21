import AppKit
import SwiftUI

/// The agent rows, clipped to `height` so the list opens and closes like a drawer. Once the rows outgrow it the region
/// scrolls, with soft fades on the edges that have more rows behind them (DESIGN section 4.4).
struct AgentsListRegion<Content: View>: View {
    let height: CGFloat
    let scrolls: Bool
    let motion: Animation
    @ViewBuilder let content: Content

    @State private var contentFrame: CGRect = .zero

    var body: some View {
        ScrollViewReader { proxy in
            ScrollView(.vertical) {
                VStack(alignment: .leading, spacing: 0) {
                    Color.clear
                        .frame(height: 0)
                        .id(RegionMetrics.top)
                    content
                }
                .onGeometryChange(for: CGRect.self) { $0.frame(in: .named(RegionMetrics.space)) } action: { contentFrame = $0 }
            }
            .coordinateSpace(.named(RegionMetrics.space))
            .scrollDisabled(!scrolls)
            // Legacy scroll bars would take width from the tok/s column; the edge fades still show there is more.
            .scrollIndicators(NSScroller.preferredScrollerStyle == .legacy ? .never : .automatic)
            .scrollIndicatorsFlash(trigger: scrolls)
            .frame(height: height, alignment: .top)
            .mask {
                EdgeFadeMask(fadesTop: fadesTop, fadesBottom: fadesBottom, fade: RegionMetrics.fade)
            }
            .onChange(of: scrolls) { _, scrolls in
                guard !scrolls else { return }
                withAnimation(motion) { proxy.scrollTo(RegionMetrics.top, anchor: .top) }
            }
        }
    }

    private var fadesTop: Bool { scrolls && contentFrame.minY < -0.5 }
    /// Before the first measurement the rows sit at the top, so a scrolling region has more below.
    private var fadesBottom: Bool { scrolls && (contentFrame.isEmpty || contentFrame.maxY > height + 0.5) }
}

private enum RegionMetrics {
    static let space = "agentsListRegion"
    static let top = "agentsListTop"
    static let fade: CGFloat = 16
}

private struct EdgeFadeMask: View {
    let fadesTop: Bool
    let fadesBottom: Bool
    let fade: CGFloat

    var body: some View {
        VStack(spacing: 0) {
            LinearGradient(colors: [.black.opacity(fadesTop ? 0 : 1), .black], startPoint: .top, endPoint: .bottom)
                .frame(height: fade)
            Rectangle()
            LinearGradient(colors: [.black, .black.opacity(fadesBottom ? 0 : 1)], startPoint: .top, endPoint: .bottom)
                .frame(height: fade)
        }
        .animation(Motion.fade, value: fadesTop)
        .animation(Motion.fade, value: fadesBottom)
    }
}
