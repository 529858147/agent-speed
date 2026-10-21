using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Presentation.Settings;

namespace AgentSpeed.Orb;

/// <summary>Visual configuration of an orb instance (floating window or dashboard hero).</summary>
internal sealed record OrbConfig(
    double Diameter,
    OrbAppearance Appearance,
    double BodyOpacity,
    EffectsPolicy Effects,
    bool HighContrast,
    bool SolidBody,
    bool PulsesEnabled,
    bool PeaksEnabled,
    double Redline,
    string UnitText,
    string PeakText,
    IReadOnlyList<string> TitleLabels)
{
    public static OrbConfig Default { get; } = new(
        AppSettings.DefaultOrbSize, OrbAppearance.Dark, 1, new EffectsPolicy(EffectsIntensity.Standard), false, false,
        true, true, HeatMath.DefaultRedline, "tok/s", "NEW PEAK",
        ["IDLE", "WALKING", "BICYCLE", "E-BIKE", "MOTORCYCLE", "CAR", "SPORTS CAR", "BULLET TRAIN", "RACE CAR", "MAGLEV", "JETLINER",
            "FIGHTER JET", "HYPERSONIC", "ROCKET"]);

    /// <summary>The label for a speed title, indexed by <see cref="SpeedTitle"/>.</summary>
    public string TitleLabel(SpeedTitle title) => (int)title < TitleLabels.Count ? TitleLabels[(int)title] : string.Empty;
}
