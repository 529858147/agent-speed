using System.Runtime.InteropServices;
using AgentSpeed.Presentation.Orb.Gpu;

namespace AgentSpeed.Presentation.Boards.Gpu;

/// <summary>A pass of the board's draw list.</summary>
public enum BoardPass
{
    /// <summary>Instances of the orbs' signed-distance-field pipeline.</summary>
    Shapes,

    /// <summary>Cached layers (<see cref="BoardLayerInstance"/>).</summary>
    Layers,
}

/// <summary>The board's rounded square as a clip for a batch.</summary>
public enum BoardClip
{
    None,

    /// <summary>Only what lies inside the rounded square (the aurora, the warp, the rows).</summary>
    Inside,

    /// <summary>
    /// Only what lies outside it, faded out along its outline to nothing <see cref="BoardDrawList.OutsideReach"/> points away (the
    /// bloom, which must never tint the glass nor be cut by the window's edge).
    /// </summary>
    Outside,
}

/// <summary>A run of instances drawn with one pipeline state.</summary>
public readonly record struct BoardBatch(BoardPass Pass, OrbBlend Blend, BoardClip Clip, int Start, int Count);

/// <summary>
/// One frame of GPU work for one board, back to front (DESIGN 2c, Two layers): shape batches of the effects and cached
/// layers interleaved in the order they were added. Canvas coordinates are points in a square window whose center is the
/// board's center. The storage is sized once and reused, so a warm frame never allocates.
/// </summary>
public sealed class BoardDrawList
{
    private OrbGpuInstance[] _instances;
    private BoardLayerInstance[] _layers;
    private BoardBatch[] _batches = new BoardBatch[64];

    public BoardDrawList(int capacity = 2048, int layers = 64)
    {
        _instances = new OrbGpuInstance[Math.Max(16, capacity)];
        _layers = new BoardLayerInstance[Math.Max(8, layers)];
    }

    public int Count { get; private set; }

    public int LayerCount { get; private set; }

    public int BatchCount { get; private set; }

    public ReadOnlySpan<OrbGpuInstance> Instances => _instances.AsSpan(0, Count);

    public ReadOnlySpan<BoardLayerInstance> Layers => _layers.AsSpan(0, LayerCount);

    public ReadOnlySpan<BoardBatch> Batches => _batches.AsSpan(0, BatchCount);

    /// <summary>Side of the square canvas (points).</summary>
    public double Canvas { get; set; }

    /// <summary>Pixels per point of the target.</summary>
    public double Scale { get; set; } = 1;

    /// <summary>Opacity of the whole board (the window's fade).</summary>
    public double Opacity { get; set; } = 1;

    /// <summary>Half the side of the board's rounded square (points), centered on the canvas.</summary>
    public double BoxHalf { get; set; }

    /// <summary>Corner radius of the rounded square (points).</summary>
    public double BoxRadius { get; set; }

    /// <summary>
    /// Distance from the rounded square (points) at which an <see cref="BoardClip.Outside"/> batch has faded to nothing, by
    /// (1 − (d / reach)²)² of the distance d; 0 keeps it unfaded.
    /// </summary>
    public double OutsideReach { get; set; }

    /// <summary>The clip of shapes and layers added from now on.</summary>
    public BoardClip Clip { get; set; }

    /// <summary>Multiplies the alpha of everything added while set.</summary>
    public float AlphaScale { get; set; } = 1;

    public void Reset()
    {
        Count = 0;
        LayerCount = 0;
        BatchCount = 0;
        Clip = BoardClip.None;
        AlphaScale = 1;
        Opacity = 1;
    }

    public void Add(in OrbGpuInstance instance, OrbBlend blend = OrbBlend.Over)
    {
        if (Count == _instances.Length)
        {
            Array.Resize(ref _instances, _instances.Length * 2);
        }

        ref var slot = ref _instances[Count];
        slot = instance;
        if (AlphaScale < 1)
        {
            slot.ScaleAlpha(AlphaScale);
        }

        Append(BoardPass.Shapes, blend, Count);
        Count++;
    }

    public void AddLayer(in BoardLayerInstance layer)
    {
        if (LayerCount == _layers.Length)
        {
            Array.Resize(ref _layers, _layers.Length * 2);
        }

        ref var slot = ref _layers[LayerCount];
        slot = layer;
        if (AlphaScale < 1)
        {
            slot.ScaleAlpha(AlphaScale);
        }

        Append(BoardPass.Layers, OrbBlend.Over, LayerCount);
        LayerCount++;
    }

    /// <summary>A 64-bit fingerprint of everything the frame draws, so an identical frame is never presented twice.</summary>
    public ulong Fingerprint()
    {
        var hash = 14695981039346656037UL;
        hash = Mix(hash, MemoryMarshal.AsBytes(Instances));
        hash = Mix(hash, MemoryMarshal.AsBytes(Layers));
        hash = Mix(hash, MemoryMarshal.AsBytes(Batches));
        Span<double> globals = [Canvas, Scale, Opacity, BoxHalf, BoxRadius];
        return Mix(hash, MemoryMarshal.AsBytes(globals));
    }

    private void Append(BoardPass pass, OrbBlend blend, int index)
    {
        if (BatchCount > 0)
        {
            ref var last = ref _batches[BatchCount - 1];
            if (last.Pass == pass && last.Blend == blend && last.Clip == Clip && last.Start + last.Count == index)
            {
                last = last with { Count = last.Count + 1 };
                return;
            }
        }

        if (BatchCount == _batches.Length)
        {
            Array.Resize(ref _batches, _batches.Length * 2);
        }

        _batches[BatchCount++] = new BoardBatch(pass, blend, Clip, index, 1);
    }

    private static ulong Mix(ulong hash, ReadOnlySpan<byte> bytes)
    {
        var words = MemoryMarshal.Cast<byte, ulong>(bytes);
        foreach (var word in words)
        {
            hash = (hash ^ word) * 1099511628211UL;
            hash ^= hash >> 29;
        }

        for (var i = words.Length * 8; i < bytes.Length; i++)
        {
            hash = (hash ^ bytes[i]) * 1099511628211UL;
        }

        return hash;
    }
}
