using AgentSpeed.Presentation.Boards.Gpu;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Hero;
using AgentSpeed.Presentation.Orb;
using AgentSpeed.Presentation.Orb.Gpu;
using AgentSpeed.Presentation.Settings;

namespace AgentSpeed.Presentation.Boards.Effects;

/// <summary>
/// Where an overtake or a new leader happened, in board coordinates (points from the square's top-left corner).
/// </summary>
/// <param name="Key">The row's key (its sweep follows the row while it moves, <see cref="BoardEffects.FollowRow"/>).</param>
/// <param name="PassedRankY">Center y of the rank of the row the climber passed (neon arcs to it); NaN when unknown.</param>
/// <param name="Source">The climber's source color.</param>
public readonly record struct BoardRowFx(string Key, double Left, double Right, double CenterY, double Height, double RankX, double RankY,
    double PassedRankY, Rgba Source);

/// <summary>
/// One board's effects layer (DESIGN 2c, Motion and effects): the perimeter's comets, ring and bloom, sparks, bursts,
/// streaks, shockwaves, branching lightning, the new-leader celebration, the row sweeps and neon's warp, all primitives of
/// the orbs' instanced SDF pipeline from preallocated pools. The API follows <c>BoardFX</c> of
/// <c>spec/prototypes/board/fx.js</c> (<see cref="SetHeat"/>, <see cref="Burst"/>, <see cref="Streak"/>,
/// <see cref="Shockwave"/>, <see cref="Lightning"/>, <see cref="Celebrate"/>, <see cref="Warp"/>) plus the board events
/// (<see cref="Overtake"/>, <see cref="NewLeader"/>, <see cref="Punch"/>, <see cref="NewPeak"/>) with each style's preset.
/// Coordinates passed in are board points; the draw list is in canvas points (<see cref="BoardCanvas"/>).
/// </summary>
public sealed class BoardEffects
{
    /// <summary>Redline sparks leave each comet head this often (seconds).</summary>
    public const double EmberInterval = 0.12;

    /// <summary>Longest step of the simulation (a late frame never jumps further).</summary>
    public const double MaxStep = 0.05;

    public const double FlashSeconds = 0.38;

    private readonly BoardComets _comets = new();
    private readonly BoardParticles _particles;
    private readonly BoardWaves _waves = new();
    private readonly BoardLightning _lightning;
    private readonly BoardWarp _warp;
    private readonly BoardSweeps _sweeps = new();
    private readonly HeroRandom _random;
    private BoardEffectsPolicy _policy = new(EffectsIntensity.Standard);
    private double _heat;
    private double _overdrive;
    private SpeedTier _tier;
    private bool _generating;
    private double _last = double.NaN;
    private double _emberClock;
    private double _flash;

    public BoardEffects(BoardStyle style, BoardSize size, uint seed = 1)
    {
        _particles = new BoardParticles(seed);
        _lightning = new BoardLightning(seed * 7919 + 1);
        _warp = new BoardWarp(seed * 104729 + 3);
        _random = new HeroRandom(seed * 31 + 11);
        Configure(style, size);
    }

    public BoardStyle Style { get; private set; }

    public BoardEffectsPreset Preset { get; private set; } = BoardEffectsPresets.Glass;

    /// <summary>The appearance (timing's sweep darkens a light row instead of lighting it).</summary>
    public bool Dark { get; set; } = true;

    public BoardCanvas Canvas { get; private set; }

    /// <summary>Neon's hyperspace streaks (the style's preset decides; a host may turn them off).</summary>
    public bool Warp { get; set; }

    public BoardEffectsPolicy Policy
    {
        get => _policy;
        set
        {
            if (_policy == value)
            {
                return;
            }

            _policy = value;
            if (!value.Particles)
            {
                _particles.Clear();
            }

            if (!value.Transients)
            {
                _waves.Clear();
                _lightning.Clear();
                _sweeps.Clear();
                _flash = 0;
            }

            if (!value.Warp)
            {
                _warp.Clear();
            }

            ApplyComets();
            if (!value.Comets)
            {
                _comets.Settle();
            }
        }
    }

    public BoardComets Comets => _comets;

    public BoardParticles Particles => _particles;

    /// <summary>Something short-lived is alive: a particle, wave, bolt, sweep, flash, gold or a comet fading.</summary>
    public bool Alive => _particles.Count > 0 || _waves.Count > 0 || _lightning.Count > 0 || _sweeps.Count > 0 || _flash > 0 ||
                         _comets.Transitioning;

    /// <summary>
    /// The frames the effects need (DESIGN 2c, Frame policy): 60 fps at overdrive and redline or while anything short-lived is
    /// alive, 30 fps while comets or the warp move at cruise and boost (or anything generates), none when nothing moves.
    /// </summary>
    public FrameClass Demand
    {
        get
        {
            if (Alive)
            {
                return FrameClass.Fps60;
            }

            var moving = (_comets.Count > 0 && Moves) || WarpRuns;
            if (!moving)
            {
                return FrameClass.None;
            }

            return _tier >= SpeedTier.Overdrive ? FrameClass.Fps60 : FrameClass.Fps30;
        }
    }

    // Comets travel only while something generates or the tier is above idle (an idle board is static).
    private bool Moves => _generating || _tier > SpeedTier.Idle;

    private bool WarpRuns => Warp && _policy.Warp && BoardWarp.Runs(_heat);

    public void Configure(BoardStyle style, BoardSize size)
    {
        Style = style;
        Preset = BoardEffectsPresets.For(style);
        Canvas = BoardCanvas.For(style, size);
        Warp = Preset.Warp;
        ApplyComets();
    }

    /// <summary>fx.js <c>setHeat</c>: the board's heat, overdrive and effect tier, and whether any of its agents generates.</summary>
    public void SetHeat(double heat, double overdrive, SpeedTier tier, bool generating)
    {
        _heat = double.IsFinite(heat) ? Math.Clamp(heat, 0, 1) : 0;
        _overdrive = double.IsFinite(overdrive) ? Math.Clamp(overdrive, 0, 1) : 0;
        _tier = tier;
        _generating = generating;
        ApplyComets();
        if (!WarpRuns)
        {
            _warp.Clear();
        }
    }

    /// <summary>Advances everything to <paramref name="now"/> (seconds on the shared clock).</summary>
    public void Advance(double now)
    {
        var dt = double.IsNaN(_last) ? 0 : Math.Clamp(now - _last, 0, MaxStep);
        _last = now;
        if (dt <= 0)
        {
            return;
        }

        _comets.Step(dt, Moves && _policy.Comets);
        _particles.Step(dt);
        _waves.Step(dt);
        _lightning.Step(dt);
        _sweeps.Step(dt);
        _flash = Math.Max(0, _flash - dt);
        if (WarpRuns)
        {
            _warp.Step(dt, _heat, _overdrive, _policy.Density, Canvas.Side);
        }

        _emberClock += dt;
        if (_emberClock >= EmberInterval)
        {
            _emberClock %= EmberInterval;
            if (_tier == SpeedTier.Redline && _policy.Particles)
            {
                Embers();
            }
        }
    }

    /// <summary>Restarts the clock without simulating the gap (the window was hidden or paused).</summary>
    public void Resume() => _last = double.NaN;

    /// <summary>Ends everything short-lived (a window that hides, a style change).</summary>
    public void Clear()
    {
        _particles.Clear();
        _waves.Clear();
        _lightning.Clear();
        _sweeps.Clear();
        _warp.Clear();
        _flash = 0;
        _comets.Settle();
    }

    // ---- fx.js API (board coordinates) ----

    public void Burst(double x, double y, in BoardBurst burst)
    {
        if (_policy.Particles)
        {
            _particles.Burst(x + BoardCanvas.Margin, y + BoardCanvas.Margin, burst, _policy.Density);
        }
    }

    public void Streak(double x0, double y, double x1, Rgba color)
    {
        if (_policy.Particles)
        {
            _particles.Streak(x0 + BoardCanvas.Margin, y + BoardCanvas.Margin, x1 + BoardCanvas.Margin, color, _policy.Density);
        }
    }

    /// <summary>A ring to <paramref name="maxRadius"/> (0: the board's diagonal) over <paramref name="life"/> seconds.</summary>
    public void Shockwave(double x, double y, Rgba color, double maxRadius = 0, double life = 0.9)
    {
        if (_policy.Transients)
        {
            _waves.Add(x + BoardCanvas.Margin, y + BoardCanvas.Margin, color, maxRadius > 0 ? maxRadius : Diagonal, life);
        }
    }

    public void Lightning(double x0, double y0, double x1, double y1, Rgba color, int branches = 2, double life = 0.32)
    {
        if (_policy.Transients)
        {
            var m = BoardCanvas.Margin;
            _lightning.Strike(x0 + m, y0 + m, x1 + m, y1 + m, color, branches, life);
        }
    }

    /// <summary>The new-leader celebration: a gold and a white-hot shockwave, ~70 gold sparks, gold comets 1.8× faster for 2.2 s.</summary>
    public void Celebrate(double x, double y)
    {
        Shockwave(x, y, HeatPalette.Gold, Diagonal * 1.1, 1.1);
        Shockwave(x, y, HeatPalette.WhiteHot, Diagonal * 0.6, 0.7);
        Burst(x, y, new BoardBurst(70, 260, 1.3,
            new BoardColors(HeatPalette.Gold, Rgba.FromHex("#FFF1A8"), Rgba.FromHex("#FB923C"), Rgba.White), 2.2, 160));
        if (_policy.Comets)
        {
            _comets.Celebrate();
        }
    }

    /// <summary>Neon's flash of the whole board (its brightness 1.6 → 1 over 0.38 s).</summary>
    public void Flash()
    {
        if (_policy.Transients)
        {
            _flash = FlashSeconds;
        }
    }

    // ---- Board events ----

    /// <summary>
    /// A pass (DESIGN 2c Overtake): a light sweep crosses the climber's row in its source color, streak particles fly off along
    /// it, a burst leaves its rank; neon arcs lightning from the climber's rank to the row it passed.
    /// </summary>
    public void Overtake(in BoardRowFx row)
    {
        RowSweep(row);
        var burst = Preset.OvertakeBurst;
        var colors = burst.Colors with { C0 = Preset.OvertakeColor ?? row.Source };
        Burst(row.RankX, row.RankY, burst with { Colors = colors });
        if (Preset.OvertakeArc && double.IsFinite(row.PassedRankY))
        {
            Lightning(row.RankX, row.RankY, row.RankX + 8, row.PassedRankY, row.Source, 1, 0.3);
        }
    }

    /// <summary>
    /// A pass to first place (DESIGN 2c New leader): the sweep and streaks, then the celebration from the first rank; neon
    /// strikes lightning from the top edge to the rank and flashes the board. Without "Celebrate new peaks" it is a pass.
    /// </summary>
    public void NewLeader(in BoardRowFx row)
    {
        if (!_policy.Celebrate)
        {
            Overtake(row);
            return;
        }

        RowSweep(row, leader: true);
        if (Preset.LeaderStrike)
        {
            Lightning(row.RankX + 30 + _random.Next() * 120, -6, row.RankX, row.RankY, HeatPalette.Gold, 3, 0.45);
            Flash();
        }

        Celebrate(row.RankX, row.RankY);
    }

    /// <summary>
    /// The hero number's punch (a rise of more than 8 % + 20 tok/s in one tick): embers by style around the number whose box
    /// is (<paramref name="left"/>, <paramref name="top"/>, <paramref name="width"/>, <paramref name="height"/>);
    /// <paramref name="rise"/> is the rise relative to the previous value.
    /// </summary>
    public void Punch(double left, double top, double width, double height, double rise)
    {
        var x = left + width / 2;
        var y = top + height / 2;
        var warm = HeatPalette.Sample(Math.Min(1, _heat + 0.1));
        if (Preset.Embers == BoardEmbers.Spray)
        {
            Burst(x + width / 2, y, new BoardBurst(10, 150, 0.6, new BoardColors(warm, Rgba.White), 1.3, 0, 2.2, 0, 0.7));
            return;
        }

        var count = Math.Min(22, 6 + (int)Math.Round(Math.Max(0, rise) * 40));
        Burst(x, y - height * 0.3, new BoardBurst(count, 90, 1.1, new BoardColors(warm, HeatPalette.WhiteHot), 1.6, -30, 1.2, -Math.PI / 2, 1.6));
        if (Preset.Embers == BoardEmbers.RisingWithArc)
        {
            var x0 = left + width * (0.1 + _random.Next() * 0.3);
            var x1 = left + width * (0.6 + _random.Next() * 0.35);
            Lightning(x0, y - height * 0.15, x1, y + height * 0.2, HeatPalette.Sample(Math.Min(1, _heat + 0.15)), 1, 0.28);
        }
    }

    /// <summary>A new <c>peakToday_A</c>: a gold ring from the number (the sheen and the chip are content).</summary>
    public void NewPeak(double x, double y)
    {
        if (_policy.Celebrate)
        {
            Shockwave(x, y, HeatPalette.Gold, 160, 0.9);
        }
    }

    /// <summary>Keeps a row's sweep on the row while the row springs to its new place.</summary>
    public void FollowRow(string key, double centerY) => _sweeps.Follow(key, centerY + BoardCanvas.Margin);

    // ---- Drawing ----

    /// <summary>What lies behind the content: the bloom (outside the square) and the warp (inside it).</summary>
    public void EmitBack(BoardDrawList list)
    {
        var canvas = Canvas;
        list.Clip = BoardClip.Outside;
        _comets.EmitBloom(list, canvas, Preset, _policy.Glows);
        if (_warp.Count > 0)
        {
            list.Clip = BoardClip.Inside;
            _warp.Emit(list, BoardCanvas.Margin, BoardCanvas.Margin, canvas.Side, _heat);
        }

        list.Clip = BoardClip.None;
    }

    /// <summary>What lies above the content: the ring and comets, the sweeps, sparks, waves, bolts and the flash.</summary>
    public void EmitFront(BoardDrawList list)
    {
        var canvas = Canvas;

        // The prototypes' perimeter and shockwaves live inside the panel's overflow: the ring's glow, the comets and the waves
        // stay inside the square (a wave left unclipped would be cut by the window's edge); sparks still fly outward.
        list.Clip = BoardClip.Inside;
        _comets.Emit(list, canvas, Preset, _policy.Ring, _policy.Glows);
        _sweeps.Emit(list);
        _waves.Emit(list);
        list.Clip = BoardClip.None;

        _particles.Emit(list);
        _lightning.Emit(list, _policy.Glows);
        if (_flash > 0)
        {
            var t = 1 - _flash / FlashSeconds;
            list.Clip = BoardClip.Inside;
            list.Add(OrbGpuInstance.RoundedRect(canvas.Center, canvas.Center, canvas.Half, canvas.Half, 0, canvas.Radius,
                Rgba.White.WithAlpha(0.3 * (1 - t) * (1 - t))), OrbBlend.Additive);
            list.Clip = BoardClip.None;
        }
    }

    private double Diagonal => Canvas.Side * Math.Sqrt(2);

    private void ApplyComets()
    {
        var count = Math.Min(Preset.CometsAt((int)_tier), _policy.MaxComets);
        _comets.Set(_heat, _overdrive, (int)_tier, count);
    }

    private void RowSweep(in BoardRowFx row, bool leader = false)
    {
        if (_policy.Transients)
        {
            var m = BoardCanvas.Margin;
            var look = (leader ? Preset.LeadSweep : null) ?? (Dark ? null : Preset.SweepLight) ?? Preset.Sweep;
            _sweeps.Add(row.Key, row.Left + m, row.Right + m, row.CenterY + m, row.Height, row.Source, look);
        }

        Streak(row.Left, row.CenterY, row.Right, row.Source);
    }

    private void Embers()
    {
        var track = Canvas.Track;
        var color = HeatPalette.Sample(_heat);
        for (var i = 0; i < _comets.Count; i++)
        {
            if (!_comets.Shows(i))
            {
                continue;
            }

            var at = _comets.Head(i) + BoardComets.CoreLaps;
            var p = track.Point(at);
            var outward = track.Outward(at);
            _particles.Burst(p.X, p.Y, new BoardBurst(3, 70, 0.6, new BoardColors(HeatPalette.WhiteHot, color), 1.4, 40,
                Angle: Math.Atan2(outward.Y, outward.X), Spread: 0.9), _policy.Density);
        }
    }
}
