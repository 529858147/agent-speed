using System.Numerics;
using System.Runtime.InteropServices;
using AgentSpeed.Presentation.Colors;

namespace AgentSpeed.Presentation.Boards.Gpu;

/// <summary>Where a cached layer (a row, the hero, the footer) lies in the platform's layer texture.</summary>
/// <param name="Width">Width of the layer in points.</param>
/// <param name="Height">Height of the layer in points.</param>
/// <param name="MeterX">Where a top-rung effort meter starts inside the layer (points; the scene shimmers it), NaN when it has none.</param>
/// <param name="Baseline">A text layer's baseline from its top (points; the scene sets text on baselines), NaN when not given.</param>
public readonly record struct BoardLayerSlot(float U0, float V0, float U1, float V1, double Width, double Height, double MeterX = double.NaN,
    double Baseline = double.NaN);

/// <summary>The cached layers a board's frame can composite (DESIGN 2c, Two layers): rasterized only when data changes.</summary>
public interface IBoardLayers
{
    /// <summary>The slot of the layer drawn for <paramref name="key"/>, false while it has not been rasterized.</summary>
    bool TryGet(string key, out BoardLayerSlot slot);
}

/// <summary>
/// One cached layer composited at its animated place (80 bytes, the layout of <c>LayerIn</c> in <c>BoardLayer.hlsl</c>).
/// Coordinates are canvas points; the texture holds premultiplied pixels.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct BoardLayerInstance
{
    /// <summary>Left, top, width, height.</summary>
    public Vector4 Rect;

    /// <summary>u0, v0, u1, v1 in the layer texture.</summary>
    public Vector4 Uv;

    /// <summary>A warm flash or dim: rgb (straight) and how much of it replaces the layer's color.</summary>
    public Vector4 Tint;

    /// <summary>
    /// Opacity; the gold sheen's center x (canvas points), half width and strength (none when the half width is 0); a negative
    /// strength draws a shimmer's light instead (<see cref="Shimmer"/>).
    /// </summary>
    public Vector4 Params;

    /// <summary>Clip rect min x, min y, max x, max y (the ranking list's frame); empty when max x ≤ min x.</summary>
    public Vector4 Clip;

    public const int Stride = 80;

    public static BoardLayerInstance Create(in BoardLayerSlot slot, double x, double y, double opacity, Rgba tint = default, double tintAmount = 0,
        RectBounds clip = default) => new()
    {
        Rect = new Vector4((float)x, (float)y, (float)slot.Width, (float)slot.Height),
        Uv = new Vector4(slot.U0, slot.V0, slot.U1, slot.V1),
        Tint = new Vector4((float)tint.R, (float)tint.G, (float)tint.B, (float)Math.Clamp(tintAmount, 0, 1)),
        Params = new Vector4((float)Math.Clamp(opacity, 0, 1), 0, 0, 0),
        Clip = new Vector4(clip.Left, clip.Top, clip.Right, clip.Bottom),
    };

    /// <summary>A gold band across the layer's glyphs (a new peak, DESIGN 2c Hero number), white at its center.</summary>
    public void Sheen(double center, double halfWidth, double strength)
    {
        Params.Y = (float)center;
        Params.Z = (float)Math.Max(0, halfWidth);
        Params.W = (float)Math.Clamp(strength, 0, 1);
    }

    /// <summary>
    /// Instead of the layer, only the light a white band adds to it (CSS <c>mix-blend-mode: overlay</c>, weighted by the layer's
    /// coverage): the band leans like a 105° CSS gradient through (<paramref name="center"/>, the clip's middle), peaks at
    /// <paramref name="peak"/> and fades to nothing <paramref name="halfWidth"/> away. Needs a clip (the top-rung meter's box).
    /// </summary>
    public void Shimmer(double center, double halfWidth, double peak)
    {
        Params.Y = (float)center;
        Params.Z = (float)Math.Max(0, halfWidth);
        Params.W = -(float)Math.Clamp(peak, 0, 1);
    }

    /// <summary>Multiplies the opacity (crossfades).</summary>
    public void ScaleAlpha(float factor) => Params.X *= factor;
}

/// <summary>An axis-aligned clip rect in canvas points.</summary>
public readonly record struct RectBounds(float Left, float Top, float Right, float Bottom)
{
    public static RectBounds From(double left, double top, double width, double height) =>
        new((float)left, (float)top, (float)(left + width), (float)(top + height));
}
