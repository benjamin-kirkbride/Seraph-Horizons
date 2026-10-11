using System.Text.RegularExpressions;
using SeraphHorizons.Mod.EidolonGantry.Core;
using SeraphHorizons.Mod.Machines.Core;
using Xunit;

namespace SeraphHorizons.Tests.EidolonGantry;

/// <summary>The eidolon gantry's winch stages (<see cref="GantryParts"/>) and its shipped rig
/// (<see cref="GantryRig"/>), held to EidolonGantry/README.md's stage table.</summary>
public class EidolonGantryTests
{
    private static string File(string name) => System.IO.File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name));

    private static GantryParts Fitted(string wood, int stages)
    {
        var parts = new GantryParts(wood);
        foreach (var stage in GantryRequires.Stages.Take(stages))
            Assert.Equal(GantryFitVerdict.Fits, parts.Fit(parts.CodesFor(stage)[0]));
        return parts;
    }

    [Fact]
    public void The_stages_are_the_readmes_in_its_order_with_its_counts()
    {
        var readme = File("eidolongantry-readme.md");
        var rows = Regex.Matches(readme, @"^\| (\d+) \| `(\w+)` \| (\d+) × `([^`]+)`", RegexOptions.Multiline)
            .Select(m => (N: int.Parse(m.Groups[1].Value), Requires: m.Groups[2].Value, Count: int.Parse(m.Groups[3].Value), Code: m.Groups[4].Value))
            .ToList();
        Assert.Equal(GantryRequires.Stages.Count, rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            var stage = GantryRequires.Stages[i];
            Assert.Equal(i + 1, rows[i].N);
            Assert.Equal(GantryRequires.Name(stage), rows[i].Requires);
            Assert.Equal(GantryParts.Needed(stage), rows[i].Count);
            // {metal} is the ferrous three, {wood} the gantry's
            var expected = rows[i].Code.Contains("{metal}")
                ? new[] { "iron", "meteoriciron", "steel" }.Select(m => rows[i].Code.Replace("{metal}", m)).ToList()
                : [rows[i].Code.Replace("{wood}", "walnut")];
            Assert.Equal(expected, GantryParts.CodesFor(stage, "walnut"));
        }
    }

    [Fact]
    public void Stages_go_in_order_and_take_their_whole_count()
    {
        var parts = new GantryParts("oak");
        Assert.Equal(GantryStage.Axles, parts.Next);
        Assert.Equal(GantryFitVerdict.OutOfOrder, parts.CanFit("game:spurgear-s", out _));
        Assert.Equal(GantryFitVerdict.TooFew, parts.CanFit("game:woodenaxle-ud", out _, held: 7));
        Assert.Equal(GantryFitVerdict.Fits, parts.CanFit("woodenaxle-ud", out var stage, held: 8));
        Assert.Equal(GantryStage.Axles, stage);
        Assert.Equal(GantryFitVerdict.NotAPart, parts.CanFit("game:rod-copper", out _));
        Assert.Equal(GantryFitVerdict.Fits, parts.Fit("game:woodenaxle-ud", 8));
        Assert.Equal(GantryFitVerdict.AlreadyFitted, parts.CanFit("game:woodenaxle-ud", out _));
        Assert.Equal(GantryStage.CrankShaft, parts.Next);
    }

    [Fact]
    public void A_rod_goes_in_as_the_crank_shaft_and_later_as_the_crank()
    {
        var parts = Fitted("oak", 1);
        Assert.Equal(GantryFitVerdict.Fits, parts.Fit("game:rod-meteoriciron"));
        // the crank's rod is out of order until the ratchet is in
        Assert.Equal(GantryFitVerdict.OutOfOrder, parts.CanFit("game:rod-steel", out _));
        parts = Fitted("oak", 6);
        Assert.Equal(GantryFitVerdict.Fits, parts.CanFit("game:rod-steel", out var stage));
        Assert.Equal(GantryStage.Crank, stage);
        Assert.Equal("game:rod-iron", parts.FittedIn(GantryStage.CrankShaft));
    }

    [Fact]
    public void The_drum_and_spine_take_only_the_gantrys_own_wood()
    {
        var parts = Fitted("pine", 3);
        Assert.Equal(GantryFitVerdict.NotAPart, parts.CanFit("game:plank-oak", out _));
        Assert.Equal(GantryFitVerdict.Fits, parts.CanFit("game:plank-pine", out _, held: 10));
        parts = Fitted("pine", 8);
        Assert.Equal(GantryFitVerdict.NotAPart, parts.CanFit("game:supportbeam-oak", out _));
        Assert.Equal(GantryFitVerdict.TooFew, parts.CanFit("game:supportbeam-pine", out _, held: 2));
        Assert.Equal(GantryFitVerdict.Fits, parts.Fit("game:supportbeam-pine", 3));
        Assert.True(parts.Complete);
    }

    [Fact]
    public void Breaking_returns_exactly_what_went_in()
    {
        var parts = new GantryParts("ebony");
        parts.FitAll();
        Assert.True(parts.Complete);
        var drops = parts.Returns();
        Assert.Equal(9, drops.Count);
        Assert.Equal(8 + 1 + 4 + 10 + 10 + 1 + 1 + 4 + 3, drops.Sum(d => d.Count));
        Assert.Contains(new GantryDrop("game:plank-ebony", 10), drops);
        Assert.Contains(new GantryDrop("game:supportbeam-ebony", 3), drops);
        Assert.Equal(2, drops.Count(d => d == new GantryDrop("game:rod-iron", 1)));
    }

    [Fact]
    public void Restore_keeps_a_run_from_the_first_stage_of_the_right_wood()
    {
        var parts = Fitted("birch", 5);
        var back = GantryParts.Restore("birch", parts.Snapshot());
        Assert.Equal(parts.Snapshot(), back.Snapshot());
        // a gap: what follows it could not have been fitted
        var gap = parts.Snapshot().Where(kv => kv.Key != "gears").ToDictionary();
        Assert.Equal(2, GantryParts.Restore("birch", gap).Snapshot().Count);
        // another wood's planks are not this gantry's drum
        Assert.Equal(3, GantryParts.Restore("oak", parts.Snapshot()).Snapshot().Count);
    }

    [Fact]
    public void Only_fitted_winch_stages_are_drawn_and_never_the_body()
    {
        var parts = Fitted("oak", 2);
        Assert.True(parts.Fitted(null));
        Assert.True(parts.Fitted("axles"));
        Assert.True(parts.Fitted("crankshaft"));
        Assert.False(parts.Fitted("gears"));
        parts.FitAll();
        Assert.True(parts.Fitted("spine"));
        foreach (var body in GantryRequires.BodyStages)
            Assert.False(parts.Fitted(body));
    }

    [Fact]
    public void The_shipped_rig_parses_with_the_gameplays_vocabulary()
    {
        var rig = GantryRig.Parse(File("eidolongantry-rig.json"));
        Assert.Equal(181, rig.Cells.Count);
        Assert.Equal(88, rig.Cells.Count(c => c.Hollow));
        Assert.Equal(new Int3(5, 1, 5), rig.CrankCell);
        Assert.Equal(new Int3(0, 0, 2), rig.PlaceCell);
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
        // the native west (the front) points back at the player
        var front = Footprint.ToWorld(Side.West, placed);
        Assert.Equal(GantryRig.Opposite(look), front);
    }
}
