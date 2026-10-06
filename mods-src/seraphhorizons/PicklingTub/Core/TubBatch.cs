namespace SeraphHorizons.Mod.PicklingTub.Core;

/// <summary>Where a batch is, by the time.</summary>
public enum TubPhase
{
    /// <summary>Gears in, no liquid for them yet: nothing happens.</summary>
    Waiting,
    /// <summary>In the liquid, not done.</summary>
    Soaking,
    /// <summary>Done, within the grace: take it out now.</summary>
    Done,
    /// <summary>Past the grace: the liquid is eating it, a gear at a time.</summary>
    Eating,
    /// <summary>Nothing left but the failure item.</summary>
    Dissolved,
}

/// <summary>A batch's state at one moment: how many gears are still the input, how many are the
/// output, how many are lost (to the chance at done and to the time after it).</summary>
public readonly record struct TubStage(TubPhase Phase, double Progress, int Inputs, int Outputs, int Lost)
{
    /// <summary>Whether the batch is done (in any of the phases from <see cref="TubPhase.Done"/> on).</summary>
    public bool Finished => Phase >= TubPhase.Done;
}

/// <summary>What taking a batch out gives: item codes and quantities, and the litres that go back
/// into the tub (the batch's litre, when it is taken out before done).</summary>
public sealed record TubTakeOut(IReadOnlyList<(string Code, int Quantity)> Items, double RefundLitres);

/// <summary>
/// The batch in a tub: <see cref="Count"/> gears of <see cref="Input"/>. It waits until there is
/// liquid with a rule for it; then it starts (<see cref="StartHours"/>, in-game hours), drawing the
/// batch's litres from the tub into itself and rolling now which gears the chance at done will take
/// (<see cref="LossAtDone"/>), so the whole batch is a function of the time from then on: nothing
/// needs ticking but the litre's accounting. Adding gears to a running batch starts its clock again.
/// </summary>
public sealed record TubBatch(string Input, int Count, string? Liquid = null, double? StartHours = null, double Litres = 0, int LossAtDone = 0)
{
    public bool Started => StartHours != null;

    /// <summary>The in-game hour the batch is done at, under <paramref name="rule"/>; null while waiting.</summary>
    public double? DoneAt(TubRuleConfig rule) => StartHours + rule.Hours;

    /// <summary>The batch at <paramref name="nowHours"/> under <paramref name="rule"/> (the rule for its
    /// input in its liquid).</summary>
    public TubStage StageAt(TubRuleConfig rule, double nowHours)
    {
        if (StartHours is not { } start)
            return new TubStage(TubPhase.Waiting, 0, Count, 0, 0);
        double elapsed = Math.Max(0, nowHours - start);
        if (elapsed < rule.Hours)
            return new TubStage(TubPhase.Soaking, elapsed / rule.Hours, Count, 0, 0);
        int lostAtDone = Math.Clamp(LossAtDone, 0, Count);
        int survivors = Count - lostAtDone;
        int eaten = EatenBy(rule, elapsed - rule.Hours, survivors);
        int outputs = survivors - eaten;
        var phase = eaten == 0 ? TubPhase.Done : outputs == 0 ? TubPhase.Dissolved : TubPhase.Eating;
        return new TubStage(phase, 1, 0, outputs, lostAtDone + eaten);
    }

    /// <summary>Gears the liquid has eaten <paramref name="hoursPastDone"/> after done, of
    /// <paramref name="survivors"/>: none within the grace, the first as it runs out, then one every
    /// <c>LossEveryHours</c>.</summary>
    public static int EatenBy(TubRuleConfig rule, double hoursPastDone, int survivors)
    {
        if (rule.LossEveryHours <= 0 || survivors <= 0)
            return 0;
        double past = hoursPastDone - rule.GraceHours;
        if (past < -1e-9)
            return 0;
        // a hair of slack, so a gear due on the hour is gone on the hour despite rounding
        int eaten = (int)Math.Floor(Math.Max(0, past) / rule.LossEveryHours + 1e-9) + 1;
        return Math.Min(survivors, eaten);
    }

    /// <summary>The batch starting at <paramref name="nowHours"/> in <paramref name="liquid"/>, holding
    /// <paramref name="litres"/> drawn from the tub, its loss at done rolled from <paramref name="next"/>
    /// (uniform in [0, 1)).</summary>
    public TubBatch Start(TubRuleConfig rule, string liquid, double litres, double nowHours, Func<double> next) =>
        this with
        {
            Liquid = liquid,
            StartHours = nowHours,
            Litres = litres,
            LossAtDone = Roll(Count, rule.LossChance, next),
        };

    /// <summary>The batch with <paramref name="more"/> gears added at <paramref name="nowHours"/>: a
    /// running batch starts its clock again (and rolls its loss again); a waiting one just waits.</summary>
    public TubBatch Add(int more, TubRuleConfig? rule, double nowHours, Func<double> next)
    {
        var bigger = this with { Count = Count + Math.Max(0, more) };
        return Started && rule != null
            ? bigger with { StartHours = nowHours, LossAtDone = Roll(bigger.Count, rule.LossChance, next) }
            : bigger;
    }

    /// <summary>What taking the batch out at <paramref name="nowHours"/> gives: before done the input
    /// back, unchanged, and its litres back to the tub; from done the output, and the failure item
    /// for every lost gear. <paramref name="rule"/> is null when there is no rule for it (a changed
    /// setting): the input comes back.</summary>
    public TubTakeOut TakeOut(TubRuleConfig? rule, double nowHours)
    {
        if (rule == null)
            return new TubTakeOut([(Input, Count)], Started ? Litres : 0);
        var stage = StageAt(rule, nowHours);
        if (!stage.Finished)
            return new TubTakeOut([(Input, Count)], Started ? Litres : 0);
        var items = new List<(string, int)>();
        if (stage.Outputs > 0)
            items.Add((rule.Output, stage.Outputs));
        if (stage.Lost > 0 && rule.FailureQuantity > 0)
            items.Add((rule.Failure, stage.Lost * rule.FailureQuantity));
        return new TubTakeOut(items, 0);
    }

    /// <summary>How many of <paramref name="count"/> gears a chance of <paramref name="chance"/> each
    /// takes, each on its own draw from <paramref name="next"/>.</summary>
    public static int Roll(int count, double chance, Func<double> next)
    {
        if (chance <= 0 || count <= 0)
            return 0;
        if (chance >= 1)
            return count;
        int lost = 0;
        for (int i = 0; i < count; i++)
            if (next() < chance)
                lost++;
        return lost;
    }
}

/// <summary>The tub's free liquid: litres from items and back, by the liquid's items per litre.</summary>
public static class TubLiquid
{
    /// <summary>Whole items making up <paramref name="litres"/>, rounded to the nearest (a litre of a
    /// 100-per-litre liquid is 100 items).</summary>
    public static int Items(double litres, float itemsPerLitre) =>
        itemsPerLitre <= 0 ? 0 : (int)Math.Round(litres * itemsPerLitre);

    public static double Litres(int items, float itemsPerLitre) => itemsPerLitre <= 0 ? 0 : items / (double)itemsPerLitre;

    /// <summary>Items of an offered <paramref name="available"/> that fit in a tub of
    /// <paramref name="capacityLitres"/> already holding <paramref name="heldItems"/>.</summary>
    public static int ItemsThatFit(double capacityLitres, int heldItems, int available, float itemsPerLitre)
    {
        if (itemsPerLitre <= 0 || available <= 0)
            return 0;
        int room = Items(capacityLitres, itemsPerLitre) - heldItems;
        return Math.Clamp(room, 0, available);
    }
}
