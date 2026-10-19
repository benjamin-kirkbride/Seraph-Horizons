namespace SeraphHorizons.Mod.Rosser.Core;

/// <summary>
/// The drip's reservoir (decision 5, rulings): it fills from a Pipes and Power Expanded pipe on the
/// water face at <see cref="RosserConfig.WaterIntakeLitresPerSecond"/> up to
/// <see cref="RosserConfig.ReservoirLitres"/>, and each log scraped while it holds
/// <see cref="RosserConfig.WaterPerLog"/> is wet and spends that much. Without a pipe it stays dry.
/// </summary>
public readonly record struct RosserWater(double Litres)
{
    public static readonly RosserWater Dry = new(0);

    /// <summary>Restored from a save: clamped to 0..capacity (a lowered capacity loses the excess).</summary>
    public static RosserWater Restore(double litres, RosserConfig config) =>
        new(double.IsFinite(litres) ? Math.Clamp(litres, 0, config.ReservoirLitres) : 0);

    /// <summary>Whether the next log would be wet (synced: the drip's particles).</summary>
    public bool Wet(RosserConfig config) => Litres + 1e-9 >= config.WaterPerLog;

    /// <summary>Litres to ask the pipe for over <paramref name="seconds"/>: the intake rate, never
    /// past full.</summary>
    public double Wanted(RosserConfig config, double seconds) =>
        Math.Max(0, Math.Min(config.ReservoirLitres - Litres, config.WaterIntakeLitresPerSecond * Math.Max(0, seconds)));

    /// <summary>The reservoir after <paramref name="litres"/> came in, never past full.</summary>
    public RosserWater Fill(double litres, RosserConfig config) =>
        new(Math.Clamp(Litres + Math.Max(0, double.IsFinite(litres) ? litres : 0), 0, Math.Max(Litres, config.ReservoirLitres)));

    /// <summary>One log scraped: wet, spending <see cref="RosserConfig.WaterPerLog"/>, if the
    /// reservoir holds it; else dry and unchanged.</summary>
    public (RosserWater Water, bool Wet) SpendForLog(RosserConfig config) =>
        Wet(config) ? (new RosserWater(Math.Max(0, Litres - config.WaterPerLog)), true) : (this, false);
}

/// <summary>One log's bark: Immersive Woodworking's roll with <see cref="ChanceMultiplier"/>
/// (the kind), and <see cref="Count"/> pieces (the stack's size).</summary>
public readonly record struct LogBark(double ChanceMultiplier, int Count, bool Wet);

/// <summary>The bark a scraped log gives, wet or dry.</summary>
public static class RosserBark
{
    /// <summary>Bark for logs scraped in one step, in order, spending water per log. <paramref name="baseCount"/>
    /// is Immersive Woodworking's count for the species (its roll's stack size, 3 by default);
    /// <paramref name="nextDouble"/> is the world's random source (0 ≤ x &lt; 1), called once per wet
    /// log with a fractional count and never otherwise.</summary>
    public static (RosserWater Water, IReadOnlyList<LogBark> Logs) Scrape(RosserWater water, int logs, int baseCount,
                                                                          RosserConfig config, Func<double> nextDouble)
    {
        var result = new List<LogBark>();
        for (int i = 0; i < logs; i++)
        {
            (water, bool wet) = water.SpendForLog(config);
            result.Add(wet
                ? new LogBark(config.WetBarkMultiplier, WetCount(baseCount, config.WetBarkCountMultiplier, nextDouble), true)
                : new LogBark(config.DryBarkMultiplier, Math.Max(0, baseCount), false));
        }
        return (water, result);
    }

    /// <summary>A wet log's pieces: floor(n × factor), and one more with chance frac(n × factor).</summary>
    public static int WetCount(int baseCount, float factor, Func<double> nextDouble)
    {
        double x = Math.Max(0, baseCount) * (double)Math.Max(0, factor);
        double whole = Math.Floor(x + 1e-9);
        double frac = x - whole;
        int count = (int)whole;
        if (frac > 1e-9 && nextDouble() < frac)
            count++;
        return count;
    }
}
