using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.MandrelStation.Core;
using Xunit;

namespace SeraphHorizons.Tests.MandrelStation;

/// <summary>MandrelStation/Core: the mandrel (no take-back: it comes out only by breaking); the hollows
/// by metal and what is refused; the forging's arithmetic (a blow at a time, two sections at the last);
/// right-click held refilling from the hotbar; the renderer's clock; the settings; and the rig reader, on
/// a rig written to the contract.</summary>
public class MandrelStationGameplayTests
{
    // ---- The mandrel ----

    [Theory]
    [InlineData("game:rod-iron", "iron")]
    [InlineData("game:rod-meteoriciron", "meteoriciron")]
    [InlineData("rod-steel", "steel")]   // a code with no domain is the game's
    public void Each_rod_is_a_mandrel_of_its_metal(string code, string metal)
    {
        Assert.True(MandrelPart.IsMandrel(code));
        Assert.Equal(metal, MandrelPart.MetalOf(code));
        Assert.Equal(MandrelFitVerdict.Fits, MandrelPart.CanFit(code, null));
        Assert.Equal(MandrelFitVerdict.AlreadyFitted, MandrelPart.CanFit(code, "game:rod-iron"));
    }

    [Theory]
    [InlineData("game:rod-copper")]
    [InlineData("game:metalplate-iron")]
    [InlineData("game:chutesection-lead")]
    [InlineData("seraphhorizons:pipesection-iron")]
    [InlineData(null)]
    public void Anything_else_is_not_a_mandrel(string? code)
    {
        Assert.False(MandrelPart.IsMandrel(code));
        Assert.Null(MandrelPart.MetalOf(code));
        Assert.Equal(MandrelFitVerdict.NotAMandrel, MandrelPart.CanFit(code, null));
    }

    [Fact]
    public void The_mandrel_is_drawn_once_fitted_and_never_the_hollows()
    {
        Assert.True(MandrelPart.Fitted(null, null));
        Assert.False(MandrelPart.Fitted("mandrel", null));
        Assert.True(MandrelPart.Fitted("mandrel", "game:rod-iron"));
        Assert.False(MandrelPart.Fitted("hollowlead", "game:rod-iron"));
        Assert.False(MandrelPart.Fitted("hollowcopper", "game:rod-iron"));
        Assert.True(MandrelRequires.KnownRequires.SetEquals(["mandrel", "hollowlead", "hollowcopper"]));
        Assert.Equal("hollowlead", MandrelRequires.Hollow(1));
        Assert.Equal("hollowcopper", MandrelRequires.Hollow(2));
        Assert.Null(MandrelRequires.Hollow(0));
    }

    [Fact]
    public void A_saved_mandrel_is_restored_only_if_it_is_one()
    {
        Assert.Equal("game:rod-steel", MandrelPart.Restore("game:rod-steel"));
        Assert.Equal("game:rod-iron", MandrelPart.Restore("rod-iron"));
        Assert.Null(MandrelPart.Restore("game:rod-copper"));
        Assert.Null(MandrelPart.Restore(null));
        Assert.Null(MandrelPart.Restore(""));
    }

    // ---- What goes on ----

    [Fact]
    public void Lead_and_copper_hollows_go_on_by_metal()
    {
        Assert.Equal(1, Forging.ClassOfHollow("game:chutesection-lead"));
        Assert.Equal(2, Forging.ClassOfHollow("game:chutesection-copper"));
        Assert.Equal(("game:chutesection-lead", "game:chutesection-copper"), (Forging.HollowFor(1), Forging.HollowFor(2)));
        Assert.Equal(("seraphhorizons:pipesection-lead", "seraphhorizons:pipesection-copper"), (Forging.SectionFor(1), Forging.SectionFor(2)));
        Assert.Null(Forging.SectionFor(0));
        Assert.Equal(("lead", "copper"), (Forging.MetalOf(1), Forging.MetalOf(2)));
    }

    [Theory]
    [InlineData("seraphhorizons:angle-lead")]
    [InlineData("seraphhorizons:angle-copper")]
    [InlineData("seraphhorizons:pipesection-lead")]
    [InlineData("seraphhorizons:pipesection-copper")]
    [InlineData("game:ingot-copper")]
    [InlineData("game:metalplate-lead")]
    [InlineData("game:chutesection-iron")]
    [InlineData(null)]
    public void An_angle_a_pipe_section_or_an_ingot_never_goes_on(string? code)
    {
        Assert.Equal(0, Forging.ClassOfHollow(code));
        Assert.Equal(HollowLoadVerdict.NotAHollow, Forging.CanLoad(code, mandrel: true, occupied: false));
    }

    [Fact]
    public void A_hollow_needs_a_mandrel_and_a_bare_one()
    {
        Assert.Equal(HollowLoadVerdict.Loads, Forging.CanLoad(Forging.LeadHollow, mandrel: true, occupied: false));
        Assert.Equal(HollowLoadVerdict.NoMandrel, Forging.CanLoad(Forging.LeadHollow, mandrel: false, occupied: false));
        Assert.Equal(HollowLoadVerdict.Occupied, Forging.CanLoad(Forging.CopperHollow, mandrel: true, occupied: true));
    }

    [Theory]
    [InlineData("game:hammer-copper", true)]
    [InlineData("game:hammer-iron", true)]
    [InlineData("hammer-steel", true)]
    [InlineData("game:chisel-iron", false)]
    [InlineData("seraphhorizons:hammer-iron", false)]
    [InlineData(null, false)]
    public void Any_game_hammer_strikes(string? code, bool hammer) => Assert.Equal(hammer, Forging.IsHammer(code));

    // ---- Blows ----

    [Fact]
    public void Each_blow_advances_W_by_one_over_the_blows_and_the_last_finishes()
    {
        var job = new ForgeJob(1, 0, 0);
        Assert.True(job.Untouched);
        for (int i = 1; i < 6; i++)
        {
            (job, bool finished) = job.Strike(6);
            Assert.False(finished, $"finished at blow {i}");
            Assert.Equal(i, job.Blows);
            Assert.Equal(i / 6.0, job.Work, 9);
            Assert.False(job.Untouched);
        }
        (job, bool last) = job.Strike(6);
        Assert.True(last);
        Assert.True(job.Done);
        Assert.Equal(1.0, job.Work);
        // nothing more after the end
        var (after, again) = job.Strike(6);
        Assert.False(again);
        Assert.Equal(job, after);
    }

    [Fact]
    public void Copper_takes_more_blows_than_lead_at_the_defaults()
    {
        var config = new MandrelStationConfig();
        Assert.Equal(9, config.BlowsPerHollow(1));
        Assert.Equal(14, config.BlowsPerHollow(2));
        Assert.Equal(0, config.BlowsPerHollow(0));
        int Blows(int k)
        {
            var job = new ForgeJob(k, 0, 0);
            int n = 0;
            for (bool done = false; !done; n++)
                (job, done) = job.Strike(config.BlowsPerHollow(k));
            return n;
        }
        Assert.Equal(9, Blows(1));
        Assert.Equal(14, Blows(2));
    }

    // ---- The hammer's tier ----

    // The game's hammers' tool tiers (survival itemtypes/tool/hammer.json, tooltierbytype): copper,
    // gold and silver 2, the three bronzes 3, iron and meteoric iron 4, steel 5.
    [Fact]
    public void The_base_hammer_is_the_copper_one_at_the_games_tier()
    {
        Assert.Equal(2, MandrelStationConfig.Defaults.BaseHammerTier);
        Assert.Equal(1.0, Forging.BlowWeight(2, 2));
        Assert.Equal(1.5, Forging.BlowWeight(3, 2));
        Assert.Equal(2.0, Forging.BlowWeight(4, 2));
        Assert.Equal(2.5, Forging.BlowWeight(5, 2));
        Assert.Equal(0.5, Forging.BlowWeight(1, 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void A_hammer_with_no_tier_counts_as_the_base(int tier)
    {
        Assert.Equal(1.0, Forging.BlowWeight(tier, 2));
        Assert.Equal(1 / 6.0, Forging.WorkPerBlow(6, tier, 2), 12);
        Assert.Equal(6, Forging.BlowsWith(6, tier, 2));
        Assert.Equal(new ForgeJob(1, 0, 0).Strike(6), new ForgeJob(1, 0, 0).Strike(6, tier, 2));
    }

    [Theory]
    // tier, lead's blows, copper's blows at the defaults (9 and 14 for the copper hammer)
    [InlineData(2, 9, 14)]   // copper, gold, silver
    [InlineData(3, 6, 10)]   // tin, bismuth and black bronze
    [InlineData(4, 5, 7)]    // iron, meteoric iron
    [InlineData(5, 4, 6)]    // steel
    public void A_better_hammer_forges_in_fewer_blows_by_its_tier(int tier, int lead, int copper)
    {
        var config = new MandrelStationConfig();
        int Blows(int k)
        {
            var job = new ForgeJob(k, 0, 0);
            int n = 0;
            for (bool done = false; !done; n++)
                (job, done) = job.Strike(config.BlowsPerHollow(k), tier, config.BaseHammerTier);
            Assert.Equal(1.0, job.Work);
            Assert.Equal(n, job.Blows);
            return n;
        }
        Assert.Equal((lead, copper), (Blows(1), Blows(2)));
        Assert.Equal((lead, copper), (Forging.BlowsWith(9, tier, 2), Forging.BlowsWith(14, tier, 2)));
    }

    [Fact]
    public void A_steel_blow_forges_two_and_a_half_copper_blows()
    {
        var (job, finished) = new ForgeJob(1, 0, 0).Strike(6, 5, 2);
        Assert.False(finished);
        Assert.Equal(2.5 / 6, job.Work, 12);
        // the third blow overshoots and is held at 1
        (job, _) = job.Strike(6, 5, 2);
        (job, finished) = job.Strike(6, 5, 2);
        Assert.True(finished);
        Assert.Equal((3, 1.0), (job.Blows, job.Work));
    }

    [Fact]
    public void One_blow_never_forges_more_than_half_a_hollow()
    {
        Assert.Equal(0.5, Forging.MaxWorkPerBlow);
        // a steel hammer on a hollow set to take one copper blow: capped at half
        Assert.Equal(0.5, Forging.WorkPerBlow(1, 5, 2));
        Assert.Equal(0.5, Forging.WorkPerBlow(2, 5, 2));
        Assert.Equal(2, Forging.BlowsWith(1, 5, 2));
        var (job, finished) = new ForgeJob(2, 0, 0).Strike(1, 5, 2);
        Assert.False(finished);
        Assert.Equal(0.5, job.Work);
        // under the cap it is untouched
        Assert.Equal(2.5 / 6, Forging.WorkPerBlow(6, 5, 2), 12);
        Assert.Equal(0, Forging.WorkPerBlow(0, 5, 2));
        Assert.Equal(0, Forging.BlowsWith(0, 5, 2));
    }

    [Fact]
    public void No_hollow_takes_no_blows()
    {
        var (job, finished) = ForgeJob.None.Strike(6);
        Assert.False(finished);
        Assert.Equal(ForgeJob.None, job);
        Assert.False(ForgeJob.None.On);
        Assert.False(ForgeJob.None.Untouched);
        Assert.Equal((new ForgeJob(1, 0, 0), false), new ForgeJob(1, 0, 0).Strike(0));
    }

    [Fact]
    public void Two_sections_come_off_a_hollow() => Assert.Equal(2, Forging.SectionsPerHollow);

    [Fact]
    public void A_saved_job_is_clamped_and_needs_a_hollows_class()
    {
        Assert.Equal(new ForgeJob(2, 4, 0.5), ForgeJob.Restore(2, 4, 0.5));
        Assert.Equal(new ForgeJob(1, 0, 1), ForgeJob.Restore(1, -3, 7));
        Assert.Equal(new ForgeJob(1, 2, 0), ForgeJob.Restore(1, 2, double.NaN));
        Assert.Equal(ForgeJob.None, ForgeJob.Restore(3, 2, 0.5));
        Assert.Equal(ForgeJob.None, ForgeJob.Restore(0, 2, 0.5));
    }

    // ---- Right-click held: the next hollow from the hotbar ----

    [Fact]
    public void A_click_counts_as_held_within_the_clients_repeat()
    {
        Assert.True(Forging.Held(250));
        Assert.True(Forging.Held(Forging.HeldClickGapMs));
        Assert.False(Forging.Held(Forging.HeldClickGapMs + 1));
        Assert.False(Forging.Held(-1));
        Assert.True(Forging.HeldClickGapMs > 2 * 250, "two of the client's repeats, one of them not yet a blow");
    }

    [Fact]
    public void The_hotbar_refills_with_the_same_hollow_only()
    {
        string?[] hotbar = ["game:hammer-copper", null, Forging.CopperHollow, "game:ingot-lead", Forging.LeadHollow, Forging.LeadHollow];
        Assert.Equal(4, Forging.RefillSlot(hotbar, Forging.LeadHollow));
        Assert.Equal(4, Forging.RefillSlot(hotbar, "chutesection-lead"));
        Assert.Equal(2, Forging.RefillSlot(hotbar, Forging.CopperHollow));
        // nothing finished, or something that is not a hollow
        Assert.Equal(-1, Forging.RefillSlot(hotbar, null));
        Assert.Equal(-1, Forging.RefillSlot(hotbar, "game:ingot-lead"));
        Assert.Equal(-1, Forging.RefillSlot(["game:hammer-copper", Forging.CopperHollow], Forging.LeadHollow));
    }

    [Fact]
    public void The_refill_never_takes_from_the_skill_slot_or_the_off_hand()
    {
        var hotbar = Enumerable.Repeat<string?>(null, 12).ToArray();
        hotbar[10] = Forging.LeadHollow;
        hotbar[11] = Forging.LeadHollow;
        Assert.Equal(-1, Forging.RefillSlot(hotbar, Forging.LeadHollow));
        hotbar[9] = Forging.LeadHollow;
        Assert.Equal(9, Forging.RefillSlot(hotbar, Forging.LeadHollow));
    }

    [Fact]
    public void Blows_are_no_faster_than_a_smith_swings()
    {
        Assert.False(Forging.Ready(Forging.BlowIntervalMs - 1));
        Assert.True(Forging.Ready(Forging.BlowIntervalMs));
        Assert.True(Forging.Ready(long.MaxValue / 2));
    }

    // ---- The renderer's clock ----

    [Fact]
    public void The_clock_eases_W_and_the_hammer_to_each_blow()
    {
        var clock = new MandrelStationClock();
        Assert.Equal(1, clock.Work);
        clock.Advance(0.05f, 1, 0, 0);
        Assert.Equal(1, clock.Class);
        Assert.Equal(0, clock.Work);
        Assert.True(clock.ShowsHollow("hollowlead"));
        Assert.False(clock.ShowsHollow("hollowcopper"));
        // a blow lands: W and θ ease to it, part of the way in a frame, all of it in a second
        clock.Advance(0.016f, 1, 1 / 6.0, 1);
        Assert.InRange(clock.Work, 0.01, 1 / 6.0 - 0.01);
        Assert.InRange(clock.Theta, 0.1, 2 * Math.PI - 0.1);
        for (int i = 0; i < 60; i++)
            clock.Advance(0.016f, 1, 1 / 6.0, 1);
        Assert.Equal(1 / 6.0, clock.Work, 4);
        Assert.Equal(2 * Math.PI, clock.Theta, 3);
        Assert.Equal(1f, clock.Presence);
        var input = clock.Input();
        Assert.Equal(1, input.Class);
        Assert.Equal(clock.Work, input.Work);
    }

    [Fact]
    public void Once_delivered_W_holds_at_the_end_while_p_eases_out_and_k_is_held()
    {
        var clock = new MandrelStationClock();
        for (int i = 0; i < 30; i++)
            clock.Advance(0.05f, 2, 8 / 9.0, 8);
        clock.Advance(0.1f, 0, 0, 0);
        Assert.Equal(1, clock.Work);
        Assert.Equal(2, clock.Class);
        Assert.InRange(clock.Presence, 0.01f, 0.99f);
        Assert.False(clock.ShowsHollow("hollowcopper"));   // delivered: the sections are items now
        for (int i = 0; i < 10; i++)
            clock.Advance(0.1f, 0, 0, 0);
        Assert.Equal(0f, clock.Presence);
        Assert.Equal(0, clock.Class);
        // the next hollow starts from the server's W
        clock.Advance(0.05f, 1, 0, 0);
        Assert.Equal((1, 0.0), (clock.Class, clock.Work));
    }

    [Fact]
    public void The_next_hollow_of_the_same_metal_starts_again_from_zero()
    {
        var clock = new MandrelStationClock();
        for (int i = 0; i < 30; i++)
            clock.Advance(0.05f, 1, 5 / 6.0, 5);
        Assert.Equal(5 / 6.0, clock.Work, 3);
        // the server finished it and loaded the next between two frames
        clock.Advance(0.05f, 1, 0, 0);
        Assert.Equal(0, clock.Work);
        Assert.Equal(0, clock.Theta);
    }

    // ---- Settings ----

    [Fact]
    public void Settings_out_of_range_fall_back_to_their_defaults()
    {
        var config = new MandrelStationConfig { BlowsPerHollowLead = 0, BlowsPerHollowCopper = 5000, HammerWearPerBlow = -1, BaseHammerTier = 0 };
        var fixes = config.Sanitise();
        Assert.Equal(4, fixes.Count);
        Assert.Equal((9, 14, 1, 2), (config.BlowsPerHollowLead, config.BlowsPerHollowCopper, config.HammerWearPerBlow, config.BaseHammerTier));
        Assert.Empty(new MandrelStationConfig { BlowsPerHollowLead = 1, HammerWearPerBlow = 0, BaseHammerTier = 1 }.Sanitise());
    }

    // ---- The rig reader, on a rig written to the contract ----

    private const string ContractRig = """
        {
          // as the contract has it, with no parts
          "cells": [
            { "pos": [0, 0, 0], "boxes": [[0, 0, 0, 1, 0.75, 1]], "lid": 0.75 },
            { "pos": [0, 0, 1], "boxes": [[0.25, 0, 0, 0.75, 0.8, 0.5]], "lid": 0.8 }
          ],
          "outputSide": "south",
          "output": { "pos": [0.5, 0.375, 1.75] },
          "strike": { "pos": [0.5, 0.8, 0.9] },
          "work": { "name": "blows", "unit": "hollows", "step": 0.005, "end": { "thin": 1, "thick": 1 } },
          "forge": {
            "blowsPerHollow": { "thin": 6, "thick": 9 },
            "hollows": { "thin": "game:chutesection-lead", "thick": "game:chutesection-copper" },
            "sections": { "thin": "seraphhorizons:pipesection-lead", "thick": "seraphhorizons:pipesection-copper" },
            "sectionsPerHollow": 2
          },
        }
        """;

    [Fact]
    public void The_reader_takes_a_rig_written_to_the_contract()
    {
        var rig = MandrelStationRig.Parse(ContractRig);
        Assert.Equal([Int3.Zero, new Int3(0, 0, 1)], rig.Cells.Select(c => c.Pos));
        Assert.Single(rig.GhostCells);
        Assert.Equal(Side.South, rig.OutputSide);
        // the sections leave past the tip
        Assert.Equal(new Int3(0, 0, 2), rig.OutputNeighbour());
        Assert.Equal(2.15f, rig.OutputDrop().Z, 4);
        Assert.Equal(new Float3(0.5f, 0.8f, 0.9f), rig.Strike);
        Assert.Equal([0, 6, 9], rig.BlowsPerHollow);
        Assert.Equal("hollows", rig.Work.Unit);
        Assert.Empty(rig.MovingParts.Parts);
    }

    [Theory]
    [InlineData("\"sectionsPerHollow\": 2", "\"sectionsPerHollow\": 4")]
    [InlineData("\"thin\": 6", "\"thin\": 0")]
    [InlineData("\"thin\": 6", "\"thin\": 6.5")]
    [InlineData("\"game:chutesection-copper\"", "\"game:ingot-copper\"")]
    [InlineData("\"seraphhorizons:pipesection-lead\"", "\"seraphhorizons:pipesection-copper\"")]
    [InlineData("\"blowsPerHollow\"", "\"blowsPerSection\"")]
    [InlineData("\"strike\"", "\"hammer\"")]
    [InlineData("\"outputSide\"", "\"powerFace\": \"east\", \"outputSide\"")]
    [InlineData("\"end\": { \"thin\": 1, \"thick\": 1 }", "\"end\": { \"thin\": 6, \"thick\": 9 }")]
    [InlineData("\"pos\": [0, 0, 0]", "\"pos\": [0, 0, 1]")]
    public void The_reader_refuses_a_rig_that_is_not_the_contracts(string from, string to)
    {
        var json = ContractRig.Replace(from, to);
        Assert.NotEqual(ContractRig, json);
        Assert.Throws<FormatException>(() => MandrelStationRig.Parse(json));
    }

    [Fact]
    public void Placed_side_is_the_look()
    {
        foreach (var side in Sides.All)
            Assert.Equal(side, MandrelStationRig.PlacedSide(side));
    }
}
