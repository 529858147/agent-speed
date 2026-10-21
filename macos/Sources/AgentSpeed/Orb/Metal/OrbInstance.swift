import AgentSpeedHero
import CoreGraphics
import simd

/// One instanced shape for the orb shaders (layout matches `Instance` in `OrbShaderSource`).
struct OrbInstance {
    enum Shape: Float {
        case roundedRect = 0
        case circle = 1
        case ring = 2
        case capsule = 3
        case radial = 4
        case arc = 5
    }

    var a: SIMD4<Float>
    var b: SIMD4<Float>
    var color: SIMD4<Float>
    var color2: SIMD4<Float>
    var extra: SIMD4<Float>

    private init(shape: Shape, center: CGPoint, rotation: Double = 0, size: SIMD2<Float>, corner: Float = 0,
                 glow: CGFloat, color: RGBColor, tail: RGBColor? = nil, core: Double, glowAlpha: Double,
                 p0: Float = 0, p1: Float = 0) {
        a = SIMD4(Float(center.x), Float(center.y), Float(rotation), shape.rawValue)
        b = SIMD4(size.x, size.y, corner, Float(glow))
        self.color = color.simd
        color2 = (tail ?? color).simd
        extra = SIMD4(Float(core), Float(glowAlpha), p0, p1)
    }

    static func roundedRect(center: CGPoint, halfSize: CGSize, rotation: Double, corner: CGFloat, color: RGBColor,
                            core: Double = 1, glow: CGFloat = 0, glowAlpha: Double = 0) -> OrbInstance {
        OrbInstance(shape: .roundedRect, center: center, rotation: rotation,
                    size: SIMD2(Float(halfSize.width), Float(halfSize.height)), corner: Float(corner),
                    glow: glow, color: color, core: core, glowAlpha: glowAlpha)
    }

    static func circle(center: CGPoint, radius: CGFloat, color: RGBColor, core: Double = 1, glow: CGFloat = 0,
                       glowAlpha: Double = 0) -> OrbInstance {
        OrbInstance(shape: .circle, center: center, size: SIMD2(Float(radius), 0), glow: glow, color: color,
                    core: core, glowAlpha: glowAlpha)
    }

    static func ring(center: CGPoint, radius: CGFloat, width: CGFloat, color: RGBColor, core: Double = 1,
                     glow: CGFloat = 0, glowAlpha: Double = 0) -> OrbInstance {
        OrbInstance(shape: .ring, center: center, size: SIMD2(Float(radius), Float(width)), glow: glow,
                    color: color, core: core, glowAlpha: glowAlpha)
    }

    /// A round-capped line from `tail` to `head`, colored `tailColor` → `color`.
    static func capsule(from tail: CGPoint, to head: CGPoint, width: CGFloat, color: RGBColor, tailColor: RGBColor? = nil,
                        core: Double = 1, glow: CGFloat = 0, glowAlpha: Double = 0) -> OrbInstance {
        let dx = head.x - tail.x, dy = head.y - tail.y
        let length = max(0.001, sqrt(dx * dx + dy * dy))
        let center = CGPoint(x: (head.x + tail.x) / 2, y: (head.y + tail.y) / 2)
        return OrbInstance(shape: .capsule, center: center, rotation: atan2(dy, dx),
                           size: SIMD2(Float(length), Float(width)), glow: glow, color: color, tail: tailColor,
                           core: core, glowAlpha: glowAlpha)
    }

    /// A soft radial halo between two radii.
    static func radial(center: CGPoint, inner: CGFloat, outer: CGFloat, color: RGBColor, alpha: Double) -> OrbInstance {
        OrbInstance(shape: .radial, center: center, size: .zero, glow: 0, color: color, core: alpha, glowAlpha: 0,
                    p0: Float(inner), p1: Float(outer))
    }

    /// A tapered arc from `start` over `sweep` (degrees, clockwise on screen), brightest at the head.
    static func arc(center: CGPoint, radius: CGFloat, width: CGFloat, start: Double, sweep: Double, color: RGBColor,
                    tailColor: RGBColor, glow: CGFloat = 0, glowAlpha: Double = 0) -> OrbInstance {
        OrbInstance(shape: .arc, center: center, size: SIMD2(Float(radius), Float(width)), glow: glow, color: color,
                    tail: tailColor, core: 1, glowAlpha: glowAlpha,
                    p0: Float(start * .pi / 180), p1: Float(sweep * .pi / 180))
    }
}

extension RGBColor {
    var simd: SIMD4<Float> { SIMD4(Float(red), Float(green), Float(blue), Float(alpha.clamped(to: 0...1))) }
}

/// Instances grouped by blending, in draw order.
struct OrbBatch {
    enum Blend {
        case over
        case additive
    }

    var blend: Blend
    var instances: [OrbInstance] = []
}

/// The dashboard hero's vehicle layer for one frame: canvas unit (0, 0) at `origin`, `unit` points per canvas unit.
struct OrbVehicleLayer {
    let list: HeroDrawList
    let origin: CGPoint
    let unit: CGFloat
}

struct OrbScene {
    /// Drawn first, over the body and under everything else (DESIGN 4c, Placement).
    var vehicle: OrbVehicleLayer?
    var batches: [OrbBatch] = []
    /// Drawn after all shapes, in order.
    var texts: [OrbTextDraw] = []

    mutating func add(_ instance: OrbInstance, blend: OrbBatch.Blend) {
        if let last = batches.indices.last, batches[last].blend == blend {
            batches[last].instances.append(instance)
        } else {
            batches.append(OrbBatch(blend: blend, instances: [instance]))
        }
    }

    var instanceCount: Int { batches.reduce(0) { $0 + $1.instances.count } }
}
