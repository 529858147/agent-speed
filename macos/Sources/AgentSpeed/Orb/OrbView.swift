import AgentSpeedCore
import SwiftUI

/// Where an orb is shown; the embedded variant is capped at Soft effects and draws an opaque body.
enum OrbStyle: Sendable {
    case floating
    case embedded
}

/// The orb instrument (DESIGN section 2): a static body under one GPU-drawn layer with the gauge, effects and readout
/// (the dashboard's embedded orb carries the hero vehicle instead, DESIGN 4c). The GPU layer runs at 60 fps (the
/// embedded orb at the display's cadence) only while something moves, at 10–30 fps while the hero's parked vehicle
/// breathes, and is paused otherwise.
struct OrbView: View {
    /// The canvas is this many times the disc diameter so the aura and sparks can leave the disc.
    static let canvasScale: CGFloat = 1.5

    let model: AppModel
    var diameter: CGFloat
    var style: OrbStyle = .embedded
    var interaction: OrbInteraction?

    @State private var ownAnimator: OrbAnimator
    private let injectedAnimator: OrbAnimator?
    private let fixedTime: TimeInterval?
    private let forcesSolidBody: Bool

    @Environment(\.orbStillTime) private var environmentStillTime
    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.colorSchemeContrast) private var contrast

    init(model: AppModel, diameter: CGFloat, style: OrbStyle = .embedded, interaction: OrbInteraction? = nil) {
        self.model = model
        self.diameter = diameter
        self.style = style
        self.interaction = interaction
        _ownAnimator = State(initialValue: OrbAnimator(style: style))
        injectedAnimator = nil
        fixedTime = nil
        forcesSolidBody = false
    }

    /// A still frame at `time` from a prepared animator (previews).
    init(model: AppModel, diameter: CGFloat, style: OrbStyle, animator: OrbAnimator, time: TimeInterval, interaction: OrbInteraction? = nil) {
        self.model = model
        self.diameter = diameter
        self.style = style
        self.interaction = interaction
        _ownAnimator = State(initialValue: animator)
        injectedAnimator = animator
        fixedTime = time
        forcesSolidBody = true
    }

    private var canvasSide: CGFloat { diameter * Self.canvasScale }

    var body: some View {
        let animator = injectedAnimator ?? ownAnimator
        let content = makeContent()
        let reduceMotion = model.accessibility.reduceMotion
        let hovered = style == .floating && (interaction?.isHovered ?? false)
        ZStack {
            OrbBody(
                diameter: diameter,
                canvasSide: canvasSide,
                tokens: content.tokens,
                opacity: model.settings.orbOpacity,
                solid: forcesSolidBody || style == .embedded || model.accessibility.reduceTransparency,
                increaseContrast: content.increaseContrast
            )
            .equatable()
            .drawingGroup()
            if let stillTime = fixedTime ?? environmentStillTime {
                let _ = fixedTime == nil ? animator.primeForStill(model: model) : ()
                if let image = OrbSnapshot.image(
                    OrbSceneBuilder.scene(content: content, animator: animator, model: model, canvasSide: canvasSide, scale: 2, time: stillTime),
                    side: canvasSide,
                    scale: 2
                ) {
                    Image(decorative: image, scale: 2)
                }
            } else {
                OrbMetalView(mode: animator.frameMode, followsDisplay: style == .embedded, redrawKey: redrawKey(content: content)) { size, scale, time in
                    OrbSceneBuilder.scene(content: content, animator: animator, model: model, canvasSide: size.width, scale: scale, time: time)
                }
                .frame(width: canvasSide, height: canvasSide)
                idleSweep(animator: animator, tokens: content.tokens)
            }
        }
        .frame(width: canvasSide, height: canvasSide)
        .scaleEffect(hovered && !reduceMotion ? 1.03 : 1)
        .animation(reduceMotion ? nil : Motion.hover, value: hovered)
        .padding(style == .embedded ? -(canvasSide - diameter) / 2 : 0)
        .allowsHitTesting(false)
        .accessibilityHidden(true)
        .onAppear {
            guard fixedTime == nil, environmentStillTime == nil else { return }
            animator.attach(model: model, interaction: interaction)
        }
        .onDisappear {
            guard fixedTime == nil, environmentStillTime == nil else { return }
            animator.detach()
        }
    }

    private struct RedrawKey: Hashable {
        var content: String
        var dark: Bool
        var readout: Double
        var heat: Double
        var tier: SpeedTier
        var title: SpeedTitle
        var effects: EffectsLevel
        var opacity: Double
    }

    private func redrawKey(content: OrbContent) -> AnyHashable {
        let gauge = model.gauge
        let text = [content.unit, content.titleLabel, content.infoLine, content.language.rawValue]
            + content.generating.map(\.rawValue) + ["\(content.diameter)", "\(content.increaseContrast)"]
        return AnyHashable(RedrawKey(
            content: text.joined(separator: "|"),
            dark: content.tokens.isDark,
            readout: gauge.readoutValue,
            heat: gauge.targetHeat,
            tier: gauge.tier,
            title: gauge.title,
            effects: model.settings.effects,
            opacity: model.settings.orbOpacity
        ))
    }

    private func idleSweep(animator: OrbAnimator, tokens: OrbTokens) -> some View {
        let gauge = model.gauge
        let effects = animator.effects(for: model)
        let visible = interaction?.isVisibleOnScreen ?? true
        let color = tokens.isDark ? RGBColor.white.opacity(0.18) : HeatPalette.color(0.02).opacity(0.45)
        return OrbIdleSweep(
            diameter: diameter,
            canvasSide: canvasSide,
            color: color,
            active: gauge.tier == .idle && effects.idleSweep && visible,
            reduceMotion: effects.reduceMotion,
            epoch: animator.idleEpoch(for: gauge)
        )
        .frame(width: canvasSide, height: canvasSide)
    }

    /// Reads the observable inputs here, in the body, so any change redraws even while frames are paused.
    private func makeContent() -> OrbContent {
        let l10n = model.l10n
        let gauge = model.gauge
        let snapshot = model.store.snapshot
        let todayTokens = model.settings.metric == .output ? snapshot.today.output : snapshot.today.total
        let generating = snapshot.agents.filter { $0.phase == .generating }.map(\.source)
        let increaseContrast = contrast == .increased || model.accessibility.increaseContrast
        return OrbContent(
            diameter: diameter,
            tokens: OrbTokens.forScheme(colorScheme),
            increaseContrast: increaseContrast,
            unit: l10n("unit.tokS"),
            titleLabel: l10n(gauge.title.titleKey),
            previousTitleLabel: l10n(gauge.previousTitle.titleKey),
            infoLine: l10n("today.summary", SpeedFormat.tokens(todayTokens), snapshot.agents.count),
            peakLabel: l10n("peak.new"),
            latinTracking: l10n.language.usesLatinCase,
            language: l10n.language,
            generating: generating,
            showsReadout: style == .floating
        )
    }
}

extension EnvironmentValues {
    /// When set, every orb in the subtree renders a still frame at this time (previews).
    @Entry var orbStillTime: TimeInterval?
}
