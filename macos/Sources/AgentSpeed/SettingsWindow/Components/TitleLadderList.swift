import AgentSpeedCore
import SwiftUI

/// "Speed titles  Bicycle 8 · E-Bike 12 · … · Rocket 400": every title that scales with the redline and the speed it
/// starts at, wrapping across lines (DESIGN section 5, Redline speed). Line breaks are decided with every value at its
/// widest over the whole slider range, so dragging the redline changes the numbers but never the line count.
/// Hovering or focusing a title highlights its tick on the heat strip.
struct TitleLadderList: View {
    let redline: Double
    let metric: SpeedMetric
    let l10n: Localizer
    @Binding var highlighted: SpeedTitle?

    @State private var hovered: SpeedTitle?
    @FocusState private var focused: SpeedTitle?
    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.colorSchemeContrast) private var contrast

    private static let titleFont = Font.subheadline
    private static let valueFont = Font.subheadline.weight(.semibold).monospacedDigit()

    var body: some View {
        TitleLadderFlow(lineSpacing: 3) {
            Text(l10n("title.ladder"))
                .font(Self.titleFont.weight(.semibold))
                .foregroundStyle(.secondary)
                .lineLimit(1)
                .fixedSize()
                .padding(.trailing, 6)
                .layoutValue(key: LadderRoleKey.self, value: .heading)
                .accessibilityAddTraits(.isHeader)
            ForEach(Array(SpeedTitle.scaledLadder.enumerated()), id: \.element) { index, title in
                if index > 0 {
                    Text(verbatim: "·")
                        .font(Self.titleFont)
                        .foregroundStyle(.tertiary)
                        .fixedSize()
                        .padding(.horizontal, 5)
                        .layoutValue(key: LadderRoleKey.self, value: .separator)
                        .accessibilityHidden(true)
                }
                reserve(for: title)
                    .layoutValue(key: LadderRoleKey.self, value: .reserve)
                entry(for: title)
                    .layoutValue(key: LadderRoleKey.self, value: .entry)
            }
        }
        // Separators that would start a line and the width probes are placed outside this clip.
        .clipShape(Rectangle().inset(by: -4))
        .accessibilityElement(children: .contain)
        .accessibilityLabel(l10n("title.ladder"))
        .onChange(of: hovered) { publish() }
        .onChange(of: focused) { publish() }
    }

    private func publish() {
        let next = hovered ?? focused
        if highlighted != next { highlighted = next }
    }

    private func name(_ title: SpeedTitle) -> String {
        l10n(title.titleKey).capitalized(with: l10n.locale)
    }

    private func entry(for title: SpeedTitle) -> some View {
        let enter = title.enterSpeed(redline: redline)
        let value = TitleLadderValues.text(enter)
        let heat = HeatPalette.heat(forFraction: title.threshold)
        let isHighlighted = highlighted == title
        let valueColor = HeatText.color(heat, dark: colorScheme == .dark, increasedContrast: contrast == .increased)
        let nameStyle: AnyShapeStyle = isHighlighted || contrast == .increased ? AnyShapeStyle(.primary) : AnyShapeStyle(.secondary)
        return (Text(name(title)).foregroundStyle(nameStyle)
            + Text(verbatim: " ")
            + Text(verbatim: value).font(Self.valueFont).foregroundStyle(valueColor))
            .font(Self.titleFont)
            .lineLimit(1)
            .fixedSize()
            .background {
                RoundedRectangle(cornerRadius: 4, style: .continuous)
                    .fill(Color.primary.opacity(isHighlighted ? 0.07 : 0))
                    .padding(.horizontal, -3)
                    .padding(.vertical, -1)
            }
            .contentShape(Rectangle())
            .onHover { inside in
                if inside {
                    hovered = title
                } else if hovered == title {
                    hovered = nil
                }
            }
            .focusable(interactions: .activate)
            .focused($focused, equals: title)
            .focusEffectDisabled()
            .animation(Motion.fade, value: isHighlighted)
            .accessibilityElement(children: .ignore)
            .accessibilityLabel("\(name(title)), \(l10n("mac.settings.speedValue", value))")
    }

    /// The entry at each width its value can take over the slider range; only measured, never shown.
    private func reserve(for title: SpeedTitle) -> some View {
        ZStack {
            ForEach(TitleLadderValues.shapes(for: title, metric: metric), id: \.self) { shape in
                (Text(name(title)) + Text(verbatim: " ") + Text(verbatim: shape).font(Self.valueFont))
                    .font(Self.titleFont)
                    .lineLimit(1)
                    .fixedSize()
            }
        }
        .hidden()
        .accessibilityHidden(true)
    }
}

/// The digit patterns a title's threshold takes over the slider range of a metric ("0.0", "00", "0.00k", …). With
/// tabular digits, the widest of them is the widest the value ever gets.
@MainActor
enum TitleLadderValues {
    private static var cache: [String: [String]] = [:]

    static func shapes(for title: SpeedTitle, metric: SpeedMetric) -> [String] {
        let key = "\(metric.rawValue)/\(title.rawValue)"
        if let cached = cache[key] { return cached }
        var shapes: [String] = []
        for redline in redlines(for: metric) {
            let shape = pattern(text(title.enterSpeed(redline: redline)))
            if !shapes.contains(shape) { shapes.append(shape) }
        }
        cache[key] = shapes
        return shapes
    }

    /// Every redline the slider can produce for the metric.
    static func redlines(for metric: SpeedMetric, steps: Int = 400) -> [Double] {
        var values: [Double] = []
        for step in 0...steps {
            let value = SettingsLimits.redline(atPosition: Double(step) / Double(steps), metric: metric)
            if values.last != value { values.append(value) }
        }
        return values
    }

    /// The speed formatter's text, except that a whole number below 10 drops its ".0" ("Bicycle 8", as in the spec).
    static func text(_ value: Double) -> String {
        value >= 1 && value < 10 && value == value.rounded() ? String(Int(value)) : SpeedFormat.speed(value)
    }

    static func pattern(_ text: String) -> String {
        String(text.map { $0.isNumber ? "0" : $0 })
    }
}

enum LadderRole {
    case heading
    case separator
    /// A hidden copy of the next entry at its widest, used only to decide line breaks.
    case reserve
    case entry
}

struct LadderRoleKey: LayoutValueKey {
    static let defaultValue = LadderRole.entry
}

/// Places entries left to right and wraps them, dropping separators that would start a line.
struct TitleLadderFlow: Layout {
    var lineSpacing: CGFloat = 3

    func sizeThatFits(proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) -> CGSize {
        arrangement(width: proposal.width ?? .infinity, subviews: subviews).size
    }

    func placeSubviews(in bounds: CGRect, proposal: ProposedViewSize, subviews: Subviews, cache: inout ()) {
        let arrangement = arrangement(width: bounds.width, subviews: subviews)
        for (index, subview) in subviews.enumerated() {
            let origin = arrangement.origins[index].map { CGPoint(x: bounds.minX + $0.x, y: bounds.minY + $0.y) }
                ?? CGPoint(x: bounds.maxX + 10_000, y: bounds.minY)
            subview.place(at: origin, anchor: .topLeading, proposal: .unspecified)
        }
    }

    private func arrangement(width: CGFloat, subviews: Subviews) -> FlowArrangement {
        FlowArrangement(
            pieces: subviews.map { FlowArrangement.Piece(role: $0[LadderRoleKey.self], size: $0.sizeThatFits(.unspecified)) },
            width: width,
            lineSpacing: lineSpacing
        )
    }
}

/// Line breaking of the ladder, free of views: entries wrap when their widest form would overflow the line; a
/// separator is shown only between two entries on the same line.
struct FlowArrangement {
    struct Piece {
        var role: LadderRole
        var size: CGSize
    }

    /// Top-left of each piece, nil for pieces that are not shown.
    private(set) var origins: [CGPoint?]
    private(set) var size: CGSize = .zero
    private(set) var lineCount = 0

    init(pieces: [Piece], width: CGFloat, lineSpacing: CGFloat) {
        origins = Array(repeating: nil, count: pieces.count)
        var budgetX: CGFloat = 0
        var placedX: CGFloat = 0
        var y: CGFloat = 0
        var lineHeight: CGFloat = 0
        var widest: CGFloat = 0
        var separator: Int?
        var reserved: CGFloat?
        for (index, piece) in pieces.enumerated() {
            switch piece.role {
            case .separator:
                separator = index
                continue
            case .reserve:
                reserved = piece.size.width
                continue
            case .heading, .entry:
                break
            }
            let natural = piece.size.width
            let budget = max(natural, reserved ?? natural)
            let gap = separator.map { pieces[$0].size.width } ?? 0
            if budgetX > 0, budgetX + gap + budget > width + 0.5 {
                y += lineHeight + lineSpacing
                budgetX = 0
                placedX = 0
                lineHeight = 0
            } else if budgetX > 0, let separator {
                origins[separator] = CGPoint(x: placedX, y: y)
                placedX += gap
                budgetX += gap
                lineHeight = max(lineHeight, pieces[separator].size.height)
            }
            if budgetX == 0 { lineCount += 1 }
            origins[index] = CGPoint(x: placedX, y: y)
            placedX += natural
            budgetX += budget
            lineHeight = max(lineHeight, piece.size.height)
            widest = max(widest, budgetX)
            separator = nil
            reserved = nil
        }
        size = CGSize(width: width.isFinite ? max(width, 0) : widest, height: y + lineHeight)
    }
}
