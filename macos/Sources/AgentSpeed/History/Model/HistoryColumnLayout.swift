import Foundation

/// How wide a column would like to be, how narrow it may get, and its share of spare width.
struct ColumnSpec: Equatable, Sendable {
    let natural: Double
    let minimum: Double
    var weight: Double = 0
}

enum HistoryColumnLayout {
    static let gap: Double = 12

    static func spec(_ column: HistoryColumn) -> ColumnSpec {
        switch column {
        case .date: ColumnSpec(natural: 124, minimum: 96)
        case .requests: ColumnSpec(natural: 68, minimum: 50)
        case .output: ColumnSpec(natural: 72, minimum: 52)
        case .input: ColumnSpec(natural: 112, minimum: 60)
        case .cacheHit: ColumnSpec(natural: 72, minimum: 52)
        case .speed: ColumnSpec(natural: 96, minimum: 72)
        case .peak: ColumnSpec(natural: 84, minimum: 60)
        case .agents: ColumnSpec(natural: 56, minimum: 42)
        case .models: ColumnSpec(natural: 150, minimum: 96, weight: 1)
        }
    }

    /// Widths left to right with `gap` between them. Spare width goes to weighted columns; when space is short,
    /// columns give up width in proportion to how far they are above their minimum, and never go below it.
    static func widths(_ specs: [ColumnSpec], width: Double, gap: Double = gap) -> [Double] {
        guard !specs.isEmpty else { return [] }
        var widths = specs.map { max(0, max($0.natural, $0.minimum)) }
        let available = max(0, (width.isFinite ? width : 0) - gap * Double(specs.count - 1))
        let natural = widths.reduce(0, +)
        if available >= natural {
            let extra = available - natural
            let weights = specs.reduce(0) { $0 + max(0, $1.weight) }
            for index in widths.indices {
                if weights > 0 {
                    widths[index] += extra * max(0, specs[index].weight) / weights
                } else if index == widths.count - 1 {
                    widths[index] += extra
                }
            }
        } else {
            let slack = specs.indices.map { max(0, widths[$0] - max(0, specs[$0].minimum)) }
            let totalSlack = slack.reduce(0, +)
            let share = totalSlack > 0 ? min(1, (natural - available) / totalSlack) : 0
            for index in widths.indices {
                widths[index] -= slack[index] * share
            }
        }
        return widths
    }
}
