using AgentSpeed.Presentation.Boards;
using AgentSpeed.Presentation.Localization;

namespace AgentSpeed.Boards.Layers;

/// <summary>
/// A board's painter (DESIGN 2c Content): each request goes to the painter of its scene's style, so a board that changes style
/// keeps its painter; the three share one DirectWrite text cache and are made on first use.
/// </summary>
internal sealed class BoardPainters : IBoardLayerPainter, IDisposable
{
    private readonly Localizer _localizer;
    private readonly BoardText _text = new();
    private readonly BoardStylePainter?[] _painters = new BoardStylePainter?[BoardStyles.All.Count];

    private BoardPainters(BoardStyle style, Localizer localizer)
    {
        _localizer = localizer;
        For(style);
    }

    public static IBoardLayerPainter Create(BoardStyle style, Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return new BoardPainters(style, localizer);
    }

    public BoardLayerSpec? Describe(in BoardLayerRequest request) => For(request.Scene.Style).Describe(request);

    public void Paint(Vortice.Direct2D1.ID2D1DeviceContext context, in BoardLayerRequest request, in BoardLayerSpec spec) =>
        For(request.Scene.Style).Paint(context, request, spec);

    public BoardRowFigures Figures(Presentation.Boards.Scene.BoardScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return For(scene.Style).Figures(scene);
    }

    public void Dispose()
    {
        foreach (var painter in _painters)
        {
            painter?.Dispose();
        }

        _text.Dispose();
    }

    private BoardStylePainter For(BoardStyle style) => _painters[(int)style] ??= style switch
    {
        BoardStyle.Timing => new TimingBoardPainter(_localizer, _text),
        BoardStyle.Neon => new NeonBoardPainter(_localizer, _text),
        _ => new GlassBoardPainter(_localizer, _text),
    };
}
