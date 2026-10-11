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

    // The pack's units (#863), which patches/oreprocessing-ore.json sets on vanilla's item (the pack
    // tests hold it to the game): about a quarter of vanilla's.
    [Theory]
    [InlineData("galena", new[] { 3, 5, 7, 10 })]
    [InlineData("cassiterite", new[] { 1, 3, 4, 6 })]
    [InlineData("hematite", new[] { 4, 6, 8, 11 })]
    [InlineData("quartz_nativesilver", new[] { 2, 4, 6, 7 })]
    [InlineData("quartz_nativegold", new[] { 1, 3, 4, 6 })]
    public void RawUnitsByGrade(string ore, int[] units)
    {
        Assert.Equal(units, OreProducts.Grades.Select(g => OreProducts.RawUnits(ore, g)!.Value));
        Assert.Null(OreProducts.RawUnits(ore, "lignite"));
    }

    // Vanilla's exceptions (ore-graded.json's metalUnitsByType) keep their ratio to its default,
    // rounded half up: the table above is made from them. A slot of 16 holds within a quarter of what
    // a slot of 4 did at vanilla's units.
    [Theory]
    [InlineData("galena", new[] { 15, 20, 25, 35 })]
    [InlineData("cassiterite", new[] { 5, 10, 15, 20 })]
    [InlineData("hematite", new[] { 20, 25, 30, 40 })]
    [InlineData("quartz_nativesilver", new[] { 10, 15, 20, 25 })]
    [InlineData("quartz_nativegold", new[] { 5, 10, 15, 20 })]
    public void RawUnitsAreVanillasQuartered(string ore, int[] vanilla)
    {
        for (int i = 0; i < 4; i++)
        {
            int units = OreProducts.RawUnits(ore, OreProducts.Grades[i])!.Value;
            Assert.Equal(units, OreProducts.Quartered(vanilla[i], OreProducts.Grades[i]));
            Assert.InRange(OreProducts.RockStack * units / (4.0 * vanilla[i]), 0.75, 1.25);
        }
        Assert.Equal(1, OreProducts.Quartered(1, "poor"));
    }

    // A slot of default rock holds 48 / 80 / 112 / 160 units (4 used to hold 60 / 80 / 100 / 140).
    [Fact]
    public void ASlotOfRock()
    {
        Assert.Equal(16, OreProducts.RockStack);
        Assert.Equal([48, 80, 112, 160], OreProducts.Grades.Select(g => OreProducts.RockStack * OreProducts.RawUnits("galena", g)!.Value));
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
    [InlineData(5, 5, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(1, 1, 1)]
    [InlineData(20, 10, 2)]
    [InlineData(2, 1, 2)]
    public void CrushingIsOneForOne(double units, double perCrushed, int crushed) =>
        Assert.Equal(crushed, OreProducts.CrushedCount(units, perCrushed));

    // Rock smelts at its grade's share, exactly: a 7-unit rich ore at 40 % 7 ingots per 250.
    [Theory]
    [InlineData(7, 0.40, 7, 250)]
    [InlineData(3, 0.10, 3, 1000)]
    [InlineData(5, 0.25, 1, 80)]
    [InlineData(10, 0.50, 1, 20)]
    [InlineData(11, 0.50, 11, 200)]
    [InlineData(1, 0.10, 1, 1000)]
    [InlineData(10, 0.425, 17, 400)]
    [InlineData(3, 0.085, 51, 20000)]
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
    // wins as in the game), 16 a stack, a grade variant each.
    [Fact]
    public void RockTypeFilesHoldRawUnitsAndStackSixteen()
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
