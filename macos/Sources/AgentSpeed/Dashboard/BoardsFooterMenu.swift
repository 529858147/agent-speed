import AgentSpeedOrbModel
import SwiftUI

/// The dashboard footer's board items (DESIGN 2c): the floating-orb pill's context menu offers `boards.show` /
/// `boards.hide` while boards exist and `boards.add` (disabled at the limit).
struct BoardsFooterMenu: View {
    let model: AppModel

    var body: some View {
        let l10n = model.l10n
        ForEach(Array(BoardMenuModel.quick(model.boards.document).enumerated()), id: \.offset) { _, item in
            switch item {
            case .visibility(let anyVisible):
                Button(l10n(anyVisible ? "boards.hide" : "boards.show")) {
                    model.boards.setAllVisible(!anyVisible)
                }
            case .add(let enabled):
                Button(l10n("boards.add")) {
                    model.commands?.addBoard(from: .none)
                }
                .disabled(!enabled)
                .help(enabled ? "" : l10n("boards.limit", BoardsDocument.maxBoards))
            }
        }
    }
}
