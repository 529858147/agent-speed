import AgentSpeedOrbModel
import SwiftUI

/// The selected board's style (DESIGN 2c `boards.style`): three cards in one row, each the style's miniature over
/// its name; the selected card carries the accent ring and fill of the orb style cards. ← / → move between cards.
struct BoardStyleSection: View {
    let model: AppModel
    let board: BoardConfig

    @FocusState private var focused: Bool

    private static let cardHeight: CGFloat = 112
    private static let thumbnail: CGFloat = 64

    var body: some View {
        let l10n = model.l10n
        Section {
            HStack(spacing: 12) {
                ForEach(BoardStyle.allCases) { style in
                    card(style, l10n: l10n)
                }
            }
            .padding(.vertical, Spacing.xxs)
            .focusable()
            .focused($focused)
            .focusEffectDisabled()
            .onKeyPress(keys: [.leftArrow, .rightArrow]) { press in
                move(press.key == .rightArrow ? 1 : -1)
                return .handled
            }
        } header: {
            Text(l10n("boards.style"))
        }
    }

    private func card(_ style: BoardStyle, l10n: Localizer) -> some View {
        let selected = board.style == style
        return Button {
            model.boards.setStyle(style, of: board.id)
        } label: {
            VStack(spacing: Spacing.s) {
                BoardStyleThumbnail(style: style, size: Self.thumbnail, readouts: board.readouts)
                Text(l10n(style.titleKey))
                    .font(.system(size: 12, weight: selected ? .semibold : .medium))
                    .foregroundStyle(selected ? AnyShapeStyle(.primary) : AnyShapeStyle(.secondary))
                    .lineLimit(1)
                    .minimumScaleFactor(0.8)
            }
            .padding(.horizontal, 6)
            .frame(maxWidth: .infinity)
            .frame(height: Self.cardHeight)
            .background(StyleCardBackground(selected: selected))
            .overlay(StyleCardRing(selected: selected))
            .overlay {
                if focused && selected {
                    RoundedRectangle(cornerRadius: 12, style: .continuous)
                        .strokeBorder(Color.accentColor.opacity(0.35), lineWidth: 3)
                        .padding(-3)
                }
            }
            .contentShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
        }
        .buttonStyle(.plain)
        .accessibilityLabel(l10n(style.titleKey))
        .accessibilityAddTraits(selected ? .isSelected : [])
    }

    private func move(_ step: Int) {
        let styles = BoardStyle.allCases
        guard let index = styles.firstIndex(of: board.style) else { return }
        let next = min(max(index + step, 0), styles.count - 1)
        if next != index { model.boards.setStyle(styles[next], of: board.id) }
    }
}
