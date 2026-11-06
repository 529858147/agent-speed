using System.Numerics;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace AgentSpeed.Boards.Layers;

/// <summary>The prototypes' font stacks on Windows (<c>--ui</c>, <c>--num</c> / <c>--disp</c>, <c>--mono</c>).</summary>
internal enum BoardFont
{
    Ui,
    Display,
    Mono,
}

/// <summary>A text style: font, size in points, weight, the CSS letter spacing in em and whether it is upper-cased.</summary>
internal readonly record struct BoardTextStyle(BoardFont Font, float Size, FontWeight Weight, float Tracking = 0, bool Upper = false)
{
    public BoardTextStyle With(float size) => this with { Size = size };
}

/// <summary>
/// DirectWrite for a painter: cached text formats per style, single-line layouts that trim with an ellipsis, measuring, and
/// CSS-like placement (line height 1, the glyphs' box centered on the line).
/// </summary>
internal sealed class BoardText : IDisposable
{
    private readonly Dictionary<(BoardFont, float, FontWeight), IDWriteTextFormat> _formats = [];
    private readonly Dictionary<BoardFont, string> _families = [];
    private readonly Dictionary<(BoardFont, FontWeight), float> _caps = [];
    private IDWriteFactory? _factory;
    private string _locale = "en-US";

    /// <summary>The locale text is shaped for (the app's language).</summary>
    public string Locale
    {
        get => _locale;
        set
        {
            if (!string.Equals(value, _locale, StringComparison.Ordinal))
            {
                _locale = value;
                ClearFormats();
            }
        }
    }

    public IDWriteFactory Factory => _factory ??= DWrite.DWriteCreateFactory<IDWriteFactory>(Vortice.DirectWrite.FactoryType.Shared);

    /// <summary>A layout of one line, at most <paramref name="maxWidth"/> wide (trimmed with an ellipsis).</summary>
    public IDWriteTextLayout Layout(string text, in BoardTextStyle style, float maxWidth = 4096)
    {
        var layout = Factory.CreateTextLayout(Shape(text, style), Format(style), Math.Max(1, maxWidth), style.Size * 2);
        if (style.Tracking != 0)
        {
            ApplyTracking(layout, text.Length, style.Tracking * style.Size);
        }

        return layout;
    }

    /// <summary>The advance width of <paramref name="text"/> in <paramref name="style"/>.</summary>
    public float Measure(string text, in BoardTextStyle style)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        using var layout = Layout(text, style);
        return layout.Metrics.WidthIncludingTrailingWhitespace;
    }

    /// <summary>
    /// Draws one line so its line box (line height 1 em, like the prototypes' <c>line-height: 1</c>) is vertically centered on
    /// <paramref name="centerY"/>, starting at <paramref name="x"/> or, when <paramref name="right"/>, ending there.
    /// Returns the drawn width.
    /// </summary>
    public float Draw(ID2D1DeviceContext context, string text, in BoardTextStyle style, float x, float centerY, ID2D1Brush brush,
        float maxWidth = 4096, bool right = false)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 1)
        {
            return 0;
        }

        using var layout = Layout(text, style, maxWidth);
        var width = Math.Min(layout.Metrics.WidthIncludingTrailingWhitespace, maxWidth);
        context.DrawTextLayout(new Vector2(right ? x - width : x, Top(layout, centerY)), layout, brush,
            DrawTextOptions.EnableColorFont | DrawTextOptions.Clip);
        return width;
    }

    /// <summary>
    /// Draws one line with its baseline at <paramref name="baseline"/>, starting at <paramref name="x"/> or, when
    /// <paramref name="right"/>, ending there; returns the drawn width.
    /// </summary>
    public float DrawOnBaseline(ID2D1DeviceContext context, string text, in BoardTextStyle style, float x, float baseline, ID2D1Brush brush,
        float maxWidth = 4096, bool right = false)
    {
        if (string.IsNullOrEmpty(text) || maxWidth <= 1)
        {
            return 0;
        }

        using var layout = Layout(text, style, maxWidth);
        var width = Math.Min(layout.Metrics.WidthIncludingTrailingWhitespace, maxWidth);
        var lines = layout.LineMetrics;
        var top = baseline - (lines.Length > 0 ? lines[0].Baseline : style.Size * 0.8f);
        context.DrawTextLayout(new Vector2(right ? x - width : x, top), layout, brush, DrawTextOptions.EnableColorFont | DrawTextOptions.Clip);
        return width;
    }

    /// <summary>The cap height of <paramref name="style"/>'s font at its size, from the font's own metrics.</summary>
    public float CapHeight(in BoardTextStyle style)
    {
        var key = (style.Font, style.Weight);
        if (!_caps.TryGetValue(key, out var share))
        {
            share = (float)Presentation.Boards.BoardRowFigures.NominalCapShare;
            using var collection = Factory.GetSystemFontCollection(false);
            if (collection.FindFamilyName(Family(style.Font), out var index))
            {
                using var family = collection.GetFontFamily(index);
                using var font = family.GetFirstMatchingFont(style.Weight, FontStretch.Normal, Vortice.DirectWrite.FontStyle.Normal);
                var metrics = font.Metrics;
                if (metrics.DesignUnitsPerEm > 0 && metrics.CapHeight > 0)
                {
                    share = (float)metrics.CapHeight / metrics.DesignUnitsPerEm;
                }
            }

            _caps[key] = share;
        }

        return share * style.Size;
    }

    /// <summary>The y at which a layout's top lands so its first line's glyph box is centered on <paramref name="centerY"/>.</summary>
    public static float Top(IDWriteTextLayout layout, float centerY)
    {
        var lines = layout.LineMetrics;
        if (lines.Length == 0)
        {
            return centerY;
        }

        // CSS centers the font's ascent + descent; DirectWrite's line adds the line gap below the descent.
        var line = lines[0];
        var baseline = line.Baseline;
        var descent = line.Height - baseline;
        return centerY - (baseline + Math.Min(descent, baseline * 0.3f)) / 2;
    }

    /// <summary>The y of the baseline when the line is centered on <paramref name="centerY"/>.</summary>
    public static float Baseline(IDWriteTextLayout layout, float centerY)
    {
        var lines = layout.LineMetrics;
        return lines.Length == 0 ? centerY : Top(layout, centerY) + lines[0].Baseline;
    }

    public void Dispose()
    {
        ClearFormats();
        _factory?.Dispose();
        _factory = null;
    }

    private static string Shape(string text, in BoardTextStyle style) =>
        style.Upper ? text.ToUpper(System.Globalization.CultureInfo.InvariantCulture) : text;

    private static void ApplyTracking(IDWriteTextLayout layout, int length, float spacing)
    {
        // Character spacing needs IDWriteTextLayout1; without it the text keeps its natural spacing.
        using var layout1 = layout.QueryInterfaceOrNull<IDWriteTextLayout1>();
        layout1?.SetCharacterSpacing(0, spacing, 0, new TextRange(0, (uint)length));
    }

    private IDWriteTextFormat Format(in BoardTextStyle style)
    {
        var key = (style.Font, style.Size, style.Weight);
        if (_formats.TryGetValue(key, out var format))
        {
            return format;
        }

        format = Factory.CreateTextFormat(Family(style.Font), null, style.Weight, Vortice.DirectWrite.FontStyle.Normal, FontStretch.Normal,
            style.Size, _locale);
        format.WordWrapping = WordWrapping.NoWrap;
        format.ParagraphAlignment = ParagraphAlignment.Near;
        using var ellipsis = Factory.CreateEllipsisTrimmingSign(format);
        format.SetTrimming(new Trimming { Granularity = TrimmingGranularity.Character }, ellipsis);
        _formats[key] = format;
        return format;
    }

    // The first installed family of each stack (Segoe UI Variable ships with Windows 11; Cascadia Mono with Windows Terminal).
    private string Family(BoardFont font)
    {
        if (_families.TryGetValue(font, out var family))
        {
            return family;
        }

        string[] stack = font switch
        {
            BoardFont.Display => ["Segoe UI Variable Display", "Segoe UI"],
            BoardFont.Mono => ["Cascadia Mono", "Consolas"],
            _ => ["Segoe UI Variable Text", "Segoe UI"],
        };
        family = stack[^1];
        using var collection = Factory.GetSystemFontCollection(false);
        foreach (var name in stack)
        {
            if (collection.FindFamilyName(name, out _))
            {
                family = name;
                break;
            }
        }

        _families[font] = family;
        return family;
    }

    private void ClearFormats()
    {
        foreach (var format in _formats.Values)
        {
            format.Dispose();
        }

        _formats.Clear();
    }
}
