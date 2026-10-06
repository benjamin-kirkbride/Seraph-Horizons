using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Tests.Ore;

public class OreTallyTests
{
    [Theory]
    [InlineData("ore-rich-nativecopper-granite", "copper", "rich")]
    [InlineData("ore-poor-hematite-basalt", "iron", "poor")]
    [InlineData("ore-bountiful-quartz_nativegold-granite", "gold", "bountiful")]
    [InlineData("ore-lignite-shale", "coal", "-")]
    [InlineData("ore-borax-limestone", "borax", "-")]
    public void ClassifiesOreBlocks(string path, string metal, string grade)
    {
        Assert.Equal((metal, grade), OreTally.Classify(path));
    }

    [Theory]
    [InlineData("rock-granite")]
    [InlineData("ore-quartz-granite")]
    [InlineData("looseores-nativecopper-granite-free")]
    public void LeavesTheRestOut(string path) => Assert.Null(OreTally.Classify(path));

    [Fact]
    public void SumsByMetalAndGrade()
    {
        var t = new OreTally();
        t.Add("copper", "rich", 10, 10 * 1.25 * 25);
        t.Add("copper", "poor", 4, 4 * 1.25 * 10);
        t.Add("copper", "rich", 2, 2 * 1.25 * 25);
        t.Add("coal", "-", 7, 0);
        Assert.Equal(23, t.Blocks);
        var rows = t.Rows();
        Assert.Equal(["coal -", "copper poor", "copper rich"], rows.Select(r => r.Metal + " " + r.Grade));
        Assert.Equal(12, rows[2].Blocks);
        var copper = t.ByMetal().Single(r => r.Metal == "copper");
        Assert.Equal(16, copper.Blocks);
        // 12 * 31.25 + 4 * 12.5 = 425 units: 4.25 ingots at 100 units an ingot.
        Assert.Equal(4.25, copper.Ingots, 6);
    }
}

public class SurveyCounterTests
{
    [Theory]
    [InlineData("ore-rich-nativecopper-granite", null, true)]
    [InlineData("saltpeterore-granite", null, true)]
    [InlineData("looseores-malachite-granite-free", null, true)]
    [InlineData("richgravel-granite", null, true)]
    [InlineData("crystal-x", "BlockOre", true)]
    [InlineData("rock-granite", "Block", false)]
    public void RecordsWhatTheToolRecords(string path, string? cls, bool recorded) =>
        Assert.Equal(recorded, SurveyCounter.Recorded(path, cls));

    [Fact]
    public void CountsAChunkAsTheToolDoes()
    {
        // Ids: 0 air, 1 rock, 2 ore.
        var counter = new SurveyCounter([false, false, true], originChunkX: 10, originChunkZ: 20);
        var heights = Enumerable.Repeat((ushort)100, 32 * 32).ToArray();
        // Chunk (11, 3, 20): rock everywhere but two ore blocks in one 8-block cell, at y 96+0 and 96+2.
        int Ore(int i) => i == Index(1, 0, 2) || i == Index(3, 2, 4) ? 2 : 1;
        counter.Count(11, 3, 20, Ore, heights);
        Assert.Equal(32 * 32 * 32, counter.BlocksCounted);
        Assert.Equal(1, counter.Cells);
        var blocks = counter.Blocks(id => (id == 1 ? "game:rock-granite" : "game:ore-rich-nativecopper-granite", null));
        Assert.Equal(2, blocks.Count);
        // Cell x is ((11 - 10) * 32 + 1) >> 3 = 4, z (0 + 2) >> 3 = 0, y 96 >> 3 = 12; depth 100 - 98 = 2 at best.
        Assert.Equal("game:ore-rich-nativecopper-granite,4,0,12,2,2\n", counter.CellsCsv(_ => "game:ore-rich-nativecopper-granite"));
    }

    private static int Index(int x, int y, int z) => (y * 32 + z) * 32 + x;
}
