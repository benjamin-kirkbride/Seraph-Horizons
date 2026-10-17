using SeraphHorizons.Mod.Jig.Core;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;
using Xunit;

namespace SeraphHorizons.Tests.Jig;

/// <summary>
/// The jig's gameplay (#919): its figures from ore processing (r 0.60, 8 rock an hour at 0.6, a steady
/// 0.1, 70 L a rock), its pass on a rock and on the sluice's tailings (the chain), the contents it draws,
/// and its build file (the README's stage table, read against its rig).
/// </summary>
public class JigGameplayTests
{
    private static string Out(string path) => Path.Combine(AppContext.BaseDirectory, path);

    private static OreRecovery Recovery() => new(System.Text.Json.JsonSerializer.Deserialize<OreProcessingConfig>(
        File.ReadAllText(Out("ore-processing.json")),
        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true })!);

    [Fact]
    public void Its_figures_are_the_issues()
    {
        var recovery = Recovery();
        var spec = recovery.Machine(OreMachine.Jig);
        Assert.Equal(0.60, spec.Recovery, 6);
        Assert.Equal(8, spec.ItemsPerHour, 6);
        Assert.Equal(70, spec.WaterPerItem, 6);
        Assert.False(spec.LoadFollowsJob);
        // steady: the working load once built, empty or full
        Assert.Equal(0.1, recovery.LoadAt(OreMachine.Jig, 0), 6);
        Assert.Equal(0.1, recovery.LoadAt(OreMachine.Jig, 1), 6);
        Assert.Equal(MachineLoad.Unassembled, MachineLoad.Resistance(false, 0.1));
        // 8 rock in an in-game hour at speed 0.6, the same turns a rock at any speed
        double turnsAnHour = OreRecovery.TurnsPerHourAtSpeed1 * OreRecovery.ReferenceSpeed;
        var (_, rocks) = WorkTurns.None.Add(turnsAnHour * 2 * Math.PI, spec.TurnsPerItem);
        Assert.Equal(8, rocks);
        // its reservoir holds at least a rock's water
        Assert.True(MachineReservoir.Capacity(new OreMachinesConfig(), spec.WaterPerItem) >= 70);
    }

    [Fact]
    public void A_rock_gives_r_of_what_can_be_caught_and_the_rest_goes_on()
    {
        var recovery = Recovery();
        var feed = OreStream.FromRock("cassiterite", "rich", OreForm.Crushed, rock: "granite");
        var wash = Jigging.Wash(recovery, feed);
        var pass = recovery.Concentrate(OreMachine.Jig, feed);
        var product = Assert.Single(wash.Products);
        Assert.Equal(OreProducts.ConcentrateCode("cassiterite"), product.Code);
        Assert.Equal(pass.Concentrate, product.Units, 9);
        Assert.Equal(recovery.ConcentrateUnits, product.UnitsPerItem);
        double catchable = feed.Units - recovery.LockedOf(feed);
        Assert.Equal(catchable * recovery.PassShare(recovery.Ore("cassiterite"), OreMachine.Jig), product.Units, 9);
        // nothing lost: the tailings carry the rest, worked by the jig, in the same rock
        Assert.Equal(feed.Units, product.Units + wash.Tailings.Units, 9);
        Assert.True(wash.Tailings.Worked.HasFlag(WorkedBy.Jig));
        Assert.Equal("granite", wash.Tailings.Rock);
        Assert.Null(wash.Middlings);
    }

    [Fact]
    public void It_works_the_sluices_tailings_and_not_its_own()
    {
        var recovery = Recovery();
        var feed = OreStream.FromRock("cassiterite", "rich", OreForm.Crushed, rock: "granite");
        var sluice = ConcentratorWash.Wash(recovery, OreMachine.Sluice, feed);
        var jig = Jigging.Wash(recovery, sluice.Tailings);
        double caught = Assert.Single(jig.Products).Units;
        double catchable = sluice.Tailings.Units - recovery.LockedOf(sluice.Tailings);
        Assert.Equal(catchable * recovery.PassShare(recovery.Ore("cassiterite"), OreMachine.Jig), caught, 9);
        Assert.Equal(WorkedBy.Sluice | WorkedBy.Jig, jig.Tailings.Worked);
        // the locked share is the sluice's split, kept: a second concentrator does not split again
        Assert.Equal(sluice.Tailings.Locked, jig.Tailings.Locked);
        // its own tailings again give nothing
        Assert.Empty(Jigging.Wash(recovery, jig.Tailings).Products);
    }

    [Fact]
    public void Placer_gravel_washes_to_the_pans_table_scaled()
    {
        var recovery = Recovery();
        var feed = PlacerLine.Gravel("granite");
        var wash = Jigging.Wash(recovery, feed, () => [("game:nugget-nativegold", 0.1, true)]);
        Assert.NotEmpty(wash.Products);
        Assert.True(wash.Tailings.Worked.HasFlag(WorkedBy.Jig));
    }

    [Theory]
    [InlineData("water", false, true, true, false)]
    [InlineData("water", true, false, false, false)]
    [InlineData("water", true, true, false, true)]
    [InlineData("bed", true, true, false, false)]
    [InlineData("bed", true, false, true, false)]
    [InlineData("bed", true, true, true, true)]
    public void It_draws_its_water_while_it_holds_some_and_its_bed_while_it_works(string requires, bool built, bool water, bool working, bool shown) =>
        Assert.Equal(shown, Jigging.ShowsContent(requires, built, water, working));

    [Fact]
    public void A_part_that_is_not_content_is_the_builds()
    {
        Assert.Null(Jigging.ShowsContent("sieve", true, true, true));
        Assert.Null(Jigging.ShowsContent(null, true, true, true));
    }

    [Fact]
    public void Its_build_is_the_readme_table_and_its_rigs_stages()
    {
        var build = MachineBuildJson.Parse(File.ReadAllText(Out("builds/jig.json")));
        Assert.Equal("seraphhorizons:jig-north", build.Block);
        Assert.Equal(FeedMode.Continuous, build.Feed);
        Assert.Equal(["timber", "sieve", "drive", "services"], build.Stages.Select(s => s.Id));
        Assert.Empty(build.Wearing);
        var rig = OreMachineRig.Parse(File.ReadAllText(Out("jig-rig.json")), Jigging.ContentRequires);
        Assert.Equal(rig.StageRequires, build.Stages.SelectMany(s => s.Adds));

        var assembly = new MachineAssembly(build);
        Assert.Equal(AssemblyVerdict.OutOfOrder, assembly.Fit("game:metalchain-iron", 1).Verdict);
        // the timber: 17 planks and 8 iron nails and strips, a stack a right-click
        Assert.Equal(AssemblyVerdict.Fitted, assembly.Fit("game:plank-oak", 17).Verdict);
        Assert.Equal(AssemblyVerdict.Fitted, assembly.Fit("game:metalnailsandstrips-iron", 8).Verdict);
        Assert.True(assembly.Shows("timber"));
        Assert.False(assembly.Shows("sieve"));
        Assert.Equal(AssemblyVerdict.Fitted, assembly.Fit("game:metalchain-iron", 1).Verdict);
        foreach (var (code, n) in new[] { ("game:rod-iron", 1), ("game:metalnailsandstrips-iron", 1), ("game:rod-steel", 1), ("game:plank-oak", 2) })
            Assert.Equal(AssemblyVerdict.Fitted, assembly.Fit(code, n).Verdict);
        Assert.True(assembly.Shows("drive"));
        Assert.True(assembly.Shows("plunger"));
        // services: the inlet drawn in the metal put in
        Assert.Equal(AssemblyVerdict.Fitted, assembly.Fit("seraphhorizons:pipesection-lead", 1).Verdict);
        foreach (var (code, n) in new[] { ("game:chute-straight-ns", 3), ("game:metalnailsandstrips-iron", 2), ("game:plank-oak", 1), ("game:supportbeam-oak", 1) })
            Assert.Equal(AssemblyVerdict.Fitted, assembly.Fit(code, n).Verdict);
        Assert.True(assembly.Built);
        Assert.True(assembly.Ready);
        Assert.True(assembly.Shows("outlets"));
        Assert.True(assembly.Shows("pipelead"));
        Assert.False(assembly.Shows("pipecopper"));
    }

    [Fact]
    public void Its_rig_reads_as_an_ore_machine_with_its_anchors()
    {
        var rig = OreMachineRig.Parse(File.ReadAllText(Out("jig-rig.json")), Jigging.ContentRequires);
        Assert.Equal(4, rig.Cells.Count);
        Assert.Equal((new Int3(0, 1, 1), CellFace.West), (rig.Anchor("power")!.Value.Cell, rig.Anchor("power")!.Value.Face));
        Assert.Equal((new Int3(0, 1, 0), CellFace.Up), (rig.Anchor("infeed")!.Value.Cell, rig.Anchor("infeed")!.Value.Face));
        Assert.Equal((new Int3(0, 1, 0), CellFace.West), (rig.Anchor("tailings")!.Value.Cell, rig.Anchor("tailings")!.Value.Face));
        Assert.Equal((new Int3(0, 0, 0), CellFace.North), (rig.Anchor("concentrate")!.Value.Cell, rig.Anchor("concentrate")!.Value.Face));
        Assert.Equal((new Int3(0, 0, 1), CellFace.South), (rig.Anchor("water")!.Value.Cell, rig.Anchor("water")!.Value.Face));
        Assert.Equal(Axis.X, rig.ShaftAxis);
    }
}
