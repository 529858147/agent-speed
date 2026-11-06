using AgentSpeed.Presentation.Boards.Gpu;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Motion;

namespace AgentSpeed.Presentation.Boards.Scene;

/// <summary>How a style's number reacts (the prototypes' <c>.ch.in</c>, <c>.ch.in.hot</c>, <c>.num.gild</c> and <c>.glow.rgb</c>).</summary>
/// <param name="RollSeconds">A changed digit's roll.</param>
/// <param name="Hot">The color a rising digit flashes.</param>
/// <param name="HotSeconds">How long the flash takes to fade.</param>
/// <param name="HotHold">The part of the flash that holds the color.</param>
/// <param name="Fringes">Neon: chromatic fringes from h = 0.85.</param>
public readonly record struct BoardNumberLook(double RollSeconds, Rgba Hot, double HotSeconds, double HotHold, bool Fringes)
{
    private static readonly BoardNumberLook Glass = new(0.26, HeatPalette.WhiteHot, 0.9, 0.45, false);
    private static readonly BoardNumberLook Timing = new(0.22, Rgba.FromHex("#22C55E"), 0.8, 0.5, false);
    private static readonly BoardNumberLook Neon = new(0.24, Rgba.White, 0.8, 0.4, true);

    public static BoardNumberLook For(BoardStyle style) => style switch
    {
        BoardStyle.Timing => Timing,
        BoardStyle.Neon => Neon,
        _ => Glass,
    };
}

/// <summary>
/// The hero number composed from cached glyph layers (DESIGN 2c Hero number): changed digits roll in, rising ones flash, a new
/// peak sweeps a gold sheen across the digits and neon's hot number gets chromatic fringes. Glyph layers are
/// <see cref="GlyphPad"/> wider on every side than the glyph's advance, so a baked glow fits.
/// </summary>
public sealed class BoardHeroNumber
{
    public const string Glyphs = "0123456789.kMB";

    /// <summary>Room around a glyph's advance box in its layer.</summary>
    public const double GlyphPad = 16;

    public const double SheenSeconds = 1.3;

    /// <summary>Neon's fringes start at this heat.</summary>
    public const double FringeHeat = 0.85;

    private static readonly string[] Keys = Glyphs.Select(c => "#n" + c).ToArray();
    private static readonly Rgba FringeRed = new(1, 40 / 255.0, 110 / 255.0);
    private static readonly Rgba FringeCyan = new(0, 225 / 255.0, 1);

    private string _text = string.Empty;
    private string _old = string.Empty;
    private double _changed = double.NegativeInfinity;
    private double _sheen = double.NegativeInfinity;
    private bool _up;

    public string Text => _text;

    /// <summary>The glyph keys, in <see cref="Glyphs"/> order.</summary>
    public static IReadOnlyList<string> GlyphKeys => Keys;

    public static string? KeyOf(char c)
    {
        var i = Glyphs.IndexOf(c, StringComparison.Ordinal);
        return i < 0 ? null : Keys[i];
    }

    /// <summary>A new value: digits that changed roll unless the length changed or nothing may move.</summary>
    public void Set(string text, bool up, double now, bool animate)
    {
        if (string.Equals(text, _text, StringComparison.Ordinal))
        {
            return;
        }

        _old = _text;
        _text = text;
        _up = up;
        _changed = animate && _old.Length == text.Length ? now : double.NegativeInfinity;
    }

    public void Sheen(double now) => _sheen = now;

    public void Settle()
    {
        _changed = double.NegativeInfinity;
        _sheen = double.NegativeInfinity;
    }

    /// <summary>Digits roll (60 fps).</summary>
    public bool Rolling(double now, in BoardNumberLook look) => now - _changed < look.RollSeconds;

    /// <summary>Something moves: a roll, a flash or the sheen.</summary>
    public bool Animating(double now, in BoardNumberLook look) =>
        now - _changed < Math.Max(look.RollSeconds, _up ? look.HotSeconds : 0) || now - _sheen < SheenSeconds;

    /// <summary>The number's advance width with <paramref name="tracking"/> between glyphs (0 while glyphs are missing).</summary>
    public static double Measure(string text, IBoardLayers layers, double tracking)
    {
        var width = 0.0;
        for (var i = 0; i < text.Length; i++)
        {
            if (TryGlyph(text[i], layers, out var slot))
            {
                width += Advance(slot) + (i > 0 ? tracking : 0);
            }
        }

        return width;
    }

    /// <summary>
    /// Draws the number with its box's top-left at (<paramref name="x"/>, <paramref name="y"/>) (canvas points), scaled by
    /// <paramref name="punch"/> about its left at 80 % of its height; returns its advance width.
    /// </summary>
    public double Emit(BoardDrawList list, IBoardLayers layers, double x, double y, double height, double tracking, double now, double punch,
        in BoardNumberLook look, double heat, bool reduceMotion)
    {
        var width = Measure(_text, layers, tracking);
        var t = (now - _changed) / look.RollSeconds;
        var rolling = t is >= 0 and < 1 && _old.Length == _text.Length;
        var roll = rolling ? Easing.OutCubic(t) : 1;
        var fringes = look.Fringes && heat >= FringeHeat && !reduceMotion;
        var sheenT = (now - _sheen) / SheenSeconds;
        var sheen = sheenT is >= 0 and < 1 && !reduceMotion;
        var clip = rolling ? RectBounds.From(x - 2 * GlyphPad, y, width + 4 * GlyphPad, height) : default;
        var pivotY = y + height * 0.8;
        var dir = _up ? 1 : -1;
        var hot = HotAmount(now, look);

        var at = x;
        var oldAt = x;
        for (var i = 0; i < _text.Length; i++)
        {
            var has = TryGlyph(_text[i], layers, out var slot);
            var changed = rolling && _old[i] != _text[i];
            if (changed && TryGlyph(_old[i], layers, out var oldSlot))
            {
                // The old digit leaves the way the value moves.
                Glyph(list, oldSlot, x, oldAt, y - dir * height * roll, punch, pivotY, 1 - roll, default, 0, clip, sheen, sheenT, width);
                oldAt += Advance(oldSlot) + tracking;
            }
            else if (has)
            {
                oldAt += Advance(slot) + tracking;
            }

            if (!has)
            {
                continue;
            }

            var offset = changed ? dir * height * (1 - roll) : 0;
            var alpha = changed ? roll : 1;
            if (fringes)
            {
                Glyph(list, slot, x, at - 1.5, y + offset, punch, pivotY, 0.75 * alpha, FringeRed, 1, clip, false, 0, width);
                Glyph(list, slot, x, at + 1.5, y + offset, punch, pivotY, 0.75 * alpha, FringeCyan, 1, clip, false, 0, width);
            }

            Glyph(list, slot, x, at, y + offset, punch, pivotY, alpha, look.Hot, changed && _up ? hot : 0, clip, sheen, sheenT, width);
            at += Advance(slot) + tracking;
        }

        return width;
    }

    private static bool TryGlyph(char c, IBoardLayers layers, out BoardLayerSlot slot)
    {
        slot = default;
        return KeyOf(c) is { } key && layers.TryGet(key, out slot);
    }

    private static double Advance(in BoardLayerSlot slot) => slot.Width - 2 * GlyphPad;

    // The flash holds its color, then fades back to the glyph's own.
    private double HotAmount(double now, in BoardNumberLook look)
    {
        var t = (now - _changed) / look.HotSeconds;
        if (t is < 0 or >= 1)
        {
            return 0;
        }

        return t < look.HotHold ? 1 : 1 - Easing.OutCubic((t - look.HotHold) / (1 - look.HotHold));
    }

    private static void Glyph(BoardDrawList list, in BoardLayerSlot slot, double originX, double x, double y, double punch, double pivotY,
        double alpha, Rgba tint, double tintAmount, RectBounds clip, bool sheen, double sheenT, double width)
    {
        if (alpha <= 0.002)
        {
            return;
        }

        var left = originX + (x - GlyphPad - originX) * punch;
        var top = pivotY + (y - GlyphPad - pivotY) * punch;
        var instance = BoardLayerInstance.Create(slot, left, top, alpha, tint, tintAmount, clip);
        instance.Rect.Z *= (float)punch;
        instance.Rect.W *= (float)punch;
        if (sheen)
        {
            // The gold band crosses from before the first digit to past the last one.
            var half = Math.Max(8, width * 0.38);
            var center = originX - half + (width + 2 * half) * Easing.InOutSine(sheenT);
            instance.Sheen(center, half, Math.Sin(Math.PI * Math.Min(1, sheenT * 1.15)));
        }

        list.AddLayer(instance);
    }
}
