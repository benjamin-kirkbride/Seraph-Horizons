using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Tests.Ore;

public class OreCellBookTests
{
    private static readonly CellPos Cell = new(2, 3);

    [Fact]
    public void ACellStartsAtItsPrimarySpot()
    {
        var book = new OreCellBook();
        Assert.Equal(0, book.ActiveIndex("copper", Cell));
        Assert.Equal(SpotStatus.Waiting, book.StatusOf("copper", Cell, 0));
        Assert.Equal(SpotStatus.Pending, book.StatusOf("copper", Cell, 1));
        Assert.Equal(0, book.Count);
    }

    [Fact]
    public void AVeinAtTheAnchorSettlesTheCell()
    {
        var book = new OreCellBook();
        Assert.True(book.OnAnchorGenerated("copper", Cell, 0, hasOre: true));
        Assert.Equal(SpotStatus.Placed, book.StatusOf("copper", Cell, 0));
        // Later anchors change nothing.
        Assert.False(book.OnAnchorGenerated("copper", Cell, 1, hasOre: false));
        Assert.False(book.OnAnchorGenerated("copper", Cell, 0, hasOre: false));
        Assert.Equal(0, book.ActiveIndex("copper", Cell));
        Assert.True(book.Get("copper", Cell).Placed);
    }

    [Fact]
    public void AnEmptyAnchorMovesToTheNextFallback()
    {
        var book = new OreCellBook();
        book.OnAnchorGenerated("iron", Cell, 0, hasOre: false);
        Assert.Equal(1, book.ActiveIndex("iron", Cell));
        Assert.Equal(SpotStatus.Failed, book.StatusOf("iron", Cell, 0));
        Assert.Equal(SpotStatus.Waiting, book.StatusOf("iron", Cell, 1));
        // Other metals and cells are separate.
        Assert.Equal(0, book.ActiveIndex("copper", Cell));
        Assert.Equal(0, book.ActiveIndex("iron", new CellPos(2, 4)));
    }

    [Fact]
    public void FallbacksGeneratedEarlyAreSkipped()
    {
        var book = new OreCellBook();
        Assert.True(book.OnAnchorGenerated("tin", Cell, 1, hasOre: false)); // before its turn
        Assert.True(book.OnFallbackGenerated("tin", Cell, 2));
        Assert.False(book.OnFallbackGenerated("tin", Cell, 2));
        Assert.Equal(SpotStatus.Consumed, book.StatusOf("tin", Cell, 1));
        book.OnAnchorGenerated("tin", Cell, 0, hasOre: false);
        Assert.Equal(3, book.ActiveIndex("tin", Cell));
    }

    [Fact]
    public void WithNoSpotLeftTheCellHasNone()
    {
        var book = new OreCellBook();
        for (int i = 0; i < OreCells.SpotCount; i++)
            book.OnAnchorGenerated("gold", Cell, i, hasOre: false);
        Assert.Null(book.ActiveIndex("gold", Cell));
        Assert.True(book.Get("gold", Cell).None);
        Assert.False(book.OnAnchorGenerated("gold", Cell, OreCells.SpotCount - 1, hasOre: true));
    }

    [Fact]
    public void RoundTripsThroughText()
    {
        var book = new OreCellBook();
        book.OnAnchorGenerated("copper", Cell, 0, hasOre: false);
        book.OnFallbackGenerated("copper", Cell, 3);
        book.OnAnchorGenerated("coal", new CellPos(-1, 0), 0, hasOre: true);
        var text = book.Serialize();
        var back = OreCellBook.Parse(text);
        Assert.Equal(text, back.Serialize());
        Assert.Equal(1, back.ActiveIndex("copper", Cell));
        Assert.Equal(SpotStatus.Consumed, back.StatusOf("copper", Cell, 3));
        Assert.True(back.Get("coal", new CellPos(-1, 0)).Placed);
        Assert.Equal(0, OreCellBook.Parse(null).Count);
        Assert.Throws<FormatException>(() => OreCellBook.Parse("v9\n"));
    }
}

public class OreWorldRecordTests
{
    private static readonly OreWorldRecord AllOn =
        new(true, 5000, new Dictionary<string, int>(), true, true, true);

    [Fact]
    public void ANewWorldTakesTheConfig() =>
        Assert.Equal(AllOn, OreWorldRecord.ForWorld(null, isNewWorld: true, AllOn));

    [Fact]
    public void AnExistingWorldWithoutARecordGetsNothing() =>
        Assert.Equal(OreWorldRecord.AllOff, OreWorldRecord.ForWorld(null, isNewWorld: false, AllOn));

    [Fact]
    public void TheSavedRecordWinsAndTheConfigCanOnlySwitchOff()
    {
        var saved = OreWorldRecord.AllOff with { OreCells = true, CellSize = 3000 };
        var config = AllOn with { CellSize = 9000, SmallerDeposits = false };
        var record = OreWorldRecord.ForWorld(saved, isNewWorld: true, config);
        Assert.Equal(saved, record);
        var effective = record.Effective(config);
        Assert.True(effective.OreCells);
        Assert.Equal(3000, effective.CellSize);
        Assert.False(effective.NoSurfaceCopper);
        Assert.False(record.Effective(config with { OreCells = false }).OreCells);
    }

    [Fact]
    public void RoundTripsThroughText()
    {
        var record = AllOn with { CellSizeByMetal = new Dictionary<string, int> { ["gold"] = 8000 }, RarerDistricts = false };
        var back = OreWorldRecord.Parse(record.Serialize());
        Assert.Equal(record, back);
        Assert.Equal(8000, back.CellSizeByMetal["gold"]);
    }
}

public class OreMetalsTests
{
    [Theory]
    [InlineData("malachite", "copper")]
    [InlineData("nativecopper", "copper")]
    [InlineData("limonite", "iron")]
    [InlineData("quartz_nativegold", "gold")]
    [InlineData("lignite", "coal")]
    [InlineData("borax", "borax")]
    [InlineData("quartz", null)]
    [InlineData("olivine", null)]
    [InlineData("garnetpyrope", null)]
    [InlineData(null, null)]
    public void MapsOresToMetals(string? ore, string? metal) => Assert.Equal(metal, OreMetals.MetalOf(ore));

    [Theory]
    [InlineData("ore-rich-malachite-limestone", "malachite")]
    [InlineData("ore-poor-quartz_nativegold-granite", "quartz_nativegold")]
    [InlineData("ore-lignite-shale", "lignite")]
    [InlineData("ore-bituminouscoal-claystone", "bituminouscoal")]
    [InlineData("rock-granite", null)]
    [InlineData("ore-x", null)]
    public void ReadsTheOreOfAnOreBlock(string path, string? ore) => Assert.Equal(ore, OreMetals.OreOfBlockPath(path));

    [Fact]
    public void ListsEveryGroup()
    {
        Assert.Contains("copper", OreMetals.All);
        Assert.Contains("rhodochrosite", OreMetals.All);
        Assert.Equal(OreMetals.All.Distinct().Count(), OreMetals.All.Count);
        Assert.Equal(["anthracite", "bituminouscoal", "lignite"], OreMetals.OresOf("coal"));
    }
}

public class VeinScalingTests
{
    private static VeinShape Shape(string type, float r, float n, float len) =>
        new(type, new SizeRange(r, r / 3), new SizeRange(n, n / 3), new SizeRange(len, len / 3));

    [Fact]
    public void DiscsShrinkByTheSquareRootOnTheRadius()
    {
        var scaled = VeinScaling.Scale(Shape("seam", 96, 20, 3), 0.25);
        Assert.Equal(48, scaled.Radius.Avg, 3);
        Assert.Equal(16, scaled.Radius.Var, 3);
        Assert.Equal(20, scaled.BranchCount.Avg);
    }

    [Fact]
    public void TubesShrinkOnTheirCount()
    {
        var scaled = VeinScaling.Scale(Shape("hydrotube", 12, 12, 256), 0.25);
        Assert.Equal(3, scaled.BranchCount.Avg, 3);
        Assert.Equal(256, scaled.BranchLength.Avg);
        Assert.Equal(12, scaled.Radius.Avg);
    }

    [Fact]
    public void BelowOneAndAHalfTendrilsTheLengthTakesTheRest()
    {
        // 6 ± 2 tendrils draw about 5.5; a tenth of that is 0.55 of one tendril.
        var scaled = VeinScaling.Scale(new VeinShape("chimney", new SizeRange(6, 2), new SizeRange(6, 2), new SizeRange(96, 32)), 0.1);
        Assert.Equal(new SizeRange(1, 0), scaled.BranchCount);
        Assert.Equal(96 * 0.55, scaled.BranchLength.Avg, 2);
    }

    [Fact]
    public void NeverGrowsAndLeavesUnknownTypesAlone()
    {
        var shape = Shape("fault", 48, 10, 3);
        Assert.Equal(shape, VeinScaling.Scale(shape, 1.5));
        Assert.Equal(Shape("disc", 5, 1, 1), VeinScaling.Scale(Shape("disc", 5, 1, 1), 0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => VeinScaling.Scale(shape, 0));
    }

    [Fact]
    public void TheTableGivesTargetOverMedianOrTheFactor()
    {
        var table = new OreSizeTable(new Dictionary<string, OreSizeTable.Entry>
        {
            ["copper"] = new(3567, 400),
            ["coal"] = new(Factor: 0.75),
            ["gold"] = new(130, 200),
            ["odd"] = new(0, 100),
        });
        Assert.Equal(400.0 / 3567, table.FactorFor("copper")!.Value, 6);
        Assert.Equal(0.75, table.FactorFor("coal"));
        Assert.Equal(1, table.FactorFor("gold"));
        Assert.Null(table.FactorFor("odd"));
        Assert.Null(table.FactorFor("iron"));
    }
}
