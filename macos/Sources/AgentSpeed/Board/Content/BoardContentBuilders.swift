import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit

/// The content builder of a board's style (DESIGN 2c Styles). `capsule` nil draws the dashboard's effort capsule.
@MainActor
enum BoardContentBuilders {
    static func make(style: BoardStyle, size: BoardSize, dark: Bool, l10n: Localizer, capsule: BoardCapsuleDrawing?,
                     highContrast: Bool = NSWorkspace.shared.accessibilityDisplayShouldIncreaseContrast) -> BoardContentBuilding {
        let capsules = capsule ?? BoardEffortCapsules.shared
        return switch style {
        case .glass: BoardGlassContent(size: size, dark: dark, highContrast: highContrast, l10n: l10n, capsules: capsules)
        case .timing: BoardTimingContent(size: size, dark: dark, highContrast: highContrast, l10n: l10n, capsules: capsules)
        case .neon: BoardNeonContent(size: size, dark: dark, highContrast: highContrast, l10n: l10n, capsules: capsules)
        }
    }

    /// The board's geometry for a tick (its switches travel with the tick).
    static func geometry(_ style: BoardStyle, _ size: BoardSize, _ tick: BoardTick) -> BoardGeometry {
        BoardGeometry(style, size, readouts: tick.metrics.readouts)
    }
}
