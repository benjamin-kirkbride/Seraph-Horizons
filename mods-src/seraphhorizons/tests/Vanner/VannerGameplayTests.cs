using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Vanner.Core;
using Xunit;

namespace SeraphHorizons.Tests.Vanner;

/// <summary>
/// The vanner's gameplay (#923): its figures from ore processing (r 0.80, a steady load of 0.08, 15 L a rock,
/// 3 rock an hour at 0.6), its pass on fresh feed and at the end of the whole route, the whole-route
/// concentrate carried apart, its build file (the README's stage table, the belt a wearing part fitted as
/// stage 4) and its rig's anchors as the shared code reads them.
/// </summary>
public class VannerGameplayTests
{
    private static string Out(string path) => Path.Combine(AppContext.BaseDirectory, path);

    private static OreRecovery Recovery() => new(System.Text.Json.JsonSerializer.Deserialize<OreProcessingConfig>(
        File.ReadAllText(Out("ore-processing.json")),
        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true })!);

    private const OreState Full = OreState.Classified | OreState.Ground | OreState.Amalgamated;

    [Fact]
    public void Its_figures_are_ore_processings()
    {
        var recovery = Recovery();
        var spec = recovery.Machine(OreMachine.Vanner);
        Assert.Equal(0.80, spec.Recovery, 6);
        Assert.False(spec.LoadFollowsJob);
        Assert.Equal(0.08, recovery.LoadAt(OreMachine.Vanner, 0), 6);
        Assert.Equal(0.08, recovery.LoadAt(OreMachine.Vanner, 1), 6);
        Assert.Equal(15, spec.WaterPerItem, 6);
        Assert.Equal(3, spec.ItemsPerHour, 6);
        // an in-game hour at speed 0.6 vans 3, at either sign
        double turnsAnHour = OreRecovery.TurnsPerHourAtSpeed1 * OreRecovery.ReferenceSpeed;
        Assert.Equal(3, WorkTurns.None.Add(turnsAnHour * 2 * Math.PI, spec.TurnsPerItem).Items);
        Assert.Equal(MachineLoad.Unassembled, MachineLoad.Resistance(false, 0.08));
    }

    [Fact]
    public void On_fresh_feed_it_catches_its_figure_of_the_catchable_and_passes_the_rest_on()
    {
        var recovery = Recovery();
        var feed = new OreStream("hematite", "medium", 10, null, OreState.Classified, WorkedBy.None, "granite");
        var wash = Vanning.Wash(recovery, feed);
        var product = Assert.Single(wash.Products);
        Assert.Equal(OreProducts.ConcentrateCode("hematite"), product.Code);
        Assert.Equal(8, product.Units, 9);
        Assert.Equal(recovery.ConcentrateUnits, product.UnitsPerItem);
        Assert.False(wash.Whole);
        Assert.Null(wash.Middlings);
        Assert.Equal(2, wash.Tailings.Units, 9);
        Assert.True(wash.Tailings.Worked.HasFlag(WorkedBy.Vanner));
        Assert.Equal("granite", wash.Tailings.Rock);
        // its own tailings again: nothing more
        Assert.Empty(Vanning.Wash(recovery, wash.Tailings).Products);
    }

    [Fact]
    public void Unclassified_feed_keeps_its_locked_share_back()
    {
        var recovery = Recovery();
        var wash = Vanning.Wash(recovery, new OreStream("hematite", "medium", 10));
        Assert.Equal(10 * 0.85 * 0.8, Assert.Single(wash.Products).Units, 9);
    }

    [Fact]
    public void Last_on_an_oxides_whole_route_it_takes_all_that_is_left()
    {
        var recovery = Recovery();
        var s = new OreStream("hematite", "medium", 10, null, Full, WorkedBy.None, "granite");
        double caught = 0;
        foreach (var m in new[] { OreMachine.Sluice, OreMachine.Jig, OreMachine.Table })
        {
            var pass = ConcentratorWash.Wash(recovery, m, s);
            caught += pass.Products.Sum(p => p.Units);
            s = pass.Tailings;
        }
        Assert.True(recovery.OnWholeRoute(s));
        var wash = Vanning.Wash(recovery, s);
        Assert.False(wash.Whole);
        Assert.Equal(10, caught + wash.Products.Sum(p => p.Units), 9);
        Assert.Equal(0, wash.Tailings.Units, 9);
    }

    [Fact]
    public void On_a_sulfides_whole_route_it_marks_its_concentrate_whole_for_the_roaster()
    {
        var recovery = Recovery();
        var s = new OreStream("chalcopyrite", "medium", 10, null, Full, WorkedBy.Sluice | WorkedBy.Jig | WorkedBy.Table);
        var wash = Vanning.Wash(recovery, s);
        Assert.True(wash.Whole);
        Assert.Equal(10 * recovery.PassShare(recovery.Ore("chalcopyrite"), OreMachine.Vanner), Assert.Single(wash.Products).Units, 9);
    }

    [Fact]
    public void Whole_and_plain_concentrate_are_carried_apart()
    {
        Assert.Equal("seraphhorizons:concentrate-galena", Vanning.CarryKey("seraphhorizons:concentrate-galena", false));
        Assert.NotEqual(Vanning.CarryKey("seraphhorizons:concentrate-galena", false), Vanning.CarryKey("seraphhorizons:concentrate-galena", true));
        var carry = new UnitCarry();
        Assert.Equal(0, carry.Add(Vanning.CarryKey("c", true), 0.6, 1));
        Assert.Equal(0, carry.Add(Vanning.CarryKey("c", false), 0.6, 1));
    }

    [Fact]
    public void Its_build_is_the_readme_table_with_the_belt_a_wearing_part()
    {
        var build = MachineBuildJson.Parse(File.ReadAllText(Out("builds/vanner.json")));
        Assert.Equal("seraphhorizons:vanner-north", build.Block);
        Assert.Equal(FeedMode.Continuous, build.Feed);
        Assert.Equal(["countershaft", "shaker", "rollers", "belt", "services"], build.Stages.Select(s => s.Id));
        var belt = Assert.Single(build.Wearing);
        Assert.Equal(("belt", 2, 4000.0), (belt.Id, belt.Count, belt.Uses));
        // the belt stage is the belt's first set and nothing else
        var material = Assert.Single(build.Stages[3].Materials);
        Assert.Equal(("belt", 2), (material.Wearing, material.Quantity));

        var assembly = new MachineAssembly(build);
        Assert.Equal(AssemblyVerdict.OutOfOrder, assembly.Fit("game:leather-normal-plain", 2).Verdict);
        foreach (var (code, n) in new[] { ("game:rod-steel", 2), ("game:metalnailsandstrips-iron", 1), ("game:plank-oak", 1) })
            Assert.Equal(AssemblyVerdict.Fitted, assembly.Fit(code, n).Verdict);
        Assert.True(assembly.Shows("countershaft"));
        Assert.False(assembly.Shows("shaker"));
        while (assembly.Build.Next is < 3)
            assembly.FillNext();
        Assert.False(assembly.Shows("belt"));
        Assert.Equal(AssemblyVerdict.TooFew, assembly.Fit("game:leather-normal-plain", 1).Verdict);
        Assert.Equal(AssemblyVerdict.Fitted, assembly.Fit("game:leather-normal-plain", 2).Verdict);
        Assert.True(assembly.Shows("belt"));
        Assert.NotNull(assembly.Wearing.Fitted("belt"));

        // services: the water line in the metal put in
        Assert.Equal(AssemblyVerdict.Fitted, assembly.Fit("game:metalnailsandstrips-iron", 1).Verdict);
        Assert.Equal(AssemblyVerdict.Fitted, assembly.Fit("game:plank-pine", 2).Verdict);
        Assert.True(assembly.Shows("feedbox"));
        Assert.Equal(AssemblyVerdict.Fitted, assembly.Fit("game:chute-straight-ns", 3).Verdict);
        Assert.Equal(AssemblyVerdict.Fitted, assembly.Fit("seraphhorizons:pipesection-lead", 2).Verdict);
        Assert.True(assembly.Built);
        Assert.True(assembly.Ready);
        Assert.True(assembly.Shows("pipelead"));
        Assert.False(assembly.Shows("pipecopper"));

        // about 4,000 rock wear the belt out; two plain leather renew it
        Assert.Equal(AssemblyVerdict.NotWorn, assembly.Fit("game:leather-normal-plain", 2).Verdict);
        for (int i = 0; i < 4000; i++)
            assembly.Wearing.WearAll(1);
        Assert.False(assembly.Ready);
        Assert.Equal(AssemblyVerdict.Renewed, assembly.Fit("game:leather-normal-plain", 2).Verdict);
        Assert.True(assembly.Ready);
    }

    [Fact]
    public void A_stage_without_materials_needs_a_wearing_part_fitted_with_it()
    {
        const string stages = """ "stages": [ { "id": "a", "adds": ["a"], "materials": [ { "codes": ["game:x"], "quantity": 1 } ] }, { "id": "b", "adds": ["b"], "materials": [] } ] """;
        var ex = Assert.Throws<FormatException>(() => MachineBuildJson.Parse("{ \"machine\": \"m\"," + stages + "}"));
        Assert.Contains("no materials", ex.Message);
        var build = MachineBuildJson.Parse("{ \"machine\": \"m\"," + stages + ", \"wearing\": [ { \"id\": \"w\", \"codes\": [\"game:y\"], \"count\": 2, \"uses\": 5, \"stage\": \"b\" } ] }");
        Assert.Equal("w", Assert.Single(build.Stages[1].Materials).Wearing);
    }

    [Fact]
    public void Its_rig_reads_as_an_ore_machine_with_its_anchors()
    {
        var rig = OreMachineRig.Parse(File.ReadAllText(Out("rigs/vanner-rig.json")));
        Assert.Equal(6, rig.Cells.Count);
        Assert.Equal((new Int3(1, 0, 2), CellFace.South), (rig.Anchor("power")!.Value.Cell, rig.Anchor("power")!.Value.Face));
        Assert.Equal((new Int3(1, 0, 0), CellFace.Up), (rig.Anchor("water")!.Value.Cell, rig.Anchor("water")!.Value.Face));
        Assert.Equal((new Int3(0, 0, 1), CellFace.Up), (rig.Anchor("infeed")!.Value.Cell, rig.Anchor("infeed")!.Value.Face));
        Assert.Equal((new Int3(0, 0, 0), CellFace.North), (rig.Anchor("concentrate")!.Value.Cell, rig.Anchor("concentrate")!.Value.Face));
        Assert.Equal((new Int3(0, 0, 2), CellFace.South), (rig.Anchor("tailings")!.Value.Cell, rig.Anchor("tailings")!.Value.Face));
        Assert.Equal(Axis.Z, rig.ShaftAxis);
        Assert.Equal(["countershaft", "shaker", "rollers", "creep", "belt", "feedbox", "chutes", "pipecopper", "pipelead"], rig.StageRequires);
    }
}
