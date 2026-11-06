import AgentSpeedCore
import AgentSpeedOrbModel
import SwiftUI

/// Settings → 懸浮窗, Boards (DESIGN 2c Interaction and window): header `boards.title` with `n / 4`, one row per
/// board (style thumbnail, style name over its sources and size, live value, visible switch) and a footer that adds
/// and removes. Without boards an inviting empty row offers `boards.add`. ↑ / ↓ select, Space shows or hides,
/// ⌫ removes (asking first).
struct BoardListSection: View {
    let model: AppModel

    @FocusState private var focused: Bool

    private var boards: BoardsController { model.boards }

    private var selection: String? {
        boards.settingsSelection.flatMap { boards.board($0)?.id } ?? boards.boards.first?.id
    }

    var body: some View {
        let l10n = model.l10n
        let list = boards.boards
        Section {
            VStack(spacing: 0) {
                if list.isEmpty {
                    BoardListEmptyRow(model: model)
                } else {
                    ForEach(Array(list.enumerated()), id: \.element.id) { index, board in
                        let selected = board.id == selection
                        let previousSelected = index > 0 && list[index - 1].id == selection
                        BoardListRow(model: model, board: board, selected: selected, focused: focused)
                            .overlay(alignment: .top) {
                                if index > 0, !selected, !previousSelected {
                                    OrbListHairline().padding(.leading, 60).padding(.trailing, 8)
                                }
                            }
                            .contentShape(Rectangle())
                            .onTapGesture {
                                boards.settingsSelection = board.id
                                focused = true
                            }
                    }
                    footer(l10n: l10n)
                        .overlay(alignment: .top) { OrbListHairline() }
                }
            }
            .focusable(interactions: .edit)
            .focused($focused)
            .focusEffectDisabled()
            .onKeyPress(keys: [.upArrow, .downArrow, .space, .delete, .deleteForward]) { press in
                handle(press.key) ? .handled : .ignored
            }
            .onKeyPress(characters: CharacterSet(charactersIn: "\u{7F}\u{8}\u{F728}")) { _ in
                handle(.delete) ? .handled : .ignored
            }
            .accessibilityElement(children: .contain)
            .accessibilityLabel(l10n("boards.title"))
        } header: {
            HStack(alignment: .firstTextBaseline) {
                Text(l10n("boards.title"))
                Spacer()
                Text(l10n("orbs.count", list.count, BoardsDocument.maxBoards))
                    .font(.subheadline)
                    .monospacedDigit()
                    .foregroundStyle(.secondary)
            }
        }
    }

    private func handle(_ key: KeyEquivalent) -> Bool {
        let list = boards.boards
        guard let selection, let index = list.firstIndex(where: { $0.id == selection }) else { return false }
        switch key {
        case .upArrow:
            if index > 0 { boards.settingsSelection = list[index - 1].id }
        case .downArrow:
            if index + 1 < list.count { boards.settingsSelection = list[index + 1].id }
        case .space:
            boards.setVisible(!list[index].visible, of: selection)
        case .delete, .deleteForward:
            BoardMenus.confirmRemove(selection, model: model)
        default:
            return false
        }
        return true
    }

    private func footer(l10n: Localizer) -> some View {
        HStack(spacing: Spacing.xxs) {
            Button {
                model.commands?.addBoard(from: selection.map { .board($0) } ?? .none)
            } label: {
                Image(systemName: "plus").frame(width: 22, height: 22)
            }
            .disabled(!boards.canAdd)
            .help(boards.canAdd ? l10n("boards.add") : l10n("boards.limit", BoardsDocument.maxBoards))
            .accessibilityLabel(l10n("boards.add"))

            Button {
                if let selection { BoardMenus.confirmRemove(selection, model: model) }
            } label: {
                Image(systemName: "minus").frame(width: 22, height: 22)
            }
            .disabled(selection == nil)
            .help(l10n("boards.remove"))
            .accessibilityLabel(l10n("boards.remove"))

            Spacer(minLength: Spacing.s)
        }
        .buttonStyle(.plain)
        .font(.system(size: 12, weight: .medium))
        .padding(.horizontal, Spacing.xs)
        .frame(height: OrbListSection.footerHeight)
    }
}

/// One board: style thumbnail, style name over sources and size, live value and the visibility switch.
struct BoardListRow: View {
    let model: AppModel
    let board: BoardConfig
    let selected: Bool
    let focused: Bool

    var body: some View {
        let l10n = model.l10n
        let boards = model.boards
        let sources = boards.sourcesText(of: board, l10n: l10n)
        let name = l10n(board.style.titleKey)
        HStack(spacing: 0) {
            BoardStyleThumbnail(style: board.style, size: 36, readouts: board.readouts)
                .frame(width: 60, height: OrbListRow.height)
            VStack(alignment: .leading, spacing: 1) {
                Text(name)
                    .font(.system(size: 13, weight: selected ? .medium : .regular))
                    .foregroundStyle(board.visible ? AnyShapeStyle(.primary) : AnyShapeStyle(.secondary))
                    .lineLimit(1)
                Text(l10n("mac.boards.sizeValue", sources, Int(board.size.side)))
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
                    .truncationMode(.middle)
            }
            Spacer(minLength: Spacing.s)
            // The runtime is not observed (it changes every engine tick); the value is read once a second.
            TimelineView(.periodic(from: .now, by: 1)) { _ in
                Text(valueText(l10n))
                    .font(.system(size: 12))
                    .monospacedDigit()
                    .foregroundStyle(.tertiary)
                    .lineLimit(1)
            }
            .padding(.trailing, Spacing.xs)
            Toggle(l10n("mac.boards.visible"), isOn: Binding(get: { board.visible }, set: { boards.setVisible($0, of: board.id) }))
                .toggleStyle(.switch)
                .controlSize(.mini)
                .labelsHidden()
                .help(l10n("mac.boards.visible"))
                .padding(.trailing, Spacing.xs)
        }
        .frame(height: OrbListRow.height)
        .opacity(board.visible ? 1 : 0.5)
        .background(OrbRowTint(selected: selected, focused: focused))
        .accessibilityElement(children: .contain)
        .accessibilityLabel("\(name), \(sources)")
        .accessibilityAddTraits(selected ? .isSelected : [])
    }

    private func valueText(_ l10n: Localizer) -> String {
        guard board.visible else { return l10n("orbs.hidden") }
        guard let tick = model.boards.runtime(board.id)?.tick, tick.valueText != "–" else { return "–" }
        return l10n("mac.settings.speedValue", tick.valueText)
    }
}

/// No board yet: the three styles side by side, what a board does, and `boards.add`.
private struct BoardListEmptyRow: View {
    let model: AppModel

    var body: some View {
        let l10n = model.l10n
        HStack(spacing: Spacing.m) {
            HStack(spacing: -10) {
                ForEach(Array(BoardStyle.allCases.enumerated()), id: \.element) { index, style in
                    BoardStyleThumbnail(style: style, size: 34)
                        .rotationEffect(.degrees(Double(index - 1) * 6))
                        .offset(y: index == 1 ? -2 : 1)
                        .zIndex(index == 1 ? 1 : 0)
                }
            }
            .padding(.leading, Spacing.xs)
            Footnote(l10n("mac.boards.empty"))
                .frame(maxWidth: .infinity, alignment: .leading)
            Button(l10n("boards.add")) {
                model.commands?.addBoard(from: .none)
            }
            .controlSize(.small)
        }
        .padding(.vertical, Spacing.s)
    }
}
