using AgentSpeed.Presentation.Boards.Gpu;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Layout;
using AgentSpeed.Presentation.Motion;
using AgentSpeed.Presentation.Orb.Gpu;

namespace AgentSpeed.Presentation.Boards.Effects;

/// <summary>
/// The perimeter (DESIGN 2c; fx.js <c>setHeat</c> and the comet dashes): comets race clockwise around the rounded square,
/// evenly spaced, each a fading tail (0.15 lap, 2 pt), a glow (0.046 lap, a 5 pt stroke blurred σ 2.4) and a bright core
/// (0.016 lap, 1.8 pt, white-hot above h = 0.85); the lap takes 7 s at idle down to 1.6 s at the redline. A full ring lights
/// from h = 0.55 and a bloom grows outside the board with heat. While gold (a new leader) comets and ring turn gold and run
/// 1.8× faster. High contrast draws each comet as a 2 pt solid segment and no glows.
/// </summary>
public sealed class BoardComets
{
    public const int MaxComets = 4;
    public const double TailLaps = 0.15;
    public const double GlowLaps = 0.046;
    public const double CoreLaps = 0.016;
    public const double GoldSeconds = 2.2;
    public const double GoldSpeedup = 1.8;

    /// <summary>Seconds a comet takes to fade in or out (fx.js <c>transition: opacity .8s</c>).</summary>
    public const double FadeSeconds = 0.8;

    private const int TailSegments = 20;
    private const int CoreSegments = 3;
    private const double GlowSpacing = 2.5;

    private static readonly Rgba GoldHead = Rgba.FromHex("#FFE58A");

    private readonly double[] _alpha = new double[MaxComets];
    private readonly CriticalSpring[] _offsets = new CriticalSpring[MaxComets];
    private double _phase;
    private int _count;
    private int _tier;
    private double _heat;
    private double _overdrive;

    public BoardComets()
    {
        for (var i = 0; i < MaxComets; i++)
        {
            _offsets[i] = new CriticalSpring(0.6, i / (double)(i + 1));
        }
    }

    /// <summary>Seconds of gold left (the new-leader celebration).</summary>
    public double Gold { get; private set; }

    /// <summary>The leading comet's head, in laps from the end of the top-left corner.</summary>
    public double Phase => _phase;

    public int Count => _count;

    /// <summary>Comets still fading in or out, or gold: the perimeter changes even at a steady heat.</summary>
    public bool Transitioning
    {
        get
        {
            if (Gold > 0)
            {
                return true;
            }

            for (var i = 0; i < MaxComets; i++)
            {
                var hidden = _alpha[i] <= 0.002 && TargetAlpha(i) == 0;
                if (Math.Abs(_alpha[i] - TargetAlpha(i)) > 0.002 || !hidden && !_offsets[i].IsSettled(Spacing(i), 0.0005))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Seconds per lap at the current heat.</summary>
    public double LapSeconds => (7 - 5.4 * Math.Clamp(_heat, 0, 1)) / (Gold > 0 ? GoldSpeedup : 1);

    public void Set(double heat, double overdrive, int tier, int count)
    {
        _heat = double.IsFinite(heat) ? Math.Clamp(heat, 0, 1) : 0;
        _overdrive = double.IsFinite(overdrive) ? Math.Clamp(overdrive, 0, 1) : 0;
        _tier = Math.Clamp(tier, 0, 4);
        _count = Math.Clamp(count, 0, MaxComets);
    }

    public void Celebrate() => Gold = GoldSeconds;

    /// <summary>Ends every fade and spacing move at once (a window that just appeared, Reduce Motion).</summary>
    public void Settle()
    {
        for (var i = 0; i < MaxComets; i++)
        {
            _alpha[i] = TargetAlpha(i);
            _offsets[i].Snap(Spacing(i));
        }
    }

    public void Step(double dt, bool moving)
    {
        Gold = Math.Max(0, Gold - dt);
        if (moving)
        {
            _phase += dt / LapSeconds;
            _phase -= Math.Floor(_phase);
        }

        var fade = dt / FadeSeconds;
        for (var i = 0; i < MaxComets; i++)
        {
            var target = TargetAlpha(i);
            _alpha[i] = _alpha[i] < target ? Math.Min(target, _alpha[i] + fade) : Math.Max(target, _alpha[i] - fade);
            if (_alpha[i] <= 0.002 && target == 0)
            {
                _offsets[i].Snap(Spacing(i));
            }
            else
            {
                _offsets[i].Step(Spacing(i), dt);
            }
        }
    }

    /// <summary>The head of comet <paramref name="index"/> in laps.</summary>
    public double Head(int index) => _phase + _offsets[Math.Clamp(index, 0, MaxComets - 1)].Value;

    /// <summary>Whether comet <paramref name="index"/> shows (its sparks fly at the redline).</summary>
    public bool Shows(int index) => index >= 0 && index < MaxComets && _alpha[index] > 0.01;

    /// <summary>The bloom outside the board (behind the content, clipped outside the rounded square).</summary>
    public void EmitBloom(BoardDrawList list, in BoardCanvas canvas, BoardEffectsPreset preset, bool glows)
    {
        if (!glows)
        {
            return;
        }

        var bloom = 0.15 + _heat * 0.6 + _overdrive * 0.4;
        var spread = preset.BloomSpread * bloom;
        // A CSS box-shadow blur B is a Gaussian of σ = B / 2. The Outside clip fades it out along the board's outline to nothing
        // BoardCanvas.BloomReach out, so the window's edge never cuts it, at any σ.
        var sigma = preset.BloomBlur * bloom / 2;

        // The square grown by the spread and blurred: about half strength at its grown edge, no hard band.
        var color = HeatPalette.Sample(_heat).WithAlpha(0.55);
        var half = canvas.Half + spread;
        list.Add(OrbGpuInstance.RoundedRect(canvas.Center, canvas.Center, half, half, 0, canvas.Radius + spread, color, core: 0,
            glow: OrbGpuInstance.Glow(sigma), glowAlpha: 0.55));
    }

    /// <summary>The ring and the comets (above the content).</summary>
    public void Emit(BoardDrawList list, in BoardCanvas canvas, BoardEffectsPreset preset, bool ring, bool glows)
    {
        var track = canvas.Track;
        var gold = Gold > 0;
        var ringOn = ring ? Math.Max(0, (_heat - 0.55) / 0.45) : 0;
        var ringColor = gold ? HeatPalette.Gold : HeatPalette.Sample(_heat);
        var center = canvas.Center;
        var half = track.Width / 2;
        var ringGlow = ringOn * 0.55 + _overdrive * 0.4 + (gold ? 0.5 : 0);
        if (glows && ring && ringGlow > 0.003)
        {
            // A 6 pt stroke blurred σ 6: its peak is erf(3 / (6√2)) ≈ 0.38 of the stroke's opacity.
            list.Add(OrbGpuInstance.RoundedRing(center, center, half, half, track.Radius, 1, ringColor.WithAlpha(Math.Min(1, ringGlow)), core: 0,
                glow: OrbGpuInstance.Glow(6.3), glowAlpha: 0.38));
        }

        var ringAlpha = Math.Min(1, 0.12 + ringOn * 0.55 + (ring ? _overdrive * 0.3 : 0));
        list.Add(OrbGpuInstance.RoundedRing(center, center, half, half, track.Radius, glows ? preset.RingWidth : 1, ringColor.WithAlpha(ringAlpha)));

        var head = gold ? GoldHead : HeatPalette.Sample(Math.Min(1, _heat + 0.12));
        var tail = gold ? HeatPalette.Gold : HeatPalette.Sample(Math.Max(0.1, _heat - 0.15));
        var core = gold || _heat > 0.85 ? HeatPalette.WhiteHot : head;

        // Every comet's tail, then every glow, then every core: comets are spaced apart, so this draws each comet as before in
        // three batches instead of three per comet.
        for (var pass = 0; pass < (glows ? 3 : 1); pass++)
        {
            for (var i = 0; i < MaxComets; i++)
            {
                var alpha = _alpha[i];
                if (alpha <= 0.002)
                {
                    continue;
                }

                var at = Head(i);
                if (!glows)
                {
                    Segment(list, track, at - TailLaps * 0.5, at, TailSegments / 2, 1, head.WithAlpha(alpha), head.WithAlpha(alpha), fadeTail: false);
                    continue;
                }

                switch (pass)
                {
                    case 0:
                        Segment(list, track, at - TailLaps, at, TailSegments, 1, tail.WithAlpha(0.35 * alpha), tail.WithAlpha(0), fadeTail: true);
                        break;
                    case 1:
                        Glow(list, track, at, head.WithAlpha(alpha));
                        break;
                    default:
                        Segment(list, track, at - CoreLaps, at, CoreSegments, 0.9, core.WithAlpha(alpha), core.WithAlpha(alpha), fadeTail: false);
                        break;
                }
            }
        }
    }

    private double TargetAlpha(int index) => index < _count ? (_tier == 0 ? 0.35 : 1) : 0;

    // A shown comet keeps 1/count of a lap from the one ahead; a hidden one waits where it appears when it is the last one.
    private double Spacing(int index) => index < _count ? index / (double)_count : index / (double)(index + 1);

    // A stroked run of the track as cut segments, so joints are covered once; the alpha ramps from the tail when asked.
    private static void Segment(BoardDrawList list, in BoardPerimeter track, double from, double to, int segments, double halfWidth, Rgba color,
        Rgba tailColor, bool fadeTail)
    {
        var step = (to - from) / segments;
        var previous = track.Point(from);
        var current = track.Point(from + step);
        for (var k = 0; k < segments; k++)
        {
            var next = track.Point(from + (k + 2) * step);
            var t = (k + 1) / (double)segments;
            var c = fadeTail ? Rgba.Lerp(tailColor, color, Math.Pow(t, 1.2)) : color;
            var startCut = k > 0 ? Cut(track.Point(from + (k - 1) * step), previous, current) : default;
            var endCut = k + 1 < segments ? Cut(previous, current, next) : default;
            list.Add(OrbGpuInstance.Stroke(previous.X, previous.Y, current.X, current.Y, 0, halfWidth, 0, c, c, 0, 1, 0, additive: false,
                startCut, endCut));
            previous = current;
            current = next;
        }
    }

    // The 5 pt glow blurred σ 2.4 as a row of Gaussian dots spaced well under their width, so their sum is a smooth band.
    private static void Glow(BoardDrawList list, in BoardPerimeter track, double head, Rgba color)
    {
        var length = GlowLaps * track.Length;
        var dots = Math.Max(2, (int)Math.Ceiling(length / GlowSpacing));
        var radius = OrbGpuInstance.Glow(2.8);
        for (var k = 0; k < dots; k++)
        {
            var p = track.Point(head - GlowLaps + GlowLaps * k / (dots - 1));
            list.Add(OrbGpuInstance.Circle(p.X, p.Y, 0.5, color, core: 0, glow: radius, glowAlpha: 0.36), OrbBlend.Additive);
        }
    }

    private static System.Numerics.Vector2 Cut(PointD before, PointD joint, PointD after)
    {
        var x0 = joint.X - before.X;
        var y0 = joint.Y - before.Y;
        var x1 = after.X - joint.X;
        var y1 = after.Y - joint.Y;
        var l0 = Math.Sqrt(x0 * x0 + y0 * y0);
        var l1 = Math.Sqrt(x1 * x1 + y1 * y1);
        if (l0 < 1e-6 || l1 < 1e-6)
        {
            return default;
        }

        var x = x0 / l0 + x1 / l1;
        var y = y0 / l0 + y1 / l1;
        var l = Math.Sqrt(x * x + y * y);
        return l < 1e-6 ? default : new System.Numerics.Vector2((float)(x / l), (float)(y / l));
    }
}
