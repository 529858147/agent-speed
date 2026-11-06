using AgentSpeed.Presentation.Boards.Gpu;

namespace AgentSpeed.Presentation.Tests.Boards;

public class BoardAtlasPackerTests
{
    [Fact]
    public void LayersOfOneHeightShareAShelf()
    {
        var packer = new BoardAtlasPacker(200, 100);

        Assert.True(packer.TryAllocate(60, 20, out var a));
        Assert.True(packer.TryAllocate(60, 20, out var b));
        Assert.True(packer.TryAllocate(60, 30, out var c));

        Assert.Equal(new BoardAtlasBox(0, 0, 62, 22), a);
        Assert.Equal(new BoardAtlasBox(62, 0, 62, 22), b);
        Assert.Equal(new BoardAtlasBox(0, 22, 62, 32), c);
        Assert.Equal(3, packer.Count);
    }

    [Fact]
    public void AFreedBoxIsReusedByTheNarrowestFit()
    {
        var packer = new BoardAtlasPacker(300, 60);
        packer.TryAllocate(100, 20, out var wide);
        packer.TryAllocate(50, 20, out var narrow);
        packer.Free(wide);
        packer.Free(narrow);

        Assert.True(packer.TryAllocate(40, 20, out var reused));

        Assert.Equal(narrow, reused);
    }

    [Fact]
    public void BoxesNeverOverlapAndStayInside()
    {
        var packer = new BoardAtlasPacker(256, 256);
        var boxes = new List<BoardAtlasBox>();
        var random = new Random(3);
        for (var i = 0; i < 400; i++)
        {
            if (boxes.Count > 0 && random.NextDouble() < 0.4)
            {
                var index = random.Next(boxes.Count);
                packer.Free(boxes[index]);
                boxes.RemoveAt(index);
                continue;
            }

            if (packer.TryAllocate(random.Next(10, 120), random.Next(1, 4) * 12, out var box))
            {
                boxes.Add(box);
            }
        }

        foreach (var box in boxes)
        {
            Assert.InRange(box.X, 0, 256 - box.Width);
            Assert.InRange(box.Y, 0, 256 - box.Height);
        }

        for (var i = 0; i < boxes.Count; i++)
        {
            for (var j = i + 1; j < boxes.Count; j++)
            {
                var a = boxes[i];
                var b = boxes[j];
                Assert.False(a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom, $"{a} overlaps {b}");
            }
        }

        Assert.Equal(boxes.Count, packer.Count);
    }

    [Fact]
    public void AFullTextureFailsUntilReset()
    {
        var packer = new BoardAtlasPacker(100, 50);
        Assert.True(packer.TryAllocate(90, 40, out _));
        Assert.False(packer.TryAllocate(90, 40, out _));
        Assert.False(packer.TryAllocate(120, 4, out _));

        packer.Reset(height: 100);

        Assert.True(packer.TryAllocate(90, 40, out _));
        Assert.True(packer.TryAllocate(90, 40, out _));
        Assert.Equal(new BoardAtlasBox(5, 7, 30, 10), BoardAtlasPacker.Content(new BoardAtlasBox(4, 6, 32, 12), 30, 10));
    }
}
