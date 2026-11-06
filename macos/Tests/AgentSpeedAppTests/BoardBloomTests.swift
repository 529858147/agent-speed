import AgentSpeedOrbModel
import CoreGraphics
import Foundation
import Testing
@testable import AgentSpeed

/// DESIGN 2c Perimeter: the bloom outside a board fades out along the board's rounded outline and is gone before the
/// canvas edge, so the edge never cuts it into a square with right-angled corners. Rendered with Metal; skipped where
/// there is no GPU.
@Suite("Board bloom")
@MainActor
struct BoardBloomTests {
    private static let scale = 2.0

    /// The bloom alone at the largest heat (h = 1, overdrive 1: the widest blur), as straight alpha per pixel.
    private func render(_ style: BoardStyle) -> (alpha: [Double], pixels: Int)? {
        var fx = BoardEffects(style: style, side: BoardSize.medium.side, seed: 1) { HeatPalette.color($0).simd }
        fx.setHeat(1, overdrive: 1, active: true)
        fx.settleNow()
        var scene = OrbScene(capacity: 4)
        scene.add(BoardEffectsRenderer.bloom(fx), blend: .over)
        let side = fx.side + 2 * BoardEffects.margin
        guard let image = OrbSnapshot.image(scene, side: side, scale: Self.scale) else { return nil }
        let pixels = image.width
        var bytes = [UInt8](repeating: 0, count: pixels * pixels * 4)
        guard let context = CGContext(data: &bytes, width: pixels, height: pixels, bitsPerComponent: 8,
                                      bytesPerRow: pixels * 4, space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                      bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }
        context.draw(image, in: CGRect(x: 0, y: 0, width: pixels, height: pixels))
        return (stride(from: 3, to: bytes.count, by: 4).map { Double(bytes[$0]) / 255 }, pixels)
    }

    /// Distance (points) of a canvas point outside the board's rounded square.
    private func distance(x: Double, y: Double, style: BoardStyle) -> Double {
        let half = BoardSize.medium.side / 2, r = style.cornerRadius, center = BoardEffects.margin + half
        let kx = abs(x - center) - half + r, ky = abs(y - center) - half + r
        return hypot(max(kx, 0), max(ky, 0)) + min(max(kx, ky), 0) - r
    }

    @Test(arguments: BoardStyle.allCases)
    func fadesAlongTheRoundedOutlineBeforeTheCanvasEdge(style: BoardStyle) throws {
        guard let (alpha, pixels) = render(style) else { return }
        // Pixels by their distance from the outline in quarter points: one alpha per distance means the light's
        // contours are the outline's offsets, rounded at the corners like the board.
        var bins = [Int: (low: Double, high: Double)]()
        var beyond = 0.0
        for row in 0..<pixels {
            for column in 0..<pixels {
                let d = distance(x: (Double(column) + 0.5) / Self.scale, y: (Double(row) + 0.5) / Self.scale, style: style)
                let a = alpha[row * pixels + column]
                if d >= BoardEffects.bloomReach {
                    beyond = max(beyond, a)
                    continue
                }
                guard d >= 1 else { continue }
                let bin = Int(d * 4)
                let seen = bins[bin] ?? (a, a)
                bins[bin] = (min(seen.low, a), max(seen.high, a))
            }
        }
        #expect(beyond == 0, "light \(beyond) beyond the reach")
        let spread = bins.values.map { $0.high - $0.low }.max() ?? 0
        #expect(spread <= 3.0 / 255, "alpha differs by \(spread) at one distance from the outline")
        let profile = bins.keys.sorted().map { bins[$0]!.high }
        let rise = zip(profile.dropFirst(), profile).map { $0 - $1 }.max() ?? 0
        #expect(rise <= 1.0 / 255, "the light rises by \(rise) going out")
        // Still a wide halo at the widest blur: about a tenth of full light halfway across the margin.
        #expect((bins[Int(BoardEffects.margin / 2 * 4)]?.high ?? 0) > 0.08)
    }
}
