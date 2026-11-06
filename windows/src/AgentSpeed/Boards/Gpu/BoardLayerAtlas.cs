using System.Numerics;
using AgentSpeed.App;
using AgentSpeed.Orb.Gpu;
using AgentSpeed.Presentation.Boards.Gpu;
using SharpGen.Runtime;
using Vortice;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace AgentSpeed.Boards.Gpu;

/// <summary>
/// One board's layer texture (DESIGN 2c, Two layers): a premultiplied BGRA Direct3D 11 texture that Direct2D draws rows,
/// the hero and the footer into — only when their content changes — and the board's frames sample. Lives on the orbs'
/// shared device and is recreated with it, or when the display scale or the board's size changes.
/// </summary>
internal sealed class BoardLayerAtlas : IBoardLayers, IDisposable
{
    public const int MaxHeight = 4096;

    private static ID2D1Factory1? s_factory;

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly BoardAtlasPacker _packer = new(1, 1);
    private ID3D11Texture2D? _texture;
    private ID3D11ShaderResourceView? _view;
    private ID2D1Device? _d2d;
    private ID2D1DeviceContext? _context;
    private ID2D1Bitmap1? _target;
    private int _generation = -1;
    private bool _drawing;

    /// <summary>The texture's view, null until <see cref="Ensure"/> succeeded.</summary>
    public ID3D11ShaderResourceView? View => _view;

    public double Scale { get; private set; } = 1;

    public int Width => _packer.Width;

    public int Height => _packer.Height;

    public int Count => _entries.Count;

    /// <summary>Layers rasterized since start (the bench).</summary>
    public long Rasterized { get; private set; }

    public IEnumerable<string> Keys => _entries.Keys;

    /// <summary>
    /// Makes the texture current for <paramref name="device"/> at <paramref name="scale"/>, <paramref name="width"/> pixels
    /// wide and at least <paramref name="height"/> high; true when every layer was dropped and must be rasterized again.
    /// </summary>
    public bool Ensure(GpuDevice device, double scale, int width, int height)
    {
        height = Math.Min(MaxHeight, Math.Max(height, Height));
        if (_texture is not null && _generation == device.Generation && Math.Abs(scale - Scale) < 0.001 && width == Width && height == Height)
        {
            return false;
        }

        Release();
        var r = device.Current;
        _texture = r.Device.CreateTexture2D(new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1,
            BindFlags.RenderTarget | BindFlags.ShaderResource, ResourceUsage.Default, CpuAccessFlags.None, 1, 0, ResourceOptionFlags.None));
        _view = r.Device.CreateShaderResourceView(_texture);
        using (var dxgi = r.Device.QueryInterface<IDXGIDevice>())
        {
            s_factory ??= D2D1.D2D1CreateFactory<ID2D1Factory1>(FactoryType.SingleThreaded);
            _d2d = s_factory.CreateDevice(dxgi);
        }

        _context = _d2d.CreateDeviceContext(DeviceContextOptions.None);
        using (var surface = _texture.QueryInterface<IDXGISurface>())
        {
            _target = _context.CreateBitmapFromDxgiSurface(surface,
                new BitmapProperties1(new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96,
                    BitmapOptions.Target | BitmapOptions.CannotDraw));
        }

        _context.Target = _target;
        _context.UnitMode = UnitMode.Pixels;
        _packer.Reset(width, height);
        _entries.Clear();
        _generation = device.Generation;
        Scale = scale;
        return true;
    }

    public bool TryGet(string key, out BoardLayerSlot slot)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            slot = entry.Slot;
            return true;
        }

        slot = default;
        return false;
    }

    /// <summary>Whether <paramref name="key"/> holds <paramref name="signature"/> at this size (points).</summary>
    public bool Holds(string key, string signature, double width, double height) =>
        _entries.TryGetValue(key, out var entry) && entry.Signature == signature && entry.PixelWidth == Pixels(width) && entry.PixelHeight == Pixels(height);

    /// <summary>Flushes the drawing to the texture; false when the device was lost (every layer is dropped).</summary>
    public bool EndDraw()
    {
        if (_context is null || !_drawing)
        {
            return true;
        }

        _drawing = false;
        var result = _context.EndDraw(out _, out _);
        if (result.Success)
        {
            return true;
        }

        AppLog.Warn($"A board's layers could not be drawn ({result}).");
        _generation = -1;
        return false;
    }

    /// <summary>
    /// Rasterizes <paramref name="key"/> (the context begins drawing on the first layer of a pass, so a pass that finds every
    /// layer current flushes nothing; <see cref="EndDraw"/> ends it); false when the texture is
    /// full (the caller clears it and draws every live layer again, or grows it).
    /// </summary>
    public bool TryDraw(string key, double width, double height, string signature, Action<ID2D1DeviceContext> paint, double meterX = double.NaN,
        double baseline = double.NaN)
    {
        if (_context is null)
        {
            return false;
        }

        var w = Pixels(width);
        var h = Pixels(height);
        if (_entries.TryGetValue(key, out var old))
        {
            _entries.Remove(key);
            _packer.Free(old.Box);
        }

        if (!_packer.TryAllocate(w, h, out var box))
        {
            return false;
        }

        var content = BoardAtlasPacker.Content(box, w, h);
        if (!_drawing)
        {
            _context.BeginDraw();
            _drawing = true;
        }

        _context.Transform = Matrix3x2.Identity;
        _context.PushAxisAlignedClip(new RawRectF(box.X, box.Y, box.Right, box.Bottom), AntialiasMode.Aliased);
        _context.Clear(new Color4(0, 0, 0, 0));
        _context.Transform = Matrix3x2.CreateScale((float)Scale) * Matrix3x2.CreateTranslation(content.X, content.Y);
        try
        {
            paint(_context);
        }
        finally
        {
            _context.PopAxisAlignedClip();
        }

        var slot = new BoardLayerSlot(content.X / (float)Width, content.Y / (float)Height, content.Right / (float)Width, content.Bottom / (float)Height,
            w / Scale, h / Scale, meterX, baseline);
        _entries[key] = new Entry(box, slot, signature, w, h);
        Rasterized++;
        return true;
    }

    public void Remove(string key)
    {
        if (_entries.Remove(key, out var entry))
        {
            _packer.Free(entry.Box);
        }
    }

    /// <summary>Drops every layer (the texture stays).</summary>
    public void Clear()
    {
        _entries.Clear();
        _packer.Reset();
    }

    public void Dispose() => Release();

    private int Pixels(double points) => Math.Max(1, (int)Math.Ceiling(points * Scale - 0.01));

    private void Release()
    {
        if (_drawing)
        {
            try
            {
                _context?.EndDraw();
            }
            catch (SharpGenException)
            {
            }

            _drawing = false;
        }

        _entries.Clear();
        _target?.Dispose();
        _context?.Dispose();
        _d2d?.Dispose();
        _view?.Dispose();
        _texture?.Dispose();
        _target = null;
        _context = null;
        _d2d = null;
        _view = null;
        _texture = null;
        _generation = -1;
    }

    private readonly record struct Entry(BoardAtlasBox Box, BoardLayerSlot Slot, string Signature, int PixelWidth, int PixelHeight);
}
