import AgentSpeedCore
import AgentSpeedOrbModel
import Foundation
import Observation

/// Whether the menu bar block's thinking line is hidden (DESIGN 10, Menu bar and taskbar, Thinking hidden at zero):
/// once the shown rate has read 0 under a live value for 3 s, until it reads above 0. Follows every engine tick; one
/// timer ends the hold, so a still bar draws nothing while it waits.
@MainActor
@Observable
final class StatusItemThinkingHold {
    private(set) var hidden = false
    @ObservationIgnored private var line = OrbThinkingLine()
    @ObservationIgnored private var timer: Timer?
    @ObservationIgnored private var timerEnd: TimeInterval?

    /// The shown rate reads 0 under a live value; without a value both numbers are `–` and nothing is held.
    static func readsZero(_ sums: IntelligenceSums) -> Bool {
        sums.shownIndex != nil && OrbThinkingLine.readsZero(SpeedFormat.speed(sums.thinking))
    }

    func update(_ sums: IntelligenceSums, at time: TimeInterval) {
        line.update(zero: Self.readsZero(sums), at: time)
        refresh(at: time)
    }

    private func refresh(at time: TimeInterval) {
        let hidden = line.isHidden(at: time)
        if self.hidden != hidden { self.hidden = hidden }
        let end = line.holdEnd(after: time)
        guard end != timerEnd else { return }
        timer?.invalidate()
        timer = nil
        timerEnd = end
        guard let end else { return }
        let timer = Timer(timeInterval: end - time, repeats: false) { [weak self] _ in
            MainActor.assumeIsolated {
                guard let self else { return }
                self.timer = nil
                self.timerEnd = nil
                self.refresh(at: max(Clock.now, end))
            }
        }
        timer.tolerance = 0.02
        RunLoop.main.add(timer, forMode: .common)
        self.timer = timer
    }
}
