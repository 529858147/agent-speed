import SwiftUI

struct OrbPane: View {
    let model: AppModel

    var body: some View {
        @Bindable var settings = model.settings
        let l10n = model.l10n
        let hidden = !settings.orbVisible
        Form {
            Section {
                SettingToggle(title: l10n("settings.orb.show"), isOn: $settings.orbVisible)
            }

            Section {
                SliderRow(
                    title: l10n("settings.orb.size"),
                    value: $settings.orbSize,
                    range: SettingsLimits.orbSize,
                    step: 4,
                    valueText: l10n("mac.settings.orb.sizeValue", Int(settings.orbSize.rounded()))
                ) {
                    SliderEndDot(size: 6)
                } maximumLabel: {
                    SliderEndDot(size: 12)
                }

                SegmentedRow(
                    title: l10n("mac.settings.orb.presets"),
                    selection: presetBinding,
                    segmentWidth: 64,
                    count: OrbSizePreset.allCases.count
                ) {
                    ForEach(OrbSizePreset.allCases) { preset in
                        Text(l10n(preset.titleKey)).tag(Optional(preset))
                    }
                }

                SliderRow(
                    title: l10n("settings.orb.opacity"),
                    value: opacityBinding,
                    range: SettingsLimits.orbOpacity,
                    step: 0.05,
                    valueText: l10n("mac.settings.percentValue", Int((settings.orbOpacity * 100).rounded()))
                ) {
                    SliderEndDot(size: 9, opacity: 0.35)
                } maximumLabel: {
                    SliderEndDot(size: 9)
                }
            }
            .disabled(hidden)

            Section {
                SettingToggle(title: l10n("menu.alwaysOnTop"), isOn: $settings.orbAlwaysOnTop)
                SettingToggle(
                    title: l10n("menu.clickThrough"),
                    subtitle: l10n("mac.settings.orb.clickThroughHint"),
                    isOn: $settings.orbClickThrough
                )
                SettingToggle(title: l10n("settings.orb.fadeIdle"), isOn: $settings.orbFadeWhenIdle)
                SettingToggle(title: l10n("settings.orb.allSpaces"), isOn: $settings.orbAllSpaces)
            } footer: {
                HStack {
                    Spacer()
                    Button(l10n("mac.settings.orb.resetPosition")) {
                        model.commands?.resetOrbPosition()
                    }
                }
            }
            .disabled(hidden)
        }
        .settingsForm()
    }

    /// The preset matching the current size, or none while the size is custom.
    private var presetBinding: Binding<OrbSizePreset?> {
        let settings = model.settings
        return Binding(
            get: { OrbSizePreset.allCases.first { abs($0.diameter - settings.orbSize) < 0.5 } },
            set: { preset in
                if let preset { settings.orbSize = preset.diameter }
            }
        )
    }

    /// Opacity in whole percent steps, so the readout and the stored value always agree.
    private var opacityBinding: Binding<Double> {
        let settings = model.settings
        return Binding(
            get: { settings.orbOpacity },
            set: { settings.orbOpacity = ($0 * 100).rounded() / 100 }
        )
    }
}
