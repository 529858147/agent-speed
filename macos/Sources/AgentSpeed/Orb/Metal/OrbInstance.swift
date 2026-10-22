import AgentSpeedHero
import CoreGraphics
import simd

/// One instanced shape for the orb shaders (layout matches `Instance` in `OrbShaderSource`).
///
/// Angles taken by the builders are degrees clockwise from 3 o'clock in screen space (y down); glows are given as the
/// Gaussian falloff radius of the shader (`OrbInstance.glow(sigma:)` converts a spec σ).
struct OrbInstance {
    enum Shape: Float {
        case roundedRect = 0
        case circle = 1
        case ring = 2
        case capsule = 3
        case radial = 4
        case arc = 5
        case bandArc = 6
        case ellipse = 7
        case blade = 8
    }

    enum Fill: Float {
        case solid = 0
        case linear = 1
        case radial = 2
        case palette = 3
    }

    var a: SIMD4<Float>
    var b: SIMD4<Float>
    var color: SIMD4<Float>
    var color2: SIMD4<Float>
    var extra: SIMD4<Float>
    var extra2: SIMD4<Float>

    private init(shape: Shape, center: CGPoint, rotation: Double = 0, size: SIMD2<Float>, corner: Float = 0,
                 glow: CGFloat, color: RGBColor, tail: RGBColor? = nil, core: Double, glowAlpha: Double,
                 p0: Float = 0, p1: Float = 0, extra2: SIMD4<Float> = SIMD4(0, 0, 0, 1)) {
        a = SIMD4(Float(center.x), Float(center.y), Float(rotation), shape.rawValue)
        b = SIMD4(size.x, size.y, corner, Float(glow))
        self.color = color.simd
        color2 = (tail ?? color).simd
        extra = SIMD4(Float(core), Float(glowAlpha), p0, p1)
        self.extra2 = extra2
    }

    /// Shader glow radius for a Gaussian glow of standard deviation `sigma` (a canvas `shadowBlur` of 2σ).
    static func glow(sigma: CGFloat) -> CGFloat { sigma * 1.414 }

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

    /// A constant-width arc from `start` over `sweep` degrees (clockwise, 0...360), colored `startColor` → `color`
    /// with its alpha rising from `startAlpha` × to 1 × at the end; butt caps unless `roundCaps`.
    static func bandArc(center: CGPoint, radius: CGFloat, width: CGFloat, start: Double, sweep: Double, color: RGBColor,
                        startColor: RGBColor? = nil, startAlpha: Double = 1, roundCaps: Bool = false, core: Double = 1,
                        glow: CGFloat = 0, glowAlpha: Double = 0) -> OrbInstance {
        OrbInstance(shape: .bandArc, center: center, size: SIMD2(Float(radius), Float(width)), corner: roundCaps ? 1 : 0,
                    glow: glow, color: color, tail: startColor, core: core, glowAlpha: glowAlpha,
                    p0: Float(start * .pi / 180), p1: Float(max(0.0001, min(360, sweep)) * .pi / 180),
                    extra2: SIMD4(Fill.solid.rawValue, 0, 0, Float(startAlpha)))
    }

    /// A band arc whose color follows the heat palette from `heatFrom` at the start to `heatTo` at the end (the
    /// spectrum of a dial); `alpha` scales it and `startAlpha` ramps it like `bandArc`.
    static func paletteArc(center: CGPoint, radius: CGFloat, width: CGFloat, start: Double, sweep: Double,
                           heatFrom: Double, heatTo: Double, alpha: Double, startAlpha: Double = 1, roundCaps: Bool = false,
                           core: Double = 1, glow: CGFloat = 0, glowAlpha: Double = 0) -> OrbInstance {
        OrbInstance(shape: .bandArc, center: center, size: SIMD2(Float(radius), Float(width)), corner: roundCaps ? 1 : 0,
                    glow: glow, color: RGBColor.white.opacity(alpha), core: core, glowAlpha: glowAlpha,
                    p0: Float(start * .pi / 180), p1: Float(max(0.0001, min(360, sweep)) * .pi / 180),
                    extra2: SIMD4(Fill.palette.rawValue, Float(heatFrom), Float(heatTo), Float(startAlpha)))
    }

    /// An ellipse with radii `radii`, rotated by `rotation` degrees.
    static func ellipse(center: CGPoint, radii: CGSize, rotation: Double = 0, color: RGBColor, core: Double = 1,
                        glow: CGFloat = 0, glowAlpha: Double = 0) -> OrbInstance {
        OrbInstance(shape: .ellipse, center: center, rotation: rotation * .pi / 180,
                    size: SIMD2(Float(radii.width), Float(radii.height)), glow: glow, color: color, core: core,
                    glowAlpha: glowAlpha)
    }

    /// The needle: pivot at `pivot`, pointing at `angle` degrees, a tapered blade to a round tip at `tipLength`
    /// (half widths `pivotHalfWidth` → `tipHalfWidth`) and a counterweight `tailLength` behind the pivot.
    static func blade(pivot: CGPoint, angle: Double, tipLength: CGFloat, tailLength: CGFloat, pivotHalfWidth: CGFloat,
                      tipHalfWidth: CGFloat, tailHalfWidth: CGFloat, color: RGBColor, core: Double = 1,
                      glow: CGFloat = 0, glowAlpha: Double = 0) -> OrbInstance {
        OrbInstance(shape: .blade, center: pivot, rotation: angle * .pi / 180,
                    size: SIMD2(Float(tipLength), Float(tailLength)), corner: Float(tailHalfWidth), glow: glow,
                    color: color, core: core, glowAlpha: glowAlpha, p0: Float(pivotHalfWidth), p1: Float(tipHalfWidth))
    }

    // MARK: Fills (circle, ring, rounded rect, ellipse, blade)

    /// Linear gradient from `color` to `end` along `angle` degrees, over `halfLength` points either side of the center.
    func linearGradient(to end: RGBColor, angle: Double = 90, halfLength: CGFloat) -> OrbInstance {
        var copy = self
        copy.color2 = end.simd
        copy.extra2 = SIMD4(Fill.linear.rawValue, Float(angle * .pi / 180), Float(halfLength), 1)
        return copy
    }

    /// Radial gradient from `color` at `offset` (from the shape center) to `end` at `radius`.
    func radialGradient(to end: RGBColor, offset: CGPoint = .zero, radius: CGFloat) -> OrbInstance {
        var copy = self
        copy.color2 = end.simd
        copy.extra2 = SIMD4(Fill.radial.rawValue, Float(offset.x), Float(offset.y), Float(radius))
        return copy
    }

    /// Multiplies every alpha (crossfades).
    mutating func scaleAlpha(_ factor: Float) {
        color.w *= factor
        color2.w *= factor
    }
}

extension RGBColor {
    var simd: SIMD4<Float> { SIMD4(Float(red), Float(green), Float(blue), Float(alpha.clamped(to: 0...1))) }
}

/// The dashboard hero's vehicle layer for one frame: canvas unit (0, 0) at `origin`, `unit` points per canvas unit.
struct OrbVehicleLayer {
    let list: HeroDrawList
    let origin: CGPoint
    let unit: CGFloat
    /// Opacity of the whole layer (the vehicle ↔ title badge crossfade, DESIGN 1 Title themes).
    var alpha: Float = 1
}

/// Blending of a run of instances: light adds up on dark glass and blends normally on porcelain.
enum OrbBlend: Equatable {
    case over
    case additive
}

/// One frame of GPU work, back to front. Its storage is reused frame after frame (`reset()` keeps capacity), so a
/// warm scene never allocates.
struct OrbScene {
    enum Pass: Equatable {
        case shapes(OrbBlend)
        /// The liquid volume shader in a mode (0 the volume, 1 the meniscus).
        case liquid(Int32)
    }

    struct Batch {
        var pass: Pass
        /// Clip radius around the canvas center in points, 0 for none.
        var clip: Float
        /// Width of a soft clip edge inside `clip` (0: an anti-aliased hard edge).
        var feather: Float = 0
        var start: Int
        var count: Int
    }

    /// Drawn first, over the body and under everything else (DESIGN 4c, Placement).
    var vehicle: OrbVehicleLayer?
    private(set) var instances: ContiguousArray<OrbInstance> = []
    private(set) var batches: ContiguousArray<Batch> = []
    /// Drawn after all shapes, in order.
    private(set) var texts: ContiguousArray<OrbTextDraw> = []
    /// Parameters of the liquid volume when a `.liquid` pass is present.
    var liquid = OrbLiquidUniforms()
    /// Multiplies the alpha of everything added while set (style crossfades).
    var alphaScale: Float = 1
    /// Clip radius for shapes added from now on (0 = none).
    var clipRadius: CGFloat = 0
    /// Soft edge of that clip: fully clear at `clipRadius`, fully kept `clipFeather` inside it.
    var clipFeather: CGFloat = 0
    /// Scale of everything around the canvas center (the 1.03 hover scale).
    var contentScale: CGFloat = 1
    var center: CGPoint = .zero
    /// Pixels per point of the target, for text rasterization.
    var backingScale: CGFloat = 2

    init(capacity: Int = 0) {
        instances.reserveCapacity(capacity)
        batches.reserveCapacity(32)
        texts.reserveCapacity(48)
    }

    mutating func reset() {
        vehicle = nil
        instances.removeAll(keepingCapacity: true)
        batches.removeAll(keepingCapacity: true)
        texts.removeAll(keepingCapacity: true)
        alphaScale = 1
        clipRadius = 0
        clipFeather = 0
        contentScale = 1
    }

    mutating func add(_ instance: OrbInstance, blend: OrbBlend) {
        var instance = instance
        if alphaScale < 1 { instance.scaleAlpha(alphaScale) }
        let clip = Float(clipRadius)
        let feather = Float(clipFeather)
        if let last = batches.indices.last, batches[last].pass == .shapes(blend), batches[last].clip == clip,
           batches[last].feather == feather, batches[last].start + batches[last].count == instances.count {
            batches[last].count += 1
        } else {
            batches.append(Batch(pass: .shapes(blend), clip: clip, feather: feather, start: instances.count, count: 1))
        }
        instances.append(instance)
    }

    mutating func addLiquid(mode: Int32) {
        batches.append(Batch(pass: .liquid(mode), clip: 0, start: instances.count, count: 0))
    }

    mutating func add(_ text: OrbTextDraw) {
        var text = text
        if alphaScale < 1 {
            text.instance.color.w *= alphaScale
            text.instance.glow.w *= alphaScale
        }
        texts.append(text)
    }

    var instanceCount: Int { instances.count }
    var hasLiquid: Bool { batches.contains { if case .liquid = $0.pass { true } else { false } } }
}
