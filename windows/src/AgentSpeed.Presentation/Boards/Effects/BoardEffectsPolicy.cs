using AgentSpeed.Presentation.Settings;

namespace AgentSpeed.Presentation.Boards.Effects;

/// <summary>
/// What a board's effects layer may draw (DESIGN 2c, Effects intensity; 2c Accessibility): Off — no comets, particles,
/// bolts, streaks or warp; Soft — one comet at most, no particles or warp; Max — 1.5× particles and warp density. Reduce
/// Motion: nothing travels or bursts. High contrast: no glows, comets as a 2 pt solid ring segment.
/// <paramref name="Celebrate"/> is the global "Celebrate new peaks" (off: no new-peak or new-leader celebrations).
/// </summary>
public readonly record struct BoardEffectsPolicy(
    EffectsIntensity Intensity = EffectsIntensity.Standard,
    bool ReduceMotion = false,
    bool HighContrast = false,
    bool Celebrate = true)
{
    private bool Moving => Intensity > EffectsIntensity.Off && !ReduceMotion;

    /// <summary>Comets race the perimeter (Soft: <see cref="MaxComets"/> is one).</summary>
    public bool Comets => Moving;

    public int MaxComets => !Comets ? 0 : Intensity == EffectsIntensity.Soft ? 1 : int.MaxValue;

    /// <summary>Sparks, embers, streaks and bursts.</summary>
    public bool Particles => Moving && Intensity >= EffectsIntensity.Standard && !HighContrast;

    /// <summary>Shockwaves, bolts, row sweeps and the neon flash.</summary>
    public bool Transients => Moving;

    public bool Warp => Moving && Intensity >= EffectsIntensity.Standard && !HighContrast;

    /// <summary>The lit ring and its glow from h = 0.55 (a static border line stays in every mode).</summary>
    public bool Ring => Moving;

    public bool Glows => !HighContrast;

    /// <summary>Multiplier of particle counts and warp density.</summary>
    public double Density => Intensity == EffectsIntensity.Max ? 1.5 : 1;
}
