import Observation
import SwiftUI

/// Live disc diameter of the floating orb, animated by the window so the renderer resizes in step with the frame.
@MainActor
@Observable
final class OrbHostState {
    var diameter: CGFloat

    init(diameter: CGFloat) {
        self.diameter = diameter
    }
}

/// Root of the floating orb's hosting view.
struct OrbHostContent: View {
    let model: AppModel
    let state: OrbHostState
    let interaction: OrbInteraction

    var body: some View {
        OrbView(model: model, diameter: state.diameter, style: .floating, interaction: interaction)
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .appEnvironment(model)
    }
}
