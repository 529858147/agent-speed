import AgentSpeedCore
import AgentSpeedOrbModel
import SwiftUI

/// Which sources the selected board counts (DESIGN 2c `sources`, as an orb's): All Sources, then every source with
/// its colour dot and status. With All Sources on the rows show checked and disabled; the last tracked source cannot
/// be unchecked.
struct BoardSourcesSection<Heading: View>: View {
    let model: AppModel
    let board: BoardConfig
    @ViewBuilder var heading: Heading

    var body: some View {
        let l10n = model.l10n
        let boards = model.boards
        let all = board.sources.isAll
        Section {
            Toggle(isOn: Binding(
                get: { all },
                set: { on in
                    let first = model.orbs.availability.detected.first ?? SourceKind.allCases[0]
                    boards.setSources(on ? .all : .explicit([first]), of: board.id)
                }
            )) {
                VStack(alignment: .leading, spacing: 2) {
                    Text(l10n("orbs.sources.all"))
                    Footnote(l10n("orbs.sources.allHint"))
                }
            }
            .toggleStyle(.checkbox)

            ForEach(SourceKind.allCases) { source in
                let tracked = board.sources.tracks(source)
                let isLast = !all && tracked && board.sources.toggling(source) == nil
                Toggle(isOn: Binding(
                    get: { tracked },
                    set: { _ in boards.toggleSource(source, of: board.id) }
                )) {
                    HStack(spacing: Spacing.xs) {
                        SourceDot(source: source, available: status(of: source) == nil)
                        Text(l10n(source.titleKey))
                        Spacer(minLength: Spacing.s)
                        if let status = status(of: source) {
                            Text(l10n(status))
                                .font(.system(size: 12))
                                .foregroundStyle(.secondary)
                        }
                    }
                }
                .toggleStyle(.checkbox)
                .disabled(all || isLast)
                .help(isLast ? l10n("orbs.sources.lastOne") : "")
            }
        } header: {
            VStack(alignment: .leading, spacing: 0) {
                heading
                Text(l10n("orbs.sources"))
            }
        }
    }

    private func status(of source: SourceKind) -> String? {
        if !model.settings.isEnabled(source) { return "source.disabled" }
        return model.orbs.availability.detected.contains(source) ? nil : "source.notFound"
    }
}

/// The board editor's heading: style name and sources (17 pt semibold).
struct BoardEditorHeading: View {
    let model: AppModel
    let board: BoardConfig

    var body: some View {
        let l10n = model.l10n
        HStack(spacing: Spacing.xs) {
            let dots = board.sources.knownSources.map { (source: $0, available: true) }
            if !dots.isEmpty { OrbSourceDots(dots: dots, size: 8) }
            Text(l10n(board.style.titleKey) + " · " + model.boards.sourcesText(of: board, l10n: l10n))
                .font(.system(size: 17, weight: .semibold))
                .lineLimit(1)
                .truncationMode(.tail)
            Spacer()
        }
        .foregroundStyle(.primary)
        .padding(.top, Spacing.xxs)
        .padding(.bottom, Spacing.xs)
        .accessibilityAddTraits(.isHeader)
    }
}
