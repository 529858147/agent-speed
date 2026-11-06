using AgentSpeed.Presentation.Boards.Gpu;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.Orb.Gpu;

namespace AgentSpeed.Presentation.Tests.Boards;

public class BoardDrawListTests
{
    private static readonly BoardLayerSlot Slot = new(0, 0, 0.5f, 0.25f, 100, 20);

    [Fact]
    public void ConsecutiveItemsOfOneStateShareABatch()
    {
        var list = new BoardDrawList();
        list.Add(OrbGpuInstance.Circle(1, 1, 2, Rgba.White));
        list.Add(OrbGpuInstance.Circle(2, 2, 2, Rgba.White));
        list.Clip = BoardClip.Inside;
        list.AddLayer(BoardLayerInstance.Create(Slot, 0, 0, 1));
        list.AddLayer(BoardLayerInstance.Create(Slot, 0, 20, 1));
        list.Add(OrbGpuInstance.Circle(3, 3, 2, Rgba.White), OrbBlend.Additive);

        Assert.Equal(3, list.BatchCount);
        Assert.Equal(new BoardBatch(BoardPass.Shapes, OrbBlend.Over, BoardClip.None, 0, 2), list.Batches[0]);
        Assert.Equal(new BoardBatch(BoardPass.Layers, OrbBlend.Over, BoardClip.Inside, 0, 2), list.Batches[1]);
        Assert.Equal(new BoardBatch(BoardPass.Shapes, OrbBlend.Additive, BoardClip.Inside, 2, 1), list.Batches[2]);
    }

    [Fact]
    public void AlphaScaleFadesWhatIsAdded()
    {
        var list = new BoardDrawList { AlphaScale = 0.5f };
        list.Add(OrbGpuInstance.Circle(0, 0, 1, Rgba.White));
        list.AddLayer(BoardLayerInstance.Create(Slot, 0, 0, 0.8));

        Assert.Equal(0.5f, list.Instances[0].Color.W, 5);
        Assert.Equal(0.4f, list.Layers[0].Params.X, 5);
    }

    [Fact]
    public void TheFingerprintFollowsTheContent()
    {
        var list = new BoardDrawList { Canvas = 400, BoxHalf = 170, BoxRadius = 28 };
        list.Add(OrbGpuInstance.Circle(1, 1, 2, Rgba.White));
        var first = list.Fingerprint();

        list.Reset();
        list.Canvas = 400;
        list.BoxHalf = 170;
        list.BoxRadius = 28;
        list.Add(OrbGpuInstance.Circle(1, 1, 2, Rgba.White));
        Assert.Equal(first, list.Fingerprint());

        list.Reset();
        list.Canvas = 400;
        list.BoxHalf = 170;
        list.BoxRadius = 28;
        list.Add(OrbGpuInstance.Circle(1, 1.5, 2, Rgba.White));
        Assert.NotEqual(first, list.Fingerprint());
    }

    [Fact]
    public void AWarmFrameDoesNotAllocate()
    {
        var list = new BoardDrawList(64, 8);
        void Frame()
        {
            list.Reset();
            for (var i = 0; i < 64; i++)
            {
                list.Add(OrbGpuInstance.Circle(i, i, 2, Rgba.White), i % 2 == 0 ? OrbBlend.Over : OrbBlend.Additive);
            }

            list.AddLayer(BoardLayerInstance.Create(Slot, 0, 0, 1));
            _ = list.Fingerprint();
        }

        Frame();
        var before = GC.GetAllocatedBytesForCurrentThread();
        Frame();
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }
}
