import AppKit
import CoreText
import Metal

/// A string rasterized once (white, premultiplied) for drawing as a tinted quad.
final class OrbTextTexture {
    let texture: MTLTexture
    /// Quad size in points, including padding.
    let size: CGSize
    /// Distance from the top of the quad to the baseline, in points.
    let baseline: CGFloat
    /// Horizontal advance of the text itself, in points.
    let advance: CGFloat
    let padding: CGFloat

    init(texture: MTLTexture, size: CGSize, baseline: CGFloat, advance: CGFloat, padding: CGFloat) {
        self.texture = texture
        self.size = size
        self.baseline = baseline
        self.advance = advance
        self.padding = padding
    }
}

struct OrbTextStyle: Hashable {
    var size: CGFloat
    var weight: NSFont.Weight
    var monospacedDigits = false
    var tracking: CGFloat = 0
    /// Room around the glyphs for a glow, in points.
    var padding: CGFloat = 1

    func font() -> NSFont {
        monospacedDigits
            ? .monospacedDigitSystemFont(ofSize: size, weight: weight)
            : .systemFont(ofSize: size, weight: weight)
    }
}

/// Rasterizes strings with Core Text into textures and keeps them, shared by every orb so equal sizes share glyphs
/// (digits, the unit and the current titles are built once per size).
@MainActor
final class OrbTextCache {
    static let shared = OrbTextCache()

    private struct Key: Hashable {
        let text: String
        let style: OrbTextStyle
        let language: AppLanguage
        let scale: CGFloat
    }

    private static let limit = 512
    private var entries: [Key: OrbTextTexture] = [:]
    private var overflowLabels: [String] = (0...99).map { "+\($0)" }

    func texture(_ text: String, style: OrbTextStyle, language: AppLanguage, scale: CGFloat) -> OrbTextTexture? {
        let key = Key(text: text, style: style, language: language, scale: scale)
        if let cached = entries[key] { return cached }
        guard let gpu = OrbGPU.shared, let texture = Self.rasterize(text, style: style, language: language, scale: scale, device: gpu.device) else { return nil }
        if entries.count >= Self.limit { entries.removeAll(keepingCapacity: true) }
        entries[key] = texture
        return texture
    }

    func texture(_ character: Character, style: OrbTextStyle, language: AppLanguage, scale: CGFloat) -> OrbTextTexture? {
        texture(String(character), style: style, language: language, scale: scale)
    }

    /// Cap height of a style's font, measured once (creating a font allocates).
    func capHeight(_ style: OrbTextStyle) -> CGFloat {
        if let cached = capHeights[style] { return cached }
        let value = style.font().capHeight
        capHeights[style] = value
        return value
    }

    private var capHeights: [OrbTextStyle: CGFloat] = [:]

    /// The `+n` label of the agent dots.
    func overflowTexture(_ count: Int, style: OrbTextStyle, language: AppLanguage, scale: CGFloat) -> OrbTextTexture? {
        texture(overflowLabels[min(max(count, 0), 99)], style: style, language: language, scale: scale)
    }

    private static func rasterize(_ text: String, style: OrbTextStyle, language: AppLanguage, scale: CGFloat, device: MTLDevice) -> OrbTextTexture? {
        let attributes: [NSAttributedString.Key: Any] = [
            .font: style.font(),
            .kern: style.tracking,
            .foregroundColor: NSColor.white,
            NSAttributedString.Key(kCTLanguageAttributeName as String): language.rawValue,
        ]
        let line = CTLineCreateWithAttributedString(NSAttributedString(string: text, attributes: attributes))
        var ascent: CGFloat = 0
        var descent: CGFloat = 0
        var leading: CGFloat = 0
        let width = CGFloat(CTLineGetTypographicBounds(line, &ascent, &descent, &leading))
        let pad = style.padding
        let size = CGSize(width: ceil(width + pad * 2), height: ceil(ascent + descent + pad * 2))
        let pixelsWide = max(1, Int(ceil(size.width * scale)))
        let pixelsHigh = max(1, Int(ceil(size.height * scale)))
        guard let context = CGContext(
            data: nil, width: pixelsWide, height: pixelsHigh, bitsPerComponent: 8, bytesPerRow: pixelsWide * 4,
            space: CGColorSpace(name: CGColorSpace.sRGB) ?? CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
        ) else { return nil }
        context.scaleBy(x: scale, y: scale)
        context.setShouldSmoothFonts(false)
        context.textPosition = CGPoint(x: pad, y: pad + descent)
        CTLineDraw(line, context)

        let descriptor = MTLTextureDescriptor.texture2DDescriptor(pixelFormat: .rgba8Unorm, width: pixelsWide, height: pixelsHigh, mipmapped: false)
        descriptor.usage = [.shaderRead]
        guard let texture = device.makeTexture(descriptor: descriptor), let data = context.data else { return nil }
        texture.replace(region: MTLRegionMake2D(0, 0, pixelsWide, pixelsHigh), mipmapLevel: 0, withBytes: data, bytesPerRow: pixelsWide * 4)
        return OrbTextTexture(
            texture: texture,
            size: CGSize(width: CGFloat(pixelsWide) / scale, height: CGFloat(pixelsHigh) / scale),
            baseline: pad + ascent,
            advance: width,
            padding: pad
        )
    }
}

/// One text quad (layout matches `TextInstance` in `OrbShaderSource`).
struct OrbTextInstance {
    /// x, y (top-left, points), width, height.
    var rect: SIMD4<Float>
    var color: SIMD4<Float>
    /// Glow color; alpha is the glow strength.
    var glow: SIMD4<Float>
    /// Shear factor, shear pivot y, glow radius (points), clip mode (`OrbTextClip`).
    var params: SIMD4<Float>
    /// Clip rect: min x, min y, max x, max y (points).
    var clip: SIMD4<Float>
}

/// Where a text quad may draw.
enum OrbTextClip: Equatable {
    case none
    case rect(CGRect)
    /// Only above (`below == false`) or below the liquid surface, inside `rect`.
    case liquid(below: Bool, rect: CGRect)

    var mode: Float {
        switch self {
        case .none: 0
        case .rect: 1
        case .liquid(let below, _): below ? 3 : 2
        }
    }

    var rect: SIMD4<Float> {
        switch self {
        case .none: SIMD4(-1e6, -1e6, 1e6, 1e6)
        case .rect(let rect), .liquid(_, let rect):
            SIMD4(Float(rect.minX), Float(rect.minY), Float(rect.maxX), Float(rect.maxY))
        }
    }
}

struct OrbTextDraw {
    let texture: OrbTextTexture
    var instance: OrbTextInstance

    /// Places the text with its baseline at `baseline` and its advance starting at `x`.
    init(_ texture: OrbTextTexture, x: CGFloat, baseline: CGFloat, color: RGBColor, glow: RGBColor? = nil,
         glowRadius: CGFloat = 0, shear: CGFloat = 0, shearPivot: CGFloat = 0, scale: CGFloat = 1, pivot: CGPoint? = nil,
         clip: OrbTextClip = .none) {
        self.texture = texture
        var origin = CGPoint(x: x - texture.padding, y: baseline - texture.baseline)
        var size = texture.size
        if scale != 1, let pivot {
            origin = CGPoint(x: pivot.x + (origin.x - pivot.x) * scale, y: pivot.y + (origin.y - pivot.y) * scale)
            size = CGSize(width: size.width * scale, height: size.height * scale)
        }
        instance = OrbTextInstance(
            rect: SIMD4(Float(origin.x), Float(origin.y), Float(size.width), Float(size.height)),
            color: color.simd,
            glow: glow?.simd ?? .zero,
            params: SIMD4(Float(shear), Float(shearPivot), Float(glowRadius), clip.mode),
            clip: clip.rect
        )
    }

    /// Centers the text horizontally on `centerX`.
    init(_ texture: OrbTextTexture, centerX: CGFloat, baseline: CGFloat, color: RGBColor, glow: RGBColor? = nil,
         glowRadius: CGFloat = 0, scale: CGFloat = 1, pivot: CGPoint? = nil, clip: OrbTextClip = .none) {
        self.init(texture, x: centerX - texture.advance / 2, baseline: baseline, color: color, glow: glow,
                  glowRadius: glowRadius, scale: scale, pivot: pivot ?? (scale == 1 ? nil : CGPoint(x: centerX, y: baseline)),
                  clip: clip)
    }
}
