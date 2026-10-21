using AgentSpeed.Presentation.Gauge;

namespace AgentSpeed.Presentation.Orb;

/// <summary>
/// Fits the title label under the orb's value (DESIGN section 2, item 6): the label may use the chord inside the gauge
/// ring at its lowest point, minus a margin, and a title wider than that shrinks down to <see cref="MinimumScale"/> of
/// its size. It is never truncated. Lengths are in units of the orb diameter S.
/// </summary>
public static class OrbLabelFit
{
    /// <summary>Label size as a fraction of S.</summary>
    public const double Size = 0.062;

    /// <summary>Part of the label size that hangs below the baseline (CJK and Latin descent).</summary>
    public const double DescentRatio = 0.16;

    /// <summary>Clear space kept between the label and the inner edge of the gauge segments.</summary>
    public const double RingMargin = 0.035;

    public const double MinimumScale = 0.8;

    /// <summary>
    /// Widest label (in S) whose bottom, <paramref name="baselineOffset"/> below the orb center plus the descent, still
    /// keeps <see cref="RingMargin"/> from the ring on both sides.
    /// </summary>
    public static double AvailableWidth(double baselineOffset)
    {
        var bottom = Math.Abs(baselineOffset) + Size * DescentRatio;
        var inner = GaugeLayout.InnerRadius;
        if (bottom >= inner)
        {
            return 0;
        }

        return Math.Max(0, 2 * (Math.Sqrt(inner * inner - bottom * bottom) - RingMargin));
    }

    /// <summary>Scale for a label of <paramref name="naturalWidth"/>: 1 when it fits, never below <see cref="MinimumScale"/>.</summary>
    public static double Scale(double naturalWidth, double availableWidth)
    {
        if (!double.IsFinite(naturalWidth) || naturalWidth <= 0 || !double.IsFinite(availableWidth) || availableWidth <= 0)
        {
            return 1;
        }

        return Math.Clamp(availableWidth / naturalWidth, MinimumScale, 1);
    }
}
