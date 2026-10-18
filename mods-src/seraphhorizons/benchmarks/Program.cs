using System.Diagnostics;
using SeraphHorizons.Mod.Eidolon.Core;

namespace SeraphHorizons.Mod.Benchmarks;

/// <summary>
/// The mine planner's real speed (#1232): a few representative areas planned again and again, each plan's
/// checkpoints timed by the planner's own clock (read after each one, <see cref="MinePlanner.Step"/>) and
/// counted in units of work (<see cref="MineWork"/>). It reports, per area, the whole plan's time, each
/// checkpoint's (median, 99th, 99.9th percentile, longest), and what a unit costs: the unit tests bound a
/// checkpoint in units (MinePlannerTests' MaxCheckpointUnits), and this is what turns that into time on
/// this machine. Nothing here passes or fails.
/// </summary>
public static class Program
{
    private const int L = 10;

    private sealed record Case(string Name, string What, Func<MinePlanInput> Input);

    private static readonly Case[] Cases =
    [
        new("stepped-slope", "a tunnel 8 high into a stepped slope (the unit tests' sliced plan)", () =>
        {
            var world = new Voxels().Box(-40, 40, 1, L - 1, -40, 40).Box(0, 12, L, L + 11, -3, 6).Box(-2, -1, L, L + 3, -3, 6).Box(-4, -3, L, L + 1, -3, 6);
            return world.Input(Area(-2, 7, L, L + 7, 0, 3), new PathCell(-6, L, 0));
        }),
        new("tall-tunnel", "a tunnel 12 high, 4 wide and 16 deep into a hill", () =>
        {
            var world = new Voxels().Box(-40, 40, 1, L - 1, -40, 40).Box(0, 30, L, L + 20, -6, 9);
            return world.Input(Area(0, 15, L, L + 11, 0, 3), new PathCell(-6, L, 1));
        }),
        new("wide-pit", "a pit a layer deep, 128 x 128 (a largest area)", () => Largest(128, 1, 128)),
        new("long-trench", "a trench a layer deep, 1024 x 16 (a largest area)", () => Largest(1024, 1, 16)),
        new("deep-pit", "a pit 64 deep, 16 x 16 (a largest area, its stair a spiral)", () => Largest(16, 64, 16)),
    ];

    private static MinePlanInput Largest(int sizeX, int sizeY, int sizeZ)
    {
        int top = L + 70;
        var world = new Voxels().Box(-8, sizeX + 8, 1, top - 1, -8, sizeZ + 8);
        return world.Input(Area(0, sizeX - 1, top - sizeY, top - 1, 0, sizeZ - 1), new PathCell(-4, top, sizeZ / 2));
    }

    public static int Main(string[] args)
    {
        int runs = args.Length > 0 && int.TryParse(args[0], out int r) ? r : 10;
        var names = args.Skip(1).ToHashSet();
        var cases = Cases.Where(c => names.Count == 0 || names.Contains(c.Name)).ToList();
        if (cases.Count == 0)
        {
            Console.Error.WriteLine($"no such area; the areas: {string.Join(", ", Cases.Select(c => c.Name))}");
            return 2;
        }
        Console.WriteLine($"MinePlanner, {runs} runs an area after 3 to warm up; {Environment.ProcessorCount} CPUs, .NET {Environment.Version}"
            + (Debugger.IsAttached ? ", debugger attached" : "")
#if DEBUG
            + ", DEBUG BUILD: run with -c Release"
#endif
        );
        Console.WriteLine();
        Console.WriteLine("| area | plan ms (median, fastest) | checkpoints | checkpoint ms p50 / p99 / p99.9 / max | the slowest's units (its phase) | units a plan | ns a unit | longest checkpoint, units |");
        Console.WriteLine("|---|---|---|---|---|---|---|---|");
        foreach (var c in cases)
            Measure(c, runs);
        Console.WriteLine();
        foreach (var c in cases)
            Console.WriteLine($"- {c.Name}: {c.What}");
        Console.WriteLine("A slowest checkpoint of few units was most likely a collection or the thread stopped, not the planner's work;");
        Console.WriteLine("one in code run once a plan (the stair's) may still be the JIT's quick first tier, as in the game's first plans");
        Console.WriteLine("(DOTNET_TieredCompilation=0 compiles everything optimised at once, to compare).");
        return 0;
    }

    private static void Measure(Case c, int runs)
    {
        var input = c.Input();
        for (int i = 0; i < 3; i++)
            Run(input);
        var plans = new List<double>();
        var checkpoints = new List<double>();
        long units = 0, longestUnits = 0, slowestUnits = 0;
        var slowestPhase = MinePlanPhase.Targets;
        double slowest = 0;
        int count = 0;
        for (int i = 0; i < runs; i++)
        {
            var (ms, gaps, gapUnits, phases) = Run(input);
            plans.Add(ms);
            checkpoints.AddRange(gaps);
            units += gapUnits.Sum();
            longestUnits = Math.Max(longestUnits, gapUnits.Max());
            for (int k = 0; k < gaps.Count; k++)
                if (gaps[k] > slowest)
                    (slowest, slowestUnits, slowestPhase) = (gaps[k], gapUnits[k], phases[k]);
            count = gaps.Count;
        }
        plans.Sort();
        checkpoints.Sort();
        double ns = plans.Sum() * 1e6 / units;
        Console.WriteLine($"| {c.Name} | {plans[plans.Count / 2]:0.0}, {plans[0]:0.0} | {count} | "
            + $"{Percentile(checkpoints, 0.5):0.000} / {Percentile(checkpoints, 0.99):0.000} / {Percentile(checkpoints, 0.999):0.000} / {checkpoints[^1]:0.000} | "
            + $"{slowestUnits} ({slowestPhase}) | {units / runs} | {ns:0.0} | {longestUnits} |");
    }

    private static double Percentile(List<double> sorted, double p) => sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * p))];

    // One plan to its end in a single step, its clock read after each checkpoint: each checkpoint's ms and units.
    private static (double Ms, List<double> Gaps, List<long> Units, List<MinePlanPhase> Phases) Run(MinePlanInput input)
    {
        var gaps = new List<double>();
        var units = new List<long>();
        var phases = new List<MinePlanPhase>();
        long start = 0, last = 0, lastUnits = 0;
        bool first = true;
        MinePlanner? planner = null;
        var phase = MinePlanPhase.Targets;
        TimeSpan Clock()
        {
            long now = Stopwatch.GetTimestamp(), u = MineWork.Units;
            if (first)
            {
                first = false;
                start = now;
            }
            else
            {
                gaps.Add(Stopwatch.GetElapsedTime(last, now).TotalMilliseconds);
                units.Add(u - lastUnits);
                phases.Add(phase); // the phase the checkpoint began in
            }
            phase = planner?.Phase ?? phase;
            (last, lastUnits) = (now, u);
            return Stopwatch.GetElapsedTime(start, now);
        }
        planner = new MinePlanner(Copy(input), Clock);
        while (!planner.Step(TimeSpan.MaxValue)) { }
        return (Stopwatch.GetElapsedTime(start, last).TotalMilliseconds, gaps, units, phases);
    }

    // A fresh state over a copy of the site: a plan never changes its input's, but each run builds its own.
    private static MinePlanInput Copy(MinePlanInput input) =>
        input with { State = new MineState(input.State.Site.Clone(), input.Home) };

    private static MarkArea Area(int x0, int x1, int y0, int y1, int z0, int z1) =>
        MarkArea.Between(new MarkPos(x0, y0, z0), new MarkPos(x1, y1, z1));

    /// <summary>Solid boxes of rock, every block mined.</summary>
    private sealed class Voxels : IMineWorld
    {
        private readonly HashSet<(int X, int Y, int Z)> _solid = new();

        public Voxels Box(int x0, int x1, int y0, int y1, int z0, int z1)
        {
            for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
                _solid.Add((x, y, z));
            return this;
        }

        public bool Loaded(int x, int z) => true;

        public MineVoxel Read(int x, int y, int z) =>
            _solid.Contains((x, y, z)) ? new MineVoxel(true, MineRule.Mine, (float)MineCosts.DefaultPlayerSeconds) : new MineVoxel(false, MineRule.Air);

        public MinePlanInput Input(MarkArea area, PathCell home)
        {
            var site = new MineSite(area, 2);
            foreach (var _ in site.Load(this)) { }
            return new MinePlanInput(new MineState(site, home), home, _ => false, new HashSet<PathCell>());
        }
    }
}
