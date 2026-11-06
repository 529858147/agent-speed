using AgentSpeed.Boards.Gpu;
using AgentSpeed.Boards.Layers;
using AgentSpeed.Presentation.Boards;
using AgentSpeed.Presentation.Boards.Demo;
using AgentSpeed.Presentation.Boards.Effects;
using AgentSpeed.Presentation.Boards.Scene;
using AgentSpeed.Presentation.Localization;

namespace AgentSpeed.Boards.Harness;

/// <summary>
/// A board fed by the demo (the C# port of <c>spec/prototypes/board/sim.js</c>) on a caller's clock, for the frame harness,
/// the bench and the window check: a sim tick every 0.5 s goes through the real grouping, ranking and layout plan into the
/// scene; frames are built by the same <see cref="BoardFrame"/> as a window's.
/// </summary>
internal sealed class BoardRig : IDisposable
{
    private double _nextTick = double.NegativeInfinity;

    /// <param name="readouts">The live metrics switches (DESIGN 2c); every one on, as a new board has them, when null.</param>
    public BoardRig(BoardStyle style, BoardSize size, BoardSimScenario scenario, BoardEffectsPolicy policy, Localizer localizer, uint seed = 7,
        bool dark = true, BoardReadouts? readouts = null)
    {
        Style = style;
        Size = size;
        Feed = new BoardDemoFeed(scenario, seed) { Localizer = localizer, Readouts = readouts ?? BoardReadouts.All };
        Frame = new BoardFrame(style, size, BoardPainters.Create(style, localizer), seed);
        Frame.Scene.Configure(style, size, policy);
        Frame.Scene.Dark = dark;
    }

    public BoardStyle Style { get; }

    public BoardSize Size { get; }

    public BoardDemoFeed Feed { get; }

    public BoardFrame Frame { get; }

    public BoardScene Scene => Frame.Scene;

    /// <summary>When the next sim tick is due (seconds on the caller's clock; −∞ before the first).</summary>
    public double NextTick => _nextTick;

    /// <summary>Ticks applied so far.</summary>
    public int Ticks { get; private set; }

    /// <summary>The last tick applied.</summary>
    public BoardTick? LastTick { get; private set; }

    /// <summary>Applies the sim's ticks due at <paramref name="now"/>; true when one was applied.</summary>
    public bool Advance(double now)
    {
        if (now < _nextTick)
        {
            return false;
        }

        _nextTick = double.IsNegativeInfinity(_nextTick) ? now + BoardSim.TickSeconds : _nextTick + BoardSim.TickSeconds;
        if (_nextTick <= now)
        {
            _nextTick = now + BoardSim.TickSeconds;
        }

        var tick = Feed.Step(Style, Size);
        Scene.Apply(tick, now);
        Frame.Invalidate();
        LastTick = tick;
        Ticks++;
        return true;
    }

    public void Dispose() => Frame.Dispose();
}
