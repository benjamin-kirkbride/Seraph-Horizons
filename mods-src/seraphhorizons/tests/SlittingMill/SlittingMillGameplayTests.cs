using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.SlittingMill.Core;
using Xunit;

namespace SeraphHorizons.Tests.SlittingMill;

/// <summary>
/// The slitting mill's gameplay rules: its build (config/builds/slittingmill.json on the shared staged build, the
/// knife, a cleaver, its durable wearing part fitted with the shear): the five stages after the frame in order, each
/// taking its materials, the knife's durability, wear and take-back; the slitting's arithmetic (SlittingMill/Core),
/// the plates it takes, the knife's wear and the load by metal, the renderer's clock and the settings.
/// </summary>
public class SlittingMillGameplayTests
{
    private const string Knife = "game:cleaver-steel";

    /// <summary>Each stage's materials in turn, as a player would put them in: full stacks of each (the shear's
    /// knife goes in after, by <see cref="Built"/>).</summary>
    private static readonly (string Code, int Count)[][] Bills =
    [
        [(SlittingMillBuild.CutterCode, 10), ("game:rod-iron", 2)],
        [("game:jonasframes-gears01", 1), ("game:metal-parts", 1)],
        [("game:jonasframes-gearbox02", 1), ("game:rod-steel", 2)],
        [("game:bracket-heavy-iron", 1), ("game:metalplate-meteoriciron", 1)],
        [("game:rod-meteoriciron", 2), ("game:plank-oak", 1)],
    ];

    private static MachineBuild Build() =>
        MachineBuildJson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "builds", "slittingmill.json")))
            .WithUses(SlittingMillBuild.KnifePart, code => code switch
            {
                "game:cleaver-steel" => 2000,
                "game:cleaver-meteoriciron" => 950,
                "game:cleaver-iron" => 800,
                _ => 0,
            });

    private static AssemblyClick Click(MachineAssembly parts, string? code, int held = 1, int maxStack = 64, double? uses = null) =>
        parts.Fit(code, held, maxStack, uses);

    private static void FitStage(MachineAssembly parts, int stage)
    {
        foreach (var (code, count) in Bills[stage])
            Assert.Equal(new AssemblyClick(AssemblyVerdict.Fitted, count, stage), Click(parts, code, 64));
    }

    /// <summary>Every stage, the knife (a steel cleaver with <paramref name="knifeLeft"/>) after the shear's
    /// materials.</summary>
    private static MachineAssembly Built(double knifeLeft = 2000, string knife = Knife)
    {
        var parts = new MachineAssembly(Build());
        for (int s = 0; s < 4; s++)
            FitStage(parts, s);
        Assert.Equal(AssemblyVerdict.Fitted, Click(parts, knife, uses: knifeLeft).Verdict);
        Assert.True(parts.Build.StageComplete(3));
        FitStage(parts, 4);
        Assert.True(parts.Ready);
        return parts;
    }

    private static double? KnifeLeft(MachineAssembly parts) => parts.Wearing.Fitted(SlittingMillBuild.KnifePart)?.Left;

    [Fact]
    public void The_stages_are_frame_then_rolls_pinions_drive_shear_and_push_bar()
    {
        var build = Build();
        Assert.Equal(["rolls", "pinions", "drive", "shear", "pusher"], build.Stages.Select(s => s.Id));
        Assert.Equal(SlittingMillRequires.Stages, build.Stages.Select(s => s.Adds[0]));
        Assert.Equal(["shear", "knife"], build.Stages[3].Adds);
        Assert.Null(build.Block);   // its records are its own in the export
        Assert.Equal(new HashSet<string> { "rolls", "pinions", "drive", "shear", "pusher", "knife", "plate" }, SlittingMillRequires.KnownRequires);
        var parts = Built();
        Assert.Null(parts.Build.Next);
        Assert.Empty(parts.Build.NextNeeds());
    }

    [Fact]
    public void Each_stage_takes_the_games_items_and_only_the_cutters_are_the_packs()
    {
        var build = Build();
        string Bill(int s) => string.Join("; ", build.Stages[s].Materials.Select(m => $"{m.Quantity} {string.Join("|", m.Codes)}"));
        const string rods = "game:rod-iron|game:rod-meteoriciron|game:rod-steel";
        Assert.Equal($"10 seraphhorizons:slittercutter-steel; 2 {rods}", Bill(0));
        Assert.Equal("1 game:jonasframes-gears01|game:jonasframes-gears02; 1 game:metal-parts", Bill(1));
        Assert.Equal($"1 game:jonasframes-gearbox01|game:jonasframes-gearbox02; 2 {rods}", Bill(2));
        Assert.Equal("1 game:bracket-heavy-iron|game:bracket-heavy-meteoriciron|game:bracket-heavy-steel; "
                     + "1 game:metalplate-iron|game:metalplate-meteoriciron|game:metalplate-steel; "
                     + "1 game:cleaver-steel|game:cleaver-meteoriciron|game:cleaver-iron", Bill(3));
        Assert.Equal($"2 {rods}; 1 game:plank-oak", Bill(4));
        Assert.Equal(SlittingMillBuild.KnifePart, build.Stages[3].Materials.Last().Wearing);
        var knife = Assert.Single(build.Wearing);
        Assert.True(knife.Durable);
        Assert.Equal("knife", knife.Requires);
        Assert.Equal(SlittingMillBuild.KnifeCodes, knife.Codes);
        // every material but the cutters is the game's
        foreach (var code in build.Stages.SelectMany(s => s.Materials).SelectMany(m => m.Codes))
            Assert.True(code.StartsWith("game:", StringComparison.Ordinal) || code == SlittingMillBuild.CutterCode, code);
        // each cleaver's uses are its own durability
        Assert.Equal(2000, knife.UsesFor("game:cleaver-steel"));
        Assert.Equal(950, knife.UsesFor("game:cleaver-meteoriciron"));
        Assert.Equal(800, knife.UsesFor("game:cleaver-iron"));
    }

    [Fact]
    public void Only_the_next_stage_is_accepted()
    {
        var parts = new MachineAssembly(Build());
        Assert.Equal(AssemblyVerdict.OutOfOrder, Click(parts, "game:jonasframes-gears01").Verdict);
        Assert.Equal(AssemblyVerdict.OutOfOrder, Click(parts, "game:plank-oak").Verdict);
        Assert.Equal(AssemblyVerdict.OutOfOrder, Click(parts, Knife, uses: 2000).Verdict);
        Assert.Equal(AssemblyVerdict.NotAPart, Click(parts, "game:rod-copper", 2).Verdict);
        Assert.Equal(AssemblyVerdict.NotAPart, Click(parts, "game:cleaver-copper", uses: 200).Verdict);
        Assert.Equal(AssemblyVerdict.NotAPart, Click(parts, null).Verdict);
        // nine cutters are too few (and not a full stack); ten of sixteen go in
        Assert.Equal(AssemblyVerdict.TooFew, Click(parts, SlittingMillBuild.CutterCode, 9, 16).Verdict);
        Assert.Equal(new AssemblyClick(AssemblyVerdict.Fitted, 10, 0), Click(parts, SlittingMillBuild.CutterCode, 16, 16));
        Assert.Equal(AssemblyVerdict.AlreadyFitted, Click(parts, SlittingMillBuild.CutterCode, 6, 16).Verdict);
        Assert.False(parts.Shows("rolls"));
        Assert.Equal(new AssemblyClick(AssemblyVerdict.Fitted, 2, 0), Click(parts, "rod-iron", 5, 16));
        Assert.True(parts.Shows("rolls"));
        // the drive's and the push bar's rods are a later stage's while the pinions are next
        Assert.Equal(AssemblyVerdict.OutOfOrder, Click(parts, "game:rod-steel", 2).Verdict);
        FitStage(parts, 1);
        FitStage(parts, 2);
        // the knife is one of the shear's materials, in any order with its bracket and bed knife, and fitted as the
        // wearing part (drawn) only once the stage is complete
        Assert.Equal(AssemblyVerdict.Fitted, Click(parts, "game:cleaver-iron", uses: 500).Verdict);
        Assert.Equal(AssemblyVerdict.AlreadyFitted, Click(parts, Knife, uses: 2000).Verdict);
        Assert.Null(KnifeLeft(parts));
        Assert.False(parts.Shows("knife"));
        Assert.Equal(AssemblyVerdict.OutOfOrder, Click(parts, "game:plank-oak").Verdict);
        FitStage(parts, 3);
        Assert.True(parts.Build.StageComplete(3));
        Assert.True(parts.Shows("shear"));
        Assert.True(parts.Shows("knife"));
        Assert.False(parts.Built);
        FitStage(parts, 4);
        Assert.True(parts.Ready);
        Assert.Equal(500, KnifeLeft(parts));
    }

    [Fact]
    public void The_knife_carries_its_durability_comes_back_out_and_a_spent_one_is_refused()
    {
        var parts = new MachineAssembly(Build());
        for (int s = 0; s < 4; s++)
            FitStage(parts, s);
        Assert.Equal(AssemblyVerdict.Spent, Click(parts, Knife, uses: 0).Verdict);
        Assert.Equal(AssemblyVerdict.Fitted, Click(parts, Knife, uses: 1500).Verdict);
        FitStage(parts, 4);
        Assert.Equal(0.75, parts.Wearing.ShareLeft(SlittingMillBuild.KnifePart), 9);
        Assert.Equal(AssemblyVerdict.NotWorn, Click(parts, Knife, uses: 2000).Verdict);
        // Ctrl: the knife first, unworn by the taking; then the plate; then nothing
        Assert.Equal(SlittingMillTakeBack.Knife, SlittingMillBuild.TakeBack(parts, plateOn: true));
        Assert.Equal((Knife, 1500.0), parts.Take(SlittingMillBuild.KnifePart));
        Assert.False(parts.Ready);
        Assert.True(parts.Built);
        Assert.True(parts.Shows("shear"));    // the crank and the bed knife stay
        Assert.False(parts.Shows("knife"));
        Assert.Equal(SlittingMillTakeBack.Plate, SlittingMillBuild.TakeBack(parts, plateOn: true));
        Assert.Equal(SlittingMillTakeBack.Nothing, SlittingMillBuild.TakeBack(parts, plateOn: false));
        // any of the three cleavers goes back in, with its own durability
        Assert.Equal(AssemblyVerdict.Renewed, Click(parts, "game:cleaver-meteoriciron", uses: 900).Verdict);
        Assert.Equal(900, KnifeLeft(parts));
        Assert.True(parts.Ready);
    }

    [Fact]
    public void Breaking_returns_exactly_what_went_in_the_knife_with_its_durability()
    {
        var parts = Built(1234);
        Assert.Equal(
            [new(SlittingMillBuild.CutterCode, 10), new("game:rod-iron", 2), new("game:jonasframes-gears01", 1), new("game:metal-parts", 1),
             new("game:jonasframes-gearbox02", 1), new("game:rod-steel", 2), new("game:bracket-heavy-iron", 1),
             new("game:metalplate-meteoriciron", 1), new("game:rod-meteoriciron", 2), new("game:plank-oak", 1),
             new StageDrop(Knife, 1, 1234)],
            parts.Returns());
        parts.Take(SlittingMillBuild.KnifePart);
        Assert.DoesNotContain(parts.Returns(), d => d.Code == Knife);
    }

    [Fact]
    public void The_creative_shortcut_fills_each_stage_in_turn_with_a_new_steel_cleaver()
    {
        var parts = new MachineAssembly(Build());
        int n = 0;
        while (!parts.Ready && parts.FillNext() && n++ < 10) { }
        Assert.Equal(5, n);
        Assert.Equal(2000, KnifeLeft(parts));
        Assert.Equal(Knife, parts.Wearing.Fitted(SlittingMillBuild.KnifePart)!.Value.Code);
        Assert.False(parts.FillNext());
    }

    [Fact]
    public void A_save_keeps_every_fitted_code_and_the_knifes_wear()
    {
        var parts = Built(700, "game:cleaver-iron");
        var (build, wearing) = parts.Snapshot();
        var restored = MachineAssembly.Restore(Build(), build, wearing, out var dropped);
        Assert.Empty(dropped);
        Assert.True(restored.Ready);
        Assert.Equal(700, KnifeLeft(restored));
        Assert.Equal(parts.Returns(), restored.Returns());
    }

    [Fact]
    public void It_takes_the_plates_of_every_metal_the_games_nail_recipe_allows_and_nothing_else()
    {
        Assert.Equal(12, Slitting.Metals.Count);
        foreach (var metal in new[] { "copper", "tinbronze", "bismuthbronze", "blackbronze", "brass", "silver", "gold", "iron",
                                      "meteoriciron", "steel", "molybdochalkos", "electrum" })
        {
            Assert.Equal(metal, Slitting.MetalOfPlate("game:metalplate-" + metal));
            Assert.Equal("game:metalnailsandstrips-" + metal, Slitting.OutputFor(metal));
            Assert.Equal("game:metalplate-" + metal, Slitting.PlateFor(metal));
        }
        foreach (var code in new[] { "game:metalplate-lead", "game:metalplate-bismuth", "game:metalplate-cupronickel", "game:metalplate-tin",
                                     "game:metalnailsandstrips-iron", "game:ingot-iron", "seraphhorizons:halfplate-copper", "metalplate-iron", null })
            Assert.Null(Slitting.MetalOfPlate(code));
    }

    [Fact]
    public void A_plate_goes_on_only_on_a_ready_mill_with_the_table_empty()
    {
        Assert.Equal(SlitLoadVerdict.Loads, Slitting.CanLoad("game:metalplate-steel", true, false));
        Assert.Equal(SlitLoadVerdict.Incomplete, Slitting.CanLoad("game:metalplate-steel", false, false));
        Assert.Equal(SlitLoadVerdict.Occupied, Slitting.CanLoad("game:metalplate-steel", true, true));
        Assert.Equal(SlitLoadVerdict.NotAPlate, Slitting.CanLoad("game:metalnailsandstrips-steel", true, false));
        Assert.Equal(SlitLoadVerdict.NotAPlate, Slitting.CanLoad("game:metalplate-lead", true, false));
        Assert.True(Slitting.Running(true, true, -0.1f, 0.05f));
        Assert.False(Slitting.Running(false, true, 0.1f, 0.05f));
        Assert.False(Slitting.Running(true, false, 0.1f, 0.05f));
        Assert.False(Slitting.Running(true, true, 0.01f, 0.05f));
    }

    [Fact]
    public void A_plate_is_five_knife_strokes_at_the_drawn_pace_its_nails_cut_at_three_and_four()
    {
        double turns = SlittingMillConfig.Defaults.TurnsPerStroke;
        Assert.Equal(1, turns);
        Assert.Equal(5, Slitting.TurnsPerPlate(turns));
        var job = Slitting.Start();
        Assert.Equal(new MachineJob(1, 0, 5), job);
        Assert.Equal(0, Slitting.CutsDone(job));
        (job, bool finished) = Slitting.Advance(job, 2 * Math.PI * 3, turns);
        Assert.False(finished);
        Assert.Equal(3, job.Work, 9);
        Assert.Equal(1, Slitting.CutsDone(job));
        (job, finished) = Slitting.Advance(job, 2 * Math.PI * 1.5, turns);
        Assert.False(finished);
        Assert.Equal(2, Slitting.CutsDone(job));
        // backwards turns count as turns once rectified; the rest hands out, W held at the end
        (job, finished) = Slitting.Advance(job, 2 * Math.PI * 10, turns);
        Assert.True(finished);
        Assert.Equal(Slitting.HandOut, job.Work);
        Assert.False(Slitting.Advance(job, 100, turns).Finished);
        Assert.Equal(0, Slitting.StrokesFor(-3, turns));
        Assert.Equal(0, Slitting.StrokesFor(3, 0));
        // a slower pace takes more turns
        Assert.Equal(0.5, Slitting.StrokesFor(2 * Math.PI, 2), 9);
        Assert.False(Slitting.Advance(MachineJob.None, 100, turns).Finished);
    }

    [Fact]
    public void A_saved_slitting_is_clamped_and_none_without_its_class()
    {
        Assert.Equal(new MachineJob(1, 2.5, 5), Slitting.Restore(1, 2.5));
        Assert.Equal(5, Slitting.Restore(1, 99).Work);
        Assert.Equal(0, Slitting.Restore(1, double.NaN).Work);
        Assert.Equal(MachineJob.None, Slitting.Restore(2, 1));
        Assert.Equal(MachineJob.None, Slitting.Restore(0, 1));
    }

    [Fact]
    public void Harder_metals_load_the_shaft_more_and_wear_the_knife_more()
    {
        var c = new SlittingMillConfig();
        Assert.Equal(Slitting.Metals.OrderBy(m => m), c.Metals.Keys.OrderBy(m => m));
        string[] soft = ["gold", "silver"], mid = ["copper", "electrum", "molybdochalkos", "brass", "tinbronze", "bismuthbronze", "blackbronze"],
                 hard = ["iron", "meteoriciron", "steel"];
        foreach (var s in soft)
        foreach (var m in mid)
        {
            Assert.True(c.Resistance(s) <= c.Resistance(m), $"{s} {m}");
            Assert.True(c.KnifeWear(s) < c.KnifeWear(m), $"{s} {m}");
        }
        foreach (var m in mid)
        foreach (var h in hard)
        {
            Assert.True(c.Resistance(m) < c.Resistance(h), $"{m} {h}");
            Assert.True(c.KnifeWear(m) < c.KnifeWear(h), $"{m} {h}");
        }
        Assert.Equal(0.5f, c.Resistance("steel"));
        Assert.Equal(40, c.KnifeWear("steel"));
        Assert.Equal(0.2f, c.Resistance(null));        // empty: the lightest
        Assert.Equal(0.2f, c.LightestResistance());
        Assert.Equal(0.5f, c.HeaviestResistance());
        Assert.Equal(0, c.KnifeWear("lead"));
        // a steel cleaver slits 50 steel plates, an iron one 40 iron plates
        Assert.Equal(50, 2000 / c.KnifeWear("steel"));
        Assert.Equal(40, 800 / c.KnifeWear("iron"));
        var parts = Built();
        int plates = 0;
        while (parts.Ready && plates < 1000)
        {
            plates++;
            parts.Wearing.Wear(SlittingMillBuild.KnifePart, c.KnifeWear("steel"));
        }
        Assert.Equal(50, plates);
    }

    [Fact]
    public void Defaults_are_the_documented_ones_and_out_of_range_values_fall_back()
    {
        var c = new SlittingMillConfig();
        Assert.Equal(1f, c.TurnsPerStroke);
        Assert.Equal(20, c.NailsPerPlate);
        Assert.Equal(0.05f, c.MinSpeed);
        Assert.Empty(c.Sanitise());
        var bad = new SlittingMillConfig { TurnsPerStroke = 0, NailsPerPlate = 0, MinSpeed = float.NaN };
        bad.Metals["steel"] = new SlittingMetalConfig(-1, -1);
        bad.Metals.Remove("gold");
        bad.Metals["lead"] = new SlittingMetalConfig(0.1f, 1);
        var fixes = bad.Sanitise();
        Assert.Equal(7, fixes.Count);
        Assert.Equal(1f, bad.TurnsPerStroke);
        Assert.Equal(20, bad.NailsPerPlate);
        Assert.Equal(0.05f, bad.MinSpeed);
        Assert.Equal(0.5f, bad.Metals["steel"].Resistance);
        Assert.Equal(40, bad.Metals["steel"].KnifeWear);
        Assert.Equal(4, bad.Metals["gold"].KnifeWear);
        Assert.False(bad.Metals.ContainsKey("lead"));
    }

    [Fact]
    public void The_clock_follows_the_shaft_but_never_strays_from_the_server()
    {
        var clock = new SlittingMillClock();
        clock.Advance(0.1f, 0, true, 0, true, 1);
        Assert.Equal(1, clock.Class);
        Assert.Equal(0.25f, clock.Presence, 5);
        Assert.True(clock.ShowsWork);
        clock.Advance(0.05f, 2 * Math.PI * 0.2, true, 0, true, 1);
        Assert.Equal(0.2, clock.Work, 6);
        clock.Advance(0.05f, 2 * Math.PI * 0.2, true, 0, true, 1);
        Assert.Equal(0, clock.Work, 6);          // too far ahead: back to the server's
        clock.Advance(0.05f, 0, true, 2, true, 1);
        Assert.Equal(2, clock.Work, 6);          // never behind it
        clock.Advance(0.05f, 2 * Math.PI * 0.1, true, 2, false, 1);
        Assert.Equal(2, clock.Work, 6);          // stopped, it does not move
        var input = clock.Input(1.5, 2.5, 0.75);
        Assert.Equal((1.5, 2.5, 2.0, 1, 0.75), (input.Theta, input.Travel, input.Work, input.Class, input.Oil));
    }

    [Fact]
    public void The_work_goes_with_the_hand_out_while_the_clutch_eases_out_then_a_new_plate_starts_from_0()
    {
        var clock = new SlittingMillClock();
        for (int i = 0; i < 10; i++)
            clock.Advance(0.1f, 0, true, Slitting.HandOut, false, 1);
        Assert.Equal(1f, clock.Presence);
        Assert.Equal(Slitting.HandOut, clock.Work, 6);
        // handed out: the work is gone at once, W and k held while p eases out
        clock.Advance(0.2f, 0, false, 0, false, 1);
        Assert.False(clock.ShowsWork);
        Assert.Equal(1, clock.Class);
        Assert.Equal(Slitting.HandOut, clock.Work, 6);
        Assert.Equal(0.5f, clock.Presence, 5);
        clock.Advance(0.2f, 0, false, 0, false, 1);
        Assert.Equal(0f, clock.Presence);
        Assert.Equal(0, clock.Class);
        Assert.Equal(0, clock.Work);
        // a plate straight after another: W from the server's 0, the work shown at once
        clock.Advance(0.1f, 0, true, 0, false, 1);
        Assert.True(clock.ShowsWork);
        Assert.Equal(0, clock.Work);
        for (int i = 0; i < 10; i++)
            clock.Advance(0.1f, 0, true, Slitting.HandOut, false, 1);
        clock.Advance(0.05f, 0, true, 0, false, 1);
        Assert.Equal(0, clock.Work);
        Assert.Equal(1f, clock.Presence);
    }

    [Theory]
    [InlineData(Side.North)]
    [InlineData(Side.East)]
    [InlineData(Side.South)]
    [InlineData(Side.West)]
    public void Placed_the_mill_runs_away_from_the_player(Side look)
    {
        // the shared machine base's rule (BlockMachine.TryPlaceBlock): native north towards the player
        var side = OreMachineRig.PlacedSide(look);
        var far = Footprint.ToWorld(new Int3(0, 0, 1), side);
        var n = look.Normal();
        Assert.Equal(new Int3(n.X, 0, n.Z), far);
    }
}
