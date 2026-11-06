namespace AgentSpeed.Modes;

/// <summary>How a capsule draws and moves: the appearance, high contrast, Reduce Motion and whether effects allow shimmer.</summary>
internal readonly record struct CapsuleAppearance(bool Dark, bool HighContrast, bool ReduceMotion, bool Shimmer)
{
    public CapsuleBrushes Brushes => new(Dark, HighContrast);

    /// <summary>The top rung shimmers only with effects on, without Reduce Motion and outside high contrast.</summary>
    public bool Shimmers => Shimmer && !ReduceMotion && !HighContrast;
}
