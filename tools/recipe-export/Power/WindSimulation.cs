using System.Diagnostics;
using Newtonsoft.Json.Linq;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.RecipeExport.Power;

/// <summary>
/// The wind a windmill sees, simulated from the wind patterns the server loaded
/// (<c>game:config/windpatterns/*.json</c>, <see cref="WeatherSystemServer.WindConfigs"/>) the
/// way <c>WeatherSimulationRegion</c> runs them: when the current pattern runs out a new one is
/// picked uniformly (<c>Rand.NextInt(n)</c>; the patterns' <c>weight</c> is not used for wind),
/// its base strength and duration drawn from its NatFloats with the game's own code, and while
/// it lasts its strength is the base plus its gust noise clamped to 0..1
/// (<c>SimplexNoise(amplitudes, frequencies, seed + index).Noise(0, totalDays × 10)</c>) when it
/// has <c>strengthNoise</c>. That is the wind at sea level; the altitude factor is left to the
/// app. Deterministic: a fixed seed and a fixed sample count.
/// </summary>
public static class WindSimulation
{
    public const int Years = 100;
    public const int SamplesPerHour = 60;
    public const int Seed = 1;
    public const double BinWidth = 0.01;

    // WeatherSimulationRegion.GetWindSpeed, above sea level: × max(1, 0.9 + blocks/100), capped at 1.5.
    public const double AltitudeBase = 0.9;
    public const double AltitudeDivisor = 100;
    public const double AltitudeCap = 1.5;

    public static JObject? Build(ICoreServerAPI api)
    {
        var configs = api.ModLoader.GetModSystem<WeatherSystemServer>()?.WindConfigs;
        if (configs == null || configs.Length == 0)
        {
            api.Logger.Warning("[seraphexport] power: the server has no wind patterns; no wind section");
            return null;
        }
        var calendar = api.World.Calendar;
        int daysPerYear = calendar?.DaysPerYear > 0 ? calendar.DaysPerYear : 108;
        double hoursPerDay = calendar?.HoursPerDay > 0 ? calendar.HoursPerDay : 24;
        long samples = (long)Math.Round(Years * daysPerYear * hoursPerDay * SamplesPerHour);

        var watch = Stopwatch.StartNew();
        var result = Run(configs, samples, hoursPerDay);
        api.Logger.Notification("[seraphexport] power: simulated {0} years of wind ({1} samples) in {2} ms",
            Years, samples, watch.ElapsedMilliseconds);

        var patterns = new JArray();
        for (int i = 0; i < configs.Length; i++)
        {
            var c = configs[i];
            patterns.Add(new JObject
            {
                ["code"] = c.Code,
                ["name"] = c.Name ?? c.Code,
                ["strengthAvg"] = PowerSection.Num(c.Strength?.avg ?? 0),
                ["strengthVar"] = PowerSection.Num(c.Strength?.var ?? 0),
                ["durationAvgHours"] = PowerSection.Num(c.DurationHours?.avg ?? 0),
                ["durationVarHours"] = PowerSection.Num(c.DurationHours?.var ?? 0),
                ["durationMeanHours"] = PowerSection.Num(c.DurationHours is { } d ? Mean(d) : 0),
                ["durationDist"] = (c.DurationHours?.dist ?? EnumDistribution.UNIFORM).ToString().ToLowerInvariant(),
                ["share"] = PowerSection.Num(result.PatternShares[i]),
                ["gusts"] = c.StrengthNoise != null,
            });
        }
        return new JObject
        {
            ["simulation"] = new JObject { ["years"] = Years, ["samplesPerHour"] = SamplesPerHour, ["seed"] = Seed },
            ["patterns"] = patterns,
            ["altitude"] = new JObject
            {
                ["base"] = PowerSection.Num(AltitudeBase), ["divisor"] = PowerSection.Num(AltitudeDivisor), ["cap"] = PowerSection.Num(AltitudeCap),
            },
            ["histogram"] = new JObject
            {
                ["binWidth"] = PowerSection.Num(BinWidth),
                ["shares"] = new JArray(result.Histogram.Select(s => PowerSection.Num(s))),
            },
            ["mean"] = PowerSection.Num(result.Mean),
            ["sources"] = Sources(api, configs),
        };
    }

    /// <summary>The asset each pattern was loaded from, and the code constants of the model.</summary>
    private static JArray Sources(ICoreServerAPI api, WindPatternConfig[] configs)
    {
        var figures = new List<Figure>();
        var fileOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var asset in api.Assets.GetMany("config/windpatterns/"))
        {
            try
            {
                foreach (var c in asset.ToObject<WindPatternConfig[]>() ?? Array.Empty<WindPatternConfig>())
                    if (c.Code != null) fileOf.TryAdd(c.Code, asset.Location.ToString());
            }
            catch (Exception e)
            {
                api.Logger.Warning("[seraphexport] power: cannot read {0}: {1}", asset.Location, e.Message);
            }
        }
        foreach (var c in configs)
        {
            bool found = fileOf.TryGetValue(c.Code ?? "", out var file);
            if (!found)
                api.Logger.Warning("[seraphexport] power: wind pattern {0} matches no config/windpatterns asset", c.Code);
            figures.Add(new Figure(0, $"wind pattern {c.Code}", found ? file! : "WeatherSystemServer.WindConfigs", !found));
        }
        const string region = "WeatherSimulationRegion";
        figures.Add(Live.Constant("next pattern chosen uniformly, Rand.NextInt(patterns); weight not used", 0, region));
        figures.Add(Live.Constant("strength = base + clamp(SimplexNoise(amplitudes, frequencies, seed + index).Noise(0, totalDays × 10), 0, 1)", 0, "WindPattern"));
        figures.Add(Live.Constant("base strength and duration drawn from the pattern's NatFloats at its start", 0, "WindPattern"));
        figures.Add(Live.Constant($"altitude factor max(1, {AltitudeBase} + blocks above sea level / {AltitudeDivisor}), capped at {AltitudeCap}", 0, region));
        return PowerSection.Sources(figures);
    }

    /// <summary>The mean of what <see cref="NatFloat.nextFloat(float, IRandom)"/> returns: avg plus var
    /// times the mean of its random term. invexp's is U1·U2 (1/4), strong 1/8, stronger 1/16; the
    /// symmetric ones' is 0; a distribution the method does not handle returns 0.</summary>
    public static double Mean(NatFloat n) => n.dist switch
    {
        EnumDistribution.INVEXP => n.offset + n.avg + n.var / 4.0,
        EnumDistribution.STRONGINVEXP => n.offset + n.avg + n.var / 8.0,
        EnumDistribution.STRONGERINVEXP => n.offset + n.avg + n.var / 16.0,
        EnumDistribution.UNIFORM or EnumDistribution.TRIANGLE or EnumDistribution.GAUSSIAN or EnumDistribution.NARROWGAUSSIAN
            or EnumDistribution.INVERSEGAUSSIAN or EnumDistribution.NARROWINVERSEGAUSSIAN or EnumDistribution.DIRAC => n.offset + n.avg,
        _ => n.offset,
    };

    public sealed record Result(double[] PatternShares, double[] Histogram, double Mean);

    /// <summary>Runs the patterns for <paramref name="samples"/> samples, <paramref name="samplesPerHour"/> to the game hour.</summary>
    public static Result Run(WindPatternConfig[] configs, long samples, double hoursPerDay, int samplesPerHour = SamplesPerHour, int seed = Seed)
    {
        var rand = new LCGRandom(seed);
        rand.InitPositionSeed(0, 0);
        var noise = configs.Select((c, i) => c.StrengthNoise is { } n ? new SimplexNoise(n.Amplitudes, n.Frequencies, seed + i) : null).ToArray();

        var counts = new long[configs.Length];
        var bins = new List<long>();
        double sum = 0;
        int current = 0;
        double baseStrength = 0, until = double.NegativeInfinity;
        for (long s = 0; s < samples; s++)
        {
            double hours = (double)s / samplesPerHour;
            if (hours > until)
            {
                current = rand.NextInt(configs.Length);
                baseStrength = configs[current].Strength?.nextFloat(1f, rand) ?? 0;
                until = hours + (configs[current].DurationHours?.nextFloat(1f, rand) ?? 0);
            }
            double strength = baseStrength;
            if (noise[current] is { } gen)
                strength += GameMath.Clamp(gen.Noise(0, hours / hoursPerDay * 10), 0, 1);
            counts[current]++;
            double v = Math.Max(0, strength);
            sum += v;
            int bin = (int)(v / BinWidth);
            while (bins.Count <= bin) bins.Add(0);
            bins[bin]++;
        }
        double n = Math.Max(1, samples);
        return new Result(counts.Select(c => c / n).ToArray(), bins.Select(c => c / n).ToArray(), sum / n);
    }
}
