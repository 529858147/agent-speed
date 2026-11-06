import AgentSpeedCore
import AgentSpeedOrbModel
import CoreGraphics

/// Plays one tick on a board's effects (DESIGN 2c Motion and effects): heat always; while the board renders, its
/// passes at the rows' new places (overtake, new leader), the number's punch and a new peak.
@MainActor
enum BoardEffectsPlayer {
    static func play(_ tick: BoardTick, layout: BoardContentLayout?, punch: Double?, on fx: inout BoardEffects) {
        fx.setHeat(tick.heat, overdrive: tick.overdrive, active: tick.generating > 0)
        guard let layout else { return }
        for event in tick.events {
            guard let anchor = layout.groupRows[event.id], let group = tick.groups.first(where: { $0.id == event.id }) else {
                continue
            }
            let color = group.root.source.tint.simd
            // The group now just below the climber is the one it passed.
            let passed = event.to < tick.groups.count
                ? layout.groupRows[tick.groups[event.to].id].map { Double($0.rank.y) } : nil
            if event.kind == .newLeader {
                fx.newLeader(row: anchor.row, rank: anchor.rank, color: color, passedRankY: passed)
            } else {
                fx.overtake(row: anchor.row, rank: anchor.rank, color: color, passedRankY: passed)
            }
        }
        if let punch { fx.punch(number: layout.number, rise: punch) }
        if tick.newPeak { fx.newPeak(at: CGPoint(x: layout.number.midX, y: layout.number.midY)) }
    }

    /// The board's effects level: the global setting as the accessibility state allows it (DESIGN 2c Effects
    /// intensity).
    static func intensity(_ level: EffectsLevel) -> BoardEffectsIntensity {
        BoardEffectsIntensity(rawValue: level.rawValue) ?? .standard
    }
}
