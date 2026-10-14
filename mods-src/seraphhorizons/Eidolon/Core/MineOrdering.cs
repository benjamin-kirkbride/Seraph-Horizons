namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>One member's stations put in the order that finishes them soonest from where it starts
/// (README "Eidolon", working together, *Routes*): Held-Karp over subsets, exact, with each station's
/// release time (it may not start before) and precedence (pairs that must keep their order). A state is the
/// set done and the last; it keeps every finish and walk that no other at it beats on both (waiting is
/// allowed and a finish only ever moves a later one later, so that is exact for the finish and then the
/// walk), and of two equal the one done in the smaller order. Ties go to the smaller order, so the result
/// is the same wherever it runs.</summary>
public static class MineOrdering
{
    /// <summary>The most stations it orders: 12 · 12 · 4096 ≈ 590 k steps; routes use at most
    /// <see cref="MineRoutes.MaxStops"/>.</summary>
    public const int MaxStations = 12;

    // labels kept at one state at most (the front is rarely more than a few)
    private const int MaxLabels = 32;

    private const double Eps = 1e-9;

    private struct Label
    {
        public double Finish, Travel;
        public int PrevLast, PrevLabel;
        public bool Dead;
    }

    /// <param name="start">when it is free at its start place</param>
    /// <param name="travel">travel[i, j]: seconds from stop i's stand to j's, index n = its start place</param>
    /// <param name="work">seconds at each</param>
    /// <param name="release">earliest start of work at each</param>
    /// <param name="before">before[j]: bitmask of stops that must finish before j starts</param>
    /// <returns>the order (indices), and the finish time of the last; null when precedence is cyclic</returns>
    public static (int[] Order, double Finish)? Order(double start, double[,] travel, double[] work, double[] release, int[] before)
    {
        int n = work.Length;
        if (n > MaxStations)
            throw new ArgumentException($"at most {MaxStations} stations", nameof(work));
        if (n == 0)
            return ([], start);
        if (Cyclic(n, before))
            return null;
        int full = (1 << n) - 1;
        var labels = new List<Label>?[(full + 1) * n];
        for (int j = 0; j < n; j++)
            if (before[j] == 0)
                Add(labels, n, 1 << j, j, Step(start, travel[n, j], release[j], work[j]), travel[n, j], -1, -1);
        for (int mask = 1; mask < full; mask++)
        for (int last = 0; last < n; last++)
        {
            if ((mask & (1 << last)) == 0 || labels[mask * n + last] is not { } list)
                continue;
            for (int k = 0; k < list.Count; k++)
            {
                var l = list[k];
                if (l.Dead)
                    continue;
                for (int j = 0; j < n; j++)
                {
                    if ((mask & (1 << j)) != 0 || (before[j] & ~mask) != 0)
                        continue;
                    Add(labels, n, mask | (1 << j), j, Step(l.Finish, travel[last, j], release[j], work[j]), l.Travel + travel[last, j], last, k);
                }
            }
        }
        (int Last, int Label)? best = null;
        for (int last = 0; last < n; last++)
        {
            if (labels[full * n + last] is not { } list)
                continue;
            for (int k = 0; k < list.Count; k++)
            {
                if (list[k].Dead)
                    continue;
                if (best is not { } b || Better(labels, n, full, last, k, b.Last, b.Label))
                    best = (last, k);
            }
        }
        var (bl, bk) = best!.Value;
        return (Sequence(labels, n, full, bl, bk), labels[full * n + bl]![bk].Finish);
    }

    /// <summary><see cref="Order"/> keeping one label a state (the least finish, then walk): exact for the
    /// finish, the walk a tie-break only; for weighing many moves quickly (the planner's relocations).</summary>
    public static (int[] Order, double Finish)? Quick(double start, double[,] travel, double[] work, double[] release, int[] before)
    {
        int n = work.Length;
        if (n == 0)
            return ([], start);
        if (n > MaxStations || Cyclic(n, before))
            return null;
        int full = (1 << n) - 1, size = (full + 1) * n;
        var finish = new double[size];
        var walk = new double[size];
        var prev = new int[size];
        Array.Fill(finish, double.PositiveInfinity);
        for (int j = 0; j < n; j++)
            if (before[j] == 0)
            {
                int s = (1 << j) * n + j;
                finish[s] = Step(start, travel[n, j], release[j], work[j]);
                walk[s] = travel[n, j];
                prev[s] = -1;
            }
        for (int mask = 1; mask < full; mask++)
        for (int last = 0; last < n; last++)
        {
            int s = mask * n + last;
            if (double.IsPositiveInfinity(finish[s]))
                continue;
            for (int j = 0; j < n; j++)
            {
                if ((mask & (1 << j)) != 0 || (before[j] & ~mask) != 0)
                    continue;
                int t = (mask | (1 << j)) * n + j;
                double f = Step(finish[s], travel[last, j], release[j], work[j]), w = walk[s] + travel[last, j];
                if (f < finish[t] - Eps || f < finish[t] + Eps && w < walk[t] - Eps)
                {
                    finish[t] = f;
                    walk[t] = w;
                    prev[t] = last;
                }
            }
        }
        int best = -1;
        for (int last = 0; last < n; last++)
        {
            int s = full * n + last;
            if (double.IsPositiveInfinity(finish[s]))
                continue;
            if (best < 0 || finish[s] < finish[full * n + best] - Eps
                || finish[s] < finish[full * n + best] + Eps && walk[s] < walk[full * n + best] - Eps)
                best = last;
        }
        if (best < 0)
            return null;
        var order = new int[n];
        int m = full, at = best;
        for (int k = n - 1; k >= 0; k--)
        {
            order[k] = at;
            int p = prev[m * n + at];
            m &= ~(1 << at);
            at = p;
        }
        return (order, finish[full * n + best]);
    }

    private static double Step(double free, double travel, double release, double work) => Math.Max(free + travel, release) + work;

    private static bool Cyclic(int n, int[] before)
    {
        int done = 0;
        for (int round = 0; round < n; round++)
        {
            bool moved = false;
            for (int j = 0; j < n; j++)
                if ((done & (1 << j)) == 0 && (before[j] & ~done & ((1 << n) - 1)) == 0)
                {
                    done |= 1 << j;
                    moved = true;
                }
            if (!moved)
                break;
        }
        return done != (1 << n) - 1;
    }

    private static void Add(List<Label>?[] labels, int n, int mask, int last, double finish, double travel, int prevLast, int prevLabel)
    {
        var list = labels[mask * n + last] ??= new List<Label>(2);
        for (int k = 0; k < list.Count; k++)
        {
            var l = list[k];
            if (l.Dead)
                continue;
            bool noWorse = l.Finish <= finish + Eps && l.Travel <= travel + Eps;
            if (noWorse)
            {
                bool equal = Math.Abs(l.Finish - finish) <= Eps && Math.Abs(l.Travel - travel) <= Eps;
                if (!equal || Compare(labels, n, mask, last, k, prevLast, prevLabel) <= 0)
                    return;
                l.Dead = true;
                list[k] = l;
                continue;
            }
            if (finish <= l.Finish + Eps && travel <= l.Travel + Eps)
            {
                l.Dead = true;
                list[k] = l;
            }
        }
        list.Add(new Label { Finish = finish, Travel = travel, PrevLast = prevLast, PrevLabel = prevLabel });
        int alive = 0;
        foreach (var l in list)
            if (!l.Dead)
                alive++;
        if (alive <= MaxLabels)
            return;
        // too many: the one with the latest finish goes
        int worst = -1;
        for (int k = 0; k < list.Count; k++)
            if (!list[k].Dead && (worst < 0 || list[k].Finish > list[worst].Finish))
                worst = k;
        var w = list[worst];
        w.Dead = true;
        list[worst] = w;
    }

    // a kept label's order against a new one's (its last and the label before it): < 0 when the kept is smaller
    private static int Compare(List<Label>?[] labels, int n, int mask, int last, int kept, int prevLast, int prevLabel)
    {
        var a = Sequence(labels, n, mask, last, kept);
        int[] b = prevLast < 0 ? [last] : [.. Sequence(labels, n, mask & ~(1 << last), prevLast, prevLabel), last];
        for (int k = 0; k < a.Length && k < b.Length; k++)
            if (a[k] != b[k])
                return a[k].CompareTo(b[k]);
        return a.Length.CompareTo(b.Length);
    }

    private static bool Better(List<Label>?[] labels, int n, int full, int last, int k, int bestLast, int bestK)
    {
        var a = labels[full * n + last]![k];
        var b = labels[full * n + bestLast]![bestK];
        if (a.Finish < b.Finish - Eps)
            return true;
        if (a.Finish > b.Finish + Eps)
            return false;
        if (a.Travel < b.Travel - Eps)
            return true;
        if (a.Travel > b.Travel + Eps)
            return false;
        var sa = Sequence(labels, n, full, last, k);
        var sb = Sequence(labels, n, full, bestLast, bestK);
        for (int i = 0; i < sa.Length; i++)
            if (sa[i] != sb[i])
                return sa[i] < sb[i];
        return false;
    }

    private static int[] Sequence(List<Label>?[] labels, int n, int mask, int last, int label)
    {
        var order = new List<int>();
        while (last >= 0)
        {
            order.Add(last);
            var l = labels[mask * n + last]![label];
            mask &= ~(1 << last);
            (last, label) = (l.PrevLast, l.PrevLabel);
        }
        order.Reverse();
        return order.ToArray();
    }
}
