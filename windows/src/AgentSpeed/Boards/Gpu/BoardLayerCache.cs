using AgentSpeed.Boards.Layers;
using AgentSpeed.Orb.Gpu;
using AgentSpeed.Presentation.Boards;
using AgentSpeed.Presentation.Boards.Gpu;
using AgentSpeed.Presentation.Boards.Scene;

namespace AgentSpeed.Boards.Gpu;

/// <summary>
/// Keeps a board's layer texture in step with its scene (DESIGN 2c, Two layers): after each tick only the layers whose
/// content changed are rasterized; layers of rows that are gone are dropped; a full texture is cleared, grown when that is
/// not enough, and drawn again.
/// </summary>
internal sealed class BoardLayerCache : IBoardLayers, IDisposable
{
    private const int InitialHeight = 1024;

    private readonly BoardLayerAtlas _atlas = new();
    private readonly HashSet<string> _live = new(StringComparer.Ordinal);
    private readonly List<string> _stale = [];
    private int _height = InitialHeight;

    public BoardLayerCache(IBoardLayerPainter painter)
    {
        Painter = painter;
    }

    public IBoardLayerPainter Painter { get; set; }

    public BoardLayerAtlas Atlas => _atlas;

    /// <summary>Stopwatch ticks spent describing layers, painting them and flushing the atlas (the bench's breakdown).</summary>
    public (long Describe, long Paint, long Flush) Ticks => (_describeTicks, _paintTicks, _flushTicks);

    /// <summary>Stopwatch ticks spent painting and layers painted, per <see cref="BoardLayerKind"/> (the bench's breakdown).</summary>
    public (long[] Ticks, long[] Count) PaintsByKind => (_paintByKind, _paintedByKind);

    /// <summary>Whether the texture must be made current again before the next frame (device, scale or size changed).</summary>
    public bool IsStale(GpuDevice device, double scale, BoardScene scene) =>
        _atlas.View is null || _atlas.Width != WidthFor(scene, scale) || Math.Abs(_atlas.Scale - scale) > 0.001 || Generation != device.Generation;

    private int Generation { get; set; } = -1;

    public bool TryGet(string key, out BoardLayerSlot slot) => _atlas.TryGet(key, out slot);

    /// <summary>Rasterizes what changed since the last call; false when the device was lost.</summary>
    public bool Update(GpuDevice device, BoardScene scene, double scale)
    {
        if (scene.Tick is null)
        {
            return true;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var width = WidthFor(scene, scale);
            _atlas.Ensure(device, scale, width, Math.Max(_height, width));
            Generation = device.Generation;
            switch (Draw(scene))
            {
                case DrawResult.Done:
                    return true;
                case DrawResult.Lost:
                    return false;
            }

            // Full: start over with every live layer, in a taller texture the second time.
            if (attempt > 0)
            {
                _height = Math.Min(BoardLayerAtlas.MaxHeight, _height * 2);
            }

            _atlas.Clear();
        }

        return true;
    }

    public void Dispose()
    {
        _atlas.Dispose();
        (Painter as IDisposable)?.Dispose();
    }

    private long _describeTicks;
    private long _paintTicks;
    private long _flushTicks;
    private readonly long[] _paintByKind = new long[Enum.GetValues<BoardLayerKind>().Length];
    private readonly long[] _paintedByKind = new long[Enum.GetValues<BoardLayerKind>().Length];

    private DrawResult Draw(BoardScene scene)
    {
        _live.Clear();
        var full = false;
        foreach (var row in scene.Rows)
        {
            full |= !Layer(row.Key, new BoardLayerRequest(BoardLayerKind.RowBody, scene, row));
            full |= !Layer(row.ValueKey, new BoardLayerRequest(BoardLayerKind.RowValue, scene, row));
            if (BoardPulses.BadgeKey(row.Entry) is { } badge)
            {
                full |= !Layer(badge, new BoardLayerRequest(BoardLayerKind.Badge, scene, row));
            }

            if (full)
            {
                break;
            }
        }

        if (!full)
        {
            full |= !Layer(BoardScene.HeroKey, new BoardLayerRequest(BoardLayerKind.Hero, scene, null));
            full |= !Layer(BoardScene.UnitKey, new BoardLayerRequest(BoardLayerKind.Unit, scene, null));
            full |= !Layer(BoardScene.HistoryKey, new BoardLayerRequest(BoardLayerKind.History, scene, null));
            full |= !Layer(BoardScene.FooterKey, new BoardLayerRequest(BoardLayerKind.Footer, scene, null));
            full |= !Layer(BoardScene.BackdropKey, new BoardLayerRequest(BoardLayerKind.Backdrop, scene, null));
            for (var i = 0; i < BoardHeroNumber.Glyphs.Length && !full; i++)
            {
                full |= !Layer(BoardHeroNumber.GlyphKeys[i], new BoardLayerRequest(BoardLayerKind.Glyph, scene, null, BoardHeroNumber.Glyphs[i]));
            }

            var sets = Sets(scene);
            for (var s = 0; s < sets.Count && !full; s++)
            {
                var set = sets[s];
                foreach (var glyph in BoardValueGlyphs.Alphabet(set))
                {
                    full |= !Layer(BoardValueGlyphs.KeyOf(glyph, set)!, new BoardLayerRequest(BoardLayerKind.ValueGlyph, scene, null, glyph, Set: set));
                    if (full)
                    {
                        break;
                    }
                }
            }

            // The live metrics' text layers (DESIGN 2c, Live metrics); a painter describes none for switched-off readouts.
            var metrics = scene.Metrics;
            if (metrics.Gauge)
            {
                foreach (var part in BoardGauge.Parts)
                {
                    full |= !Layer(BoardGauge.KeyOf(part), new BoardLayerRequest(BoardLayerKind.GaugeText, scene, null, Part: part));
                }
            }

            if (metrics.IntelligenceColumn)
            {
                full |= !Layer(BoardScene.CaptionsKey, new BoardLayerRequest(BoardLayerKind.Captions, scene, null));
            }

            foreach (var callout in scene.Callouts)
            {
                full |= !Layer(callout.Key, new BoardLayerRequest(BoardLayerKind.Callout, scene, null, Callout: callout));
            }
        }

        var flush = System.Diagnostics.Stopwatch.GetTimestamp();
        var ended = _atlas.EndDraw();
        _flushTicks += System.Diagnostics.Stopwatch.GetTimestamp() - flush;
        if (!ended)
        {
            return DrawResult.Lost;
        }

        if (full)
        {
            return DrawResult.Full;
        }

        _stale.Clear();
        foreach (var key in _atlas.Keys)
        {
            if (!_live.Contains(key))
            {
                _stale.Add(key);
            }
        }

        foreach (var key in _stale)
        {
            _atlas.Remove(key);
        }

        return DrawResult.Done;
    }

    private bool Layer(string key, BoardLayerRequest request)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        var described = Painter.Describe(request);
        _describeTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
        if (described is not { } spec)
        {
            return true;
        }

        _live.Add(key);
        if (_atlas.Holds(key, spec.Signature, spec.Width, spec.Height))
        {
            return true;
        }

        var painter = Painter;
        start = System.Diagnostics.Stopwatch.GetTimestamp();
        var drawn = _atlas.TryDraw(key, spec.Width, spec.Height, spec.Signature, context => painter.Paint(context, request, spec), spec.MeterX,
            spec.Baseline);
        var spent = System.Diagnostics.Stopwatch.GetTimestamp() - start;
        _paintTicks += spent;
        _paintByKind[(int)request.Kind] += spent;
        _paintedByKind[(int)request.Kind]++;
        return drawn;
    }

    private IReadOnlyList<BoardValueSet> _sets = [];
    private BoardMetrics? _setsFor;

    // The glyph sets of the scene's style and switches, made again only when its metrics change.
    private IReadOnlyList<BoardValueSet> Sets(BoardScene scene)
    {
        if (!ReferenceEquals(_setsFor, scene.Metrics))
        {
            _setsFor = scene.Metrics;
            _sets = BoardValueGlyphs.SetsFor(scene.Style, scene.Metrics);
        }

        return _sets;
    }

    private static int WidthFor(BoardScene scene, double scale)
    {
        var pixels = (int)Math.Ceiling(scene.Canvas.Side * scale) + 2 * BoardAtlasPacker.Gutter;
        return (pixels + 15) / 16 * 16;
    }

    private enum DrawResult
    {
        Done,
        Full,
        Lost,
    }
}
