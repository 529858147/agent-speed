import AgentSpeedOrbModel
import AppKit
import QuartzCore

/// Neon's hyperspace streaks behind the rows (DESIGN 2c Style ambience) as layers the render server animates, so the
/// app draws no frames for them: one streak flies out from the warp center accelerating and lengthening as the
/// prototype's (d' = v (0.4 + d / 140), tail 6 + 0.18 d (0.3 + h), alpha d / 120), replicated around the center at an
/// irrational angle and staggered in time by another, so any count of copies spreads evenly in angle and phase. The
/// heat sets the count, speed, length, width and color; the frame class caps the render server's rate.
@MainActor
final class BoardWarpLayer {
    /// The streaks' container: the board's rounded square, clipped.
    let layer = CALayer()
    private let field = CAReplicatorLayer()
    private let streaks = [CALayer(), CALayer()]
    private var side: CGFloat = 340
    private var shown = false
    private var look: Look?
    /// When the streaks' trips began, in the field's time (which only moves on): a new look keeps each streak's place.
    private var epoch: CFTimeInterval?

    /// What the streaks show; equal looks change nothing.
    struct Look: Equatable {
        var count: Int
        /// Heat in 1/20 steps (length, width, color, alpha) and overdrive in 1/10 steps (speed).
        var heat: Double
        var overdrive: Double
        var frameRate: Float
        var colors: [CGColor]

        static func == (a: Look, b: Look) -> Bool {
            a.count == b.count && a.heat == b.heat && a.overdrive == b.overdrive && a.frameRate == b.frameRate
        }
    }


    /// The speed the animation's keyframes are laid out for (pt/s); the field's `speed` scales to the heat's.
    private static let baseSpeed = 100.0
    private static let startDistance = 10.0
    private static let samples = 14
    /// Each copy turns by √2 − 1 of a turn and waits φ − 1 of a trip after the one before: a low-discrepancy pair, so
    /// streaks fill angles and distances evenly at any count without a pattern.
    private static let turn = 2.squareRoot() - 1
    private static let stagger = (5.squareRoot() - 1) / 2

    init() {
        layer.masksToBounds = true
        layer.opacity = 0
        layer.isHidden = true
        layer.addSublayer(field)
        field.instanceTransform = CATransform3DMakeRotation(CGFloat(Self.turn * 2 * .pi), 0, 0, 1)
        for (index, streak) in streaks.enumerated() {
            streak.anchorPoint = CGPoint(x: 1, y: 0.5)
            streak.opacity = 0
            // The second set starts half a turn and half a trip away from the first.
            streak.transform = CATransform3DMakeRotation(CGFloat(index) * .pi, 0, 0, 1)
            field.addSublayer(streak)
        }
    }

    /// The board's size and corner (the warp center sits at 72 % across and 28 % down, as fx.js).
    func setBoard(side: CGFloat, corner: CGFloat) {
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        self.side = side
        layer.frame = CGRect(x: 0, y: 0, width: side, height: side)
        layer.cornerRadius = corner
        let reach = Self.reach(side)
        field.bounds = CGRect(x: 0, y: 0, width: 2 * reach, height: 2 * reach)
        field.position = CGPoint(x: side * 0.72, y: side * (1 - 0.28))
        for streak in streaks { streak.position = CGPoint(x: reach, y: reach) }
        CATransaction.commit()
        if let look {
            self.look = nil
            show(look)
        }
    }

    private static func reach(_ side: CGFloat) -> Double { Double(hypot(side, side)) + 8 }

    /// Shows the streaks for `look`, or fades them out (nil). `running` false holds them where they are.
    func update(_ look: Look?, running: Bool) {
        if let look { show(look) } else { hide() }
        let speed: Float = running ? Float(speedScale) : 0
        if field.speed != speed { setSpeed(speed) }
    }

    private var speedScale: Double {
        guard let look else { return 0 }
        return (60 + look.heat * 520 + look.overdrive * 400) / Self.baseSpeed
    }

    private func show(_ next: Look) {
        let previous = look
        guard next != previous || !shown else { return }
        look = next
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        field.instanceCount = max(1, next.count / streaks.count)
        let width = CGFloat(0.8 + next.heat * 0.8)
        for (index, streak) in streaks.enumerated() {
            streak.bounds = CGRect(x: 0, y: 0, width: 6, height: width)
            streak.cornerRadius = width / 2
            streak.backgroundColor = next.colors[index % next.colors.count]
        }
        let opacity = Float(0.25 + next.heat * 0.55)
        if previous?.heat != next.heat || previous?.frameRate != next.frameRate || !shown { animate(next) }
        if !shown {
            shown = true
            layer.isHidden = false
            fade(to: opacity)
        } else if layer.opacity != opacity {
            fade(to: opacity)
        }
        CATransaction.commit()
        if field.speed != 0 { setSpeed(Float(speedScale)) }
    }

    private func hide() {
        guard shown else { return }
        shown = false
        look = nil
        fade(to: 0)
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.45) { [weak self] in
            MainActor.assumeIsolated {
                guard let self, !self.shown else { return }
                self.layer.isHidden = true
                self.streaks.forEach { $0.removeAllAnimations() }
            }
        }
    }

    private func fade(to opacity: Float) {
        let fade = CABasicAnimation(keyPath: "opacity")
        fade.fromValue = layer.presentation()?.opacity ?? layer.opacity
        fade.toValue = opacity
        fade.duration = 0.4
        fade.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        layer.add(fade, forKey: "fade")
        layer.opacity = opacity
        CATransaction.commit()
    }

    /// The trip of one streak at the base speed: distance, tail and alpha keyframes along the exact solution of
    /// d' = v (0.4 + d / 140), d(t) = (d₀ + 56) e^{vt/140} − 56, until it leaves the board.
    private func animate(_ look: Look) {
        let reach = Self.reach(side)
        let v = Self.baseSpeed
        let duration = 140 / v * log((reach + 56) / (Self.startDistance + 56))
        var positions: [NSNumber] = [], tails: [NSNumber] = [], alphas: [NSNumber] = []
        var times: [NSNumber] = []
        for k in 0...Self.samples {
            let u = Double(k) / Double(Self.samples)
            let d = (Self.startDistance + 56) * exp(v * u * duration / 140) - 56
            times.append(NSNumber(value: u))
            positions.append(NSNumber(value: reach + d))
            tails.append(NSNumber(value: min(60, 6 + d * 0.18 * (0.3 + look.heat))))
            alphas.append(NSNumber(value: min(1, d / 120)))
        }
        let rate = CAFrameRateRange(minimum: 15, maximum: look.frameRate, preferred: look.frameRate)
        let start = epoch ?? field.convertTime(CACurrentMediaTime(), from: nil) - duration * Double(400)
        epoch = start
        for (index, streak) in streaks.enumerated() {
            let position = CAKeyframeAnimation(keyPath: "position.x")
            position.values = positions
            position.keyTimes = times
            let tail = CAKeyframeAnimation(keyPath: "bounds.size.width")
            tail.values = tails
            tail.keyTimes = times
            let alpha = CAKeyframeAnimation(keyPath: "opacity")
            alpha.values = alphas
            alpha.keyTimes = times
            let trip = CAAnimationGroup()
            trip.animations = [position, tail, alpha]
            trip.duration = duration
            trip.repeatCount = .infinity
            trip.preferredFrameRateRange = rate
            // Begun long ago, so every copy is already mid-flight; the same epoch keeps each streak where it is.
            trip.beginTime = start - duration * Double(index) * 0.5
            streak.add(trip, forKey: "trip")
        }
        field.instanceDelay = duration * Self.stagger
    }

    /// A new speed continues the field's time from where it is.
    private func setSpeed(_ speed: Float) {
        let now = CACurrentMediaTime()
        let local = field.convertTime(now, from: nil)
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        field.beginTime = layer.convertTime(now, from: nil)
        field.timeOffset = local
        field.speed = speed
        CATransaction.commit()
    }
}

extension BoardWarpLayer.Look {
    /// What the effects' heat asks for (DESIGN 2c: count 20 → 90 and Max's 1.5×, speed, length and width), in 1/20 steps
    /// so a wobbling value keeps its streaks, with a second color a little hotter.
    @MainActor
    init(_ fx: BoardEffects) {
        let heat = (fx.heat * 20).rounded() / 20
        let density = fx.intensity == .max ? 1.5 : 1
        self.init(count: min(BoardEffects.starCapacity, Int(((20 + heat * 70) * density).rounded())), heat: heat,
                  overdrive: (fx.overdrive * 10).rounded() / 10, frameRate: fx.frameClass == .fps60 ? 60 : 30,
                  colors: [fx.heatColor(at: heat), fx.heatColor(at: min(1, heat + 0.15))].map { RGBColor($0).cgColor })
    }
}
