import Accelerate
import AppKit

/// Glows of the board's content (CSS `text-shadow`, `box-shadow`, `drop-shadow`), made on the calling thread: the
/// shapes' coverage at the target's pixels, three box passes (the Gaussian of σ = blur / 2), then the glow color filled
/// through it. Core Graphics shadows spread each blur over many worker threads, which cost more than the blur itself,
/// on every value a row redraws. (A smaller mask scaled up costs more in the interpolated fill than it saves.)
@MainActor
enum BoardGlow {
    struct Layer {
        var color: RGBColor
        /// CSS blur radius, points.
        var blur: CGFloat
    }

    /// Fills the glow of what `shapes` draws (in `context`'s coordinates), not the shapes themselves.
    static func fill(_ color: RGBColor, blur: CGFloat, in context: CGContext, shapes: (CGContext) -> Void) {
        fill([Layer(color: color, blur: blur)], in: context, shapes: shapes)
    }

    /// Several glows of the same shapes, back to front.
    static func fill(_ layers: [Layer], in context: CGContext, shapes: (CGContext) -> Void) {
        guard context.width > 0, context.height > 0, let widest = layers.map(\.blur).max(), widest > 0 else { return }
        let ctm = context.ctm
        let pixelsPerPoint = (ctm.a * ctm.a + ctm.b * ctm.b).squareRoot()
        // Shapes up to three σ outside the target still light its edges.
        let pad = Int((widest * pixelsPerPoint * 1.5).rounded(.up)) + 1
        let width = context.width + 2 * pad
        let height = context.height + 2 * pad
        let rowBytes = (width + 15) & ~15
        let count = rowBytes * height
        let memory = UnsafeMutableRawPointer.allocate(byteCount: count * 3, alignment: 16)
        defer { memory.deallocate() }
        let coverage = memory, glow = memory + count, scratch = memory + 2 * count
        memset(coverage, 0, count)
        guard let mask = CGContext(data: coverage, width: width, height: height, bitsPerComponent: 8, bytesPerRow: rowBytes,
                                   space: CGColorSpaceCreateDeviceGray(), bitmapInfo: CGImageAlphaInfo.alphaOnly.rawValue)
        else { return }
        mask.translateBy(x: CGFloat(pad), y: CGFloat(pad))
        mask.concatenate(ctm)
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(cgContext: mask, flipped: true)
        shapes(mask)
        NSGraphicsContext.restoreGraphicsState()

        let target = CGRect(x: -pad, y: -pad, width: width, height: height)
        for layer in layers where layer.blur > 0 {
            memcpy(glow, coverage, count)
            blur(glow, scratch: scratch, width: width, height: height, rowBytes: rowBytes, sigma: layer.blur * pixelsPerPoint / 2)
            guard let provider = CGDataProvider(dataInfo: nil, data: glow, size: count, releaseData: { _, _, _ in }),
                  let image = CGImage(width: width, height: height, bitsPerComponent: 8, bitsPerPixel: 8, bytesPerRow: rowBytes,
                                      space: CGColorSpaceCreateDeviceGray(), bitmapInfo: CGBitmapInfo(rawValue: 0), provider: provider,
                                      decode: nil, shouldInterpolate: false, intent: .defaultIntent)
            else { continue }
            context.saveGState()
            context.concatenate(ctm.inverted())
            context.clip(to: target, mask: image)
            context.setFillColor(layer.color.cgColor)
            context.fill(target)
            context.restoreGState()
        }
    }

    /// The glow of shapes in their own colors (each casts its color, as CSS `box-shadow: 0 0 blur var(--c)` per shape):
    /// the shapes drawn in color, blurred, laid under whatever follows.
    static func fillColored(blur: CGFloat, in context: CGContext, shapes: (CGContext) -> Void) {
        guard context.width > 0, context.height > 0, blur > 0 else { return }
        let ctm = context.ctm
        let pixelsPerPoint = (ctm.a * ctm.a + ctm.b * ctm.b).squareRoot()
        let pad = Int((blur * pixelsPerPoint * 1.5).rounded(.up)) + 1
        let width = context.width + 2 * pad
        let height = context.height + 2 * pad
        let rowBytes = (width * 4 + 15) & ~15
        let count = rowBytes * height
        let memory = UnsafeMutableRawPointer.allocate(byteCount: count * 2, alignment: 16)
        defer { memory.deallocate() }
        memset(memory, 0, count)
        guard let space = context.colorSpace ?? CGColorSpace(name: CGColorSpace.sRGB),
              let colors = CGContext(data: memory, width: width, height: height, bitsPerComponent: 8, bytesPerRow: rowBytes, space: space,
                                     bitmapInfo: CGImageAlphaInfo.premultipliedFirst.rawValue | CGBitmapInfo.byteOrder32Little.rawValue)
        else { return }
        colors.translateBy(x: CGFloat(pad), y: CGFloat(pad))
        colors.concatenate(ctm)
        shapes(colors)
        var box = Int((4 * pow(blur * pixelsPerPoint / 2, 2) + 1).squareRoot().rounded())
        if box % 2 == 0 { box += 1 }
        if box > 1 {
            var a = vImage_Buffer(data: memory, height: vImagePixelCount(height), width: vImagePixelCount(width), rowBytes: rowBytes)
            var b = vImage_Buffer(data: memory + count, height: vImagePixelCount(height), width: vImagePixelCount(width), rowBytes: rowBytes)
            var clear: [UInt8] = [0, 0, 0, 0]
            let flags = vImage_Flags(kvImageBackgroundColorFill | kvImageDoNotTile)
            let size = UInt32(box)
            vImageBoxConvolve_ARGB8888(&a, &b, nil, 0, 0, size, size, &clear, flags)
            vImageBoxConvolve_ARGB8888(&b, &a, nil, 0, 0, size, size, &clear, flags)
            vImageBoxConvolve_ARGB8888(&a, &b, nil, 0, 0, size, size, &clear, flags)
            memcpy(memory, memory + count, count)
        }
        guard let image = colors.makeImage() else { return }
        context.saveGState()
        context.concatenate(ctm.inverted())
        context.interpolationQuality = .none
        context.draw(image, in: CGRect(x: -pad, y: -pad, width: width, height: height))
        context.restoreGState()
    }

    /// Three box passes approaching a Gaussian of `sigma` pixels, on this thread; the edges read as empty.
    private static func blur(_ data: UnsafeMutableRawPointer, scratch: UnsafeMutableRawPointer, width: Int, height: Int, rowBytes: Int,
                             sigma: CGFloat) {
        var box = Int((4 * sigma * sigma + 1).squareRoot().rounded())
        if box % 2 == 0 { box += 1 }
        guard box > 1 else { return }
        var a = vImage_Buffer(data: data, height: vImagePixelCount(height), width: vImagePixelCount(width), rowBytes: rowBytes)
        var b = vImage_Buffer(data: scratch, height: vImagePixelCount(height), width: vImagePixelCount(width), rowBytes: rowBytes)
        let flags = vImage_Flags(kvImageBackgroundColorFill | kvImageDoNotTile)
        let size = UInt32(box)
        vImageBoxConvolve_Planar8(&a, &b, nil, 0, 0, size, size, 0, flags)
        vImageBoxConvolve_Planar8(&b, &a, nil, 0, 0, size, size, 0, flags)
        vImageBoxConvolve_Planar8(&a, &b, nil, 0, 0, size, size, 0, flags)
        memcpy(data, scratch, rowBytes * height)
    }
}
