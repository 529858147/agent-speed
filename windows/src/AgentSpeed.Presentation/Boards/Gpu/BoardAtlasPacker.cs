namespace AgentSpeed.Presentation.Boards.Gpu;

/// <summary>A box of the layer texture in pixels, the gutter included.</summary>
public readonly record struct BoardAtlasBox(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;
}

/// <summary>
/// Places a board's cached layers in its layer texture (DESIGN 2c, Two layers): shelves of rows that share a height, a
/// freed box reused by the next layer of its shelf that fits, and a failure when the texture is full (the platform then
/// starts over and rasterizes every live layer again). Each box keeps a transparent gutter so filtering never bleeds a
/// neighbor into a scaled layer.
/// </summary>
public sealed class BoardAtlasPacker
{
    public const int Gutter = 1;

    private readonly List<Shelf> _shelves = [];
    private readonly List<BoardAtlasBox> _free = [];
    private int _bottom;

    public BoardAtlasPacker(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>Boxes in use.</summary>
    public int Count { get; private set; }

    /// <summary>A box for a layer of <paramref name="width"/> × <paramref name="height"/> pixels (gutter added), false when full.</summary>
    public bool TryAllocate(int width, int height, out BoardAtlasBox box)
    {
        var w = Math.Max(1, width) + 2 * Gutter;
        var h = Math.Max(1, height) + 2 * Gutter;
        box = default;
        if (w > Width)
        {
            return false;
        }

        // A freed box of the same shelf height, the narrowest that fits.
        var best = -1;
        for (var i = 0; i < _free.Count; i++)
        {
            var candidate = _free[i];
            if (candidate.Height == h && candidate.Width >= w && (best < 0 || candidate.Width < _free[best].Width))
            {
                best = i;
            }
        }

        if (best >= 0)
        {
            box = _free[best];
            _free.RemoveAt(best);
            Count++;
            return true;
        }

        foreach (var shelf in _shelves)
        {
            if (shelf.Height == h && Width - shelf.X >= w)
            {
                box = new BoardAtlasBox(shelf.X, shelf.Y, w, h);
                shelf.X += w;
                Count++;
                return true;
            }
        }

        if (Height - _bottom < h)
        {
            return false;
        }

        var added = new Shelf(_bottom, h) { X = w };
        _shelves.Add(added);
        _bottom += h;
        box = new BoardAtlasBox(0, added.Y, w, h);
        Count++;
        return true;
    }

    /// <summary>Returns a box from <see cref="TryAllocate"/> to its shelf.</summary>
    public void Free(BoardAtlasBox box)
    {
        _free.Add(box);
        Count = Math.Max(0, Count - 1);
    }

    /// <summary>Forgets every box (the texture is cleared or recreated), optionally at a new size.</summary>
    public void Reset(int? width = null, int? height = null)
    {
        Width = Math.Max(1, width ?? Width);
        Height = Math.Max(1, height ?? Height);
        _shelves.Clear();
        _free.Clear();
        _bottom = 0;
        Count = 0;
    }

    /// <summary>The part of a box a layer of <paramref name="width"/> × <paramref name="height"/> pixels is drawn into.</summary>
    public static BoardAtlasBox Content(BoardAtlasBox box, int width, int height) => new(box.X + Gutter, box.Y + Gutter, width, height);

    private sealed class Shelf(int y, int height)
    {
        public int Y { get; } = y;

        public int Height { get; } = height;

        public int X { get; set; }
    }
}
