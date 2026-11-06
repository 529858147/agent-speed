using System.Globalization;
using AgentSpeed.Core;
using AgentSpeed.Presentation.Boards;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Localization;
using Vortice;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace AgentSpeed.Boards.Layers;

/// <summary>
/// The foundation's neutral rows (phase 2 replaces them with the glass, timing and neon rows): rank, source dot, project
/// and model, the effort and speed capsule when the app supplies one, the value, the more lines and the hero number.
/// </summary>
internal sealed class PlaceholderBoardPainter : IBoardLayerPainter, IDisposable
{
    private const string TextFamily = "Segoe UI Variable Text";
    private const string DisplayFamily = "Segoe UI Variable Display";
    private const double ValueWidthGroup = 74;
    private const double ValueWidthSub = 62;
    private const double CapsuleGap = 6;

    private readonly Localizer _localizer;
    private readonly Dictionary<(string Family, float Size, FontWeight Weight, TextAlignment Align), IDWriteTextFormat> _formats = [];
    private IDWriteFactory? _factory;

    public PlaceholderBoardPainter(Localizer localizer)
    {
        _localizer = localizer;
    }

    /// <summary>PHASE 2 SEAM: the effort and speed capsule of group rows (null until execution modes are wired).</summary>
    public IBoardModeCapsule? Capsule { get; set; }

    public BoardRowFigures Figures(Presentation.Boards.Scene.BoardScene scene) => BoardRowFigures.Nominal;

    public BoardLayerSpec? Describe(in BoardLayerRequest request)
    {
        var scene = request.Scene;
        var metrics = scene.Metrics;
        var tick = scene.Tick;
        switch (request.Kind)
        {
            case BoardLayerKind.RowBody when request.Row is { } row:
            {
                var entry = row.Entry;
                var width = metrics.ListWidth - ValueWidth(entry.Kind);
                var signature = entry.Kind switch
                {
                    BoardEntryKind.Group => string.Join('|', "g", entry.Rank, entry.Group.Root.Source, entry.Group.Root.Project, entry.Group.Root.Model,
                        entry.Group.BadgePhase, Capsule is null ? 0 : entry.Group.Root.Modes.GetHashCode(), scene.Style),
                    BoardEntryKind.Sub => string.Join('|', "s", entry.Agent?.Source, entry.Agent?.Model, entry.Agent?.Phase, scene.Style),
                    _ => string.Join('|', "m", MoreText(entry, row.Live), scene.Style, _localizer.LanguageCode),
                };
                return new BoardLayerSpec(width, entry.Height, signature);
            }

            case BoardLayerKind.RowValue when request.Row is { Entry.Kind: BoardEntryKind.Group or BoardEntryKind.Sub } row:
                return new BoardLayerSpec(ValueWidth(row.Entry.Kind), row.Entry.Height, SpeedFormat.Speed(row.Live) + "|" + scene.Style);

            case BoardLayerKind.Hero when tick is not null:
                return new BoardLayerSpec(metrics.ListWidth, metrics.HeroHeight, tick.ValueText + "|" + scene.Style + "|" + metrics.ValueSize);

            default:
                return null;
        }
    }

    public void Paint(ID2D1DeviceContext context, in BoardLayerRequest request, in BoardLayerSpec spec)
    {
        context.TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode.Grayscale;
        using var primary = context.CreateSolidColorBrush(new Color4(1, 1, 1, 0.92f));
        using var secondary = context.CreateSolidColorBrush(new Color4(1, 1, 1, 0.55f));
        var width = (float)spec.Width;
        var height = (float)spec.Height;
        var scene = request.Scene;
        switch (request.Kind)
        {
            case BoardLayerKind.RowBody when request.Row is { } row:
                PaintBody(context, row.Entry, row.Live, width, height, primary, secondary);
                break;
            case BoardLayerKind.RowValue when request.Row is { } row:
            {
                var group = row.Entry.Kind == BoardEntryKind.Group;
                var format = Format(TextFamily, group ? 13 : 11.5f, group ? FontWeight.SemiBold : FontWeight.Medium, TextAlignment.Trailing);
                context.DrawText(SpeedFormat.Speed(row.Live), format, new Rect(0, 0, width - 8, height), group ? primary : secondary);
                break;
            }

            case BoardLayerKind.Hero when scene.Tick is { } tick:
            {
                var format = Format(DisplayFamily, (float)scene.Metrics.ValueSize, FontWeight.Bold, TextAlignment.Leading);
                context.DrawText(tick.ValueText, format, new Rect(0, 12, width, height - 12), primary);
                break;
            }
        }
    }

    public void Dispose()
    {
        foreach (var format in _formats.Values)
        {
            format.Dispose();
        }

        _formats.Clear();
        _factory?.Dispose();
        _factory = null;
    }

    private void PaintBody(ID2D1DeviceContext context, BoardEntry entry, double live, float width, float height, ID2D1Brush primary,
        ID2D1Brush secondary)
    {
        var mid = height / 2;
        switch (entry.Kind)
        {
            case BoardEntryKind.Group:
            {
                var root = entry.Group.Root;
                context.DrawText(entry.Rank.ToString(CultureInfo.InvariantCulture), Format(TextFamily, 12, FontWeight.Bold, TextAlignment.Center),
                    new Rect(0, 0, 24, height), secondary);
                using (var dot = context.CreateSolidColorBrush(ToColor(HeatPalette.SourceColor(root.Source))))
                {
                    context.FillEllipse(new Ellipse(new System.Numerics.Vector2(31, mid), 3.5f, 3.5f), dot);
                }

                var right = width - 4;
                if (Capsule is { } capsule && !root.Modes.IsEmpty)
                {
                    var capsuleHeight = Math.Min(18, height - 8);
                    var capsuleWidth = (float)capsule.Measure(root.Modes, capsuleHeight);
                    if (capsuleWidth > 0)
                    {
                        right -= capsuleWidth;
                        capsule.Paint(context, root.Modes, new RawRectF(right, (float)(mid - capsuleHeight / 2), right + capsuleWidth,
                            (float)(mid + capsuleHeight / 2)));
                        right -= (float)CapsuleGap;
                    }
                }

                var text = new Rect(42, 0, Math.Max(0, right - 42), height);
                using var layout = Layout(root.Project, Format(TextFamily, 12.5f, FontWeight.SemiBold, TextAlignment.Leading), text.Width, height);
                context.DrawTextLayout(new System.Numerics.Vector2(42, 0), layout, primary);
                var used = Math.Min(layout.Metrics.WidthIncludingTrailingWhitespace, text.Width);
                if (!string.IsNullOrEmpty(root.Model) && used + 8 < text.Width)
                {
                    context.DrawText(root.Model, Format(TextFamily, 10.5f, FontWeight.Normal, TextAlignment.Leading),
                        new Rect(42 + used + 8, 1, text.Width - used - 8, height), secondary);
                }

                break;
            }

            case BoardEntryKind.Sub:
            {
                using var line = context.CreateSolidColorBrush(new Color4(1, 1, 1, 0.18f));
                context.DrawLine(new System.Numerics.Vector2(30, 0), new System.Numerics.Vector2(30, mid), line, 1);
                context.DrawLine(new System.Numerics.Vector2(30, mid), new System.Numerics.Vector2(38, mid), line, 1);
                var agent = entry.Agent;
                var label = agent?.Model ?? agent?.Project ?? string.Empty;
                context.DrawText(label, Format(TextFamily, 10.5f, FontWeight.Medium, TextAlignment.Leading), new Rect(44, 0, width - 48, height), secondary);
                break;
            }

            default:
                context.DrawText(MoreText(entry, live), Format(TextFamily, 10.5f, FontWeight.Medium, TextAlignment.Leading),
                    new Rect(entry.Kind == BoardEntryKind.MoreSubs ? 44 : 10, 0, width - 12, height), secondary);
                break;
        }
    }

    private string MoreText(BoardEntry entry, double live) =>
        _localizer.Format(entry.Kind == BoardEntryKind.MoreSubs ? "boards.moreSubs" : "boards.moreGroups", entry.Rest, SpeedFormat.Speed(live));

    private static double ValueWidth(BoardEntryKind kind) => kind switch
    {
        BoardEntryKind.Group => ValueWidthGroup,
        BoardEntryKind.Sub => ValueWidthSub,
        _ => 0,
    };

    private IDWriteTextLayout Layout(string text, IDWriteTextFormat format, float width, float height) =>
        Factory.CreateTextLayout(text, format, Math.Max(1, width), Math.Max(1, height));

    private IDWriteFactory Factory => _factory ??= DWrite.DWriteCreateFactory<IDWriteFactory>(Vortice.DirectWrite.FactoryType.Shared);

    private IDWriteTextFormat Format(string family, float size, FontWeight weight, TextAlignment align)
    {
        if (_formats.TryGetValue((family, size, weight, align), out var format))
        {
            return format;
        }

        format = Factory.CreateTextFormat(family, null, weight, Vortice.DirectWrite.FontStyle.Normal, FontStretch.Normal, size,
            _localizer.Culture.Name);
        format.TextAlignment = align;
        format.ParagraphAlignment = ParagraphAlignment.Center;
        format.WordWrapping = WordWrapping.NoWrap;
        using var ellipsis = Factory.CreateEllipsisTrimmingSign(format);
        format.SetTrimming(new Trimming { Granularity = TrimmingGranularity.Character }, ellipsis);
        _formats[(family, size, weight, align)] = format;
        return format;
    }

    private static Color4 ToColor(Rgba color) => new((float)color.R, (float)color.G, (float)color.B, (float)color.A);
}
