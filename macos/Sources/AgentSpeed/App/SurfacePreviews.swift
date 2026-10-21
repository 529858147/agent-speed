import AgentSpeedCore
import SwiftUI

/// Orb and menu bar renders for `--render-previews`.
@MainActor
enum SurfacePreviews {
    struct TierSample {
        let name: String
        let fraction: Double
        let tier: SpeedTier
    }

    static let tiers: [TierSample] = [
        TierSample(name: "idle", fraction: 0, tier: .idle),
        TierSample(name: "cruise", fraction: 0.07, tier: .cruise),
        TierSample(name: "boost", fraction: 0.24, tier: .boost),
        TierSample(name: "overdrive", fraction: 0.52, tier: .overdrive),
        TierSample(name: "redline", fraction: 0.9, tier: .redline),
        TierSample(name: "beyond", fraction: 1.45, tier: .redline),
    ]

    static let sizes: [CGFloat] = [112, 148, 196]

    static func renderOrbs(model: AppModel, into directory: URL) {
        let redline = model.settings.redline
        for dark in [false, true] {
            var sheet: [[NSImage]] = []
            for size in sizes {
                var row: [NSImage] = []
                for sample in tiers {
                    let image = orbImage(model: model, size: size, dark: dark, speed: redline * sample.fraction, tier: sample.tier)
                    if let image {
                        PreviewSnapshot.write(image, to: directory.appendingPathComponent("orb-\(sample.name)-\(Int(size))-\(dark ? "dark" : "light").png"))
                        row.append(NSImage(cgImage: image, size: .zero))
                    }
                }
                sheet.append(row)
            }
            writeSheet(sheet, dark: dark, to: directory.appendingPathComponent("orb-sheet-\(dark ? "dark" : "light").png"))

            let extras: [(String, OrbExtra)] = [("peak", .peak), ("hover", .hover), ("pulse", .pulse)]
            for (name, extra) in extras {
                if let image = orbImage(model: model, size: 196, dark: dark, speed: redline * 0.95, tier: .redline, extra: extra) {
                    PreviewSnapshot.write(image, to: directory.appendingPathComponent("orb-\(name)-196-\(dark ? "dark" : "light").png"))
                }
            }
        }
    }

    enum OrbExtra {
        case none, peak, hover, pulse
    }

    static func orbImage(model: AppModel, size: CGFloat, dark: Bool, speed: Double, tier: SpeedTier, extra: OrbExtra = .none) -> CGImage? {
        let snapshot = PreviewData.snapshot(speed: speed, redline: model.settings.redline)
        model.store.publishLive(snapshot)
        let now = Clock.now
        model.gauge.snap(to: speed, tier: tier, now: now - 3)
        let interaction = OrbInteraction()
        interaction.isHovered = extra == .hover
        let animator = OrbAnimator(style: .floating)
        animator.attach(model: model, interaction: interaction)
        let effects = animator.effects(for: model)
        let gauge = model.gauge
        let input = OrbAnimator.StepInput(
            heat: gauge.heat(at: now),
            overdrive: gauge.overdrive(at: now),
            tier: tier,
            effects: effects,
            dark: dark,
            hovered: extra == .hover
        )
        switch extra {
        case .peak: animator.celebrate(at: now - 0.62)
        case .pulse: animator.addPulse(at: now - 0.2, strength: 0.9, heat: gauge.heat(at: now))
        case .none, .hover: break
        }
        animator.prewarm(from: now - 2.4, to: now, input: input)
        let view = OrbView(model: model, diameter: size, style: .floating, animator: animator, time: now, interaction: interaction)
            .appEnvironment(model)
            .environment(\.colorScheme, dark ? .dark : .light)
        let renderer = ImageRenderer(content: view)
        renderer.scale = 2
        renderer.isOpaque = false
        let image = renderer.cgImage
        animator.detach()
        return image
    }

    static func renderMenuBar(model: AppModel, into directory: URL) {
        let redline = model.settings.redline
        let values: [Double] = [0, 4.2, 45, 364, 1280]
        for dark in [false, true] {
            var images: [NSImage] = []
            for value in values {
                model.store.publishLive(PreviewData.snapshot(speed: value, redline: redline))
                model.gauge.snap(to: value, now: Clock.now - 3)
                let state = StatusItemState(model: model, time: Clock.now)
                let size = CGSize(width: 120, height: state.layout.height)
                let image = NSImage(size: size, flipped: true) { rect in
                    (dark ? NSColor(white: 0.16, alpha: 1) : NSColor(white: 0.93, alpha: 1)).setFill()
                    rect.fill()
                    NSColor.systemRed.withAlphaComponent(0.35).setFill()
                    CGRect(x: 10 + state.layout.length, y: 0, width: 1, height: rect.height).fill()
                    NSGraphicsContext.current?.cgContext.translateBy(x: 10, y: 0)
                    StatusItemArtist.draw(state, dark: dark, in: CGRect(origin: .zero, size: CGSize(width: state.layout.length, height: state.layout.height)))
                    return true
                }
                images.append(scaled(image, by: 4))
            }
            writeColumn(images, to: directory.appendingPathComponent("menubar-\(dark ? "dark" : "light").png"))
        }
    }

    private static func scaled(_ image: NSImage, by scale: CGFloat) -> NSImage {
        let size = CGSize(width: image.size.width * scale, height: image.size.height * scale)
        guard let rep = NSBitmapImageRep(
            bitmapDataPlanes: nil, pixelsWide: Int(size.width), pixelsHigh: Int(size.height),
            bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
            colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0
        ) else { return image }
        rep.size = image.size
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
        image.draw(in: CGRect(origin: .zero, size: image.size))
        NSGraphicsContext.restoreGraphicsState()
        let result = NSImage(size: size)
        rep.size = size
        result.addRepresentation(rep)
        return result
    }

    private static func writeSheet(_ rows: [[NSImage]], dark: Bool, to url: URL) {
        let cellWidth = rows.flatMap { $0 }.map(\.size.width).max() ?? 0
        let rowHeights = rows.map { $0.map(\.size.height).max() ?? 0 }
        let width = cellWidth * CGFloat(rows.map(\.count).max() ?? 0)
        let height = rowHeights.reduce(0, +)
        guard width > 0, height > 0 else { return }
        let image = NSImage(size: CGSize(width: width, height: height), flipped: true) { _ in
            let background = dark
                ? NSGradient(colors: [NSColor(red: 0.12, green: 0.14, blue: 0.2, alpha: 1), NSColor(red: 0.05, green: 0.05, blue: 0.08, alpha: 1)])
                : NSGradient(colors: [NSColor(red: 0.86, green: 0.89, blue: 0.94, alpha: 1), NSColor(red: 0.72, green: 0.78, blue: 0.86, alpha: 1)])
            background?.draw(in: CGRect(x: 0, y: 0, width: width, height: height), angle: 90)
            var y: CGFloat = 0
            for (index, row) in rows.enumerated() {
                for (column, cell) in row.enumerated() {
                    cell.draw(in: CGRect(x: CGFloat(column) * cellWidth + (cellWidth - cell.size.width) / 2, y: y + (rowHeights[index] - cell.size.height) / 2, width: cell.size.width, height: cell.size.height))
                }
                y += rowHeights[index]
            }
            return true
        }
        if let cg = image.cgImage(forProposedRect: nil, context: nil, hints: nil) {
            PreviewSnapshot.write(cg, to: url)
        }
    }

    private static func writeColumn(_ images: [NSImage], to url: URL) {
        let width = images.map(\.size.width).max() ?? 0
        let height = images.reduce(0) { $0 + $1.size.height + 8 }
        guard width > 0 else { return }
        let image = NSImage(size: CGSize(width: width, height: height), flipped: true) { _ in
            NSColor(white: 0.5, alpha: 1).setFill()
            CGRect(x: 0, y: 0, width: width, height: height).fill()
            var y: CGFloat = 0
            for item in images {
                item.draw(in: CGRect(x: 0, y: y, width: item.size.width, height: item.size.height))
                y += item.size.height + 8
            }
            return true
        }
        if let cg = image.cgImage(forProposedRect: nil, context: nil, hints: nil) {
            PreviewSnapshot.write(cg, to: url)
        }
    }
}
