import SwiftUI

struct MenuBarPane: View {
    let model: AppModel

    var body: some View {
        @Bindable var settings = model.settings
        let l10n = model.l10n
        Form {
            Section {
                MenuBarPreview(model: model)
            }

            Section {
                Picker(l10n("settings.bar.mode"), selection: $settings.barMode) {
                    ForEach(BarDisplayMode.allCases) { mode in
                        Text(l10n(mode.titleKey)).tag(mode)
                    }
                }
                .pickerStyle(.radioGroup)

                SettingToggle(title: l10n("settings.bar.colored"), isOn: $settings.barColoredGlyph)
                    .disabled(!settings.barMode.showsGlyph)

                SettingToggle(title: l10n("settings.bar.unit"), isOn: $settings.barShowsUnit)
                    .disabled(!settings.barMode.showsValue)
            }

            Section {
                LiveUnitPicker(model: model)
            } footer: {
                SectionFootnote(l10n("settings.liveUnit.hint"))
            }
        }
        .settingsForm()
    }
}
