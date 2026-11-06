import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit

/// The colors of a board style in one appearance (the prototypes' `--ink`, `--line`, … of each theme). Increase
/// Contrast (DESIGN 2c Accessibility) strengthens the secondary inks and lines, makes fills solid and drops glows.
struct BoardPalette {
    let style: BoardStyle
    let dark: Bool
    let highContrast: Bool

    var ink: RGBColor {
        switch (style, dark) {
        case (.glass, true): RGBColor(red: 244 / 255, green: 246 / 255, blue: 252 / 255, alpha: 0.96)
        case (.glass, false): RGBColor(red: 17 / 255, green: 20 / 255, blue: 30 / 255, alpha: 0.94)
        case (.timing, true): RGBColor(hex: 0xF5F7FB)
        case (.timing, false): RGBColor(hex: 0x0E1118)
        case (.neon, true): RGBColor(hex: 0xF3F6FF)
        case (.neon, false): RGBColor(hex: 0x0A0D18)
        }
    }

    /// Secondary text.
    var ink2: RGBColor { inkBase.opacity(highContrast ? 0.86 : dark ? (style == .glass ? 0.62 : 0.66) : (style == .glass ? 0.6 : 0.62)) }
    /// Tertiary text: ranks, details, footer labels.
    var ink3: RGBColor { inkBase.opacity(highContrast ? 0.74 : dark ? (style == .timing ? 0.36 : style == .neon ? 0.36 : 0.38) : 0.38) }

    private var inkBase: RGBColor {
        switch (style, dark) {
        case (.glass, true): RGBColor(red: 226 / 255, green: 230 / 255, blue: 242 / 255)
        case (.glass, false): RGBColor(red: 30 / 255, green: 36 / 255, blue: 52 / 255)
        case (.timing, true): RGBColor(red: 232 / 255, green: 236 / 255, blue: 245 / 255)
        case (.timing, false): RGBColor(red: 14 / 255, green: 17 / 255, blue: 24 / 255)
        case (.neon, true): RGBColor(red: 214 / 255, green: 224 / 255, blue: 1)
        case (.neon, false): RGBColor(red: 10 / 255, green: 13 / 255, blue: 24 / 255)
        }
    }

    /// Hairlines: the hero rule, row separators, sub-row connectors.
    var line: RGBColor {
        if highContrast { return inkBase.opacity(0.5) }
        switch (style, dark) {
        case (.glass, true): return RGBColor.white.opacity(0.08)
        case (.glass, false): return RGBColor.black.opacity(0.07)
        case (.timing, _): return (dark ? RGBColor.white : RGBColor.black).opacity(0.06)
        case (.neon, true): return RGBColor(red: 140 / 255, green: 170 / 255, blue: 1, alpha: 0.10)
        case (.neon, false): return RGBColor(red: 20 / 255, green: 30 / 255, blue: 80 / 255, alpha: 0.08)
        }
    }

    /// Faint fills: hovered rows, chips.
    var fill: RGBColor {
        switch (style, dark) {
        case (.glass, true): RGBColor.white.opacity(0.055)
        case (.glass, false): RGBColor.black.opacity(0.045)
        case (.timing, true): RGBColor.white.opacity(0.05)
        case (.timing, false): RGBColor.black.opacity(0.045)
        case (.neon, true): RGBColor(red: 140 / 255, green: 170 / 255, blue: 1, alpha: 0.07)
        case (.neon, false): RGBColor(red: 20 / 255, green: 30 / 255, blue: 80 / 255, alpha: 0.05)
        }
    }

    /// Unlit LED segments.
    var unlit: RGBColor {
        switch (style, dark) {
        case (.neon, true): RGBColor(red: 140 / 255, green: 170 / 255, blue: 1, alpha: highContrast ? 0.4 : 0.16)
        case (.neon, false): RGBColor(red: 20 / 255, green: 30 / 255, blue: 80 / 255, alpha: highContrast ? 0.4 : 0.14)
        default: (dark ? RGBColor.white : RGBColor.black).opacity(highContrast ? 0.4 : dark ? 0.18 : 0.14)
        }
    }

    /// Quiet values of the live metrics (`–`, resting grades, a real `0`, the subscription tag): `ink-3`, `ink-2` with
    /// Increase Contrast (DESIGN 2c Live metrics, Accessibility).
    var quiet: RGBColor { highContrast ? ink2 : ink3 }

    /// The sub-gauge's hairlines: glass's dial track, timing's data box stroke and separators.
    var line2: RGBColor {
        if highContrast { return ink3 }
        switch (style, dark) {
        case (.glass, true): return RGBColor.white.opacity(0.14)
        case (.glass, false): return RGBColor.black.opacity(0.11)
        case (.neon, true): return RGBColor(red: 140 / 255, green: 170 / 255, blue: 1, alpha: 0.16)
        case (.neon, false): return RGBColor(red: 20 / 255, green: 30 / 255, blue: 80 / 255, alpha: 0.12)
        default: return (dark ? RGBColor.white : RGBColor.black).opacity(0.1)
        }
    }

    /// The `ink-3` token itself, not strengthened by Increase Contrast: what Increase Contrast draws in `ink-3` behind
    /// a value (glass's dial track, neon's unlit meter segments; DESIGN 2c Accessibility), so the value, a resting one
    /// and the proof mark's dots still stand out from it.
    var ink3Token: RGBColor { inkBase.opacity(dark ? (style == .glass ? 0.38 : 0.36) : 0.4) }

    /// Glass's dial track: `line-2`, `ink-3` with Increase Contrast.
    var dialTrack: RGBColor { highContrast ? ink3Token : line2 }

    /// Glass's pearl gradient (`pearl-c` → `pearl-a` → `pearl-b`).
    var pearl: [RGBColor] {
        dark ? [RGBColor(hex: 0xA5F3FC), RGBColor(hex: 0xC7D2FE), RGBColor(hex: 0xF5D0FE)]
            : [RGBColor(hex: 0x1AA3C4), RGBColor(hex: 0x6D7CF0), RGBColor(hex: 0xC05BD9)]
    }

    /// Neon's LED digits (`--led`), their ghost segments and the mind meter's lit segments.
    var led: RGBColor { dark ? RGBColor(hex: 0xEAF2FF) : RGBColor(hex: 0x141A33) }
    var ghost: RGBColor { dark ? RGBColor(red: 160 / 255, green: 190 / 255, blue: 1, alpha: 0.075) : RGBColor(red: 20 / 255, green: 30 / 255, blue: 80 / 255, alpha: 0.06) }
    var mind: RGBColor { highContrast ? ink : dark ? RGBColor(hex: 0xCFE0FF) : RGBColor(hex: 0x3D4F9A) }
    /// Neon's glow of the live metrics' digits (dark only): the table's and the rows' 6 pt.
    static let ledGlow = RGBColor(red: 140 / 255, green: 180 / 255, blue: 1, alpha: 0.45)

    /// Timing's position blocks and footer bar.
    var block: RGBColor { dark ? RGBColor(hex: 0x1B1F2A) : RGBColor(hex: 0xE6E9F0) }
    var blockInk: RGBColor { dark ? RGBColor(hex: 0xE8ECF5) : RGBColor(hex: 0x1A1E28) }
    var slab2: RGBColor {
        switch (style, dark) {
        case (.neon, true): RGBColor(hex: 0x0A0D18)
        case (.neon, false): RGBColor(hex: 0xECEFF7)
        default: dark ? RGBColor(hex: 0x12151D) : RGBColor(hex: 0xF1F3F7)
        }
    }

    /// Glows (text shadows, drop shadows, bar halos) are dropped with Increase Contrast.
    var glows: Bool { !highContrast }

    static let gold = RGBColor(hex: 0xFACC15)
    static let mint = RGBColor(hex: 0x34D399)
    static let green = RGBColor(hex: 0x22C55E)
    static let red = RGBColor(hex: 0xEF4444)

    /// The heat color for graphics (bars, LEDs, rails), tuned for the appearance as everywhere in the app; pushed to
    /// 4.5:1 with Increase Contrast on light boards.
    func heat(_ h: Double) -> RGBColor {
        highContrast && !dark ? DashboardPalette.legible(HeatPalette.color(h), dark: false, minimum: 4.5) : HeatPalette.color(h, dark: dark)
    }

    /// The heat color for text: legible at 4.5:1 (7:1 with Increase Contrast) on light boards.
    func heatText(_ h: Double) -> RGBColor {
        dark ? HeatPalette.color(h) : DashboardPalette.legible(HeatPalette.color(h), dark: false, minimum: highContrast ? 7 : 4.5)
    }

    /// A source's color as the dashboard draws it (DESIGN 4 badges).
    func source(_ kind: SourceKind) -> RGBColor {
        dark ? kind.tint : kind.tint.darkened(0.14)
    }
}
