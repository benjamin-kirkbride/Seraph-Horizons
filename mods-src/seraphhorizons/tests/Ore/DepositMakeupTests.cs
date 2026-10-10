using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Tests.Ore;

/// <summary>#692: a deposit's makeup (ores, grades, host rock) as maps name it.</summary>
public class DepositMakeupTests
{
    private static OreTally Galena()
    {
        var t = new OreTally();
        // Lead ore: mostly poor galena, some cerussite, a speck of wulfenite; mostly in limestone.
        t.Add(new OreBlockKind("lead", "galena", "poor", "limestone"), 80, 80 * 1.25 * 15);
        t.Add(new OreBlockKind("lead", "galena", "medium", "limestone"), 30, 30 * 1.25 * 20);
        t.Add(new OreBlockKind("lead", "cerussite", "poor", "shale"), 30, 30 * 1.25 * 15);
        t.Add(new OreBlockKind("lead", "wulfenite", "poor", "limestone"), 2, 2 * 1.25 * 15);
        // Another metal's ore in the same columns is left out.
        t.Add(new OreBlockKind("zinc", "sphalerite", "rich", "granite"), 500, 500 * 1.25 * 25);
        return t;
    }

    [Fact]
    public void NamesTheOresHoldingATenthOfTheMetalRichestFirst()
    {
        var m = Galena().Makeup("lead");
        Assert.Equal(["galena", "cerussite"], m.MainOres());
        Assert.Equal("limestone", m.HostRock());
        Assert.Equal(new GradeMix(GradeMix.Mostly, ["poor"]), m.Mix());
        Assert.Equal(112, m.Grades["poor"]);
        Assert.DoesNotContain("sphalerite", m.Ores.Keys);
    }

    [Fact]
    public void TheRichestOreIsNamedEvenAlongsideManyAndAtMostThree()
    {
        var m = new DepositMakeup { Ores = new() { ["a"] = 30, ["b"] = 29, ["c"] = 28, ["d"] = 27 } };
        Assert.Equal(["a", "b", "c"], m.MainOres());
        Assert.Equal(["a"], new DepositMakeup { Ores = new() { ["a"] = 1 } }.MainOres());
        Assert.Empty(new DepositMakeup().MainOres());
        Assert.Null(new DepositMakeup().HostRock());
        Assert.Null(new DepositMakeup().Mix());
    }

    [Theory]
    [InlineData(95, 5, 0, "only:poor")]
    [InlineData(65, 35, 0, "mostly:poor")]
    [InlineData(40, 50, 10, "mixed:poor,medium")]
    [InlineData(10, 40, 50, "mixed:medium,rich")]
    [InlineData(0, 0, 100, "only:rich")]
    public void SaysTheGradesInWords(long poor, long medium, long rich, string code)
    {
        var mix = GradeMix.Of(new Dictionary<string, long> { ["poor"] = poor, ["medium"] = medium, ["rich"] = rich });
        Assert.Equal(code, mix!.Code);
        Assert.Equal(mix, GradeMix.Parse(code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("mostly")]
    [InlineData("mostly:poor,rich")]
    [InlineData("mixed:poor")]
    [InlineData("only:shiny")]
    [InlineData("some:poor")]
    public void ACodeThatDoesNotReadIsNoMix(string? code) => Assert.Null(GradeMix.Parse(code));

    [Fact]
    public void UngradedOreHasNoMix() =>
        Assert.Null(GradeMix.Of(new Dictionary<string, long> { ["-"] = 40 }));

    [Fact]
    public void ListsReadBackAsWritten()
    {
        Assert.Equal("galena,cerussite", OreNames.Csv(["galena", "cerussite"]));
        Assert.Equal(["galena", "cerussite"], OreNames.Split("galena, cerussite"));
        Assert.Empty(OreNames.Split(null));
        Assert.Equal("orename-quartz_nativegold", OreNames.LangKey("quartz_nativegold"));
    }

    [Fact]
    public void EveryOreOfAMetalWithMapsHasANameAndEveryGradeAndListWord()
    {
        var lang = JsonNode.Parse(File.ReadAllText(Path.Combine(ModDir(), "assets", "seraphhorizons", "lang", "en.json")))!.AsObject();
        var missing = new List<string>();
        foreach (string metal in OreMetals.All.Where(m => !OreMetals.NonMetals.Contains(m)))
            foreach (string ore in OreMetals.OresOf(metal))
                if (!lang.ContainsKey(OreNames.LangKey(ore))) missing.Add(OreNames.LangKey(ore));
        foreach (string grade in OreTally.Grades)
            if (!lang.ContainsKey("ore-grade-" + grade)) missing.Add("ore-grade-" + grade);
        foreach (string key in new[] { "ore-grades-only", "ore-grades-mostly", "ore-grades-mixed", "ore-list-and", "ore-list-comma" })
            if (!lang.ContainsKey(key)) missing.Add(key);
        foreach (var (key, _) in lang.Where(kv => kv.Key.StartsWith("oremap-metal-", StringComparison.Ordinal)).ToList())
            if (!lang.ContainsKey("ore-metal-" + key["oremap-metal-".Length..])) missing.Add("ore-metal-" + key["oremap-metal-".Length..]);
        Assert.True(missing.Count == 0, "Missing: " + string.Join(", ", missing));
    }

    private static string ModDir([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
