using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.PressBrake.Core;
using Xunit;

namespace SeraphHorizons.Tests.PressBrake;

/// <summary>PressBrake: its build (config/builds/pressbrake.json on the shared staged build): the order,
/// what each stage takes and that nothing takes a part back out; the half plates by metal; the fold's
/// arithmetic (W only while the lever is worked, one angle at W = 1); who holds the lever; the
/// renderer's clock; and the settings.</summary>
public class PressBrakeGameplayTests
{
    private static MachineBuild Build() =>
        MachineBuildJson.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "builds", "pressbrake.json")));

    private static MachineAssembly Complete(string screws = "game:metal-parts", string edge = "game:metalplate-iron")
    {
        var parts = new MachineAssembly(Build());
        Assert.Equal(AssemblyVerdict.Fitted, parts.Fit(screws, 1).Verdict);
        Assert.Equal(AssemblyVerdict.Fitted, parts.Fit(edge, 1).Verdict);
        return parts;
    }

    // ---- The build ----

    [Fact]
    public void The_stages_go_in_in_the_contracts_order()
    {
        var build = Build();
        Assert.Equal(["screws", "edge"], build.Stages.Select(s => s.Id));
        Assert.Equal(["screws", "edge"], build.Stages.Select(s => Assert.Single(s.Adds)));
        Assert.All(build.Stages, s => Assert.Equal(1, Assert.Single(s.Materials).Quantity));
        Assert.Null(build.Block);   // its construction record is its own in the export
        var parts = new MachineAssembly(build);
        Assert.Equal(0, parts.Build.Next);
        // the creative shortcut fits each stage's first code: metal parts, then an iron plate
        Assert.True(parts.FillNext());
        Assert.Equal("game:metal-parts", parts.Build.CodeIn(0, 0));
        Assert.Equal(1, parts.Build.Next);
        Assert.False(parts.Built);
        Assert.Equal(AssemblyVerdict.Fitted, parts.Fit("game:metalplate-steel", 1).Verdict);
        Assert.True(parts.Built);
        Assert.Null(parts.Build.Next);
        // only the edges have a metal of their own: the screws are always cupronickel
        Assert.Null(PressBrakeRequires.EdgeMetal(parts.Build.CodeIn(0, 0)));
        Assert.Equal("steel", PressBrakeRequires.EdgeMetal(parts.Build.CodeIn(1, 0)));
        Assert.Null(PressBrakeRequires.EdgeMetal("game:rod-steel"));
    }

    [Fact]
    public void Only_the_next_stage_is_accepted()
    {
        var parts = new MachineAssembly(Build());
        Assert.Equal(AssemblyVerdict.OutOfOrder, parts.Fit("game:metalplate-iron", 1).Verdict);
        Assert.Equal(AssemblyVerdict.NotAPart, parts.Fit("game:rod-copper", 1).Verdict);
        // the screws were once a rod: none fits now
        foreach (var rod in new[] { "game:rod-iron", "game:rod-meteoriciron", "game:rod-steel" })
            Assert.Equal(AssemblyVerdict.NotAPart, parts.Fit(rod, 1).Verdict);
        Assert.Equal(AssemblyVerdict.NotAPart, parts.Fit("game:metal-scraps", 1).Verdict);
        Assert.Equal(AssemblyVerdict.NotAPart, parts.Fit("game:metalplate-meteoriciron", 1).Verdict);
        Assert.Equal(AssemblyVerdict.NotAPart, parts.Fit("game:metalplate-lead", 1).Verdict);   // the work, not a part
        Assert.Equal(AssemblyVerdict.NotAPart, parts.Fit(null, 1).Verdict);
        var fit = parts.Fit("metal-parts", 2);   // a code with no domain is the game's; one of a stack of two
        Assert.Equal((AssemblyVerdict.Fitted, 1), (fit.Verdict, fit.Take));
        Assert.Equal(AssemblyVerdict.AlreadyFitted, parts.Fit("game:metal-parts", 1).Verdict);
        Assert.Equal(1, parts.Build.Next);
    }

    [Theory]
    [InlineData("game:metalplate-iron")]
    [InlineData("game:metalplate-steel")]
    public void Metal_parts_and_each_edge_plate_fit(string plate) => Assert.True(Complete(edge: plate).Built);

    [Fact]
    public void Each_stage_is_drawn_once_it_is_in_and_the_plates_are_not_the_builds()
    {
        var parts = new MachineAssembly(Build());
        Assert.True(parts.Shows(null));
        Assert.False(parts.Shows("screws"));
        parts.Fit("game:metal-parts", 1);
        Assert.True(parts.Shows("screws"));
        Assert.False(parts.Shows("edge"));
        Assert.True(Complete().Shows("edge"));
        // the plates are the work's: no stage adds them (BEPressBrake.ShowsPart draws them by its clock)
        Assert.DoesNotContain(Build().Stages, s => s.Adds.Contains("platelead") || s.Adds.Contains("platecopper"));
        Assert.True(PressBrakeRequires.KnownRequires.SetEquals(["screws", "edge", "platelead", "platecopper"]));
        Assert.Equal("platelead", PressBrakeRequires.Plate(1));
        Assert.Equal("platecopper", PressBrakeRequires.Plate(2));
        Assert.Null(PressBrakeRequires.Plate(0));
    }

    [Fact]
    public void Fitted_parts_never_come_back_out()
    {
        // only a wearing part may come out of a built machine, and this one has none: breaking is the
        // only way back to the parts (Returns)
        var parts = Complete();
        Assert.Empty(parts.Wearing.Parts);
        Assert.Null(parts.Take("edge"));
        Assert.True(parts.Built);
    }

    [Fact]
    public void Breaking_returns_every_fitted_item_and_a_save_keeps_them()
    {
        var parts = Complete(edge: "game:metalplate-steel");
        Assert.Equal([new StageDrop("game:metal-parts", 1), new StageDrop("game:metalplate-steel", 1)], parts.Returns());
        var (build, wearing) = parts.Snapshot();
        var restored = MachineAssembly.Restore(Build(), build, wearing, out var dropped);
        Assert.True(restored.Built);
        Assert.Empty(dropped);
        Assert.Equal(parts.Returns(), restored.Returns());
        // a gap ends the run, and a code that is not its stage's part is given back
        var gap = MachineAssembly.Restore(Build(), new Dictionary<string, int> { ["1/0/game:metalplate-iron"] = 1 }, wearing, out dropped);
        Assert.False(gap.Build.StageComplete(1));
        Assert.Equal([new StageDrop("game:metalplate-iron", 1)], dropped);
        var wrong = MachineAssembly.Restore(Build(), new Dictionary<string, int> { ["0/0/game:rod-copper"] = 1 }, wearing, out dropped);
        Assert.Empty(wrong.Returns());
        Assert.Equal([new StageDrop("game:rod-copper", 1)], dropped);
    }

    // ---- Half plates ----

    [Fact]
    public void Half_plates_and_angles_by_metal()
    {
        Assert.Equal(1, Folding.ClassOfPlate("seraphhorizons:halfplate-lead"));
        Assert.Equal(2, Folding.ClassOfPlate("seraphhorizons:halfplate-copper"));
        // a whole plate is the squaring shear's work, never the brake's
        Assert.Equal(0, Folding.ClassOfPlate("game:metalplate-lead"));
        Assert.Equal(0, Folding.ClassOfPlate("game:metalplate-copper"));
        Assert.Equal(0, Folding.ClassOfPlate("game:metalplate-iron"));
        Assert.Equal(0, Folding.ClassOfPlate("game:ingot-lead"));
        Assert.Equal(0, Folding.ClassOfPlate("seraphhorizons:angle-lead"));   // what comes off never goes back on
        Assert.Equal(0, Folding.ClassOfPlate("game:chutesection-copper"));
        Assert.Equal(0, Folding.ClassOfPlate("seraphhorizons:pipesection-copper"));
        Assert.Equal("seraphhorizons:angle-lead", Folding.AngleFor(1));
        Assert.Equal("seraphhorizons:angle-copper", Folding.AngleFor(2));
        Assert.Null(Folding.AngleFor(0));
        Assert.Equal("seraphhorizons:halfplate-lead", Folding.PlateFor(1));
        Assert.Equal("seraphhorizons:halfplate-copper", Folding.PlateFor(2));
        Assert.Null(Folding.PlateFor(0));
        Assert.Equal(("lead", "copper"), (Folding.MetalOf(1), Folding.MetalOf(2)));
        Assert.Equal(1, Folding.AnglesPerPlate);
    }

    [Fact]
    public void A_half_plate_goes_on_a_complete_empty_brake_only()
    {
        Assert.Equal(FoldLoadVerdict.Loads, Folding.CanLoad(Folding.LeadHalfPlate, true, false));
        Assert.Equal(FoldLoadVerdict.Loads, Folding.CanLoad(Folding.CopperHalfPlate, true, false));
        Assert.Equal(FoldLoadVerdict.Incomplete, Folding.CanLoad(Folding.LeadHalfPlate, false, false));
        Assert.Equal(FoldLoadVerdict.Occupied, Folding.CanLoad(Folding.CopperHalfPlate, true, true));
        Assert.Equal(FoldLoadVerdict.NotAPlate, Folding.CanLoad("game:metalplate-copper", true, false));
        Assert.Equal(FoldLoadVerdict.NotAPlate, Folding.CanLoad("game:ingot-copper", true, false));
        Assert.Equal(FoldLoadVerdict.NotAPlate, Folding.CanLoad("seraphhorizons:angle-copper", true, false));
        Assert.Equal(FoldLoadVerdict.NotAPlate, Folding.CanLoad("game:chutesection-lead", true, false));
    }

    // ---- The fold ----

    [Theory]
    [InlineData(1, 1.5)]
    [InlineData(2, 2.25)]
    public void W_advances_with_the_lever_and_the_plate_is_done_at_1(int k, double turns)
    {
        var job = Folding.Start(k);
        Assert.True(job.Untouched);
        (job, bool finished) = Folding.Advance(job, 2 * Math.PI * turns / 2, turns);
        Assert.Equal(0.5, job.Work, 9);
        Assert.False(finished);
        Assert.False(job.Untouched);
        (job, finished) = Folding.Advance(job, 2 * Math.PI * turns * 0.49, turns);
        Assert.False(finished);
        (job, finished) = Folding.Advance(job, 2 * Math.PI * turns, turns);   // more than enough: held at 1
        Assert.True(finished);
        Assert.Equal(1.0, job.Work);
        // done, it goes no further and does not finish twice
        (job, finished) = Folding.Advance(job, 100, turns);
        Assert.False(finished);
        Assert.Equal(MachineJob.None, Folding.Advance(MachineJob.None, 100, turns).Job);
        Assert.Equal(MachineJob.None, Folding.Start(3));
    }

    [Fact]
    public void W_advances_only_while_the_lever_is_held()
    {
        Assert.True(Folding.Running(complete: true, plateOn: true, held: true));
        Assert.False(Folding.Running(true, true, false));
        Assert.False(Folding.Running(true, false, true));
        Assert.False(Folding.Running(false, true, true));
        // one second of holding is one lever turn: a lead half plate takes 1.5, copper 2.25
        Assert.Equal(2 * Math.PI, Folding.LeverRadiansPerSecond, 9);
        var c = new PressBrakeConfig();
        Assert.Equal(1 / 1.5, Folding.PlatesFor(Folding.LeverRadiansPerSecond, c.LeverTurnsPerPlate(1)), 9);
        Assert.Equal(1 / 2.25, Folding.PlatesFor(Folding.LeverRadiansPerSecond, c.LeverTurnsPerPlate(2)), 9);
        Assert.Equal(0, Folding.PlatesFor(-3, 6));
        Assert.Equal(0, Folding.PlatesFor(3, 0));
    }

    [Fact]
    public void The_folds_crossed_in_a_step_are_counted()
    {
        double[] moments = [0.25, 0.75];
        Assert.Equal(0, MachineJob.Crossed(moments, 0, 0.2));
        Assert.Equal(1, MachineJob.Crossed(moments, 0.2, 0.25));
        Assert.Equal(0, MachineJob.Crossed(moments, 0.25, 0.3));
        Assert.Equal(2, MachineJob.Crossed(moments, 0, 1));
    }

    [Fact]
    public void A_saved_fold_is_clamped_and_a_bad_class_is_none()
    {
        Assert.Equal(new MachineJob(2, 1, 1), Folding.Restore(2, 7));
        Assert.Equal(new MachineJob(1, 0, 1), Folding.Restore(1, double.NaN));
        Assert.Equal(MachineJob.None, Folding.Restore(3, 0.5));
    }

    [Fact]
    public void A_player_holds_the_lever_until_they_let_go_or_go_quiet()
    {
        // the shared hand holds (BEMachine.HandWorked), at the quern's 600 ms
        var holds = new HandHolds();
        Assert.False(holds.Any(0));
        holds.Hold("a", 1000);
        Assert.True(holds.Any(1000 + HandHolds.DefaultTimeoutMs));
        holds.Hold("b", 1500);
        Assert.True(holds.Any(1000 + HandHolds.DefaultTimeoutMs + 1));   // a went quiet, b holds
        holds.Release("b");
        Assert.False(holds.Any(1600));
        holds.Hold("c", 2000);
        holds.Clear();
        Assert.False(holds.Any(2000));
    }

    // ---- The renderer's clock ----

    [Fact]
    public void The_clock_turns_theta_and_predicts_W_only_while_held_and_follows_the_server()
    {
        var clock = new PressBrakeClock();
        clock.Advance(0.1f, 1, 0, held: false, 6);
        Assert.Equal((0.0, 0.0, 1), (clock.Theta, clock.Work, clock.Class));
        Assert.Equal(0.25f, clock.Presence, 5);
        Assert.True(clock.ShowsPlate("platelead"));
        Assert.False(clock.ShowsPlate("platecopper"));
        // held for 0.1 s: a tenth of a lever turn, a sixtieth of a lead plate, before the server's W
        // has moved (it is predicted: HeldWorkFollower)
        clock.Advance(0.1f, 1, 0, held: true, 6);
        Assert.Equal(0.2 * Math.PI, clock.Theta, 6);
        Assert.Equal(1 / 60.0, clock.Work, 6);
        // held on with the server still at 0, it goes on, never faster than the pace
        double last = clock.Work;
        for (int i = 0; i < 30; i++)
        {
            clock.Advance(1 / 60f, 1, 0, true, 6);
            Assert.InRange(clock.Work - last, 0, 1 / 360.0 + 1e-9);
            last = clock.Work;
        }
        // let go, with the server well ahead: W is taken at once; not held, θ stays
        double theta = clock.Theta;
        clock.Advance(0.01f, 1, 0.4, false, 6);
        Assert.Equal(0.4, clock.Work, 9);
        Assert.Equal(theta, clock.Theta);
        var input = clock.Input();
        Assert.Equal((clock.Theta, Math.Abs(clock.Theta), 0.4, 1), (input.Theta, input.Psi, input.Work, input.Class));
    }

    [Fact]
    public void Held_the_clock_moves_W_every_frame_while_the_servers_W_arrives_in_steps()
    {
        // the server's 50 ms tick (here 66 ms, as it fires on the server's frames) moves W in steps;
        // the shown W moves a little every frame, and never back
        var clock = new PressBrakeClock();
        clock.Advance(1f, 1, 0, false, 1);
        double server = 0, last = 0, sinceTick = 0;
        for (int frame = 0; frame < 50; frame++)
        {
            if ((sinceTick += 1 / 60.0) >= 0.066)
            {
                sinceTick -= 0.066;
                server = Math.Min(1, server + 0.066);
            }
            clock.Advance(1 / 60f, 1, server, true, 1);
            if (frame > 0)
                Assert.InRange(clock.Work - last, 0.5 / 60 - 1e-9, 1.3 / 60);
            last = clock.Work;
        }
    }

    [Fact]
    public void The_clock_holds_W_at_1_and_k_while_p_eases_out_and_hides_the_sheet_once_delivered()
    {
        var clock = new PressBrakeClock();
        clock.Advance(1f, 2, 0.98, false, 9);
        Assert.Equal(1f, clock.Presence);
        clock.Advance(0.2f, 0, 0, false, 9);   // delivered and cleared
        Assert.Equal(1.0, clock.Work);
        Assert.Equal(2, clock.Class);
        Assert.Equal(0.5f, clock.Presence, 5);
        Assert.False(clock.ShowsPlate("platecopper"));
        clock.Advance(0.3f, 0, 0, false, 9);
        Assert.Equal((0f, 0), (clock.Presence, clock.Class));
        // the next plate starts from the server's 0
        clock.Advance(0.1f, 1, 0, false, 6);
        Assert.Equal((0.0, 1), (clock.Work, clock.Class));
        // and so does a second plate of the same metal after the first is done
        clock.Advance(0.1f, 1, 0.99, false, 6);
        clock.Advance(0.1f, 1, 0, false, 6);
        Assert.Equal(0.0, clock.Work);
    }

    // ---- Settings ----

    [Fact]
    public void Defaults_are_the_documented_ones_and_out_of_range_values_fall_back()
    {
        var c = new PressBrakeConfig();
        Assert.Equal((1.5f, 2.25f), (c.LeverTurnsPerPlateLead, c.LeverTurnsPerPlateCopper));
        Assert.Equal(c.LeverTurnsPerPlateLead * 1.5, c.LeverTurnsPerPlateCopper, 3);   // copper half as much again
        Assert.Equal(0, c.LeverTurnsPerPlate(0));
        Assert.Empty(c.Sanitise());
        var bad = new PressBrakeConfig { LeverTurnsPerPlateLead = float.NaN, LeverTurnsPerPlateCopper = -1 };
        Assert.Equal(2, bad.Sanitise().Count);
        Assert.Equal((1.5f, 2.25f), (bad.LeverTurnsPerPlateLead, bad.LeverTurnsPerPlateCopper));
    }

    // The player looks along the brake: it runs away from them, the leaf end nearest.
    [Theory]
    [InlineData(Side.North)]
    [InlineData(Side.East)]
    [InlineData(Side.South)]
    [InlineData(Side.West)]
    public void Placed_the_brake_runs_away_from_the_player(Side look)
    {
        var side = PressBrakeRig.PlacedSide(look);
        Assert.Equal(look.Normal(), Footprint.ToWorld(new Int3(0, 0, 1), side));
    }
}
