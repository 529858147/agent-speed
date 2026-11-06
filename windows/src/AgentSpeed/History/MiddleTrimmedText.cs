using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace AgentSpeed.History;

/// <summary>
/// One line of text that keeps both ends and drops the middle (<c>gpt-5.5-…-2026-04-23</c>) when it does not fit, as
/// model ids differ at either end (DESIGN 9 <i>Unknown prices</i>). Never splits a surrogate pair or a combining sequence.
/// </summary>
internal sealed class MiddleTrimmedText : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(MiddleTrimmedText),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(typeof(MiddleTrimmedText),
        new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(typeof(MiddleTrimmedText),
        new FrameworkPropertyMetadata(SystemFonts.MessageFontSize, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty FontWeightProperty = TextElement.FontWeightProperty.AddOwner(typeof(MiddleTrimmedText),
        new FrameworkPropertyMetadata(FontWeights.Normal, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty FontStyleProperty = TextElement.FontStyleProperty.AddOwner(typeof(MiddleTrimmedText),
        new FrameworkPropertyMetadata(FontStyles.Normal, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(MiddleTrimmedText),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    private const string Ellipsis = "…";

    private FormattedText? _shown;

    public MiddleTrimmedText()
    {
        // The tooltip (the whole text) only matters when something was cut.
        ToolTipOpening += (_, e) => e.Handled = !IsTrimmed;
    }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }

    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }

    public FontWeight FontWeight { get => (FontWeight)GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }

    public FontStyle FontStyle { get => (FontStyle)GetValue(FontStyleProperty); set => SetValue(FontStyleProperty, value); }

    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    /// <summary>Whether the last layout cut the text.</summary>
    public bool IsTrimmed { get; private set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var text = Text ?? string.Empty;
        var full = Format(text);
        _shown = full;
        IsTrimmed = false;
        if (full.WidthIncludingTrailingWhitespace > availableSize.Width && !double.IsInfinity(availableSize.Width))
        {
            var elements = TextElements(text);
            int low = 0, high = elements.Count - 1;
            var best = Format(Ellipsis);
            while (low <= high)
            {
                var keep = (low + high) / 2;
                var candidate = Format(Trimmed(elements, keep));
                if (candidate.WidthIncludingTrailingWhitespace <= availableSize.Width)
                {
                    best = candidate;
                    low = keep + 1;
                }
                else
                {
                    high = keep - 1;
                }
            }

            _shown = best;
            IsTrimmed = true;
        }

        var width = Math.Min(_shown.WidthIncludingTrailingWhitespace, double.IsInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width);
        return new Size(Math.Max(0, width), full.Height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (_shown is { } text)
        {
            text.SetForegroundBrush(Foreground);
            drawingContext.DrawText(text, new Point(0, 0));
        }
    }

    private static List<string> TextElements(string text)
    {
        var list = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            list.Add(enumerator.GetTextElement());
        }

        return list;
    }

    /// <summary>The first and last text elements, <paramref name="keep"/> in all (the extra one in front), around an ellipsis.</summary>
    private static string Trimmed(List<string> elements, int keep)
    {
        var head = (keep + 1) / 2;
        var tail = keep - head;
        return string.Concat(elements.Take(head)) + Ellipsis + string.Concat(elements.Skip(elements.Count - tail));
    }

    private FormattedText Format(string text) => new(
        text,
        CultureInfo.CurrentUICulture,
        FlowDirection,
        new Typeface(FontFamily, FontStyle, FontWeight, FontStretches.Normal),
        FontSize,
        Foreground,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
