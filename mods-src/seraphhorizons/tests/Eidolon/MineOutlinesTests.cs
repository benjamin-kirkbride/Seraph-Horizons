using SeraphHorizons.Mod.Eidolon.Core;
using static SeraphHorizons.Mod.Tests.Eidolon.MineTestWorld;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>The boxes the command tool outlines cells out of reach with: exact for a box and an L, one a
/// layer's bounds past the most it draws.</summary>
public class MineOutlinesTests
{
    private static HashSet<MarkPos> Covered(IEnumerable<MarkArea> boxes) =>
        boxes.SelectMany(b => Cells(b.Min.X, b.Max.X, b.Min.Y, b.Max.Y, b.Min.Z, b.Max.Z)).ToHashSet();

    [Fact]
    public void ABoxOfCellsIsOneBox()
    {
        var cells = Cells(0, 7, 14, 17, 0, 1).ToList();
        var boxes = MineOutlines.Boxes(cells);
        Assert.Equal([Area(0, 7, 14, 17, 0, 1)], boxes);
    }

    [Fact]
    public void AnLShapeIsCoveredExactly()
    {
        var cells = Cells(0, 5, 3, 3, 0, 1).Concat(Cells(0, 1, 3, 3, 2, 6)).Concat(Cells(0, 1, 4, 4, 2, 6)).ToHashSet();
        var boxes = MineOutlines.Boxes(cells);
        Assert.Equal(cells, Covered(boxes));
        Assert.Equal(cells.Count, boxes.Sum(b => b.Volume));
        Assert.True(boxes.Count <= 3);
    }

    [Fact]
    public void PastTheMostItDrawsEachLayerIsItsBounds()
    {
        // a checkerboard: every cell its own box
        var cells = Cells(0, 15, 0, 1, 0, 15).Where(c => (c.X + c.Z + c.Y) % 2 == 0).ToHashSet();
        var boxes = MineOutlines.Boxes(cells);
        Assert.Equal([Area(0, 15, 0, 0, 0, 15), Area(0, 15, 1, 1, 0, 15)], boxes);
        Assert.Superset(cells, Covered(boxes));
        Assert.Empty(MineOutlines.Boxes([]));
    }
}
