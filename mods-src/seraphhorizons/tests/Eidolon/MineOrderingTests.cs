using SeraphHorizons.Mod.Eidolon.Core;
using Xunit.Abstractions;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>One member's stations ordered exactly (<see cref="MineOrdering"/>, README "Eidolon", working
/// together, *Routes*): the best of every order by brute force, ten in bounded time, cyclic precedence, and
/// ties.</summary>
public class MineOrderingTests(ITestOutputHelper output)
{
    private sealed record Instance(double[,] Travel, double[] Work, double[] Release, int[] Before);

    private static Instance Random(Random random, int n)
    {
        var travel = new double[n + 1, n + 1];
        for (int i = 0; i <= n; i++)
        for (int j = 0; j <= n; j++)
            travel[i, j] = i == j ? 0 : random.Next(1, 21);
        var work = Enumerable.Range(0, n).Select(_ => (double)random.Next(1, 11)).ToArray();
        var release = Enumerable.Range(0, n).Select(_ => (double)random.Next(0, 41)).ToArray();
        var before = new int[n];
        for (int i = 0; i < n; i++)
        for (int j = i + 1; j < n; j++)
            if (random.NextDouble() < 0.15)
                before[j] |= 1 << i;
        return new Instance(travel, work, release, before);
    }

    // every order that keeps the precedence: its last finish and its walking
    private static (double Finish, double Travel)? Brute(Instance x)
    {
        int n = x.Work.Length;
        (double Finish, double Travel)? best = null;
        foreach (var order in Permutations(Enumerable.Range(0, n).ToList()))
        {
            int done = 0;
            bool ok = true;
            double t = 0, walk = 0;
            int at = n;
            foreach (int j in order)
            {
                if ((x.Before[j] & ~done) != 0)
                {
                    ok = false;
                    break;
                }
                t = Math.Max(t + x.Travel[at, j], x.Release[j]) + x.Work[j];
                walk += x.Travel[at, j];
                at = j;
                done |= 1 << j;
            }
            if (!ok)
                continue;
            if (best is not { } b || t < b.Finish - 1e-9 || Math.Abs(t - b.Finish) <= 1e-9 && walk < b.Travel - 1e-9)
                best = (t, walk);
        }
        return best;
    }

    private static IEnumerable<List<int>> Permutations(List<int> items)
    {
        if (items.Count <= 1)
        {
            yield return items;
            yield break;
        }
        for (int k = 0; k < items.Count; k++)
        {
            var rest = items.Where((_, i) => i != k).ToList();
            foreach (var p in Permutations(rest))
                yield return p.Prepend(items[k]).ToList();
        }
    }

    private static double Walk(Instance x, int[] order)
    {
        double walk = 0;
        int at = x.Work.Length;
        foreach (int j in order)
        {
            walk += x.Travel[at, j];
            at = j;
        }
        return walk;
    }

    [Fact]
    public void Held_Karp_order_is_the_best_of_every_order()
    {
        var random = new Random(374);
        for (int k = 0; k < 300; k++)
        {
            int n = 1 + k % 7;
            var x = Random(random, n);
            var brute = Brute(x);
            var found = MineOrdering.Order(0, x.Travel, x.Work, x.Release, x.Before);
            Assert.NotNull(brute);
            Assert.NotNull(found);
            var (order, finish) = found!.Value;
            Assert.Equal(n, order.Distinct().Count());
            Assert.Equal(brute!.Value.Finish, finish, 6);
            Assert.Equal(brute.Value.Travel, Walk(x, order), 6);
            // the order it gives keeps the precedence
            int done = 0;
            foreach (int j in order)
            {
                Assert.Equal(0, x.Before[j] & ~done);
                done |= 1 << j;
            }
        }
    }

    [Fact]
    public void Ten_stations_are_ordered_in_bounded_time()
    {
        var random = new Random(10);
        var warm = Random(random, 10);
        MineOrdering.Order(0, warm.Travel, warm.Work, warm.Release, warm.Before);
        // each instance's work in thread CPU time, the fastest of three runs (ThreadCpuClock: a shared
        // runner's descheduling and GC pauses are not the ordering's cost)
        double longest = 0;
        for (int k = 0; k < 50; k++)
        {
            var x = Random(random, 10);
            longest = Math.Max(longest, ThreadCpuClock.Fastest(3, () =>
                Assert.NotNull(MineOrdering.Order(0, x.Travel, x.Work, x.Release, x.Before))));
        }
        output.WriteLine($"ten stations: the longest of 50 took {longest:0.0} ms ({ThreadCpuClock.Kind})");
        Assert.True(longest <= 20, $"{longest:0.0} ms");
    }

    [Fact]
    public void A_cyclic_precedence_gives_no_order()
    {
        var travel = new double[4, 4];
        double[] work = [1, 1, 1];
        double[] release = [0, 0, 0];
        // 0 before 1, 1 before 2, 2 before 0
        int[] before = [1 << 2, 1 << 0, 1 << 1];
        Assert.Null(MineOrdering.Order(0, travel, work, release, before));
        Assert.Null(MineOrdering.Quick(0, travel, work, release, before));
    }

    [Fact]
    public void Order_is_deterministic()
    {
        // all alike: every order ties, and the smallest wins
        var travel = new double[6, 6];
        for (int i = 0; i < 6; i++)
        for (int j = 0; j < 6; j++)
            travel[i, j] = i == j ? 0 : 2;
        double[] work = [3, 3, 3, 3, 3];
        double[] release = [0, 0, 0, 0, 0];
        int[] before = [0, 0, 0, 0, 0];
        var a = MineOrdering.Order(5, travel, work, release, before)!.Value;
        var b = MineOrdering.Order(5, travel, work, release, before)!.Value;
        Assert.Equal([0, 1, 2, 3, 4], a.Order);
        Assert.Equal(a.Order, b.Order);
        Assert.Equal(5 + 5 * 5, a.Finish, 9);
        // and the same on random cases, run twice
        var random = new Random(7);
        for (int k = 0; k < 20; k++)
        {
            var x = Random(random, 8);
            Assert.Equal(MineOrdering.Order(0, x.Travel, x.Work, x.Release, x.Before)!.Value.Order,
                MineOrdering.Order(0, x.Travel, x.Work, x.Release, x.Before)!.Value.Order);
        }
    }
}
