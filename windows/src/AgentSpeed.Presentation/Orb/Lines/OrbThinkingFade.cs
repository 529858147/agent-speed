using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Presentation.Motion;

namespace AgentSpeed.Presentation.Orb.Lines;

/// <summary>
/// The intelligence readout's thinking line at zero (DESIGN 10, Thinking hidden at zero): while its shown value is 0 it stays
/// for 3 s, then fades out (0.25 s) as the intelligence line glides (smoothstep, 0.3 s) to the single-line position; a value
/// above 0 brings it back at once, the fade and the glide reversing from wherever they are. Reduce Motion makes both
/// instant. Times are seconds on the shared orb clock; the default is a thinking line that shows.
/// </summary>
/// <param name="HideAt">When the line begins to hide, 3 s after its value began to read 0; null while it reads above 0.</param>
/// <param name="FadeInAt">When it began fading back in, moved earlier by the opacity it still had; null: it never came back.</param>
/// <param name="GlideBackAt">When the intelligence line began gliding back up, moved earlier by the part of the glide it had not run.</param>
public readonly record struct OrbThinkingFade(double? HideAt = null, double? FadeInAt = null, double? GlideBackAt = null)
{
    public const double FadeOut = 0.25;
    public const double FadeIn = 0.25;
    public const double Glide = 0.3;

    /// <summary>Applies one sample at <paramref name="time"/>: <paramref name="zero"/> when its thinking value reads 0.</summary>
    public OrbThinkingFade Next(bool zero, double time)
    {
        if (zero)
        {
            return HideAt is null ? this with { HideAt = time + ThinkingZeroHold.Seconds } : this;
        }

        if (HideAt is not { } hideAt)
        {
            return this;
        }

        // During the hold nothing has moved yet; after it, the line returns from its current opacity and position.
        return time < hideAt
            ? this with { HideAt = null }
            : new OrbThinkingFade(null, time - Alpha(time, reduceMotion: false) * FadeIn, time - (1 - Away(time, reduceMotion: false)) * Glide);
    }

    /// <summary>The line's value reads 0 and its hold is over at <paramref name="time"/>: the label reads the intelligence alone.</summary>
    public bool Hides(double time) => HideAt is { } hideAt && time >= hideAt;

    /// <summary>The thinking line's opacity at <paramref name="time"/>, on top of the readout's own.</summary>
    public double Alpha(double time, bool reduceMotion)
    {
        var outgoing = HideAt is { } hideAt ? 1 - Ramp(time, hideAt, reduceMotion ? 0 : FadeOut) : 1;
        var incoming = FadeInAt is { } fadeInAt ? Ramp(time, fadeInAt, reduceMotion ? 0 : FadeIn) : 1;
        return Math.Min(outgoing, incoming);
    }

    /// <summary>How far the intelligence line has glided to the single-line position at <paramref name="time"/>, eased: 0 two lines, 1 alone.</summary>
    public double Single(double time, bool reduceMotion) => Easing.SmoothStep(0, 1, Away(time, reduceMotion));

    /// <summary>The fade or the glide runs at <paramref name="time"/> (frames at 30 fps).</summary>
    public bool IsMoving(double time, bool reduceMotion) =>
        !reduceMotion && (Within(time, HideAt, Math.Max(FadeOut, Glide)) || Within(time, FadeInAt, FadeIn) || Within(time, GlideBackAt, Glide));

    /// <summary>The hold's end when it is still ahead of <paramref name="time"/>: a static orb wakes for it.</summary>
    public double? NextChange(double time) => HideAt is { } hideAt && hideAt > time ? hideAt : null;

    /// <summary>The glide's linear progress toward the single-line position.</summary>
    private double Away(double time, bool reduceMotion)
    {
        var glide = reduceMotion ? 0 : Glide;
        var away = HideAt is { } hideAt ? Ramp(time, hideAt, glide) : 0;
        var back = GlideBackAt is { } backAt ? Ramp(time, backAt, glide) : 1;
        return Math.Max(away, 1 - back);
    }

    private static double Ramp(double time, double start, double duration) =>
        duration > 0 ? Math.Clamp((time - start) / duration, 0, 1) : time >= start ? 1 : 0;

    private static bool Within(double time, double? start, double duration) => start is { } at && time >= at && time < at + duration;
}
