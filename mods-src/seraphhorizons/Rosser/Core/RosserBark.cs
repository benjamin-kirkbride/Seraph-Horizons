using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.Rosser.Core;

/// <summary>One log's bark: Immersive Woodworking's roll with <see cref="ChanceMultiplier"/>
/// (the kind), and <see cref="Count"/> pieces (the stack's size).</summary>
public readonly record struct LogBark(double ChanceMultiplier, int Count, bool Wet);

/// <summary>
/// The bark a scraped log gives, wet or dry (decision 5, rulings). The drip's reservoir is the shared machine
/// base's (<see cref="MachineReservoir"/>): it fills from a Pipes and Power Expanded pipe on the water face at
/// <see cref="RosserConfig.WaterIntakeLitresPerSecond"/> up to <see cref="RosserConfig.ReservoirLitres"/>, and each
/// log scraped while it holds <see cref="RosserConfig.WaterPerLog"/> is wet and spends that much. Without a pipe it
/// stays dry, and the rosser debarks dry.
/// </summary>
public static class RosserBark
{
    /// <summary>Bark for logs scraped in one step, in order, spending water per log. <paramref name="baseCount"/>
    /// is Immersive Woodworking's count for the species (its roll's stack size, 3 by default);
    /// <paramref name="nextDouble"/> is the world's random source (0 ≤ x &lt; 1), called once per wet
    /// log with a fractional count and never otherwise.</summary>
    public static (MachineReservoir Water, IReadOnlyList<LogBark> Logs) Scrape(MachineReservoir water, int logs, int baseCount,
                                                                               RosserConfig config, Func<double> nextDouble)
    {
        var result = new List<LogBark>();
        for (int i = 0; i < logs; i++)
        {
            (water, bool wet) = water.Spend(config.WaterPerLog);
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
