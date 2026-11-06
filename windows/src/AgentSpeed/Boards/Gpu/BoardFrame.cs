using AgentSpeed.App;
using AgentSpeed.Boards.Layers;
using AgentSpeed.Orb.Gpu;
using AgentSpeed.Presentation.Boards;
using AgentSpeed.Presentation.Boards.Gpu;
using AgentSpeed.Presentation.Boards.Scene;
using Vortice.Direct3D11;

namespace AgentSpeed.Boards.Gpu;

/// <summary>
/// One board's frame work, shared by the window's surface and the harnesses: the scene, its cached layers (brought up to
/// date only after a tick or a change of device, scale or size) and the draw list of the current frame.
/// </summary>
internal sealed class BoardFrame : IDisposable
{
    private bool _dirty = true;

    public BoardFrame(BoardStyle style, BoardSize size, IBoardLayerPainter painter, uint seed = 1)
    {
        Scene = new BoardScene(style, size, seed);
        Layers = new BoardLayerCache(painter);
    }

    public BoardScene Scene { get; }

    public BoardLayerCache Layers { get; }

    public BoardDrawList List { get; } = new();

    /// <summary>Stopwatch ticks spent checking and rasterizing layers (the bench's breakdown).</summary>
    public long UpdateTicks { get; private set; }

    /// <summary>The layers must be checked again before the next frame (a tick, a restyle, a resize).</summary>
    public void Invalidate() => _dirty = true;

    /// <summary>Builds the frame at <paramref name="now"/>; false when the device failed (the caller recovers it).</summary>
    public bool Build(GpuDevice device, double now, double scale, double opacity = 1)
    {
        // The figures' widths shape the columns the row layers are laid out in: they come first.
        Scene.Figures = Layers.Painter.Figures(Scene);
        if (_dirty || Layers.IsStale(device, scale, Scene))
        {
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                if (!Layers.Update(device, Scene, scale))
                {
                    return false;
                }
            }
            catch (SharpGen.Runtime.SharpGenException ex)
            {
                AppLog.Warn("A board's layers failed on the GPU.", ex);
                return false;
            }

            finally
            {
                UpdateTicks += System.Diagnostics.Stopwatch.GetTimestamp() - start;
            }

            _dirty = false;
        }

        List.Reset();
        List.Scale = scale;
        Scene.Render(now, Layers, List);
        List.Opacity = opacity;
        return true;
    }

    public bool Draw(GpuDevice device, ID3D11RenderTargetView target, int pixels) =>
        BoardGpuRenderer.Draw(device, List, Layers.Atlas.View, target, pixels, pixels);

    /// <summary>A 64-bit identity of the frame on screen: the draw list and the layers' content.</summary>
    public ulong Fingerprint(int pixels) => List.Fingerprint() ^ (ulong)pixels ^ ((ulong)Layers.Atlas.Rasterized << 32);

    public void Dispose() => Layers.Dispose();
}
