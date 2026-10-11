using System.Text.Json;
using System.Text.Json.Nodes;
using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Tests.Ore;

/// <summary>Ore processing's items and rules (#686, #687, #688, #847): the grade rules, one-for-one
/// crushing, the smelting rates, and the item type files against the shipped figures.</summary>
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
    [InlineData("poor", true)]
    [InlineData("medium", true)]
    [InlineData("rich", true)]
    [InlineData("bountiful", true)]
    [InlineData("lignite", false)]
    [InlineData(null, false)]
    public void Grades(string? grade, bool isGrade) => Assert.Equal(isGrade, OreProducts.IsGrade(grade));

    // Vanilla's ore-graded.json metalUnitsByType (the pack tests hold it to the game).
    [Theory]
    [InlineData("galena", new[] { 15, 20, 25, 35 })]
    [InlineData("cassiterite", new[] { 5, 10, 15, 20 })]
    [InlineData("hematite", new[] { 20, 25, 30, 40 })]
    [InlineData("quartz_nativesilver", new[] { 10, 15, 20, 25 })]
    [InlineData("quartz_nativegold", new[] { 5, 10, 15, 20 })]
    public void RawUnitsByGrade(string ore, int[] units)
    {
        Assert.Equal(units, OreProducts.Grades.Select(g => OreProducts.RawUnits(ore, g)!.Value));
        Assert.Null(OreProducts.RawUnits(ore, "lignite"));
    }

    [Fact]
    public void Codes()
    {
        Assert.Equal("seraphhorizons:crushedore-chromite-medium", OreProducts.CrushedCode("chromite", "medium"));
        Assert.Equal("seraphhorizons:groundore-quartz_nativegold-poor", OreProducts.GroundCode("quartz_nativegold", "poor"));
        Assert.Equal("seraphhorizons:roastedore-galena-rich", OreProducts.RoastedRockCode("galena", "rich"));
        Assert.Equal("seraphhorizons:concentrate-galena", OreProducts.ConcentrateCode("galena"));
        Assert.Equal("seraphhorizons:roastedconcentrate-galena", OreProducts.RoastedCode("galena"));
        Assert.Equal("nativegold", OreProducts.NuggetOf("quartz_nativegold"));
        Assert.Equal("galena", OreProducts.NuggetOf("galena_nativesilver"));
    }

    // Crushing is one for one (#847): a raw ore into one crushed ore holding its units, crystallised
    // ore (twice the units) into two.
    [Theory]
    [InlineData(20, 20, 1)]
    [InlineData(35, 35, 1)]
    [InlineData(70, 35, 2)]
    [InlineData(40, 20, 2)]
    public void CrushingIsOneForOne(double units, double perCrushed, int crushed) =>
        Assert.Equal(crushed, OreProducts.CrushedCount(units, perCrushed));

    // Rock smelts at its grade's share, exactly: a 35-unit bountiful ore at 55 % 77 ingots per 400.
    [Theory]
    [InlineData(35, 0.55, 77, 400)]
    [InlineData(15, 0.10, 3, 200)]
    [InlineData(20, 0.25, 1, 20)]
    [InlineData(25, 0.40, 1, 10)]
    [InlineData(40, 0.55, 11, 50)]
    [InlineData(5, 1, 1, 20)]
    public void RatesAreExact(double units, double share, int output, int ratio)
    {
        var rate = OreProducts.Rate(units, share);
        Assert.Equal(new SmeltRate(output, ratio), rate);
        Assert.Equal(units * share, rate!.Value.UnitsPerItem, 9);
    }

    [Fact]
    public void NothingSmeltsAtZero() => Assert.Null(OreProducts.Rate(20, 0));

    // Every share of every grade's units, roasted rock's too, has an exact rate.
    [Fact]
    public void EveryRockShareIsExact()
    {
        foreach (var ore in OreProducts.Ores)
            foreach (var grade in OreProducts.Grades)
            {
                int units = OreProducts.RawUnits(ore, grade)!.Value;
                var spec = R.Ore(ore);
                foreach (var form in new[] { OreForm.Raw, OreForm.RoastedRock })
                    foreach (int times in new[] { 1, 2 })
                    {
                        double share = R.SmeltShare(spec, form, grade);
                        if (share == 0) continue;
                        var rate = OreProducts.Rate(units * times, share)!.Value;
                        Assert.Equal(units * times * share, rate.UnitsPerItem, 9);
                    }
            }
    }

    [Fact]
    public void LithargeLosesALittle() => Assert.InRange(OreProducts.LithargeRate.UnitsPerItem / 5, 0.9, 0.99);

    // The type files list every ore, the roasted concentrate the sulfides, the amalgam and sponge the free metals.
    [Fact]
    public void TypeFilesListTheFiguresOres()
    {
        Assert.Equal(OreProducts.Ores, OreStates("crushedore.json"));
        Assert.Equal(OreProducts.Ores, OreStates("groundore.json"));
        Assert.Equal(OreProducts.Ores.Where(o => OreProducts.Roasts(R.Ore(o))), OreStates("roastedore.json"));
        Assert.Equal(OreProducts.Ores, OreStates("concentrate.json"));
        Assert.Equal(OreProducts.Ores.Where(o => OreProducts.Roasts(R.Ore(o))), OreStates("roastedconcentrate.json"));
        Assert.Equal(OreProducts.Ores.Where(o => OreProducts.Amalgamates(R.Ore(o))), OreStates("amalgam.json"));
        Assert.Equal(OreProducts.Ores.Where(o => OreProducts.Amalgamates(R.Ore(o))), OreStates("sponge.json"));
        foreach (var ore in OreProducts.Ores)
            Assert.True(R.Ores.Any(s => s.Ore == ore), $"{ore} is not in ore-processing.json");
    }

    // The rock forms hold the raw ore's units, by ore and grade (their metalUnitsByType, first match
    // wins as in the game), 4 a stack, a grade variant each.
    [Fact]
    public void RockTypeFilesHoldRawUnitsAndStackFour()
    {
        foreach (var file in new[] { "crushedore.json", "groundore.json", "roastedore.json" })
        {
            var type = Type(file);
            string code = (string)type["code"]!;
            Assert.Equal(OreProducts.RockStack, (int)type["maxstacksize"]!);
            Assert.Equal(OreProducts.Grades, type["variantgroups"]!.AsArray().Single(g => (string?)g!["code"] == "grade")!["states"]!
                .AsArray().Select(s => (string)s!));
            var byType = type["attributes"]!["metalUnitsByType"]!.AsObject();
            foreach (var ore in OreStates(file))
                foreach (var grade in OreProducts.Grades)
                {
                    string path = $"{code}-{ore}-{grade}";
                    var match = byType.First(kv => System.Text.RegularExpressions.Regex.IsMatch(path,
                        "^" + System.Text.RegularExpressions.Regex.Escape(kv.Key).Replace("\\*", ".*") + "$"));
                    Assert.Equal(OreProducts.RawUnits(ore, grade), (int)match.Value!);
                }
        }
    }

    [Fact]
    public void TypeFilesHoldConcentrateUnitsAndStack()
    {
        foreach (var (file, stack) in new[] { ("concentrate.json", 128),
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
