import AgentSpeedOrbModel
import AppKit
import QuartzCore

/// The board's content as cached Core Animation layers (DESIGN 2c Two layers, Rows): each item is rasterized once per
/// revision; moves, entrances and exits are layer animations the render server runs, so the app draws no frames for
/// them. The container is the board's rounded square (y up, as Core Animation), clipped to its corners.
@MainActor
final class BoardContentStack {
    /// How long a stretched item takes to reach a new share (the prototypes' `transition: width .55s`).
    static let stretchSeconds = 0.55
    /// How long a tinted item takes to reach a new color.
    static let tintSeconds = 0.5

    /// The row spring per style (DESIGN 2c: glass critically damped, settle ≈ 0.45 s; timing damping 0.72; neon 0.9).
    struct RowSpring: Equatable {
        var duration: Double
        var bounce: Double

        static func of(_ style: BoardStyle) -> RowSpring {
            switch style {
            case .glass: RowSpring(duration: 0.45, bounce: 0)
            case .timing: RowSpring(duration: 0.45, bounce: 0.28)
            case .neon: RowSpring(duration: 0.45, bounce: 0.1)
            }
        }
    }

    static let fadeSeconds = 0.3
    static let enterDrop: CGFloat = 10

    let layer = CALayer()
    var spring = RowSpring.of(.glass)
    var reduceMotion = false {
        didSet { if reduceMotion != oldValue { refreshAmbience() } }
    }
    /// Style ambience (aurora drift, tube breath, live pulse); off with Effects intensity Off.
    var ambience = true {
        didSet { if ambience != oldValue { refreshAmbience() } }
    }
    /// `Celebrate new peaks`: off leaves out the new-peak and new-leader decals.
    var celebrations = true
    /// Pixels per point of the images; a change redraws every item.
    var scale: CGFloat = 2 {
        didSet { if scale != oldValue { invalidate() } }
    }
    /// The window's color space: images drawn in it reach the render server as they are, without a per-commit copy
    /// and color conversion of every changed row. A change redraws every item.
    var colorSpace: CGColorSpace? {
        didSet { if colorSpace != oldValue { invalidate() } }
    }
    /// Images rasterized since launch (benchmarks: data changes only, never per frame).
    private(set) var rasterCount = 0

    /// The sub-gauge's glide (DESIGN 2c Live metrics, *Thinking hidden at zero*): smoothstep, 0.3 s.
    static let glideSeconds = 0.3
    static let glideTiming = CAMediaTimingFunction(controlPoints: 0.33, 0, 0.67, 1)
    /// The arc's sweep and the tip's turn follow the hero's digit roll: 0.24 s on its curve.
    static let sweepSeconds = 0.24
    static let rollTiming = CAMediaTimingFunction(controlPoints: 0.2, 0.8, 0.2, 1)

    private struct Entry {
        let layer: CALayer
        var group: BoardContentItem.Group
        var revision: AnyHashable
        var frame: CGRect
        var stretch: Double?
        var stretchAnchor: CGFloat = 0
        var tint: CGColor?
        var sweep: BoardContentItem.Sweep?
        var turn: Double?
        var image: CGImage?
        var ambient: BoardContentAmbient?
    }

    private var entries: [String: Entry] = [:]
    private var order: [String] = []
    /// The motion of every item shown, for the way it leaves.
    private var leaving: [String: BoardContentItem.Motion] = [:]
    /// The hero's pieces, in one layer so a switch change crossfades the hero as a whole.
    private let heroLayer = CALayer()

    init() {
        layer.masksToBounds = true
        layer.anchorPoint = .zero
        heroLayer.anchorPoint = .zero
    }

    func setBoard(side: CGFloat, corner: CGFloat) {
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        layer.bounds = CGRect(x: 0, y: 0, width: side, height: side)
        layer.cornerRadius = corner
        heroLayer.bounds = layer.bounds
        heroLayer.position = .zero
        CATransaction.commit()
    }

    private var side: CGFloat { layer.bounds.height }

    private func refreshAmbience() {
        for entry in entries.values { setAmbient(entry.ambient, on: entry.layer) }
    }

    private func invalidate() {
        for key in entries.keys { entries[key]?.revision = AnyHashable(UUID()) }
    }

    /// Shows `items`: new ones fade in from 10 pt below, missing ones fade out, moved ones spring to their place
    /// (Reduce Motion: a crossfade instead); the sub-gauge's pieces glide and fade in place. `animated` false places
    /// everything at once. `crossfade` (a live metrics switch change, DESIGN 2c): the hero crossfades as a whole and
    /// changed rows crossfade their images while they move, over that many seconds.
    func apply(_ items: [BoardContentItem], animated: Bool, crossfade: Double? = nil) {
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        var seen = Set<String>()
        var moved = false
        // New layers go right above the previous item's in their layer, so each keeps the items' order (back to front).
        var below: [BoardContentItem.Group: CALayer] = [:]
        for item in items {
            let parent = item.group == .hero ? heroLayer : layer
            if item.group == .hero, heroLayer.superlayer == nil {
                if let previous = below[.main] { layer.insertSublayer(heroLayer, above: previous) } else { layer.insertSublayer(heroLayer, at: 0) }
            }
            defer {
                below[item.group] = entries[item.key]?.layer
                if item.group == .hero { below[.main] = heroLayer }
            }
            seen.insert(item.key)
            // A switch change crossfades the hero as a whole: its pieces take their places at once inside it.
            let live = animated && !(crossfade != nil && item.group == .hero)
            let target = layerFrame(item.frame)
            // An item that starts or stops being tinted or revealed, or changes layers, gets a new layer.
            if let entry = entries[item.key],
               (entry.tint == nil) != (item.tint == nil) || (entry.sweep == nil) != (item.sweep == nil) || entry.group != item.group {
                entry.layer.removeFromSuperlayer()
                entries[item.key] = nil
            }
            if var entry = entries[item.key] {
                if item.tint == nil { Self.setPlate(item.plate, on: entry.layer) }
                if let tint = item.tint, entry.tint != tint {
                    setTint(tint, on: entry.layer, animated: live)
                    entry.tint = tint
                }
                if entry.stretch != item.stretch || entry.stretchAnchor != item.stretchAnchor {
                    entry.stretch = item.stretch
                    entry.stretchAnchor = item.stretchAnchor
                    setStretch(item.stretch, anchor: item.stretchAnchor, on: entry.layer, at: target.origin, width: target.width,
                               animated: live)
                }
                if entry.layer.masksToBounds != item.clips { entry.layer.masksToBounds = item.clips }
                if entry.revision != item.revision || entry.frame.size != item.frame.size {
                    let old = entry.image
                    entry.image = rasterize(item)
                    Self.show(entry.image, on: entry.layer, tinted: item.tint != nil)
                    entry.revision = item.revision
                    if live { change(entry.layer, from: old, to: entry.image, item: item) }
                    if animated, let crossfade, item.group == .main, item.motion == .row { fade(entry.layer, seconds: crossfade) }
                }
                if entry.frame != item.frame {
                    let from = entry.layer.presentation()?.position ?? entry.layer.position
                    entry.layer.bounds = CGRect(origin: .zero, size: target.size)
                    entry.layer.mask?.frame = entry.layer.bounds
                    let (anchor, position) = placement(item, target: target)
                    entry.layer.anchorPoint = anchor
                    entry.layer.position = position
                    if let sweep = item.sweep { setSweep(sweep, on: entry.layer, animated: false) }
                    if live {
                        switch item.motion {
                        case .row where reduceMotion:
                            moved = true
                        case .row:
                            let animation = CASpringAnimation(perceptualDuration: spring.duration, bounce: spring.bounce)
                            animation.keyPath = "position"
                            animation.fromValue = NSValue(point: from)
                            animation.toValue = NSValue(point: position)
                            animation.duration = animation.settlingDuration
                            entry.layer.add(animation, forKey: "move")
                        case .glide where !reduceMotion:
                            let glide = CABasicAnimation(keyPath: "position")
                            glide.fromValue = NSValue(point: from)
                            glide.toValue = NSValue(point: position)
                            glide.duration = Self.glideSeconds
                            glide.timingFunction = Self.glideTiming
                            entry.layer.add(glide, forKey: "move")
                        default:
                            break
                        }
                    }
                    entry.frame = item.frame
                }
                if let sweep = item.sweep, sweep != entry.sweep {
                    setSweep(sweep, on: entry.layer, animated: live)
                    entry.sweep = sweep
                }
                if item.turn != entry.turn {
                    setTurn(item.turn, on: entry.layer, animated: live)
                    entry.turn = item.turn
                }
                if live { play(item, on: entry.layer) }
                if entry.ambient != item.ambient {
                    entry.ambient = item.ambient
                    setAmbient(item.ambient, on: entry.layer)
                }
                entries[item.key] = entry
            } else {
                let child = CALayer()
                let (anchor, position) = placement(item, target: target)
                child.anchorPoint = anchor
                child.bounds = CGRect(origin: .zero, size: target.size)
                child.position = position
                child.contentsScale = scale
                child.contentsGravity = .resize
                child.masksToBounds = item.clips
                if let tint = item.tint {
                    let mask = CALayer()
                    mask.frame = child.bounds
                    mask.contentsScale = scale
                    mask.contentsGravity = .resize
                    child.mask = mask
                    child.backgroundColor = tint
                } else {
                    Self.setPlate(item.plate, on: child)
                }
                let image = rasterize(item)
                Self.show(image, on: child, tinted: item.tint != nil)
                if let previous = below[item.group] { parent.insertSublayer(child, above: previous) } else { parent.insertSublayer(child, at: 0) }
                entries[item.key] = Entry(layer: child, group: item.group, revision: item.revision, frame: item.frame,
                                          stretch: item.stretch, stretchAnchor: item.stretchAnchor, tint: item.tint,
                                          sweep: item.sweep, turn: item.turn, image: image, ambient: item.ambient)
                if item.stretch != nil {
                    setStretch(item.stretch, anchor: item.stretchAnchor, on: child, at: target.origin, width: target.width,
                               animated: false)
                }
                if let sweep = item.sweep { setSweep(sweep, on: child, animated: false) }
                if item.turn != nil { setTurn(item.turn, on: child, animated: false) }
                setAmbient(item.ambient, on: child)
                if live {
                    switch item.motion {
                    case .row: enter(child, at: position)
                    case .glide(let seconds): appear(child, seconds: seconds)
                    case .none: break
                    }
                    play(item, on: child)
                }
            }
        }
        var motions: [String: BoardContentItem.Motion] = [:]
        for item in items { motions[item.key] = item.motion }
        for key in order where !seen.contains(key) {
            guard let entry = entries.removeValue(forKey: key) else { continue }
            let motion = leaving[key] ?? .row
            // Rows and the sub-gauge's pieces fade out; a digit or another piece of the hero is replaced at once.
            let fades = animated && !(crossfade != nil && entry.group == .hero)
            switch motion {
            case .row: leave(entry.layer, animated: fades, seconds: Self.fadeSeconds)
            case .glide(let seconds): leave(entry.layer, animated: fades && !reduceMotion, seconds: seconds)
            case .none: leave(entry.layer, animated: false, seconds: 0)
            }
        }
        leaving = motions
        order = items.map(\.key)
        if moved {
            let fade = CATransition()
            fade.type = .fade
            fade.duration = 0.25
            layer.add(fade, forKey: "crossfade")
        }
        if animated, let crossfade { fade(heroLayer, seconds: crossfade) }
        CATransaction.commit()
    }

    /// The layer's anchor and position for an item: turned items turn about their frame's center, stretched ones grow
    /// from their anchor, the rest hang from their top-left.
    private func placement(_ item: BoardContentItem, target: CGRect) -> (CGPoint, CGPoint) {
        if item.turn != nil { return (CGPoint(x: 0.5, y: 0.5), CGPoint(x: target.midX, y: target.midY)) }
        if item.stretch != nil {
            let anchor = target.width > 0 ? min(1, max(0, item.stretchAnchor / target.width)) : 0
            return (CGPoint(x: anchor, y: 0), stretched(target.origin, anchor: item.stretchAnchor))
        }
        return (.zero, target.origin)
    }

    private func fade(_ layer: CALayer, seconds: Double) {
        let fade = CATransition()
        fade.type = .fade
        fade.duration = seconds
        layer.add(fade, forKey: "switch")
    }

    /// An arc shown up to `sweep.fraction` by a stroked mask the render server moves (0.24 s; Reduce Motion: at once).
    private func setSweep(_ sweep: BoardContentItem.Sweep, on layer: CALayer, animated: Bool) {
        let mask = (layer.mask as? CAShapeLayer) ?? CAShapeLayer()
        if layer.mask !== mask {
            mask.fillColor = nil
            mask.strokeColor = CGColor(gray: 0, alpha: 1)
            mask.lineCap = .butt
            layer.mask = mask
        }
        mask.frame = layer.bounds
        var flip = CGAffineTransform(translationX: 0, y: layer.bounds.height).scaledBy(x: 1, y: -1)
        mask.path = sweep.maskLine.copy(using: &flip)
        mask.lineWidth = sweep.width
        let end = sweep.strokeEnd(sweep.fraction)
        if animated, !reduceMotion {
            let move = CABasicAnimation(keyPath: "strokeEnd")
            move.fromValue = mask.presentation()?.strokeEnd ?? mask.strokeEnd
            move.toValue = end
            move.duration = Self.sweepSeconds
            move.timingFunction = Self.rollTiming
            mask.add(move, forKey: "sweep")
        } else {
            // A count-up's frames follow the shown value: nothing may lag behind them.
            mask.removeAnimation(forKey: "sweep")
        }
        mask.strokeEnd = end
    }

    /// Turns the layer clockwise on screen by `angle` about its center (0.24 s; Reduce Motion: at once).
    private func setTurn(_ angle: Double?, on layer: CALayer, animated: Bool) {
        // Layers are y up: a clockwise turn on screen is a negative rotation.
        let to = -(angle ?? 0)
        if animated, !reduceMotion {
            let turn = CABasicAnimation(keyPath: "transform.rotation.z")
            turn.fromValue = (layer.presentation()?.value(forKeyPath: "transform.rotation.z") as? Double) ?? 0
            turn.toValue = to
            turn.duration = Self.sweepSeconds
            turn.timingFunction = Self.rollTiming
            layer.add(turn, forKey: "turn")
        } else {
            layer.removeAnimation(forKey: "turn")
        }
        layer.setValue(to, forKeyPath: "transform.rotation.z")
    }

    /// The cached images at their resting frames (board points, y down), back to front, for offscreen frames.
    var snapshot: [(image: CGImage, frame: CGRect)] {
        order.compactMap { key in entries[key].flatMap { entry in entry.image.map { ($0, entry.frame) } } }
    }

    /// A tinted item's image is its mask; any other item shows it as its contents.
    private static func show(_ image: CGImage?, on layer: CALayer, tinted: Bool) {
        if tinted { layer.mask?.contents = image } else { layer.contents = image }
    }

    private func setTint(_ tint: CGColor, on layer: CALayer, animated: Bool) {
        if animated, !reduceMotion, let from = layer.presentation()?.backgroundColor ?? layer.backgroundColor {
            let blend = CABasicAnimation(keyPath: "backgroundColor")
            blend.fromValue = from
            blend.toValue = tint
            blend.duration = Self.tintSeconds
            blend.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
            layer.add(blend, forKey: "tint")
        }
        layer.backgroundColor = tint
    }

    private static func setPlate(_ plate: BoardContentItem.Plate?, on layer: CALayer) {
        if layer.backgroundColor != plate?.color { layer.backgroundColor = plate?.color }
        let radius = plate?.cornerRadius ?? 0
        if layer.cornerRadius != radius { layer.cornerRadius = radius }
    }

    private func layerFrame(_ frame: CGRect) -> CGRect {
        CGRect(x: frame.minX, y: side - frame.maxY, width: frame.width, height: frame.height)
    }

    /// A stretched item shows `fraction` of its image from `anchor` points into its frame: the layer scales about that
    /// point (its start, after the glow's room), so a new share eases across without a redraw, and what lies beyond the
    /// bar (its glow) comes along. Reduce Motion: at once. The frame itself stays whole.
    private func setStretch(_ fraction: Double?, anchor: CGFloat, on layer: CALayer, at origin: CGPoint, width: CGFloat,
                            animated: Bool) {
        let fraction = min(1, max(0, fraction ?? 1))
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        layer.anchorPoint = CGPoint(x: width > 0 ? min(1, max(0, anchor / width)) : 0, y: 0)
        layer.position = stretched(origin, anchor: anchor)
        CATransaction.commit()
        if animated, !reduceMotion {
            let grow = CABasicAnimation(keyPath: "transform.scale.x")
            grow.fromValue = layer.presentation()?.transform.m11 ?? layer.transform.m11
            grow.toValue = fraction
            grow.duration = Self.stretchSeconds
            grow.timingFunction = CAMediaTimingFunction(controlPoints: 0.2, 0.8, 0.2, 1)
            layer.add(grow, forKey: "stretch")
        }
        layer.transform = CATransform3DMakeScale(fraction, 1, 1)
    }

    private func stretched(_ origin: CGPoint, anchor: CGFloat) -> CGPoint {
        CGPoint(x: origin.x + anchor, y: origin.y)
    }

    /// A sub-gauge piece fades in where it is.
    private func appear(_ child: CALayer, seconds: Double) {
        guard !reduceMotion else { return }
        let fade = CABasicAnimation(keyPath: "opacity")
        fade.fromValue = 0
        fade.toValue = 1
        fade.duration = seconds
        fade.timingFunction = CAMediaTimingFunction(name: .easeOut)
        child.add(fade, forKey: "enter")
    }

    private func enter(_ child: CALayer, at position: CGPoint) {
        let group = CAAnimationGroup()
        let fade = CABasicAnimation(keyPath: "opacity")
        fade.fromValue = 0
        fade.toValue = 1
        group.animations = [fade]
        if !reduceMotion {
            let rise = CABasicAnimation(keyPath: "position")
            rise.fromValue = NSValue(point: CGPoint(x: position.x, y: position.y - Self.enterDrop))
            rise.toValue = NSValue(point: position)
            group.animations?.append(rise)
        }
        group.duration = Self.fadeSeconds
        group.timingFunction = CAMediaTimingFunction(name: .easeOut)
        child.add(group, forKey: "enter")
    }

    private func leave(_ child: CALayer, animated: Bool, seconds: Double) {
        guard animated, seconds > 0 else {
            child.removeFromSuperlayer()
            return
        }
        DispatchQueue.main.asyncAfter(deadline: .now() + seconds) { [weak child] in
            MainActor.assumeIsolated { child?.removeFromSuperlayer() }
        }
        let fade = CABasicAnimation(keyPath: "opacity")
        fade.fromValue = child.presentation()?.opacity ?? 1
        fade.toValue = 0
        fade.duration = seconds
        child.opacity = 0
        child.add(fade, forKey: "leave")
    }

    private func rasterize(_ item: BoardContentItem) -> CGImage? {
        rasterCount += 1
        return BoardRaster.image(size: item.frame.size, scale: item.rasterScale ?? scale, space: colorSpace, draw: item.draw)
    }
}

/// Offscreen bitmaps for the board's cached content.
enum BoardRaster {
    /// `draw` as its layer shows it: with a tint, the tint through the drawing's coverage (offscreen frames).
    static func tinted(_ draw: @escaping (CGContext, CGSize) -> Void, with tint: CGColor?) -> (CGContext, CGSize) -> Void {
        guard let tint else { return draw }
        return { context, size in
            context.saveGState()
            context.beginTransparencyLayer(auxiliaryInfo: nil)
            context.saveGState()
            draw(context, size)
            context.restoreGState()
            context.setBlendMode(.sourceIn)
            context.setFillColor(tint)
            context.fill(CGRect(origin: .zero, size: size))
            context.endTransparencyLayer()
            context.restoreGState()
        }
    }

    /// A premultiplied image of `size` points at `scale` in `space` (sRGB by default), drawn by `draw` in a y-down
    /// context.
    static func image(size: CGSize, scale: CGFloat, space: CGColorSpace? = nil, draw: (CGContext, CGSize) -> Void) -> CGImage? {
        let width = max(1, Int((size.width * scale).rounded(.up)))
        let height = max(1, Int((size.height * scale).rounded(.up)))
        guard let space = space ?? CGColorSpace(name: CGColorSpace.sRGB),
              let context = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0,
                                      space: space, bitmapInfo: CGImageAlphaInfo.premultipliedFirst.rawValue
                                          | CGBitmapInfo.byteOrder32Little.rawValue)
        else { return nil }
        context.scaleBy(x: scale, y: scale)
        context.translateBy(x: 0, y: size.height)
        context.scaleBy(x: 1, y: -1)
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(cgContext: context, flipped: true)
        draw(context, size)
        NSGraphicsContext.restoreGraphicsState()
        return context.makeImage()
    }
}



