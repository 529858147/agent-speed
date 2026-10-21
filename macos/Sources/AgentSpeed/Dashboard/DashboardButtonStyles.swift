import SwiftUI

/// Borderless icon button with a soft hover plate (footer).
struct DashboardIconButtonStyle: ButtonStyle {
    var isOn = false

    func makeBody(configuration: Configuration) -> some View {
        HoverPlate(isPressed: configuration.isPressed, isOn: isOn) {
            configuration.label
                .font(.system(size: 13.5, weight: .regular))
                .frame(width: 28, height: 28)
        }
    }
}

/// Borderless icon-and-title button; tinted when it represents an active state.
struct DashboardPillButtonStyle: ButtonStyle {
    var isOn = false

    func makeBody(configuration: Configuration) -> some View {
        HoverPlate(isPressed: configuration.isPressed, isOn: isOn) {
            configuration.label
                .font(.system(size: 12, weight: .medium))
                .padding(.horizontal, 9)
                .frame(height: 28)
        }
    }
}

/// Full-width row button whose hover plate bleeds slightly past the column, so its content stays aligned with the
/// rows above while the layout keeps the plain row height (agents disclosure).
struct DashboardRowButtonStyle: ButtonStyle {
    private static let bleed: CGFloat = 6
    private static let lift: CGFloat = 2

    func makeBody(configuration: Configuration) -> some View {
        HoverPlate(isPressed: configuration.isPressed, isOn: false) {
            configuration.label
                .padding(.horizontal, Self.bleed)
                .frame(maxWidth: .infinity, minHeight: DashboardLayout.rowHeight + Self.lift * 2, alignment: .leading)
        }
        .padding(.horizontal, -Self.bleed)
        .padding(.vertical, -Self.lift)
    }
}

private struct HoverPlate<Label: View>: View {
    let isPressed: Bool
    let isOn: Bool
    @ViewBuilder var label: Label

    @State private var isHovered = false
    @Environment(\.isEnabled) private var isEnabled
    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.colorSchemeContrast) private var contrast

    var body: some View {
        let dark = colorScheme == .dark
        label
            .foregroundStyle(foreground(dark: dark))
            .background {
                RoundedRectangle(cornerRadius: 7, style: .continuous)
                    .fill(plate(dark: dark))
            }
            .contentShape(RoundedRectangle(cornerRadius: 7, style: .continuous))
            .opacity(isEnabled ? 1 : 0.4)
            .onHover { isHovered = $0 && isEnabled }
            .animation(Motion.fade, value: isHovered)
            .animation(Motion.fade, value: isOn)
    }

    private func foreground(dark: Bool) -> AnyShapeStyle {
        if isOn || isHovered || isPressed { return AnyShapeStyle(.primary) }
        return AnyShapeStyle(.dashboardSecondary)
    }

    private func plate(dark: Bool) -> Color {
        let base = isOn ? Color.accentColor : (dark ? Color.white : Color.black)
        var level: Double = 0
        if isOn { level = 0.14 }
        if isHovered { level += isOn ? 0.06 : (dark ? 0.1 : 0.07) }
        if isPressed { level += 0.06 }
        if contrast == .increased, level > 0 { level += 0.06 }
        return base.opacity(level)
    }
}
