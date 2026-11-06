import AppKit
import QuartzCore

/// The content's accents and ambience (DESIGN 2c Motion and effects), all Core Animation the render server runs, so a
/// digit roll, a value flash or an overtake chip costs the app no frames. Reduce Motion crossfades changes and plays
/// nothing that travels.
extension BoardContentStack {
    /// The item's image changed: rolls, warm flashes and dims.
    func change(_ layer: CALayer, from old: CGImage?, to new: CGImage?, item: BoardContentItem) {
        guard !item.accents.isEmpty else { return }
        if reduceMotion {
            let fade = CATransition()
            fade.type = .fade
            fade.duration = item.accents.lazy.compactMap { accent -> Double? in
                if case .crossfade(let seconds) = accent { return seconds }
                return nil
            }.first ?? 0.2
            layer.add(fade, forKey: "change")
            return
        }
        for accent in item.accents {
            switch accent {
            case .roll(let rising):
                let push = CATransition()
                push.type = .push
                push.subtype = rising ? .fromBottom : .fromTop
                push.duration = 0.24
                push.timingFunction = CAMediaTimingFunction(controlPoints: 0.2, 0.8, 0.2, 1)
                layer.add(push, forKey: "roll")
                if rising, let hot = item.hot { cool(layer, from: hot, to: new, size: item.frame.size) }
            case .warm:
                if let hot = item.hot { cool(layer, from: hot, to: new, size: item.frame.size) }
            case .dim:
                let dim = CAKeyframeAnimation(keyPath: "opacity")
                dim.values = [0.45, 1]
                dim.duration = 0.8
                dim.timingFunction = CAMediaTimingFunction(name: .easeOut)
                layer.add(dim, forKey: "dim")
            case .crossfade(let seconds):
                let fade = CATransition()
                fade.type = .fade
                fade.duration = seconds
                layer.add(fade, forKey: "change")
            case .punch, .decal:
                break
            }
        }
    }

    /// Accents that do not depend on a new image: punches and decals.
    func play(_ item: BoardContentItem, on layer: CALayer) {
        guard !reduceMotion else { return }
        for accent in item.accents {
            switch accent {
            case .punch(let anchor):
                let local = CGPoint(x: anchor.x - item.frame.minX, y: item.frame.maxY - anchor.y)
                func scaled(_ s: CGFloat) -> NSValue {
                    let toAnchor = CATransform3DMakeTranslation(-local.x, -local.y, 0)
                    let back = CATransform3DMakeTranslation(local.x, local.y, 0)
                    return NSValue(caTransform3D: CATransform3DConcat(CATransform3DConcat(toAnchor, CATransform3DMakeScale(s, s, 1)), back))
                }
                let punch = CAKeyframeAnimation(keyPath: "transform")
                punch.values = [scaled(1), scaled(1.06), scaled(1)]
                punch.keyTimes = [0, 0.35, 1]
                punch.timingFunctions = [CAMediaTimingFunction(controlPoints: 0.3, 1.4, 0.4, 1), CAMediaTimingFunction(name: .easeInEaseOut)]
                punch.duration = 0.42
                layer.add(punch, forKey: "punch")
            case .decal(let decal):
                guard celebrations || !decal.celebration else { continue }
                add(decal, to: layer, itemHeight: item.frame.height)
            case .roll, .warm, .dim, .crossfade:
                break
            }
        }
    }

    /// Starts or stops an item's ambient motion.
    func setAmbient(_ ambient: BoardContentAmbient?, on layer: CALayer) {
        let previous = layer.animation(forKey: "ambient")
        layer.removeAnimation(forKey: "ambient")
        layer.sublayers?.filter { $0.name == "pulse" }.forEach { $0.removeFromSuperlayer() }
        guard let ambient, ambience, !reduceMotion else { return }
        switch ambient {
        case .breathe(let period, let low):
            let breath = CABasicAnimation(keyPath: "opacity")
            breath.fromValue = 1
            breath.toValue = low
            breath.duration = period / 2
            breath.autoreverses = true
            breath.repeatCount = .infinity
            breath.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
            // A new period keeps the breath's phase instead of snapping back to full.
            if let previous, previous.duration > 0, previous.beginTime > 0 {
                let cycle = previous.duration * 2
                let elapsed = layer.convertTime(CACurrentMediaTime(), from: nil) - previous.beginTime
                breath.timeOffset = elapsed.truncatingRemainder(dividingBy: cycle) / cycle * period
            }
            layer.add(breath, forKey: "ambient")
        case .drift(let dx, let dy, let scale, let period):
            let center = CGPoint(x: layer.bounds.midX, y: layer.bounds.midY)
            var transform = CATransform3DMakeTranslation(-center.x, -center.y, 0)
            transform = CATransform3DConcat(transform, CATransform3DMakeScale(scale, scale, 1))
            transform = CATransform3DConcat(transform, CATransform3DMakeTranslation(center.x + dx, center.y - dy, 0))
            let drift = CABasicAnimation(keyPath: "transform")
            drift.fromValue = NSValue(caTransform3D: CATransform3DIdentity)
            drift.toValue = NSValue(caTransform3D: transform)
            drift.duration = period
            drift.autoreverses = true
            drift.repeatCount = .infinity
            drift.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
            layer.add(drift, forKey: "ambient")
        case .pulse(let period):
            let ring = CALayer()
            ring.name = "pulse"
            ring.frame = layer.bounds
            ring.cornerRadius = layer.bounds.width / 2
            ring.backgroundColor = CGColor(srgbRed: 239 / 255, green: 68 / 255, blue: 68 / 255, alpha: 0.55)
            let grow = CABasicAnimation(keyPath: "transform.scale")
            grow.fromValue = 1
            grow.toValue = 3
            let fade = CABasicAnimation(keyPath: "opacity")
            fade.fromValue = 1
            fade.toValue = 0
            let group = CAAnimationGroup()
            group.animations = [grow, fade]
            group.duration = period
            group.repeatCount = .infinity
            group.timingFunction = CAMediaTimingFunction(name: .easeOut)
            ring.opacity = 0
            ring.add(group, forKey: "ambient")
            layer.insertSublayer(ring, at: 0)
        }
    }

    // MARK: Pieces

    /// Starts from the white-hot image and cools to `image` in 0.8 s.
    private func cool(_ layer: CALayer, from hot: BoardContentDrawing, to image: CGImage?, size: CGSize) {
        guard let image, let warm = BoardRaster.image(size: size, scale: scale, space: colorSpace, draw: hot.draw) else { return }
        let cool = CAKeyframeAnimation(keyPath: "contents")
        cool.values = [warm, warm, image]
        cool.keyTimes = [0, 0.3, 1]
        cool.duration = 0.8
        layer.add(cool, forKey: "cool")
    }

    private func add(_ decal: BoardDecal, to layer: CALayer, itemHeight: CGFloat) {
        let host = CALayer()
        host.frame = CGRect(x: decal.frame.minX, y: itemHeight - decal.frame.maxY, width: decal.frame.width, height: decal.frame.height)
        host.contentsScale = scale
        let size = decal.frame.size
        let seconds: Double
        switch decal.motion {
        case .pop(let duration):
            seconds = duration
            host.contents = BoardRaster.image(size: size, scale: scale, space: colorSpace, draw: decal.draw)
            let fade = CAKeyframeAnimation(keyPath: "opacity")
            fade.values = [0, 1, 1, 0]
            fade.keyTimes = [0, 0.14, 0.75, 1]
            let pop = CAKeyframeAnimation(keyPath: "transform.scale")
            pop.values = [0.6, 1, 1, 0.95]
            pop.keyTimes = [0, 0.14, 0.75, 1]
            let group = CAAnimationGroup()
            group.animations = [fade, pop]
            group.duration = duration
            group.timingFunction = CAMediaTimingFunction(controlPoints: 0.2, 0.8, 0.2, 1)
            host.opacity = 0
            host.add(group, forKey: "decal")
        case .fade(let duration):
            seconds = duration
            host.contents = BoardRaster.image(size: size, scale: scale, space: colorSpace, draw: decal.draw)
            let fade = CABasicAnimation(keyPath: "opacity")
            fade.fromValue = 1
            fade.toValue = 0
            fade.duration = duration
            fade.timingFunction = CAMediaTimingFunction(name: .easeOut)
            host.opacity = 0
            host.add(fade, forKey: "decal")
        case .sweep(let duration):
            seconds = duration
            if let path = decal.mask {
                let mask = CAShapeLayer()
                var flip = CGAffineTransform(translationX: 0, y: size.height).scaledBy(x: 1, y: -1)
                mask.path = path.copy(using: &flip)
                host.mask = mask
            } else {
                host.masksToBounds = true
            }
            let band = CALayer()
            band.contentsScale = scale
            band.contents = BoardRaster.image(size: CGSize(width: decal.bandWidth, height: size.height), scale: scale, space: colorSpace,
                                              draw: decal.draw)
            band.frame = CGRect(x: -decal.bandWidth, y: 0, width: decal.bandWidth, height: size.height)
            let travel = CABasicAnimation(keyPath: "position.x")
            travel.fromValue = -decal.bandWidth / 2
            travel.toValue = size.width + decal.bandWidth / 2
            travel.duration = duration
            travel.timingFunction = CAMediaTimingFunction(controlPoints: 0.3, 0.7, 0.2, 1)
            band.add(travel, forKey: "decal")
            host.addSublayer(band)
        case .banner(let duration):
            seconds = duration
            host.contents = BoardRaster.image(size: size, scale: scale, space: colorSpace, draw: decal.draw)
            let mask = CALayer()
            mask.backgroundColor = CGColor(gray: 0, alpha: 1)
            mask.frame = CGRect(origin: .zero, size: size)
            let wipe = CAKeyframeAnimation(keyPath: "position.x")
            wipe.values = [-size.width / 2, size.width / 2, size.width / 2, size.width * 1.5]
            wipe.keyTimes = [0, 0.18, 0.78, 1]
            wipe.duration = duration
            wipe.timingFunction = CAMediaTimingFunction(controlPoints: 0.2, 0.9, 0.2, 1)
            mask.position.x = size.width * 1.5
            mask.add(wipe, forKey: "decal")
            host.mask = mask
        }
        layer.addSublayer(host)
        DispatchQueue.main.asyncAfter(deadline: .now() + seconds + 0.05) { [weak host] in
            MainActor.assumeIsolated { host?.removeFromSuperlayer() }
        }
    }
}
