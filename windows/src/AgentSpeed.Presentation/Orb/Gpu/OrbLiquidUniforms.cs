using System.Numerics;
using System.Runtime.InteropServices;

namespace AgentSpeed.Presentation.Orb.Gpu;

/// <summary>
/// Per-frame parameters of the <c>liquid</c> volume shader (DESIGN 2a.3), the layout of <c>LiquidParams</c> in the HLSL source
/// (21 × float4, a constant buffer). Positions and lengths are canvas points (y down); colors straight-alpha sRGB.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct OrbLiquidUniforms
{
    /// <summary>Center x, center y, S, glass radius.</summary>
    public Vector4 Frame;

    /// <summary>Mean level y_L, wave amplitude, chop amplitude, back wave on (1/0).</summary>
    public Vector4 Surface;

    /// <summary>φ1, φ2, φ3, φ4 (radians).</summary>
    public Vector4 Phases;

    /// <summary>Ripple: impact x (canvas), front distance 0.42 S τ, amplitude (points), active (1/0).</summary>
    public Vector4 Ripple0;

    public Vector4 Ripple1;

    public Vector4 Ripple2;

    public Vector4 Ripple3;

    /// <summary>L (rgb), front alpha.</summary>
    public Vector4 Liquid;

    /// <summary>Darkened L (rgb), alpha at the bottom.</summary>
    public Vector4 Deep;

    /// <summary>Back wave color, alpha.</summary>
    public Vector4 Back;

    /// <summary>Spill color, alpha at the surface (0: none).</summary>
    public Vector4 Spill;

    /// <summary>Inner glow color, alpha at its center (0: none).</summary>
    public Vector4 Glow;

    /// <summary>Inner glow center x, center y, radius, additive (1/0).</summary>
    public Vector4 GlowGeom;

    /// <summary>Spherical shade color, alpha at the glass.</summary>
    public Vector4 Shade;

    /// <summary>Current filament color, alpha (0: none).</summary>
    public Vector4 Current;

    /// <summary>Unused, blur σ (points), drift phase, additive (1/0).</summary>
    public Vector4 CurrentGeom;

    /// <summary>Meniscus color, alpha.</summary>
    public Vector4 Meniscus;

    /// <summary>Meniscus glow color, alpha (0: none).</summary>
    public Vector4 MeniscusGlow;

    /// <summary>Meniscus width (points), glow σ (points), additive (1/0), unused.</summary>
    public Vector4 MeniscusGeom;

    /// <summary>New-peak gold band center y, alpha, half height (points), additive (1/0).</summary>
    public Vector4 Gold;

    /// <summary>Spill additive (1/0), scrim strength (0: none), body gradient top y, body gradient bottom y.</summary>
    public Vector4 Flags;

    public const int Size = 21 * 16;

    /// <summary>
    /// The front surface's height (points, y down) at canvas <paramref name="x"/>, as the shader's <c>orb_liquid_surface_y</c>
    /// evaluates it: what splits the readout into its above- and below-surface colors.
    /// </summary>
    public readonly double SurfaceY(double x)
    {
        const double Tau = 2 * Math.PI;
        var s = (double)Frame.Z;
        if (!(s > 0))
        {
            return Surface.X;
        }

        var xr = x - Frame.X;
        var y = (double)Surface.X;
        if (Surface.Y > 0)
        {
            y += Surface.Y * (0.55 * Math.Sin(xr / (0.9 * s) * Tau + Phases.X)
                + 0.30 * Math.Sin(xr / (0.47 * s) * Tau - Phases.Y)
                + 0.15 * Math.Sin(xr / (0.23 * s) * Tau + Phases.Z));
        }

        if (Surface.Z > 0)
        {
            y += Surface.Z * Math.Sin(xr / (0.11 * s) * Tau + Phases.W) * (0.7 + 0.3 * Math.Sin(xr / (0.05 * s) + Phases.W * 1.7));
        }

        return y - Ripple(Ripple0, x, s) - Ripple(Ripple1, x, s) - Ripple(Ripple2, x, s) - Ripple(Ripple3, x, s);
    }

    public void SetRipple(int index, Vector4 value)
    {
        switch (index)
        {
            case 0:
                Ripple0 = value;
                break;
            case 1:
                Ripple1 = value;
                break;
            case 2:
                Ripple2 = value;
                break;
            default:
                Ripple3 = value;
                break;
        }
    }

    private static double Ripple(Vector4 ripple, double x, double s)
    {
        if (!(ripple.W > 0))
        {
            return 0;
        }

        var u = Math.Abs(x - ripple.X) - ripple.Y;
        var k = u / (0.075 * s);
        return ripple.Z * Math.Exp(-k * k) * Math.Cos(2 * Math.PI * u / (0.1 * s));
    }
}
