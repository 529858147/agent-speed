import SwiftUI

/// One settings pane at the window's fixed width and its natural height.
struct SettingsPaneView: View {
    let pane: SettingsPane
    let model: AppModel

    static let width: CGFloat = 520

    var body: some View {
        content
            .frame(width: Self.width)
            .fixedSize(horizontal: false, vertical: true)
    }

    @ViewBuilder
    private var content: some View {
        switch pane {
        case .general: GeneralPane(model: model)
        case .menuBar: MenuBarPane(model: model)
        case .orb: OrbPane(model: model)
        case .effects: EffectsPane(model: model)
        case .sources: SourcesPane(model: model)
        case .about: AboutPane(model: model)
        }
    }
}

/// A pane inside the window: reports its natural height and stays pinned to the top while the window resizes. When
/// the screen is shorter than the pane, the window stops at the screen and the pane scrolls.
struct SettingsPaneHost: View {
    let pane: SettingsPane
    let model: AppModel
    let onHeightChange: @MainActor (CGFloat) -> Void

    var body: some View {
        // The window follows the pane's natural height; only a pane taller than the screen allows scrolls.
        ScrollView(.vertical) {
            SettingsPaneView(pane: pane, model: model)
                .onGeometryChange(for: CGFloat.self) { proxy in
                    proxy.size.height
                } action: { height in
                    onHeightChange(height)
                }
        }
        .scrollBounceBehavior(.basedOnSize)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .appEnvironment(model)
    }
}

extension View {
    /// The grouped, non-scrolling form every pane uses.
    func settingsForm() -> some View {
        formStyle(.grouped)
            .scrollDisabled(true)
    }
}
