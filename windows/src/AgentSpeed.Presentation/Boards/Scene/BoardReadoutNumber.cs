using System.Globalization;
using AgentSpeed.Presentation.Boards.Gpu;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Motion;

namespace AgentSpeed.Presentation.Boards.Scene;

/// <summary>
/// One readout number of the sub-gauge composed from value glyphs (DESIGN 2c, Updates, motion and quiet): changed characters
/// roll in 0.24 s on cubic-bezier(0.2, 0.8, 0.2, 1), upward when the value rises and downward when it falls; a change in the
/// number of characters, or between <c>–</c> or the tag and a number, crossfades in 0.15 s instead (Reduce Motion: always).
/// No flash, punch or embers. Nothing allocates per frame.
/// </summary>
public sealed class BoardReadoutNumber
{
    public const double RollSeconds = 0.24;
    public const double FadeSeconds = 0.15;

    private string _text = string.Empty;
    private string _old = string.Empty;
    private double _value = double.NaN;
    private double _changed = double.NegativeInfinity;
    private bool _up = true;
    private bool _roll;

    public string Text => _text;

    /// <summary>The text that is fading out (a crossfade's old text); empty when none.</summary>
    public string Previous => _old;

    /// <summary>A new value at <paramref name="now"/>; <paramref name="animate"/> false shows it at once.</summary>
    public void Set(string text, double now, bool animate, bool reduceMotion)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.Equals(text, _text, StringComparison.Ordinal))
        {
            return;
        }

        var value = Parse(text);
        _old = _text;
        _up = !double.IsFinite(_value) || !double.IsFinite(value) || value >= _value;
        _value = value;
        _text = text;
        _roll = !reduceMotion && _old.Length == text.Length && double.IsFinite(value) && Rollable(_old);
        _changed = animate && _old.Length > 0 ? now : double.NegativeInfinity;
    }

    public void Settle() => _changed = double.NegativeInfinity;

    /// <summary>Characters roll or the text crossfades (60 fps).</summary>
    public bool Animating(double now) => now - _changed < (_roll ? RollSeconds : FadeSeconds);

    /// <summary>
    /// Draws the number on <paramref name="baseline"/> anchored at <paramref name="x"/> (<paramref name="align"/> −1 left, 0 centre,
    /// 1 right) in <paramref name="set"/>; <paramref name="size"/> is the font size (the roll window is 1.1 em). Returns its width.
    /// </summary>
    public double Emit(BoardDrawList list, IBoardLayers layers, BoardValueSet set, double x, int align, double baseline, double size,
        double tracking, Rgba tint, double amount, double alpha, double now)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(layers);
        var duration = _roll ? RollSeconds : FadeSeconds;
        var t = (now - _changed) / duration;
        if (t is < 0 or >= 1 || _old.Length == 0)
        {
            return BoardValueGlyphs.EmitOnBaseline(list, layers, _text, set, x, align, baseline, tracking, tint, amount, alpha);
        }

        if (!_roll)
        {
            var e = Easing.OutQuad(t);
            BoardValueGlyphs.EmitOnBaseline(list, layers, _old, set, x, align, baseline, tracking, tint, amount, alpha * (1 - e));
            return BoardValueGlyphs.EmitOnBaseline(list, layers, _text, set, x, align, baseline, tracking, tint, amount, alpha * e);
        }

        var roll = Easing.CubicBezier(0.2, 0.8, 0.2, 1, t);
        var width = BoardValueGlyphs.Measure(layers, _text, set, tracking);
        var oldWidth = BoardValueGlyphs.Measure(layers, _old, set, tracking);
        var box = 1.1 * size;
        var clip = RectBounds.From(x - width - 64, baseline - 0.95 * size, width + oldWidth + 128, box);
        var color = tint.WithAlpha(1);
        var opacity = alpha * (amount > 0 ? tint.A : 1);
        var dir = _up ? 1 : -1;
        Run(list, layers, _old, set, Start(x, align, oldWidth), baseline, tracking, color, amount, opacity, clip, -dir * box * roll, 1 - roll, true);
        Run(list, layers, _text, set, Start(x, align, width), baseline, tracking, color, amount, opacity, clip, dir * box * (1 - roll), roll, false);
        return width;
    }

    // One text's glyphs: the characters that differ from the other text move by dy and fade to alpha; the rest stand still
    // (only the new text draws them).
    private void Run(BoardDrawList list, IBoardLayers layers, string text, BoardValueSet set, double at, double baseline, double tracking, Rgba color,
        double amount, double opacity, RectBounds clip, double dy, double changedAlpha, bool leaving)
    {
        var other = leaving ? _text : _old;
        var pad = BoardValueGlyphs.PadOf(set);
        for (var i = 0; i < text.Length; i++)
        {
            if (!BoardValueGlyphs.TryGet(text[i], set, layers, out var slot))
            {
                continue;
            }

            var changed = i >= other.Length || other[i] != text[i];
            if (changed || !leaving)
            {
                var alpha = changed ? opacity * changedAlpha : opacity;
                if (alpha > 0.002)
                {
                    list.AddLayer(BoardLayerInstance.Create(slot, at - pad, baseline - BoardValueGlyphs.Baseline(slot) + (changed ? dy : 0),
                        alpha, color, amount, changed ? clip : default));
                }
            }

            at += slot.Width - 2 * pad + tracking;
        }
    }

    private static double Start(double x, int align, double width) => align < 0 ? x : align == 0 ? x - width / 2 : x - width;

    private static bool Rollable(string text)
    {
        foreach (var c in text)
        {
            if (c is not ((>= '0' and <= '9') or '.' or ',' or 'k' or 'M' or 'B'))
            {
                return false;
            }
        }

        return true;
    }

    private static double Parse(string text)
    {
        var digits = text.Replace(",", string.Empty, StringComparison.Ordinal);
        var scale = 1.0;
        if (digits.Length > 0 && digits[^1] is 'k' or 'M' or 'B')
        {
            scale = digits[^1] switch
            {
                'k' => 1e3,
                'M' => 1e6,
                _ => 1e9,
            };
            digits = digits[..^1];
        }

        return double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value * scale : double.NaN;
    }
}
