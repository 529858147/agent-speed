import Foundation
import Testing
@testable import AgentSpeed

/// The menu bar ring keeps only what can be seen (issue #29): each new menu bar image makes the system re-lay out the
/// item and snapshot it for every display, so spring motion below a half-pixel step must not produce one.
@Suite("Menu bar ring")
@MainActor
struct StatusItemRingTests {
    private static let start = 1_000.0

    /// The ring at 10 fps for as long as the renderer asks for frames (until the springs settle).
    private static func framesUntilSettled(_ gauge: LiveGauge) -> [StatusItemState.Ring] {
        var rings: [StatusItemState.Ring] = []
        var time = start
        while !gauge.isSettled(at: time), rings.count < 100 {
            rings.append(StatusItemState.Ring(gauge: gauge, at: time, colored: true, flickers: true))
            time += StatusItemRenderer.frameInterval
        }
        return rings
    }

    private static func changes(_ rings: [StatusItemState.Ring]) -> Int {
        zip(rings, rings.dropFirst()).filter { $0 != $1 }.count
    }

    @Test func aSmallSpeedChangeRedrawsOnlyTheStepsItCrosses() {
        let gauge = LiveGauge()
        gauge.redline = 400
        gauge.snap(to: 100, now: Self.start)
        gauge.ingest(104, now: Self.start)
        let steps = abs(gauge.targetHeat - gauge.heat(at: Self.start)) * StatusItemState.Ring.fractionSteps
        let rings = Self.framesUntilSettled(gauge)
        // The spring moves for several frames (each one used to be a new image) but crosses fewer than two steps.
        #expect(steps < 1.5)
        #expect(rings.count >= 4)
        #expect(Self.changes(rings) <= 2)
        #expect(Self.changes(rings) < rings.count - 1)
    }

    @Test func aLargeSpeedChangeStillAnimatesToItsTarget() {
        let gauge = LiveGauge()
        gauge.redline = 400
        gauge.snap(to: 0, now: Self.start)
        gauge.ingest(400, now: Self.start)
        let rings = Self.framesUntilSettled(gauge)
        #expect(Self.changes(rings) >= 4)
        let final = StatusItemState.Ring(gauge: gauge, at: Self.start + 5, colored: true, flickers: false)
        #expect(final.fraction == 1)
        #expect(final == StatusItemState.Ring(gauge: gauge, at: Self.start + 6, colored: true, flickers: false))
    }

    @Test func theRingKeepsHalfPixelStepsAndNoGlowWhenMonochrome() {
        let gauge = LiveGauge()
        gauge.redline = 400
        gauge.snap(to: 37, now: Self.start)
        let ring = StatusItemState.Ring(gauge: gauge, at: Self.start + 1, colored: false, flickers: true)
        let steps = ring.fraction * StatusItemState.Ring.fractionSteps
        #expect(abs(steps - steps.rounded()) < 1e-9)
        #expect(abs(ring.fraction - gauge.heat(at: Self.start + 1)) <= 0.5 / StatusItemState.Ring.fractionSteps)
        #expect(ring.glow == 0)
        #expect(ring.fillOpacity == 1)
        #expect(StatusItemState.Ring.hidden.fraction == 0)
    }
}
