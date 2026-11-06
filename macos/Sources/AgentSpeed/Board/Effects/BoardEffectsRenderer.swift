import AgentSpeedOrbModel
import CoreGraphics

/// Draws `BoardEffects` with the orbs' instanced SDF shapes (DESIGN 2c Frame policy, Two layers): the overlay above
/// the rows (bloom, ring, comets, particles, waves, bolts, flash) on a canvas `BoardEffects.margin` larger than the
/// board on every side. Neon's warp under the rows is `BoardWarpLayer`.
/// Glows are analytic Gaussians of the fx.js SVG blurs; nothing allocates once the scene is warm.
enum BoardEffectsRenderer {
    /// A stroke `width` wide blurred by `blur` (σ) is about a Gaussian of σ √(blur² + width² / 12) peaking at
    /// erf(width / (2 √2 blur)).
    private static let cometGlow = (radius: OrbInstance.glow(sigma: 2.8), peak: 0.70)
    private static let ringGlow = (radius: OrbInstance.glow(sigma: 6.24), peak: 0.38)
    /// Streak particles are drawn over the distance they travel in 3.5 frames at 60 fps (fx.js: one frame plus 2.5
    /// frames behind).
    private static let streakSeconds = 3.5 / 60

    /// The bloom outside the board (DESIGN 2c Perimeter): the prototypes' `box-shadow` (blur `bloom × bloomBlur`, so
    /// σ = half of it, and spread `bloom × bloomSpread`) fading out along the board's outline to nothing
    /// `BoardEffects.bloomReach` points out, so the canvas edge never cuts it.
    static func bloom(_ fx: BoardEffects) -> OrbInstance {
        let m = BoardEffects.margin
        return .outsideBloom(center: CGPoint(x: m + fx.side / 2, y: m + fx.side / 2),
                             halfSize: CGSize(width: fx.side / 2, height: fx.side / 2),
                             corner: CGFloat(fx.preset.cornerRadius), spread: CGFloat(fx.bloom * fx.preset.bloomSpread),
                             sigma: CGFloat(fx.bloom * fx.preset.bloomBlur / 2), reach: CGFloat(BoardEffects.bloomReach),
                             color: RGBColor(fx.bloomColor))
    }

    static func overlay(_ fx: BoardEffects, into scene: inout OrbScene, highContrast: Bool = false) {
        scene.reset()
        guard fx.intensity != .off || fx.ringAlpha > 0 else { return }
        let m = BoardEffects.margin
        let half = fx.side / 2 - BoardEffects.outlineInset
        let center = CGPoint(x: m + fx.side / 2, y: m + fx.side / 2)
        let halfSize = CGSize(width: half, height: half)
        let corner = CGFloat(fx.preset.cornerRadius - BoardEffects.outlineInset)
        let length = fx.outline.length

        if !highContrast {
            scene.add(bloom(fx), blend: .over)
            scene.add(.perimeter(center: center, halfSize: halfSize, corner: corner, width: 0,
                                 color: RGBColor(fx.ringColor), core: 0, glow: ringGlow.radius,
                                 glowAlpha: ringGlow.peak * min(1, fx.ringGlowAlpha)), blend: .over)
        }
        scene.add(.perimeter(center: center, halfSize: halfSize, corner: corner,
                             width: CGFloat(highContrast ? 1 : fx.preset.ringWidth),
                             color: RGBColor(fx.ringColor).opacity(highContrast ? 1 : min(1, fx.ringAlpha))), blend: .over)
        guard fx.intensity != .off else { return }

        let head = RGBColor(fx.cometHeadColor)
        let tail = RGBColor(fx.cometTailColor)
        let core = RGBColor(fx.cometCoreColor)
        for index in fx.comets.indices {
            let opacity = fx.comets[index].opacity
            guard opacity > 0.002 else { continue }
            let at = CGFloat(fx.cometHead(index) * length)
            if highContrast {
                scene.add(.perimeter(center: center, halfSize: halfSize, corner: corner, width: 2, head: at,
                                     length: CGFloat(BoardEffects.glowFraction * length), color: head.opacity(opacity)),
                          blend: .over)
                continue
            }
            scene.add(.perimeter(center: center, halfSize: halfSize, corner: corner, width: 2, head: at,
                                 length: CGFloat(BoardEffects.tailFraction * length), color: tail.opacity(0.35 * opacity),
                                 tailAlpha: 0, alphaCurve: 1.2), blend: .over)
            scene.add(.perimeter(center: center, halfSize: halfSize, corner: corner, width: 0, head: at,
                                 length: CGFloat(BoardEffects.glowFraction * length), color: head.opacity(0.9 * opacity),
                                 core: 0, glow: cometGlow.radius, glowAlpha: cometGlow.peak), blend: .over)
            scene.add(.perimeter(center: center, halfSize: halfSize, corner: corner, width: 1.8, head: at,
                                 length: CGFloat(BoardEffects.coreFraction * length), color: core.opacity(opacity)),
                      blend: .over)
        }

        for p in fx.particles {
            let color = RGBColor(p.color).opacity(p.fade)
            let point = CGPoint(x: m + p.x, y: m + p.y)
            if p.streak {
                let from = CGPoint(x: point.x - p.vx * streakSeconds, y: point.y - p.vy * streakSeconds)
                scene.add(.capsule(from: from, to: point, width: CGFloat(p.size), color: color), blend: .additive)
            } else {
                scene.add(.circle(center: point, radius: CGFloat(p.size), color: color), blend: .additive)
            }
        }
        for wave in fx.waves {
            scene.add(.ring(center: CGPoint(x: m + wave.x, y: m + wave.y), radius: CGFloat(max(0, wave.radius)),
                            width: CGFloat(wave.width), color: RGBColor(wave.color).opacity(wave.alpha)), blend: .additive)
        }
        let origin = CGPoint(x: m, y: m)
        for b in fx.bolts.indices where fx.bolts[b].alive {
            let bolt = fx.bolts[b]
            let flicker = bolt.flicker
            let color = RGBColor(bolt.color)
            for k in 0..<bolt.pathCount {
                let branch = k > 0 ? 0.6 : 1
                scene.addPolyline(fx.boltPath(b, k), origin: origin, glowColor: color, coreColor: .white,
                                  outer: OrbStroke(width: 6 * branch, alpha: 0.25 * flicker * branch),
                                  mid: OrbStroke(width: 2.2 * branch, alpha: 0.8 * flicker * branch),
                                  core: OrbStroke(width: 1 * branch, alpha: flicker * branch), blend: .additive)
            }
        }
        if fx.flash > 0 {
            let eased = fx.flash * fx.flash
            scene.add(.roundedRect(center: CGPoint(x: m + fx.side / 2, y: m + fx.side / 2),
                                   halfSize: CGSize(width: fx.side / 2, height: fx.side / 2), rotation: 0,
                                   corner: CGFloat(fx.preset.cornerRadius), color: RGBColor.white.opacity(0.16 * eased)),
                      blend: .additive)
        }
    }
}

extension RGBColor {
    init(_ color: BoardFXColor) {
        self.init(red: Double(color.x), green: Double(color.y), blue: Double(color.z), alpha: Double(color.w))
    }
}
