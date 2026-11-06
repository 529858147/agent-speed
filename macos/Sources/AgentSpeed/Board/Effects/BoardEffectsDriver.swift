import AgentSpeedOrbModel
import AppKit

/// Runs a board's `BoardEffects` on the shared frame clock (DESIGN 2c Frame policy): the overlay canvas advances the
/// simulation and draws it while the board can be seen and asks for nothing otherwise, so an idle, hidden or occluded
/// board renders nothing. Neon's warp under the rows is animated by the render server (`BoardWarpLayer`) from the
/// tick's heat: a second surface presented on every frame would cost as much again.
@MainActor
final class BoardEffectsDriver {
    let overlay = OrbCanvasView(frame: .zero)
    let warp = BoardWarpLayer()
    private(set) var effects: BoardEffects
    /// Visible, not occluded and not faded out.
    var canRender = true {
        didSet {
            guard canRender != oldValue else { return }
            if canRender { lastTime = nil }
            updateRequests()
            if canRender { refresh() }
            updateWarp()
        }
    }
    var highContrast = false {
        didSet { if highContrast != oldValue { refresh() } }
    }
    /// The board's appearance: the prototype adds the warp's light (`lighter`), which vanishes on a light deck, so a
    /// light board shows none.
    var dark = true {
        didSet { if dark != oldValue { updateWarp() } }
    }
    /// Frames rendered by the overlay (benchmarks).
    var framesDrawn: Int { overlay.framesDrawn }

    private var lastTime: TimeInterval?

    init(style: BoardStyle, side: Double) {
        effects = BoardEffects(style: style, side: side) { HeatPalette.color($0).simd }
        effects.simulatesWarp = false
        overlay.sceneProvider = { [unowned self] scene, _, _, time in
            advance(to: time)
            BoardEffectsRenderer.overlay(effects, into: &scene, highContrast: highContrast)
        }
        overlay.onFrame = { [unowned self] in updateRequests() }
        updateRequests()
    }

    /// Changes the effects (heat, events, settings) and wakes the canvas when it needs frames or one redraw; a change
    /// that leaves the picture as it is draws nothing.
    func update(_ change: (inout BoardEffects) -> Void) {
        let before = effects.still
        change(&effects)
        updateRequests()
        if effects.still != before { refresh() }
        updateWarp()
    }

    func setBoard(side: Double, corner: CGFloat) {
        effects.setSide(side)
        warp.setBoard(side: CGFloat(side), corner: corner)
        refresh()
    }

    private func advance(to time: TimeInterval) {
        let dt = lastTime.map { time - $0 } ?? 0
        lastTime = time
        effects.advance(dt)
    }

    private func updateRequests() {
        let frameClass = canRender ? effects.frameClass : .none
        overlay.request = OrbFrameRequest(frameClass: frameClass)
        if frameClass == .none { lastTime = nil }
    }

    /// One frame of the current state when nothing animates (a static board keeps its last frame).
    private func refresh() {
        guard canRender, overlay.request.frameClass == .none else { return }
        overlay.setNeedsFrame()
    }

    /// The warp follows the tick's heat; it holds still while the board cannot be seen.
    private func updateWarp() {
        warp.update(effects.warpRequested && dark ? BoardWarpLayer.Look(effects) : nil, running: canRender)
    }
}
