import AgentSpeedOrbModel
import SwiftUI

/// The selected orb's unit (DESIGN 9, Orb, Switching 3): a two-segment control directly under the style cards.
/// Heat, tiers and titles keep following tokens.
struct OrbUnitSection: View {
    let model: AppModel
    let orb: OrbConfig

    var body: some View {
        let l10n = model.l10n
        let orbs = model.orbs
        let id = orb.id
        Section {
            SegmentedRow(title: l10n("orbs.unit"), selection: Binding(get: { orb.unit }, set: { orbs.setUnit($0, of: id) }),
                         segmentWidth: 84, count: 2) {
                Text(l10n("unit.tokS")).tag(OrbUnit.tokens)
                Text(model.cost.unitPerSecond).tag(OrbUnit.cost)
            }
            .help(l10n("cost.apiEquivalent.hint"))
        } footer: {
            if orb.unit == .cost { SectionFootnote(l10n("cost.apiEquivalent.hint")) }
        }
    }
}
