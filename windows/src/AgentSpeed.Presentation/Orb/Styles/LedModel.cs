using AgentSpeed.Presentation.Heat;

namespace AgentSpeed.Presentation.Orb.Styles;

/// <summary>
/// The <c>led</c> style (DESIGN 2a.1): its instrument state (gauge heat, comet, sparks, embers, shimmer, edge glow)
/// lives in the shared <see cref="OrbScene"/> as it always has; this adapter only reports the style's frame class.
/// </summary>
public sealed class LedModel : IOrbStyleModel
{
    private readonly OrbScene _scene;

    public LedModel(OrbScene scene)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
    }

    public OrbStyle Style => OrbStyle.Led;

    public bool Small { get; private set; }

    public bool Thumbnail { get; private set; }

    /// <summary>Idle: static apart from the 30 fps sweep; cruise and boost (h &lt; 0.45) 30 fps; overdrive and redline 60 fps.</summary>
    public FrameClass Demand
    {
        get
        {
            var scene = _scene;
            var effects = scene.Effects;
            var lingering = scene.Particles.Count > 0 || scene.CometOpacity > 0.005 && scene.Tier == SpeedTier.Idle;
            if (scene.Tier == SpeedTier.Idle)
            {
                return scene.IdleSweep is not null || lingering ? FrameClass.Fps30 : FrameClass.None;
            }

            if (!effects.AuraBreathing && !effects.Comet && !lingering)
            {
                return FrameClass.None;
            }

            if (scene.Heat >= 0.45 || scene.Tier >= SpeedTier.Overdrive)
            {
                return effects.Level >= Settings.EffectsIntensity.Soft ? FrameClass.Fps60 : FrameClass.Fps30;
            }

            return FrameClass.Fps30;
        }
    }

    public void Update(in OrbFrame frame)
    {
        Small = frame.Small;
        Thumbnail = frame.Thumbnail;
        _scene.ParticleRateScale = frame.Small ? 0.5 : 1;
    }

    public void Snap(in OrbFrame frame)
    {
        Update(frame);
        _scene.Particles.Clear();
    }

    public void Pulse(double heat, double strength)
    {
    }

    public void Peak()
    {
    }
}
