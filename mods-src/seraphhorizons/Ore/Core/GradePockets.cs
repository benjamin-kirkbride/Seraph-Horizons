namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>How a vein's off-grade share is split between one grade poorer and one richer.</summary>
public enum GradeWeighting
{
    /// <summary>So the vein's mean metal units per block stay those of its drawn grade.</summary>
    Units,

    /// <summary>Evenly, so the mean grade stays the drawn one (units rise a little, as the steps
    /// between grades grow richer).</summary>
    Index,
}

/// <summary>
/// The settings of <see cref="GradePockets"/>: <paramref name="PocketSize"/> the pockets' typical
/// width in blocks, <paramref name="Spread"/> the share of a vein's blocks one grade off its drawn
/// grade where it can go both ways, <paramref name="EndSpread"/> that share where it can only go one
/// way (a poor vein, or one at the top of what its host rock allows), which moves its mean.
/// </summary>
public sealed record GradePocketSettings(
    double PocketSize = GradePocketSettings.DefaultPocketSize,
    double Spread = GradePocketSettings.DefaultSpread,
    double EndSpread = 0,
    GradeWeighting Weighting = GradeWeighting.Units)
{
    public const double DefaultPocketSize = 8, DefaultSpread = 0.35;

    public static readonly GradePocketSettings Default = new();

    /// <summary>In range: pockets 2 to 64 blocks, shares 0 to 1.</summary>
    public GradePocketSettings Clamped() => this with
    {
        PocketSize = double.IsFinite(PocketSize) ? Math.Clamp(PocketSize, 2, 64) : DefaultPocketSize,
        Spread = double.IsFinite(Spread) ? Math.Clamp(Spread, 0, 1) : DefaultSpread,
        EndSpread = double.IsFinite(EndSpread) ? Math.Clamp(EndSpread, 0, 1) : 0,
    };
}

/// <summary>
/// Grade within a vein (#944): Interesting Ore Gen draws one grade per vein and gives it to every
/// block, so a deposit is all one grade. This varies each block's grade by one step around the
/// drawn one, in pockets: the world is cut into cells about <see cref="GradePocketSettings.PocketSize"/>
/// across (the nearest of a jittered lattice of points, a Worley pattern), each with a value from
/// the seed, and a block takes its cell's value (<see cref="Pocket"/>), so a pocket is richer or
/// poorer as a whole and the blocks either side of a chunk border agree. Nothing here draws from
/// the vein's own random sequence: the grade depends on the world seed and the block's position
/// alone, so every chunk pass over a vein places the same grades.
///
/// A pocket's value is uniform in [0, 1) whatever its size, so the share of a vein's blocks that go
/// poorer or richer is <see cref="Odds"/>' on average. Those are weighted per drawn grade so the
/// vein's mean metal units per block stay its drawn grade's (<see cref="GradeWeighting.Units"/>):
/// with units 3, 5, 7, 10 a medium vein goes poorer and richer alike, a rich one poorer more often
/// (5 is two units below 7, 10 three above). A vein at an end of its host rock's grades can only go
/// one way, which would move its mean; it gets <see cref="GradePocketSettings.EndSpread"/>.
/// </summary>
public sealed class GradePockets
{
    private const ulong Salt = 0x6772616465706f63UL; // "gradepoc"

    private readonly ulong _seed;

    public GradePocketSettings Settings { get; }

    public GradePockets(long worldSeed, GradePocketSettings settings)
    {
        _seed = Mix((ulong)worldSeed ^ Salt);
        Settings = settings.Clamped();
    }

    /// <summary>
    /// The share of blocks one grade poorer and one richer than <paramref name="grade"/>, whose
    /// units per block are <paramref name="units"/> (poorest first; its length is the grades the host
    /// rock allows). A grade past the last is the last (the rock's cap).
    /// </summary>
    public static (double Poorer, double Richer) Odds(IReadOnlyList<double> units, int grade, GradePocketSettings settings)
    {
        int n = units.Count;
        if (n <= 1) return (0, 0);
        int g = Math.Clamp(grade, 0, n - 1);
        if (g == 0) return (0, settings.EndSpread);
        if (g == n - 1) return (settings.EndSpread, 0);
        double s = settings.Spread;
        double a = units[g - 1], b = units[g], c = units[g + 1];
        if (settings.Weighting == GradeWeighting.Index || !(c > a) || b < a || b > c)
            return (s / 2, s / 2);
        // p·a + q·c = s·b with p + q = s.
        double richer = s * (b - a) / (c - a);
        return (s - richer, richer);
    }

    /// <summary>The mean units per block of a vein drawn at <paramref name="grade"/>, as placed.</summary>
    public static double MeanUnits(IReadOnlyList<double> units, int grade, GradePocketSettings settings)
    {
        int g = Math.Clamp(grade, 0, units.Count - 1);
        var (p, q) = Odds(units, g, settings);
        return (1 - p - q) * units[g] + (p > 0 ? p * units[g - 1] : 0) + (q > 0 ? q * units[g + 1] : 0);
    }

    /// <summary>The grade a block at (x, y, z) of a vein drawn at <paramref name="drawn"/> takes.</summary>
    public int GradeAt(IReadOnlyList<double> units, int drawn, int x, int y, int z)
    {
        int n = units.Count;
        if (n == 0) return drawn;
        int g = Math.Clamp(drawn, 0, n - 1);
        var (poorer, richer) = Odds(units, g, Settings);
        if (poorer <= 0 && richer <= 0) return g;
        double u = Pocket(x, y, z);
        if (u < poorer) return g - 1;
        if (u >= 1 - richer) return g + 1;
        return g;
    }

    /// <summary>The value, in [0, 1), of the pocket holding block (x, y, z).</summary>
    public double Pocket(int x, int y, int z)
    {
        double size = Settings.PocketSize;
        // Block centres, in cells.
        double px = (x + 0.5) / size, py = (y + 0.5) / size, pz = (z + 0.5) / size;
        long cx = (long)Math.Floor(px), cy = (long)Math.Floor(py), cz = (long)Math.Floor(pz);
        double best = double.MaxValue;
        ulong bestHash = 0;
        for (long dx = -1; dx <= 1; dx++)
        for (long dy = -1; dy <= 1; dy++)
        for (long dz = -1; dz <= 1; dz++)
        {
            ulong h = CellHash(cx + dx, cy + dy, cz + dz);
            double fx = cx + dx + Unit(h) - px;
            double fy = cy + dy + Unit(Mix(h ^ 1)) - py;
            double fz = cz + dz + Unit(Mix(h ^ 2)) - pz;
            double d = fx * fx + fy * fy + fz * fz;
            if (d < best)
            {
                best = d;
                bestHash = h;
            }
        }
        return Unit(Mix(bestHash ^ 3));
    }

    private ulong CellHash(long x, long y, long z)
    {
        ulong h = Mix(_seed ^ (ulong)x);
        h = Mix(h ^ (ulong)y * 0x9e3779b97f4a7c15UL);
        return Mix(h ^ (ulong)z * 0xc2b2ae3d27d4eb4fUL);
    }

    // The top 53 bits, as a double in [0, 1).
    private static double Unit(ulong h) => (h >> 11) * (1.0 / (1UL << 53));

    // SplitMix64's finaliser.
    private static ulong Mix(ulong z)
    {
        z += 0x9e3779b97f4a7c15UL;
        z = (z ^ (z >> 30)) * 0xbf58476d1ce4e5b9UL;
        z = (z ^ (z >> 27)) * 0x94d049bb133111ebUL;
        return z ^ (z >> 31);
    }
}
