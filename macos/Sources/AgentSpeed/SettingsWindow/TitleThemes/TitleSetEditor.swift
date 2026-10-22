import AgentSpeedCore
import AgentSpeedOrbModel
import AppKit
import SwiftUI

/// The editor of the selected custom title set (DESIGN 5, Title themes 4): name, base, the 14-row title table with the
/// live thresholds, the length hint and the footer, beside (from 600 pt) or under the live previews.
struct TitleSetEditor: View {
    let model: AppModel
    let set: CustomTitleSet
    /// Width of the group's content.
    let width: CGFloat
    /// The pane can be seen: the previews run.
    let active: Bool
    @Binding var focusRequest: TitleEditorFocus?

    @State private var feeds = TitleEditorPreviewFeeds()
    @State private var name = ""
    @State private var nameSet: String?
    @State private var focusedLevel: Int?
    @State private var requestedLevel: Int?
    @State private var message: String?
    @State private var messageToken = 0
    @FocusState private var nameFocused: Bool

    static let previewWidth: CGFloat = 200

    private var titles: TitleThemesController { model.titles }
    private var twoColumns: Bool { width >= 600 }

    var body: some View {
        let l10n = model.l10n
        VStack(alignment: .leading, spacing: Spacing.s) {
            Text(l10n("titles.custom.editing", set.name))
                .font(.system(size: 17, weight: .semibold))
                .lineLimit(1)
            if twoColumns {
                HStack(alignment: .top, spacing: Spacing.l) {
                    form(l10n: l10n)
                    preview
                        .frame(width: Self.previewWidth)
                }
            } else {
                preview
                form(l10n: l10n)
            }
        }
        .padding(.vertical, Spacing.xxs)
        .onAppear {
            syncName()
            feeds.show(.rocket, redline: model.settings.redline, animated: false)
            if focusRequest == .name {
                nameFocused = true
                focusRequest = nil
            }
        }
        .onChange(of: set.id) {
            syncName()
            focusedLevel = nil
            feeds.show(.rocket, redline: model.settings.redline, animated: false)
        }
        .onChange(of: focusRequest) {
            guard focusRequest == .name else { return }
            nameFocused = true
            focusRequest = nil
        }
        .onChange(of: model.settings.redline) {
            feeds.show(focusedLevel.flatMap(SpeedTitle.init(rawValue:)) ?? .rocket, redline: model.settings.redline, animated: false)
        }
    }

    private var preview: some View {
        TitleEditorPreview(model: model, feeds: feeds, theme: titles.general, active: active)
    }

    // MARK: Form

    private func form(l10n: Localizer) -> some View {
        let baseName = l10n(set.base.nameKey)
        return VStack(alignment: .leading, spacing: Spacing.xs) {
            Grid(alignment: .leading, horizontalSpacing: Spacing.s, verticalSpacing: Spacing.xs) {
                GridRow {
                    Text(l10n("titles.custom.name"))
                        .foregroundStyle(.secondary)
                    TextField(l10n("titles.custom.name"), text: $name)
                        .labelsHidden()
                        .textFieldStyle(.roundedBorder)
                        .focused($nameFocused)
                        .onSubmit { commitName() }
                        .onChange(of: nameFocused) { if !nameFocused { commitName() } }
                }
                GridRow {
                    Text(l10n("titles.custom.base"))
                        .foregroundStyle(.secondary)
                    Picker(l10n("titles.custom.base"), selection: Binding(
                        get: { set.base },
                        set: { titles.setBase($0, of: set.id) }
                    )) {
                        ForEach(TitlePreset.allCases) { preset in
                            Text(l10n(preset.nameKey)).tag(preset)
                        }
                    }
                    .labelsHidden()
                    .pickerStyle(.menu)
                    .fixedSize()
                }
            }
            Footnote(l10n("titles.custom.baseHint", baseName))
            if set.base.showsVehicleArt {
                Footnote(l10n("titles.custom.vehicleArt"))
            }
            table(l10n: l10n)
            if let message {
                Notice(text: message, symbol: "exclamationmark.triangle.fill", tint: .orange)
                    .transition(.opacity)
            }
            Footnote(l10n("titles.custom.lengthHint"))
            footer(l10n: l10n)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .animation(.easeOut(duration: 0.2), value: message)
    }

    private func syncName() {
        guard nameSet != set.id || !nameFocused else { return }
        nameSet = set.id
        name = set.name
    }

    /// An empty name keeps the previous one.
    private func commitName() {
        titles.rename(set.id, to: name)
        if CustomTitleSet.cleanName(name) == nil { name = set.name }
    }

    // MARK: Table

    private func table(l10n: Localizer) -> some View {
        VStack(spacing: 0) {
            HStack(spacing: Spacing.s) {
                Color.clear.frame(width: 18)
                Text(l10n("titles.custom.speedColumn", l10n("unit.tokS")))
                    .frame(width: 92, alignment: .trailing)
                Text(l10n("titles.custom.titleColumn"))
                Spacer(minLength: 0)
            }
            .font(.system(size: 11, weight: .semibold))
            .foregroundStyle(.secondary)
            .frame(height: 24)
            ForEach(0...TitlePreset.levelCount, id: \.self) { level in
                Divider().opacity(0.6)
                row(level, l10n: l10n)
            }
        }
    }

    private func row(_ level: Int, l10n: Localizer) -> some View {
        let title = SpeedTitle(rawValue: level) ?? .idle
        let base = TitleTheme.preset(set.base).text(level: level, localized: { l10n($0) })
        let threshold = TitleLadderValues.text(title.enterSpeed(redline: model.settings.redline))
        return TitleSetRow(
            level: level,
            text: set.field(level: level),
            placeholder: base,
            threshold: level == 0 ? l10n("titles.custom.idleRow") : threshold,
            accessibilityLabel: level == 0
                ? l10n("titles.custom.idle.a11y", base)
                : l10n("titles.custom.row.a11y", level, "\(threshold) \(l10n("unit.tokS"))", base),
            resetLabel: l10n("titles.custom.reset", base),
            focused: focusedLevel == level,
            wantsFocus: requestedLevel == level,
            onFocus: { focus(level) },
            onDraft: { titles.setDraft(level: level, of: set.id, text: $0) },
            onCommit: { commit(level, $0) },
            onMove: { delta in
                let next = level + delta
                if (0...TitlePreset.levelCount).contains(next) { requestedLevel = next }
            },
            onBlur: { blur(level) }
        )
        .equatable()
    }

    // MARK: Behaviour

    private func focus(_ level: Int) {
        requestedLevel = nil
        guard focusedLevel != level else { return }
        focusedLevel = level
        feeds.show(SpeedTitle(rawValue: level) ?? .idle, redline: model.settings.redline, animated: true)
    }

    private func blur(_ level: Int) {
        titles.clearDraft()
        DispatchQueue.main.async {
            guard focusedLevel == level else { return }
            focusedLevel = nil
            feeds.show(.rocket, redline: model.settings.redline, animated: true)
        }
    }

    private func commit(_ level: Int, _ text: String) -> String {
        let result = titles.commitField(level: level, of: set.id, text: text)
        let l10n = model.l10n
        if result.cut {
            show(l10n("titles.custom.tooLong"))
        } else if result.removedEmoji {
            show(l10n("titles.custom.noEmoji"))
        }
        return result.text
    }

    /// Shows a note for 4 s and announces it politely.
    private func show(_ text: String) {
        message = text
        messageToken &+= 1
        let token = messageToken
        NSAccessibility.post(element: NSApp as Any, notification: .announcementRequested, userInfo: [
            .announcement: text,
            .priority: NSAccessibilityPriorityLevel.medium.rawValue,
        ])
        DispatchQueue.main.asyncAfter(deadline: .now() + 4) {
            if messageToken == token { message = nil }
        }
    }

    // MARK: Footer

    private func footer(l10n: Localizer) -> some View {
        HStack(spacing: Spacing.m) {
            Button(l10n("titles.custom.resetAll")) {
                titles.resetAll(set.id)
            }
            .disabled(set.idle.isEmpty && set.titles.allSatisfy(\.isEmpty))
            Button(l10n("titles.custom.duplicate")) {
                if titles.copy(set.id) != nil { focusRequest = .name }
            }
            .disabled(!titles.document.canAddCustom)
            .help(titles.document.canAddCustom ? "" : l10n("titles.custom.limit", TitleThemesDocument.maxCustom))
            Spacer(minLength: Spacing.s)
            Button(l10n("titles.custom.delete"), role: .destructive) {
                titles.delete(set.id)
            }
            .foregroundStyle(.red)
        }
        .buttonStyle(.borderless)
        .font(.system(size: 13))
        .tint(.accentColor)
        .padding(.top, Spacing.xxs)
    }
}

/// One row of the title table. Equatable on what it shows (its actions only reach stable state), so moving the focus
/// redraws just the two rows involved.
private struct TitleSetRow: View, Equatable {
    let level: Int
    let text: String
    let placeholder: String
    /// The threshold at the current redline, or the idle row's label.
    let threshold: String
    let accessibilityLabel: String
    let resetLabel: String
    let focused: Bool
    let wantsFocus: Bool
    var onFocus: () -> Void
    var onDraft: (String) -> Void
    var onCommit: (String) -> String
    var onMove: (Int) -> Void
    var onBlur: () -> Void

    static let height: CGFloat = 30

    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.colorSchemeContrast) private var contrast

    nonisolated static func == (a: Self, b: Self) -> Bool {
        a.level == b.level && a.text == b.text && a.placeholder == b.placeholder && a.threshold == b.threshold
            && a.accessibilityLabel == b.accessibilityLabel && a.resetLabel == b.resetLabel && a.focused == b.focused
            && a.wantsFocus == b.wantsFocus
    }

    var body: some View {
        HStack(spacing: Spacing.s) {
            Text(level == 0 ? "" : "\(level)")
                .font(.system(size: 11))
                .monospacedDigit()
                .foregroundStyle(.tertiary)
                .frame(width: 18, alignment: .trailing)
                .accessibilityHidden(true)
            Group {
                if level == 0 {
                    Text(threshold)
                        .font(.system(size: 13))
                        .foregroundStyle(.secondary)
                } else {
                    Text(threshold)
                        .font(.system(size: 13, weight: .semibold))
                        .monospacedDigit()
                        .foregroundStyle(HeatText.color(TitleThemeText.heat(SpeedTitle(rawValue: level) ?? .idle),
                                                        dark: colorScheme == .dark, increasedContrast: contrast == .increased))
                }
            }
            .lineLimit(1)
            .frame(width: 92, alignment: .trailing)
            .accessibilityHidden(true)
            TitleField(
                text: text,
                placeholder: placeholder,
                accessibilityLabel: accessibilityLabel,
                wantsFocus: wantsFocus,
                onFocus: onFocus,
                onDraft: onDraft,
                onCommit: onCommit,
                onMove: onMove,
                onBlur: onBlur
            )
            .frame(height: 22)
            Button {
                _ = onCommit("")
            } label: {
                Image(systemName: "arrow.counterclockwise")
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundStyle(.secondary)
                    .frame(width: 18, height: 18)
                    .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .opacity(text.isEmpty ? 0 : 1)
            .disabled(text.isEmpty)
            .help(resetLabel)
            .accessibilityLabel(resetLabel)
            .animation(.easeOut(duration: 0.2), value: text.isEmpty)
        }
        .padding(.horizontal, 4)
        .frame(height: Self.height)
        .background(
            RoundedRectangle(cornerRadius: 6, style: .continuous)
                .fill(Color.accentColor.opacity(focused ? 0.09 : 0))
        )
    }
}
