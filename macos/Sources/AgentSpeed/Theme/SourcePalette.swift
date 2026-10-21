import AgentSpeedCore
import SwiftUI

extension SourceKind {
    var tint: RGBColor {
        switch self {
        case .codex: RGBColor(hex: 0x34D399)
        case .claudeCode: RGBColor(hex: 0xFF8A65)
        case .claudeDesktop: RGBColor(hex: 0xB39DFF)
        }
    }

    /// Source color adjusted for legibility on light surfaces.
    func tint(dark: Bool) -> Color {
        dark ? tint.color : tint.darkened(0.14).color
    }
}
