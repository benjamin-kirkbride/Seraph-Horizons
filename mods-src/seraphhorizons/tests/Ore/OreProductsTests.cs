using System.Text.Json;
using System.Text.Json.Nodes;
using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Tests.Ore;

/// <summary>Ore processing's items and rules (#686, #687, #688): the grade rules, the 5-unit
/// crushing rule, the smelting rates, and the item type files against the shipped figures.</summary>
public class OreProductsTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly OreRecovery R = new(JsonSerializer.Deserialize<OreProcessingConfig>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ore-processing.json")), Options)!);

    private static JsonObject Type(string file) => JsonNode.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "oreprocessing", file)),
        documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject();

    private static string[] OreStates(string file) =>
        Type(file)["variantgroups"]!.AsArray().Single(g => (string?)g!["code"] == "ore")!["states"]!.AsArray()
            .Select(s => (string)s!).ToArray();

    [Theory]
    [InlineData("poor", OreForm.Raw, 4, OreGrain.Fine)]
    [InlineData("medium", OreForm.Raw, 4, OreGrain.Coarse)]
    [InlineData("rich", OreForm.Chunk, 16, OreGrain.Coarse)]
    [InlineData("bountiful", OreForm.Chunk, 16, OreGrain.Coarse)]
    public void Grades(string grade, OreForm form, int stack, OreGrain grain)
    {
        Assert.Equal(form, OreProducts.FormOfGrade(grade));
        Assert.Equal(stack, OreProducts.StackOfGrade(grade));
        Assert.Equal(grain, OreProducts.GrainOfGrade(grade));
    }

    [Fact]
    public void Codes()
    {
        Assert.Equal("game:crushed-chromite-coarse", OreProducts.CrushedCode("chromite", OreGrain.Coarse));
        Assert.Equal("game:crushed-quartz_nativegold-fine", OreProducts.CrushedCode("quartz_nativegold", OreGrain.Fine));
        Assert.Equal("seraphhorizons:concentrate-galena", OreProducts.ConcentrateCode("galena"));
        Assert.Equal("seraphhorizons:roastedconcentrate-galena", OreProducts.RoastedCode("galena"));
        Assert.Equal("nativegold", OreProducts.NuggetOf("quartz_nativegold"));
        Assert.Equal("galena", OreProducts.NuggetOf("galena_nativesilver"));
        Assert.Equal("quartz_nativegold", OreProducts.OreOfNugget("nativegold"));
        Assert.Equal("hematite", OreProducts.OreOfNugget("hematite"));
        Assert.Null(OreProducts.OreOfNugget("wolframite"));
    }

    // Crushing loses nothing: a medium chromite raw ore (20 units) gives 4, a nugget 1, every grade of
    // ore-graded.json's metalUnitsByType a whole number.
    [Theory]
    [InlineData(20, 4)]
    [InlineData(5, 1)]
    [InlineData(15, 3)]
    [InlineData(35, 7)]
    [InlineData(40, 8)]
    [InlineData(10, 2)]
    public void CrushingIsUnitsOverFive(double units, int crushed) =>
        Assert.Equal(crushed, OreProducts.CrushedCount(units, R.ConcentrateUnits));

    // A chunk smelts at half, exactly: a 25-unit chunk 1 ingot per 8, a 35-unit one 7 per 40.
    [Theory]
    [InlineData(25, 0.5, 1, 8)]
    [InlineData(35, 0.5, 7, 40)]
    [InlineData(30, 0.5, 3, 20)]
    [InlineData(15, 0.5, 3, 40)]
    [InlineData(40, 0.5, 1, 5)]
    [InlineData(5, 0.5, 1, 40)]
    [InlineData(5, 1, 1, 20)]
    public void RatesAreExact(double units, double share, int output, int ratio)
    {
        var rate = OreProducts.Rate(units, share);
        Assert.Equal(new SmeltRate(output, ratio), rate);
        Assert.Equal(units * share, rate!.Value.UnitsPerItem, 9);
    }

    [Fact]
    public void NothingSmeltsAtZero() => Assert.Null(OreProducts.Rate(20, 0));

    [Fact]
    public void LithargeLosesALittle() => Assert.InRange(OreProducts.LithargeRate.UnitsPerItem / 5, 0.9, 0.99);

    // The type files list every ore, the roasted concentrate the sulfides, the amalgam and sponge the free metals.
    [Fact]
    public void TypeFilesListTheFiguresOres()
    {
        Assert.Equal(OreProducts.Ores, OreStates("crushedore.json"));
        Assert.Equal(OreProducts.Ores, OreStates("groundore.json"));
        Assert.Equal(OreProducts.Ores, OreStates("concentrate.json"));
        Assert.Equal(OreProducts.Ores.Where(o => OreProducts.Roasts(R.Ore(o))), OreStates("roastedconcentrate.json"));
        Assert.Equal(OreProducts.Ores.Where(o => OreProducts.Amalgamates(R.Ore(o))), OreStates("amalgam.json"));
        Assert.Equal(OreProducts.Ores.Where(o => OreProducts.Amalgamates(R.Ore(o))), OreStates("sponge.json"));
        foreach (var ore in OreProducts.Ores)
            Assert.True(R.Ores.Any(s => s.Ore == ore), $"{ore} is not in ore-processing.json");
    }

    [Fact]
    public void TypeFilesHoldConcentrateUnitsAndStack()
    {
        foreach (var (file, stack) in new[] { ("crushedore.json", 16), ("groundore.json", 16), ("concentrate.json", 128),
                     ("roastedconcentrate.json", 128), ("amalgam.json", 128), ("sponge.json", 128), ("litharge.json", 64) })
        {
            var type = Type(file);
            Assert.Equal(R.ConcentrateUnits, (double)type["attributes"]!["metalUnits"]!);
            Assert.Equal(stack, (int)type["maxstacksize"]!);
        }
    }

    [Fact]
    public void GalenaNativesilverIsASulfide() => Assert.True(R.Ore("galena_nativesilver").IsSulfide);
}
