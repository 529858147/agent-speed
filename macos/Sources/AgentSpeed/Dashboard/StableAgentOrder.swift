import AgentSpeedCore

/// Keeps the agents list from reshuffling on noise. The engine order (METRICS 6.7) decides who is shown and wins
/// whenever the gap is real; near-ties within the same phase keep the order the user is already looking at.
@MainActor
final class StableAgentOrder {
    private var previous: [String: Int] = [:]

    func arrange(_ agents: [AgentStatus], limit: Int, speed: (AgentStatus) -> Double) -> [AgentStatus] {
        var rows = Array(agents.prefix(limit))
        // An agent that just slipped below the cut stays if it near-ties the newcomer that would replace it.
        for candidate in agents.dropFirst(limit) where previous[candidate.id] != nil {
            guard let slot = rows.lastIndex(where: { previous[$0.id] == nil }),
                  Self.rank(rows[slot].phase) == Self.rank(candidate.phase),
                  Self.isNearTie(speed(rows[slot]), speed(candidate)) else { continue }
            rows[slot] = candidate
        }
        var swapped = true
        var passes = 0
        while swapped, passes < limit {
            swapped = false
            passes += 1
            for index in rows.indices.dropLast() {
                let upper = rows[index], lower = rows[index + 1]
                guard let was = previous[upper.id], let other = previous[lower.id], other < was,
                      Self.rank(upper.phase) == Self.rank(lower.phase),
                      Self.isNearTie(speed(upper), speed(lower)) else { continue }
                rows.swapAt(index, index + 1)
                swapped = true
            }
        }
        previous = Dictionary(uniqueKeysWithValues: rows.enumerated().map { ($1.id, $0) })
        return rows
    }

    private static func rank(_ phase: AgentPhase) -> Int {
        switch phase {
        case .generating: 0
        case .tools: 1
        case .idle: 2
        }
    }

    private static func isNearTie(_ a: Double, _ b: Double) -> Bool {
        abs(a - b) <= max(3, 0.15 * max(a, b))
    }
}
