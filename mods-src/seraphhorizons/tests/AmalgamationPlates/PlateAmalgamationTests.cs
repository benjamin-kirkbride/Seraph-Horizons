using System.Text.Json;
using SeraphHorizons.Mod.AmalgamationPlates.Core;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Tests.AmalgamationPlates;

/// <summary>The amalgamation plates (#917) on the shipped <c>config/ore-processing.json</c>: free gold and silver
/// caught as amalgam, the rest on amalgamated, the mercury it takes off the dressing, and the plates' limits.</summary>
public class PlateAmalgamationTests
{
    private static readonly OreRecovery R = new(JsonSerializer.Deserialize<OreProcessingConfig>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ore-processing.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!);

    private static readonly OreMachine[] Concentrators = [OreMachine.Sluice, OreMachine.Jig, OreMachine.Table, OreMachine.Vanner];

    private static string Out(string name) => Path.Combine(AppContext.BaseDirectory, name);

    private static double Line(OreStream s)
    {
        double caught = 0;
        foreach (var m in Concentrators)
        {
            var pass = R.Concentrate(m, s);
            caught += pass.Concentrate;
            s = pass.Tailings;
        }
        return caught;
    }

    private const double Plenty = 1e6;

    [Fact]
    public void ItsFiguresAreTheIssues()
    {
        var spec = R.Machine(OreMachine.AmalgamationPlates);
        Assert.Equal(0.65, spec.Recovery, 9);
        Assert.Equal(5, spec.WaterPerItem, 9);
        Assert.Equal(8, spec.ItemsPerHour, 9);
        Assert.False(spec.Batch);
        Assert.Equal(0, spec.Load, 9);
        // an amalgam item's mercury: 10 portions for 5 units
        Assert.Equal(2, PlateAmalgamation.MercuryPerUnit(R), 9);
    }

    [Fact]
    public void GoldQuartzOverThePlatesIsCaughtAndTheRestGoesOnAmalgamated()
    {
        // from the stamp battery and ball mill: classified and ground, so only the free metal's × 0.7 is locked
        var feed = OreStream.FromRock("quartz_nativegold", "rich", OreForm.Ground, OreState.Classified, units: 1, rock: "granite");
        var pass = PlateAmalgamation.Pass(R, feed, 1, Plenty, 0, Plenty);
        Assert.Equal(PlateVerdict.Caught, pass.Verdict);
        Assert.Equal("seraphhorizons:amalgam-quartz_nativegold", pass.AmalgamCode);
        Assert.Equal(0.65, pass.AmalgamUnits, 9);
        Assert.Equal(1.3, pass.MercurySpent, 9);
        Assert.True(pass.Rest.State.HasFlag(OreState.Amalgamated));
        Assert.Equal(0.35, pass.Rest.Units, 9);
        Assert.Equal("granite", pass.Rest.Rock);
        // #917: a full line catches 69.2 % without the plates, 99.6 % with them
        double without = Line(feed);
        double with = pass.AmalgamUnits + Line(pass.Rest);
        Assert.Equal(0.692, without, 3);
        Assert.Equal(0.996, with, 3);
    }

    [Fact]
    public void AStackIsCaughtWhole()
    {
        var feed = OreStream.FromRock("quartz_nativesilver", "medium", OreForm.Crushed, units: 4);
        var one = PlateAmalgamation.Pass(R, feed, 1, Plenty, 0, Plenty);
        var four = PlateAmalgamation.Pass(R, feed, 4, Plenty, 0, Plenty);
        Assert.Equal(one.AmalgamUnits * 4, four.AmalgamUnits, 9);
        Assert.Equal(one.Rest, four.Rest);
        Assert.Equal("seraphhorizons:amalgam-quartz_nativesilver", four.AmalgamCode);
    }

    [Fact]
    public void OtherOresAndAmalgamatedOrePassUnchanged()
    {
        var galena = OreStream.FromRock("galena", "rich", OreForm.Crushed);
        var p = PlateAmalgamation.Pass(R, galena, 1, Plenty, 0, Plenty);
        Assert.Equal(PlateVerdict.NotFree, p.Verdict);
        Assert.Same(galena, p.Rest);
        Assert.Equal(0, p.AmalgamUnits);

        var panned = OreStream.FromRock("quartz_nativegold", "rich", OreForm.Crushed, OreState.Amalgamated);
        var q = PlateAmalgamation.Pass(R, panned, 1, Plenty, 0, Plenty);
        Assert.Equal(PlateVerdict.AlreadyAmalgamated, q.Verdict);
        Assert.Same(panned, q.Rest);
    }

    [Fact]
    public void UndressedOrFullPlatesLetTheGoldBy()
    {
        var feed = OreStream.FromRock("quartz_nativegold", "bountiful", OreForm.Crushed);
        var dry = PlateAmalgamation.Pass(R, feed, 1, 0, 0, Plenty);
        Assert.Equal(PlateVerdict.Undressed, dry.Verdict);
        Assert.Same(feed, dry.Rest);
        var caught = PlateAmalgamation.Pass(R, feed, 1, Plenty, 0, Plenty);
        var low = PlateAmalgamation.Pass(R, feed, 1, caught.MercurySpent * 0.5, 0, Plenty);
        Assert.Equal(PlateVerdict.Undressed, low.Verdict);
        var full = PlateAmalgamation.Pass(R, feed, 1, Plenty, 60, 60);
        Assert.Equal(PlateVerdict.Full, full.Verdict);
        Assert.Same(feed, full.Rest);
        // just enough mercury and room
        var exact = PlateAmalgamation.Pass(R, feed, 1, caught.MercurySpent, 60 - caught.AmalgamUnits, 60);
        Assert.Equal(PlateVerdict.Caught, exact.Verdict);
    }

    [Theory]
    [InlineData(OreForm.Raw)]
    [InlineData(OreForm.RoastedRock)]
    [InlineData(OreForm.Concentrate)]
    [InlineData(null)]
    public void ItTakesOnlyCrushedAndGroundOre(OreForm? form)
    {
        var s = OreStream.FromRock("quartz_nativegold", "rich", OreForm.Crushed);
        Assert.False(PlateAmalgamation.Takes(form, s));
        Assert.True(PlateAmalgamation.Takes(OreForm.Crushed, s));
        Assert.True(PlateAmalgamation.Takes(OreForm.Ground, s));
        Assert.False(PlateAmalgamation.Takes(OreForm.Crushed, null));
        Assert.False(PlateAmalgamation.Takes(OreForm.Crushed, s with { Grade = "-" }));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.1, 1)]
    [InlineData(20, 1)]
    [InlineData(20.5, 2)]
    [InlineData(40, 2)]
    [InlineData(41, 3)]
    [InlineData(60, 3)]
    public void TheAmalgamIsDrawnInThirds(double held, int state) =>
        Assert.Equal(state, PlateAmalgamation.AmalgamState(held, 60));

    [Fact]
    public void ScrapingTakesTheWholeItemsAndLeavesTheRest()
    {
        Assert.Equal((2, 1.5), PlateAmalgamation.Scrape(11.5, 5));
        Assert.Equal((0, 4.9), PlateAmalgamation.Scrape(4.9, 5));
        Assert.Equal((3, 0), PlateAmalgamation.Scrape(15 - 1e-12, 5));
    }

    [Fact]
    public void TheSettingsDefaultsAndSanitising()
    {
        var c = new AmalgamationPlatesConfig();
        Assert.Equal(100, PlateAmalgamation.DressingPortions(c), 9);
        Assert.Equal(60, PlateAmalgamation.CapacityUnits(R, c), 9);
        Assert.Empty(c.Sanitise());
        var bad = new AmalgamationPlatesConfig { DressingLitres = double.NaN, AmalgamItems = 0 };
        Assert.Equal(2, bad.Sanitise().Count);
        Assert.Equal(1, bad.DressingLitres);
        Assert.Equal(12, bad.AmalgamItems);
    }

    [Fact]
    public void TheBuildIsTheReadmesStages()
    {
        var build = MachineBuildJson.Parse(File.ReadAllText(Out("builds/amalgamationplates.json")));
        Assert.Equal(["plates", "trap", "services"], build.Stages.Select(s => s.Id));
        Assert.Equal(FeedMode.Continuous, build.Feed);
        Assert.Empty(build.Wearing);
        var plates = build.Stages[0].Materials.Single();
        Assert.Equal(3, plates.Quantity);
        Assert.True(plates.Matches("seraphhorizons:halfplate-copper"));
        // the dressing and the amalgam are the machine's own, drawn by its state: in no stage
        Assert.DoesNotContain(build.Stages.SelectMany(s => s.Adds), a => a is "dressing" or "amalgam1" or "amalgam2" or "amalgam3");
        var rig = OreMachineRig.Parse(File.ReadAllText(Out("rigs/amalgamationplates-rig.json")), ["dressing", "amalgam1", "amalgam2", "amalgam3"]);
        Assert.Null(rig.Anchor(OreMachineRig.Power));
        Assert.NotNull(rig.Anchor(OreMachineRig.Infeed));
        Assert.NotNull(rig.Anchor(OreMachineRig.Water));
        Assert.NotNull(rig.Anchor("output"));
    }

    [Fact]
    public void ItsTailingsAreSandALayerARock()
    {
        var rule = OreMachinesConfig.Defaults.TailingsFor("amalgamationplates")!;
        Assert.Equal(TailingsForm.Sand, rule.Form);
        Assert.Equal(1, rule.LayersPerRock, 9);
    }

    [Fact]
    public void WorkHoursCountItemsByTheHour()
    {
        var (h, n) = WorkHours.None.Add(0.25, 8);
        Assert.Equal(2, n);
        Assert.Equal(0, h.Items, 9);
        (h, n) = h.Add(0.1, 8);
        Assert.Equal(0, n);
        Assert.Equal(0.8, h.Progress, 9);
    }
}
