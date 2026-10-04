using SeraphHorizons.Mod.BuckingSawmill.Core;

namespace SeraphHorizons.Mod.Tests;

public class TrunkBoxTests
{
    private static Rig Shipped() => Rig.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "buckingmill-rig.json")));

    [Theory]
    [InlineData("xs", TrunkClass.Thin, "lg")]
    [InlineData("sm", TrunkClass.Thin, "lg")]
    [InlineData("md", TrunkClass.Thin, "lg")]
    [InlineData("lg", TrunkClass.Thin, "lg")]
    [InlineData("xl", TrunkClass.Thick, "xxl")]
    [InlineData("xxl", TrunkClass.Thick, "xxl")]
    [InlineData(null, TrunkClass.Thin, "lg")]
    public void Every_size_is_shown_as_lg_or_xxl(string? size, TrunkClass trunk, string shown)
    {
        Assert.Equal(trunk, TrunkBox.ClassOf(size));
        Assert.Equal(shown, TrunkBox.DisplaySize(trunk));
        Assert.Null(TrunkBox.DisplaySize(TrunkClass.None));
    }

    // The saws come down onto the shown model's top.
    [Theory]
    [InlineData("xs", 1)]
    [InlineData("lg", 1)]
    [InlineData("xl", 2)]
    [InlineData("xxl", 2)]
    public void The_thickness_the_saws_touch_is_the_shown_models(string size, int thickness)
    {
        Assert.Equal(thickness, SawDepth.TrunkThickness(size));
        Assert.Equal(thickness, TrunkBox.Size(TrunkBox.ClassOf(size)).Height);
    }

    [Fact]
    public void The_box_lies_centred_on_the_bed()
    {
        var bed = Shipped().TrunkBed!;
        var (min, max) = TrunkBox.Bounds(bed, TrunkClass.Thin);
        Assert.Equal(new Float3(bed.Origin.X - 2, bed.Origin.Y, bed.Origin.Z - 0.5f), min);
        Assert.Equal(new Float3(bed.Origin.X + 2, bed.Origin.Y + 1, bed.Origin.Z + 0.5f), max);
        (min, max) = TrunkBox.Bounds(bed, TrunkClass.Thick);
        Assert.Equal(new Float3(bed.Origin.X - 2.5f, bed.Origin.Y, bed.Origin.Z - 1), min);
        Assert.Equal(new Float3(bed.Origin.X + 2.5f, bed.Origin.Y + 2, bed.Origin.Z + 1), max);
        // A bed along z turns it.
        var alongZ = new TrunkBed(new Float3(0, 0, 0), Axis.Z, 5);
        Assert.Equal((new Float3(-0.5f, 0, -2), new Float3(0.5f, 1, 2)), TrunkBox.Bounds(alongZ, TrunkClass.Thin));
    }

    /// <summary>The boxes' volume summed, and the box's own.</summary>
    private static (float Boxes, float Whole) Volumes(IReadOnlyDictionary<Int3, Box> boxes, (Float3 Min, Float3 Max) t) =>
        (boxes.Values.Sum(b => (b.X2 - b.X1) * (b.Y2 - b.Y1) * (b.Z2 - b.Z1)),
         (t.Max.X - t.Min.X) * (t.Max.Y - t.Min.Y) * (t.Max.Z - t.Min.Z));

    [Fact]
    public void Clipped_into_full_cells_the_boxes_add_up_to_the_trunk()
    {
        // every cell present: each part goes into its own cell, nothing reaches up
        var cells = new List<Int3>();
        for (int x = -5; x <= 1; x++)
        for (int y = 0; y <= 3; y++)
        for (int z = -1; z <= 1; z++)
            cells.Add(new Int3(x, y, z));
        var t = TrunkBox.Bounds(Shipped().TrunkBed!, TrunkClass.Thick);
        var boxes = TrunkBox.CellBoxes(cells, t);
        var (sum, whole) = Volumes(boxes, t);
        Assert.Equal(whole, sum, 3);
        Assert.All(boxes.Values, b => Assert.True(b.X1 >= 0 && b.X2 <= 1 && b.Y1 >= 0 && b.Y2 <= 1 && b.Z1 >= 0 && b.Z2 <= 1, b.ToString()));
    }

    [Fact]
    public void A_cell_with_nothing_above_it_reaches_up_one_cell()
    {
        // a one-block-high stack: the trunk's part above the lower cell is that cell's
        var t = (new Float3(0.2f, 0.5f, 0.2f), new Float3(0.8f, 1.5f, 0.8f));
        var box = Assert.Single(TrunkBox.CellBoxes([new Int3(0, 0, 0)], t)).Value;
        Assert.Equal(new Box(0.2f, 0.5f, 0.2f, 0.8f, 1.5f, 0.8f), box);
        // with a cell above, each keeps its own part
        var both = TrunkBox.CellBoxes([new Int3(0, 0, 0), new Int3(0, 1, 0)], t);
        Assert.Equal(new Box(0.2f, 0.5f, 0.2f, 0.8f, 1f, 0.8f), both[new Int3(0, 0, 0)]);
        Assert.Equal(new Box(0.2f, 0f, 0.2f, 0.8f, 0.5f, 0.8f), both[new Int3(0, 1, 0)]);
        // never more than one cell up: the game looks no further
        var tall = (new Float3(0.2f, 0.5f, 0.2f), new Float3(0.8f, 2.5f, 0.8f));
        Assert.Equal(2f, Assert.Single(TrunkBox.CellBoxes([new Int3(0, 0, 0)], tall)).Value.Y2);
    }

    // In the shipped rig the row over the bed's middle is empty (y 1, z 0), so the cells under
    // it carry the trunk up through it. Thin: everything is covered. Thick: everything but the
    // half block of it over the controller's column (x 0..0.5), where only the bed's two cells
    // are the mill's: its north strip (z -0.3125..0, in cells [0,0,-1] and [0,1,-1]) and its top
    // (y 2..2.5, two cells up from the bed) get no box.
    [Theory]
    [InlineData(TrunkClass.Thin, 0f)]
    [InlineData(TrunkClass.Thick, 0.5f * 1.5f * 0.3125f + 0.5f * 0.5f * 2f)]
    public void The_shipped_cells_cover_the_trunk(TrunkClass trunk, float uncovered)
    {
        var rig = Shipped();
        var t = TrunkBox.Bounds(rig.TrunkBed!, trunk);
        var boxes = TrunkBox.CellBoxes(rig.Cells.Select(c => c.Pos), t);
        var (sum, whole) = Volumes(boxes, t);
        Assert.Equal(whole - uncovered, sum, 3);
        Assert.All(boxes.Keys, c => Assert.NotNull(rig.CellAt(c)));
    }

    [Fact]
    public void Contains_takes_a_hit_on_a_face()
    {
        var t = (new Float3(-4, 0.5f, 0.1875f), new Float3(0, 1.5f, 1.1875f));
        Assert.True(TrunkBox.Contains(t, new Float3(-2, 1.5f, 0.5f)));
        Assert.True(TrunkBox.Contains(t, new Float3(0, 1f, 1f)));
        Assert.False(TrunkBox.Contains(t, new Float3(-2, 1.6f, 0.5f)));
        Assert.False(TrunkBox.Contains(t, new Float3(0.1f, 1f, 0.5f)));
    }
}
