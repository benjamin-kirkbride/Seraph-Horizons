using System.Text.Json;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.ReverberatoryRoaster.Core;

namespace SeraphHorizons.Tests.ReverberatoryRoaster;

/// <summary>The reverberatory roaster (#736): its rules (<see cref="ReverberatoryRoasting"/>), its rig's gameplay half, its
/// build file against its rig, and its figures in the shipped <c>config/ore-processing.json</c> (100 %, 76 concentrate
/// an hour, 2 L a concentrate, load 0.1 to 0.5 with the bed, native sulfur 0.65).</summary>
public class ReverberatoryTests
{
    private static readonly OreRecovery R = new(JsonSerializer.Deserialize<OreProcessingConfig>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ore-processing.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!);

    private static string Out(string path) => Path.Combine(AppContext.BaseDirectory, path);

    private static readonly string RigJson = File.ReadAllText(Out("rigs/reverberatory-rig.json"));

    private static readonly ReverberatoryConfig C = new();

    [Fact]
    public void Its_figures_are_the_issues()
    {
        Assert.Empty(R.Problems);
        var spec = R.Machine(OreMachine.Reverberatory);
        Assert.Equal(1.0, spec.Recovery, 9);
        Assert.Equal(76, spec.ItemsPerHour, 9);
        Assert.Equal(2, spec.WaterPerItem, 9);
        Assert.True(spec.LoadFollowsJob);
        // the rabbles' load follows the bed: 0.1 empty, 0.5 with the hearth full
        Assert.Equal(0.1, R.LoadAt(OreMachine.Reverberatory, 0), 9);
        Assert.Equal(0.3, R.LoadAt(OreMachine.Reverberatory, 0.5), 9);
        Assert.Equal(0.5, R.LoadAt(OreMachine.Reverberatory, 1), 9);
        // native sulfur (#938): 13 units a rock, 0.65 melted out
        Assert.Equal(13, R.NativeSulfurUnits, 9);
        Assert.Equal(0.65, R.NativeSulfurMelt(OreMachine.Reverberatory), 9);
        Assert.Equal(0, R.NativeSulfurMelt(OreMachine.StallRoaster), 9);
    }

    [Fact]
    public void It_takes_a_sulfides_concentrate_and_native_sulfur_only()
    {
        Assert.Equal(ReverberatoryFeed.Concentrate, ReverberatoryRoasting.FeedOf("seraphhorizons:concentrate-galena", "concentrate", R.Ore("galena")));
        Assert.Equal(ReverberatoryFeed.Concentrate, ReverberatoryRoasting.FeedOf("seraphhorizons:concentrate-pyrite", "concentrate", R.Ore("pyrite")));
        // an oxide's concentrate smelts as it is; a sulfide's rock is the stall's; roasted concentrate is done
        Assert.Equal(ReverberatoryFeed.None, ReverberatoryRoasting.FeedOf("seraphhorizons:concentrate-hematite", "concentrate", R.Ore("hematite")));
        Assert.Equal(ReverberatoryFeed.None, ReverberatoryRoasting.FeedOf("seraphhorizons:crushedore-galena-rich", "crushed", R.Ore("galena")));
        Assert.Equal(ReverberatoryFeed.None, ReverberatoryRoasting.FeedOf("seraphhorizons:roastedconcentrate-galena", "roastedconcentrate", R.Ore("galena")));
        Assert.Equal(ReverberatoryFeed.NativeSulfur, ReverberatoryRoasting.FeedOf("game:ore-sulfur", null, null));
        Assert.Equal(ReverberatoryFeed.None, ReverberatoryRoasting.FeedOf("game:stick", null, null));
    }

    [Fact]
    public void A_concentrate_roasts_whole_and_whole_route_concentrate_is_made_whole_at_the_last_step()
    {
        // 100 %: 5 units in, 5 out
        Assert.Equal((5.0, false), ReverberatoryRoasting.Roast(R, R.Ore("chalcopyrite"), 5, whole: false));
        // off the whole route, a sulfide with no by-product ends here: its chain's share restored
        var (units, carry) = ReverberatoryRoasting.Roast(R, R.Ore("chalcopyrite"), 5, whole: true);
        Assert.Equal(5 * R.WholeRestore(R.Ore("chalcopyrite")), units, 9);
        Assert.False(carry);
        // galena ends at the Parkes kettle: roasted whole, the mark carried on to it
        var (galena, galenaCarry) = ReverberatoryRoasting.Roast(R, R.Ore("galena"), 5, whole: true);
        Assert.Equal(5, galena, 9);
        Assert.True(galenaCarry);
    }

    [Fact]
    public void Native_sulfur_melts_out_at_065_of_its_13_units()
    {
        Assert.Equal(13 * 0.65, ReverberatoryRoasting.Melt(R), 9);
        // 1.69 powder a rock, carried: 100 rock give 169
        var carry = new UnitCarry();
        int powders = Enumerable.Range(0, 100).Sum(_ => carry.Add("sulfur", ReverberatoryRoasting.Melt(R), R.SulfurUnitsPerPowder));
        Assert.Equal(169, powders);
        // it takes as long, as much water and coal as the concentrate holding as many units
        Assert.Equal(2.6, ReverberatoryRoasting.Weight(ReverberatoryFeed.NativeSulfur, R.ConcentrateUnits, R.NativeSulfurUnits), 9);
        Assert.Equal(1, ReverberatoryRoasting.Weight(ReverberatoryFeed.Concentrate, R.ConcentrateUnits, R.NativeSulfurUnits), 9);
    }

    [Fact]
    public void The_bed_fills_with_the_hearths_charge()
    {
        Assert.Equal(32, ReverberatoryRoasting.Capacity(C.HearthCharge, 1));
        Assert.Equal(12, ReverberatoryRoasting.Capacity(C.HearthCharge, 2.6));
        Assert.Equal(0, ReverberatoryRoasting.Fill(0, 1, 32));
        Assert.Equal(0.5, ReverberatoryRoasting.Fill(16, 1, 32), 9);
        Assert.Equal(1, ReverberatoryRoasting.Fill(64, 1, 32), 9);
        Assert.Equal(12 * 2.6 / 32, ReverberatoryRoasting.Fill(12, 2.6, 32), 9);
    }

    [Fact]
    public void A_coal_every_six_concentrate_taken_as_the_last_burns_out()
    {
        double left = 0;
        var takes = new List<int>();
        for (int i = 0; i < 60; i++)
        {
            (int take, left) = ReverberatoryRoasting.Coal(left, 1, C.ConcentratePerCoal);
            takes.Add(take);
        }
        Assert.Equal(10, takes.Sum());
        // the first concentrate lights on a fresh coal, the seventh on the next
        Assert.Equal([1, 0, 0, 0, 0, 0, 1], takes.Take(7).ToArray());
        Assert.Equal(0, left, 6);
        // a native sulfur rock burns 2.6 concentrates' coal
        var (t, l) = ReverberatoryRoasting.Coal(0, 2.6, 6);
        Assert.Equal(1, t);
        Assert.Equal(3.4, l, 9);
        Assert.Equal(0, ReverberatoryRoasting.Coal(3.4, 2.6, 6).Take);
        Assert.True(ReverberatoryRoasting.IsFuel("game:ore-bituminouscoal") && ReverberatoryRoasting.IsFuel("game:coke"));
        Assert.False(ReverberatoryRoasting.IsFuel("game:firewood"));
    }

    [Fact]
    public void The_pace_is_the_dampers_on_a_drawing_row_with_the_fire_lit()
    {
        Assert.Equal(1, ReverberatoryRoasting.Pace(Damper.Open, rowWorks: true, lit: true, C.HalfDamperPace));
        Assert.Equal(0.5, ReverberatoryRoasting.Pace(Damper.Half, true, true, C.HalfDamperPace));
        Assert.Equal(0, ReverberatoryRoasting.Pace(Damper.Shut, true, true, C.HalfDamperPace));
        Assert.Equal(0, ReverberatoryRoasting.Pace(Damper.Open, false, true, C.HalfDamperPace));
        Assert.Equal(0, ReverberatoryRoasting.Pace(Damper.Open, true, false, C.HalfDamperPace));
    }

    [Fact]
    public void Bad_settings_fall_back_with_a_line_each()
    {
        var c = new ReverberatoryConfig { HearthCharge = 0, ConcentratePerCoal = double.NaN, HalfDamperPace = 2 };
        Assert.Equal(3, c.Sanitise().Count);
        Assert.Equal((32, 6.0, 0.5), (c.HearthCharge, c.ConcentratePerCoal, c.HalfDamperPace));
        Assert.Empty(new ReverberatoryConfig().Sanitise());
    }

    [Fact]
    public void The_rig_gives_gameplay_its_outlet_ash_mouth_cap_bricks_and_partner()
    {
        var rig = ReverberatoryRig.Parse(RigJson);
        Assert.Equal(new Int3(-1, 1, 4), rig.OutletCell);
        Assert.Equal(new Int3(-1, 2, 4), rig.ChimneyBase);
        Assert.Equal(Side.North, rig.AshSide);
        Assert.Equal(("game:metalplate-iron", 1), (rig.CapCode, rig.CapCount));
        Assert.Equal((6, 2), (rig.MinChimney, rig.RowMax));
        Assert.Equal("red", rig.Bricks.Default);
        // back to back on the west wall, turned half round: its controller three west and at the far end
        Assert.Equal(new Int3(-3, 0, 4), rig.Partner);
        var cells = OreMachineRig.Parse(RigJson, ReverberatoryRequires.Extra).Cells.Select(c => c.Pos).ToHashSet();
        // the partner's footprint (this one's turned half round about its controller) meets this one's nowhere
        foreach (var c in cells)
            Assert.DoesNotContain(new Int3(rig.Partner.X - c.X, c.Y, rig.Partner.Z - c.Z), cells);
    }

    [Fact]
    public void The_shared_rig_reads_every_part_and_the_fuel_and_infeed_anchors()
    {
        var rig = OreMachineRig.Parse(RigJson, ReverberatoryRequires.Extra);
        // the stages' fits and their states (the damper's places, the pipes' metals)
        foreach (var r in new[] { "brickwork", "lining", "arch", "hopper", "doors", "damperopen", "damperhalf", "dampershut",
                     "drive", "rabbles", "feeder", "stoker", "output", "services", "pipecopper", "pipelead" })
            Assert.Contains(r, rig.StageRequires);
        Assert.Equal(new[] { "damperopen", "damperhalf", "dampershut" }, Enum.GetValues<Damper>().Select(ReverberatoryRequires.DamperPart));
        foreach (var a in new[] { "power", "infeed", "output", "fuel", "water", "outlet" })
            Assert.True(rig.Anchors.ContainsKey(a), a);
        Assert.NotEqual(Int3.Zero, rig.Anchors["fuel"].Cell);
    }

    [Fact]
    public void The_build_is_the_rigs_its_masonry_measured()
    {
        var build = MachineBuildJson.Parse(File.ReadAllText(Out("builds/reverberatory.json")));
        Assert.Equal(FeedMode.Continuous, build.Feed);
        Assert.Equal(["masonry", "hopperdoors", "drive", "feeding", "services"], build.Stages.Select(s => s.Id).ToArray());
        using var doc = Mod.Machines.Core.RigJson.Parse(RigJson);
        Assert.Empty(MachineBuildJson.CheckAgainstRig(build, doc.RootElement));
        // the model's measured masonry: 26 fired bricks of one type, 17 + 9 refractory, 7 + 4 fire clay
        var masonry = build.Stages[0].Materials;
        Assert.True(masonry[0].OneType);
        Assert.Equal(26, masonry[0].Quantity);
        Assert.Equal(("game:refractorybrick-fired-tier3", 26), (masonry[1].Codes.Single(), masonry[1].Quantity));
        Assert.Equal(("game:clay-fire", 11), (masonry[2].Codes.Single(), masonry[2].Quantity));
        var rig = ReverberatoryRig.Parse(RigJson);
        Assert.Equal(rig.Bricks.Types.Select(t => rig.Bricks.ItemOf(t.Id)).Order(), masonry[0].Codes.Order());
        Assert.Equal(rig.Bricks.ItemOf(rig.Bricks.Default), masonry[0].Codes[0]);
        // the stages add what the rig's fits require, in order; the services last
        var rigStages = doc.RootElement.GetProperty("build").GetProperty("stages").EnumerateArray().ToList();
        Assert.Equal(rigStages.Count, build.Stages.Count);
        for (int i = 0; i < rigStages.Count; i++)
        {
            Assert.Equal(rigStages[i].GetProperty("id").GetString(), build.Stages[i].Id);
            var fits = rigStages[i].GetProperty("fits").EnumerateArray().Select(f => f.GetProperty("requires").GetString()!).ToList();
            Assert.All(fits, f => Assert.Contains(f, build.Stages[i].Adds));
        }
        // every material within the grid's limit per click (a stack)
        Assert.All(build.Stages.SelectMany(s => s.Materials), m => Assert.InRange(m.Quantity, 1, 64));
    }
}
