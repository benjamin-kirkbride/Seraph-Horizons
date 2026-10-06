using SeraphHorizons.Mod.PicklingTub.Core;
using C = SeraphHorizons.Mod.PicklingTub.Core.PicklingTubConfig;

namespace SeraphHorizons.Mod.Tests;

public class PicklingTubTests
{
    private static readonly TubRuleBook Book = new(new C());

    private static TubRuleConfig Rule(string liquid, string input) =>
        Book.For(liquid, input) ?? throw new Xunit.Sdk.XunitException($"no rule for {input} in {liquid}");

    private static Func<double> Always(double value) => () => value;

    [Fact]
    public void The_acids_pickle_degreased_gears_slowest_to_fastest()
    {
        var vinegar = Rule(C.Vinegar, C.Degreased);
        var sulfuric = Rule(C.Sulfuric, C.Degreased);
        var hydrochloric = Rule(C.Hydrochloric, C.Degreased);
        Assert.Equal((24, 8, 2), (vinegar.Hours, sulfuric.Hours, hydrochloric.Hours));
        foreach (var rule in new[] { vinegar, sulfuric, hydrochloric })
        {
            Assert.Equal(C.Pickled, rule.Output);
            Assert.Equal(C.Bits, rule.Failure);
            Assert.Equal(rule.Hours / 2, rule.GraceHours);
            Assert.Equal(TubRuleKind.Pickle, rule.Kind);
            Assert.Equal(0, rule.LossChance);
        }
        // Brine pickles nothing, and the acids do not rust.
        Assert.Null(Book.For(C.Brine, C.Degreased));
        Assert.Null(Book.For(C.Sulfuric, C.Rusty));
    }

    [Fact]
    public void A_short_acid_dip_takes_a_steel_gear_bare_and_brine_rusts_both()
    {
        foreach (string acid in new[] { C.Vinegar, C.Sulfuric, C.Hydrochloric })
        {
            Assert.Equal(C.SteelBare, Rule(acid, C.Steel).Output);
            Assert.True(Rule(acid, C.Steel).Hours < Rule(acid, C.Degreased).Hours);
            Assert.Null(Book.For(acid, C.SteelBare));
        }
        var steel = Rule(C.Brine, C.Steel);
        var bare = Rule(C.Brine, C.SteelBare);
        Assert.Equal((C.Rusty, C.Rusty), (steel.Output, bare.Output));
        Assert.Equal(48, steel.Hours);
        Assert.Equal(4, bare.Hours);
        Assert.Equal((0.1, 0.1), (steel.LossChance, bare.LossChance));
        Assert.Equal(TubRuleKind.Rust, steel.Kind);
        Assert.Equal(0, steel.LossEveryHours);
        // Pickled gears and degreased ones are not brine's.
        Assert.Null(Book.For(C.Brine, C.Pickled));
    }

    [Fact]
    public void The_brine_settings_feed_its_rules()
    {
        var book = new TubRuleBook(new C { BrineRustHours = 100, BareBrineRustHours = 7, OverRustChance = 0.25, BrineLiquids = ["game:brine*"] });
        Assert.Equal(100, book.For("game:brineportion", C.Steel)!.Hours);
        Assert.Equal(7, book.For("game:brineportion", C.SteelBare)!.Hours);
        Assert.Equal(0.25, book.For("game:brineportion", C.SteelBare)!.LossChance);
    }

    [Fact]
    public void Codes_match_without_case_or_domain_and_the_first_rule_wins()
    {
        Assert.NotNull(Book.For("Acid-Full-Sulfuric", "seraphhorizons:GEAR-DEGREASED"));
        Assert.NotNull(Book.For("brineportion", C.Steel));
        var book = new TubRuleBook(new C
        {
            AcidRules = [new("game:acid-full-*", C.Degreased, C.Pickled, 5, 1, 1), new(C.Sulfuric, C.Degreased, C.Pickled, 9, 1, 1)],
        });
        Assert.Equal(5, book.For(C.Sulfuric, C.Degreased)!.Hours);
        Assert.Equal(5, book.For("game:acid-full-nitric", C.Degreased)!.Hours);
    }

    [Fact]
    public void A_batch_waits_without_liquid_then_soaks_to_done()
    {
        var rule = Rule(C.Sulfuric, C.Degreased);
        var batch = new TubBatch(C.Degreased, 8);
        Assert.Equal(new TubStage(TubPhase.Waiting, 0, 8, 0, 0), batch.StageAt(rule, 1000));
        batch = batch.Start(rule, C.Sulfuric, 1, 100, Always(0.5));
        Assert.Equal(TubPhase.Soaking, batch.StageAt(rule, 100).Phase);
        Assert.Equal(0.5, batch.StageAt(rule, 104).Progress, 6);
        Assert.Equal(8, batch.StageAt(rule, 107.99).Inputs);
        Assert.Equal(new TubStage(TubPhase.Done, 1, 0, 8, 0), batch.StageAt(rule, 108));
        Assert.Equal(108, batch.DoneAt(rule));
        // Time before the start (a clock set back) counts as none.
        Assert.Equal(TubPhase.Soaking, batch.StageAt(rule, 50).Phase);
    }

    [Fact]
    public void Past_done_and_the_grace_the_acid_eats_one_gear_at_a_time_to_bits()
    {
        var rule = Rule(C.Sulfuric, C.Degreased); // 8 h, grace 4 h, a gear an hour
        var batch = new TubBatch(C.Degreased, 8).Start(rule, C.Sulfuric, 1, 0, Always(0.5));
        Assert.Equal(TubPhase.Done, batch.StageAt(rule, 11.99).Phase);
        var first = batch.StageAt(rule, 12);
        Assert.Equal((TubPhase.Eating, 7, 1), (first.Phase, first.Outputs, first.Lost));
        Assert.Equal(6, batch.StageAt(rule, 13).Outputs);
        Assert.Equal(6, batch.StageAt(rule, 13.99).Outputs);
        var last = batch.StageAt(rule, 19);
        Assert.Equal((TubPhase.Dissolved, 0, 8), (last.Phase, last.Outputs, last.Lost));
        Assert.Equal(last, batch.StageAt(rule, 1000));
        // Order of loss: monotonic, one at a time, never more than the batch.
        int previous = 0;
        for (double h = 8; h < 30; h += 0.125)
        {
            int lost = batch.StageAt(rule, h).Lost;
            Assert.InRange(lost - previous, 0, 1);
            previous = lost;
        }
        Assert.Equal(8, previous);
    }

    [Fact]
    public void Taking_out_early_gives_the_input_back_and_the_litre_back_to_the_tub()
    {
        var rule = Rule(C.Vinegar, C.Degreased);
        var batch = new TubBatch(C.Degreased, 5).Start(rule, C.Vinegar, 1, 0, Always(0.5));
        var early = batch.TakeOut(rule, 23.9);
        Assert.Equal([(C.Degreased, 5)], early.Items);
        Assert.Equal(1, early.RefundLitres);
        // A waiting batch drew nothing.
        Assert.Equal(0, new TubBatch(C.Degreased, 5).TakeOut(rule, 0).RefundLitres);
        // No rule any more (a changed setting): the input back.
        Assert.Equal([(C.Degreased, 5)], batch.TakeOut(null, 100).Items);
    }

    [Fact]
    public void Taking_out_after_done_gives_the_output_and_the_bits_and_uses_the_litre()
    {
        var rule = Rule(C.Hydrochloric, C.Degreased); // 2 h, grace 1 h, a gear every 15 min
        var batch = new TubBatch(C.Degreased, 8).Start(rule, C.Hydrochloric, 1, 0, Always(0.5));
        var done = batch.TakeOut(rule, 2.5);
        Assert.Equal([(C.Pickled, 8)], done.Items);
        Assert.Equal(0, done.RefundLitres);
        var eaten = batch.TakeOut(rule, 3.5); // three gears gone
        Assert.Equal([(C.Pickled, 5), (C.Bits, 3)], eaten.Items);
        var gone = batch.TakeOut(rule, 50);
        Assert.Equal([(C.Bits, 8)], gone.Items);
        var lumpy = new TubRuleConfig(C.Sulfuric, C.Degreased, C.Pickled, 1, 0, 1) { FailureQuantity = 3 };
        Assert.Equal([(C.Pickled, 1), (C.Bits, 3)], new TubBatch(C.Degreased, 2).Start(lumpy, C.Sulfuric, 1, 0, Always(1)).TakeOut(lumpy, 1).Items);
    }

    [Fact]
    public void Brine_over_rusts_its_share_at_done_and_never_eats_more_by_time()
    {
        var rule = Rule(C.Brine, C.Steel);
        // Draws below the chance are lost: 0.05 < 0.1 for every gear, 0.5 for none.
        var all = new TubBatch(C.Steel, 8).Start(rule, C.Brine, 1, 0, Always(0.05));
        Assert.Equal(8, all.LossAtDone);
        var none = new TubBatch(C.Steel, 8).Start(rule, C.Brine, 1, 0, Always(0.5));
        Assert.Equal([(C.Rusty, 8)], none.TakeOut(rule, 48).Items);
        Assert.Equal([(C.Rusty, 8)], none.TakeOut(rule, 48 * 100).Items);
        var draws = new Queue<double>([0.01, 0.9, 0.9, 0.09, 0.9, 0.9, 0.9, 0.9]);
        var two = new TubBatch(C.Steel, 8).Start(rule, C.Brine, 1, 0, draws.Dequeue);
        Assert.Equal(new TubStage(TubPhase.Done, 1, 0, 6, 2), two.StageAt(rule, 48));
        Assert.Equal([(C.Rusty, 6), (C.Bits, 2)], two.TakeOut(rule, 60).Items);
        // Hidden until done: early it is the steel gears back, all of them.
        Assert.Equal([(C.Steel, 8)], two.TakeOut(rule, 47).Items);
    }

    [Fact]
    public void A_pickle_first_rusts_in_hours_not_days()
    {
        var steel = Rule(C.Brine, C.Steel);
        var bare = Rule(C.Brine, C.SteelBare);
        var dip = Rule(C.Hydrochloric, C.Steel);
        Assert.True(dip.Hours + bare.Hours < steel.Hours / 4);
        var batch = new TubBatch(C.SteelBare, 8).Start(bare, C.Brine, 1, 0, Always(0.5));
        Assert.True(batch.StageAt(bare, 4).Finished);
        Assert.False(new TubBatch(C.Steel, 8).Start(steel, C.Brine, 1, 0, Always(0.5)).StageAt(steel, 4).Finished);
    }

    [Fact]
    public void The_loss_share_over_a_sample_is_near_the_chance()
    {
        var rule = Rule(C.Brine, C.Steel);
        var random = new Random(4821);
        int lost = 0, total = 0;
        for (int i = 0; i < 2000; i++)
        {
            var batch = new TubBatch(C.Steel, 8).Start(rule, C.Brine, 1, 0, random.NextDouble);
            lost += batch.StageAt(rule, 48).Lost;
            total += 8;
        }
        Assert.InRange(lost / (double)total, 0.09, 0.11);
    }

    [Fact]
    public void Adding_to_a_running_batch_starts_its_clock_again_and_a_waiting_one_just_grows()
    {
        var rule = Rule(C.Sulfuric, C.Degreased);
        var waiting = new TubBatch(C.Degreased, 3).Add(2, rule, 50, Always(0.5));
        Assert.Equal(5, waiting.Count);
        Assert.False(waiting.Started);
        var running = new TubBatch(C.Degreased, 3).Start(rule, C.Sulfuric, 1, 0, Always(0.5)).Add(2, rule, 5, Always(0.5));
        Assert.Equal((5, 5.0), (running.Count, running.StartHours!.Value));
        Assert.Equal(1, running.Litres);
        Assert.Equal(TubPhase.Soaking, running.StageAt(rule, 12).Phase);
    }

    [Fact]
    public void The_tub_takes_known_gears_in_their_liquid_one_batch_at_a_time()
    {
        var sulfuric = Rule(C.Sulfuric, C.Degreased);
        Assert.Equal(TubRefusal.None, Book.CanAdd(C.Degreased, null, null, 0));
        Assert.Equal(TubRefusal.None, Book.CanAdd(C.Degreased, C.Sulfuric, null, 0));
        Assert.Equal(TubRefusal.WrongLiquid, Book.CanAdd(C.Degreased, C.Brine, null, 0));
        Assert.Equal(TubRefusal.None, Book.CanAdd(C.Steel, C.Brine, null, 0));
        Assert.Equal(TubRefusal.NotAGear, Book.CanAdd("game:gear-temporal", null, null, 0));
        Assert.Equal(TubRefusal.NotAGear, Book.CanAdd(C.Rusty, C.Brine, null, 0));
        Assert.Equal(TubRefusal.Refused, Book.CanAdd("seraphhorizons:largegear-steel", C.Brine, null, 0));
        var batch = new TubBatch(C.Degreased, 6).Start(sulfuric, C.Sulfuric, 1, 0, Always(0.5));
        Assert.Equal(TubRefusal.None, Book.CanAdd(C.Degreased, C.Sulfuric, batch, 1));
        Assert.Equal(TubRefusal.OtherBatch, Book.CanAdd(C.Steel, C.Sulfuric, batch, 1));
        Assert.Equal(TubRefusal.BatchFinished, Book.CanAdd(C.Degreased, C.Sulfuric, batch, 8));
        Assert.Equal(TubRefusal.Full, Book.CanAdd(C.Degreased, C.Sulfuric, batch with { Count = 8 }, 1));
        // The batch's own liquid decides, even when the tub's free liquid is gone.
        Assert.Equal(TubRefusal.None, Book.CanAdd(C.Degreased, null, batch, 1));
    }

    [Fact]
    public void Liquids_pour_on_gears_they_have_a_rule_for()
    {
        Assert.True(Book.CanPour(C.Brine, null));
        Assert.True(Book.CanPour(C.Vinegar, null));
        Assert.False(Book.CanPour("game:waterportion", null));
        Assert.False(Book.CanPour(C.Brine, new TubBatch(C.Degreased, 2)));
        Assert.True(Book.CanPour(C.Hydrochloric, new TubBatch(C.Degreased, 2)));
    }

    [Fact]
    public void Litres_and_items_convert_by_items_per_litre_and_fill_to_the_capacity()
    {
        Assert.Equal(100, TubLiquid.Items(1, 100));
        Assert.Equal(1.5, TubLiquid.Litres(150, 100));
        Assert.Equal(900, TubLiquid.ItemsThatFit(10, 100, 1000, 100));
        Assert.Equal(0, TubLiquid.ItemsThatFit(10, 1000, 5, 100));
        Assert.Equal(0, TubLiquid.ItemsThatFit(10, 0, 5, 0));
    }

    [Fact]
    public void Broken_settings_fall_back_and_broken_rules_are_dropped()
    {
        var config = new C
        {
            BatchSize = 0,
            CapacityLitres = -1,
            LitresPerBatch = 50,
            BrineRustHours = double.NaN,
            BareBrineRustHours = 0,
            OverRustChance = 2,
            AcidRules = [new(C.Vinegar, C.Degreased, C.Pickled, 0, 1, 1), new("", C.Degreased, C.Pickled, 1, 1, 1),
                new(C.Vinegar, C.Degreased, C.Pickled, 2, 1, 1) { LossChance = -0.5 }, new(C.Sulfuric, C.Degreased, C.Pickled, 3, 1, 1)],
        };
        var fixes = config.Sanitise();
        Assert.Equal(9, fixes.Count);
        Assert.Equal((8, 10.0, 1.0, 48.0, 4.0, 0.1), (config.BatchSize, config.CapacityLitres, config.LitresPerBatch,
            config.BrineRustHours, config.BareBrineRustHours, config.OverRustChance));
        Assert.Equal(C.Sulfuric, Assert.Single(config.AcidRules).Liquid);
        Assert.Empty(new C().Sanitise());
    }
}
