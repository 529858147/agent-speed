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
    IReadOnlyList<string> TierLabels)
{
    public static OrbConfig Default { get; } = new(
        AppSettings.DefaultOrbSize, OrbAppearance.Dark, 1, new EffectsPolicy(EffectsIntensity.Standard), false, false,
        true, true, HeatMath.DefaultRedline, "tok/s", "NEW PEAK", ["IDLE", "CRUISE", "BOOST", "OVERDRIVE", "REDLINE"]);

    public string TierLabel(SpeedTier tier) => (int)tier < TierLabels.Count ? TierLabels[(int)tier] : string.Empty;
}
