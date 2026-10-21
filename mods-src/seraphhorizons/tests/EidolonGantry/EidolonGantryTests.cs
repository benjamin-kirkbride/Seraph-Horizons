using System.Text.RegularExpressions;
using SeraphHorizons.Mod.EidolonGantry.Core;
using SeraphHorizons.Mod.Machines.Core;
using Xunit;

namespace SeraphHorizons.Tests.EidolonGantry;

/// <summary>The eidolon gantry's winch (its build file, <c>config/builds/eidolongantry.json</c>, on the
/// shared staged build in a gantry's wood, <see cref="GantryBuild.ForWood"/>) and its shipped rig
/// (<see cref="GantryRig"/>), held to EidolonGantry/README.md's stage table.</summary>
public class EidolonGantryTests
{
    private static string File(string name) => System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name));

    private static MachineBuild Build(string wood) => GantryBuild.ForWood(MachineBuildJson.Parse(File("builds/eidolongantry.json")), wood);

    private static MachineAssembly Fitted(string wood, int stages)
    {
        var a = new MachineAssembly(Build(wood));
        for (int i = 0; i < stages; i++)
            Assert.True(a.FillNext());
        return a;
    }

    private static AssemblyVerdict Click(MachineAssembly a, string code, int held = 64) => a.Check(code, held).Verdict;

    [Fact]
    public void The_stages_are_the_readmes_in_its_order_with_its_counts()
    {
        var readme = File("eidolongantry-readme.md");
        var rows = Regex.Matches(readme, @"^\| (\d+) \| `(\w+)` \| (\d+) × `([^`]+)`", RegexOptions.Multiline)
            .Select(m => (N: int.Parse(m.Groups[1].Value), Requires: m.Groups[2].Value, Count: int.Parse(m.Groups[3].Value), Code: m.Groups[4].Value))
            .ToList();
        var build = Build("walnut");
        Assert.Equal(GantryRequires.WinchStages, build.Stages.Select(s => s.Id));
        Assert.Equal(build.Stages.Count, rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            var stage = build.Stages[i];
            Assert.Equal(i + 1, rows[i].N);
            Assert.Equal(rows[i].Requires, stage.Id);
            Assert.Equal([stage.Id], stage.Adds);
            var material = Assert.Single(stage.Materials);
            Assert.Equal(rows[i].Count, material.Quantity);
            // {metal} is the ferrous three, {wood} the gantry's
            var expected = rows[i].Code.Contains("{metal}")
                ? new[] { "iron", "meteoriciron", "steel" }.Select(m => rows[i].Code.Replace("{metal}", m)).ToList()
                : [rows[i].Code.Replace("{wood}", "walnut")];
            Assert.Equal(expected, material.Codes);
        }
        Assert.Empty(build.Wearing);
        Assert.Null(build.Block);
    }

    [Fact]
    public void Stages_go_in_order_and_take_their_whole_count()
    {
        var a = new MachineAssembly(Build("oak"));
        Assert.Equal(0, a.Build.Next);
        Assert.Equal(AssemblyVerdict.OutOfOrder, Click(a, "game:spurgear-s"));
        Assert.Equal(AssemblyVerdict.TooFew, Click(a, "game:woodenaxle-ud", 7));
        Assert.Equal(AssemblyVerdict.NotAPart, Click(a, "game:rod-copper"));
        var fit = a.Fit("woodenaxle-ud", 16, 16);
        Assert.Equal(AssemblyVerdict.Fitted, fit.Verdict);
        Assert.Equal(8, fit.Take);
        Assert.Equal(AssemblyVerdict.AlreadyFitted, Click(a, "game:woodenaxle-ud"));
        Assert.Equal(1, a.Build.Next);
    }

    [Fact]
    public void A_rod_goes_in_as_the_crank_shaft_and_later_as_the_crank()
    {
        var a = Fitted("oak", 1);
        Assert.Equal(AssemblyVerdict.Fitted, a.Fit("game:rod-meteoriciron", 1).Verdict);
        // the crank's rod is out of order until the ratchet is in
        Assert.Equal(AssemblyVerdict.OutOfOrder, Click(a, "game:rod-steel"));
        a = Fitted("oak", 6);
        Assert.Equal(AssemblyVerdict.Fitted, a.Fit("game:rod-steel", 1).Verdict);
        Assert.True(a.Build.Fitted("crank"));
        Assert.Equal("game:rod-iron", a.Build.CodeIn(1, 0));
        Assert.Equal("game:rod-steel", a.Build.CodeIn(6, 0));
    }

    [Fact]
    public void The_drum_and_spine_take_only_the_gantrys_own_wood()
    {
        var a = Fitted("pine", 3);
        Assert.Equal(AssemblyVerdict.NotAPart, Click(a, "game:plank-oak"));
        Assert.Equal(AssemblyVerdict.Fitted, Click(a, "game:plank-pine", 10));
        a = Fitted("pine", 8);
        Assert.Equal(AssemblyVerdict.NotAPart, Click(a, "game:supportbeam-oak"));
        Assert.Equal(AssemblyVerdict.TooFew, Click(a, "game:supportbeam-pine", 2));
        Assert.Equal(AssemblyVerdict.Fitted, a.Fit("game:supportbeam-pine", 3, 32).Verdict);
        Assert.True(a.Built);
    }

    [Fact]
    public void Breaking_returns_exactly_what_went_in()
    {
        var a = Fitted("ebony", 9);
        Assert.True(a.Built);
        var drops = a.Returns();
        // the two rods are one drop of two
        Assert.Equal(8, drops.Count);
        Assert.Equal(8 + 1 + 4 + 10 + 10 + 1 + 1 + 4 + 3, drops.Sum(d => d.Count));
        Assert.Contains(new StageDrop("game:plank-ebony", 10), drops);
        Assert.Contains(new StageDrop("game:supportbeam-ebony", 3), drops);
        Assert.Contains(new StageDrop("game:rod-iron", 2), drops);
    }

    [Fact]
    public void Restore_keeps_a_run_from_the_first_stage_of_the_right_wood()
    {
        var a = Fitted("birch", 5);
        var (build, wearing) = a.Snapshot();
        var back = MachineAssembly.Restore(Build("birch"), build, wearing, out var lost);
        Assert.Equal(build, back.Snapshot().Build);
        Assert.Empty(lost);
        // a gap: what follows it could not have been fitted, and is given back
        var gap = build.Where(kv => !kv.Key.StartsWith("2/", StringComparison.Ordinal)).ToDictionary();
        Assert.Equal(2, MachineAssembly.Restore(Build("birch"), gap, wearing, out lost).Build.StagesDone);
        Assert.Equal(2, lost.Count);
        // another wood's planks are not this gantry's drum
        Assert.Equal(3, MachineAssembly.Restore(Build("oak"), build, wearing, out _).Build.StagesDone);
    }

    [Fact]
    public void Only_fitted_winch_stages_are_drawn()
    {
        var a = Fitted("oak", 2);
        Assert.True(a.Shows(null));
        Assert.True(a.Shows("axles"));
        Assert.True(a.Shows("crankshaft"));
        Assert.False(a.Shows("gears"));
        while (a.FillNext()) { }
        Assert.True(a.Shows("spine"));
        // the body's stages are not the build's (GantryBody draws them)
        Assert.All(GantryRequires.BodyStages, s => Assert.DoesNotContain(a.Build.Stages, st => st.Adds.Contains(s)));
    }

    [Fact]
    public void The_shipped_rig_parses_with_the_gameplays_vocabulary()
    {
        var rig = GantryRig.Parse(File("eidolongantry-rig.json"));
        Assert.Equal(181, rig.Cells.Count);
        Assert.Equal(88, rig.Cells.Count(c => c.Hollow));
        Assert.Equal(new Int3(5, 1, 5), rig.CrankCell);
        Assert.Equal(new Int3(0, 0, 2), rig.PlaceCell);
        Assert.Equal(new Int3(0, 0, 2), ((IMachineRig)rig).PlaceCell);
        Assert.Empty(rig.Anchors);
        Assert.Equal(Side.West, rig.ExitSide);
        var requires = rig.MovingParts.Parts.Select(p => p.Requires).OfType<string>().ToHashSet();
        Assert.Equal(GantryRequires.KnownRequires.Order(), requires.Order());
        // the frame is the block's shape: one part with no requires, no ride and no drivers
        Assert.Single(rig.MovingParts.Parts, p => p.Requires == null && p.Ride == null && p.Drivers.Count == 0);
        // every body stage rides the hook down with the spine
        Assert.All(rig.MovingParts.Parts.Where(p => GantryRequires.IsBodyStage(p.Requires)), p => Assert.Equal("hook", p.Ride));
    }

    [Theory]
    [InlineData(Side.East, Side.South)]
    [InlineData(Side.West, Side.North)]
    [InlineData(Side.North, Side.East)]
    [InlineData(Side.South, Side.West)]
    public void The_open_front_faces_the_player(Side look, Side placed)
    {
        Assert.Equal(placed, GantryRig.PlacedSide(look));
        // the shared frame places it so (BlockMachine.TryPlaceBlock)
        Assert.Equal(placed, ((IMachineRig)GantryRig.Parse(File("eidolongantry-rig.json"))).PlacedSide(look));
        // the native west (the front) points back at the player
        var front = Footprint.ToWorld(Side.West, placed);
        Assert.Equal(GantryRig.Opposite(look), front);
    }
}
