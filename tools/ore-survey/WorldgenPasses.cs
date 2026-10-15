using System.Diagnostics;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.OreSurvey;

/// <summary>
/// The standard world type's chunk column handlers, per pass: listed in the log, optionally timed
/// (ORE_SURVEY_PROFILE=1), and with ORE_SURVEY_FAST=1 those of the passes after
/// <see cref="EnumWorldGenPass.TerrainFeatures"/> taken out, but the pack's own (stable veins lay
/// cell veins at PreDone). Every ore placer runs in
/// TerrainFeatures or earlier (the game's GenDeposits, Interesting Ore Gen's districts, the pack's
/// placer fields), and nothing in the later passes writes ore (README, "Fast"). The passes still
/// run, empty, so columns complete as before; only their handlers are gone.
/// </summary>
public sealed class WorldgenPasses
{
    private readonly Dictionary<string, (long Ticks, long Calls)> _times = new();
    private readonly object _lock = new();

    public bool Fast { get; }
    public bool Profile { get; }
    public List<string> Removed { get; } = [];

    private WorldgenPasses(bool fast, bool profile)
    {
        Fast = fast;
        Profile = profile;
    }

    /// <param name="skip">Handler names (substrings, e.g. GenStructures) to take out of any pass, for
    /// experiments (ORE_SURVEY_SKIP).</param>
    public static WorldgenPasses Apply(ICoreServerAPI api, bool fast, bool profile, Action<string> log, IReadOnlyCollection<string>? skip = null)
    {
        var passes = new WorldgenPasses(fast, profile);
        var handler = api.Event.GetRegisteredWorldGenHandlers("standard");
        if (handler == null)
        {
            log("worldgen: no handlers for the standard world type");
            return passes;
        }
        var lists = handler.OnChunkColumnGen;
        for (int pass = 0; pass < lists.Length; pass++)
        {
            var list = lists[pass];
            if (list == null) continue;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                string name = Name(list[i]);
                bool named = skip?.Any(n => name.Contains(n, StringComparison.Ordinal)) == true;
                // The pack's own handlers stay: with stable veins (#946) cell veins are laid at PreDone.
                bool pack = name.StartsWith("SeraphHorizons.", StringComparison.Ordinal);
                bool remove = named || fast && pass > (int)EnumWorldGenPass.TerrainFeatures && !pack;
                log($"worldgen: pass {(EnumWorldGenPass)pass} handler {name}{(remove ? named ? " (removed: skip)" : " (removed: fast)" : "")}");
                if (remove)
                {
                    passes.Removed.Add(name);
                    list.RemoveAt(i);
                }
                else if (profile)
                {
                    var inner = list[i];
                    string key = $"{(EnumWorldGenPass)pass} {name}";
                    list[i] = request =>
                    {
                        long t0 = Stopwatch.GetTimestamp();
                        try { inner(request); }
                        finally { passes.Add(key, Stopwatch.GetTimestamp() - t0); }
                    };
                }
            }
        }
        return passes;
    }

    private void Add(string key, long ticks)
    {
        lock (_lock)
        {
            var t = _times.GetValueOrDefault(key);
            _times[key] = (t.Ticks + ticks, t.Calls + 1);
        }
    }

    /// <summary>Time per handler, most first (ORE_SURVEY_PROFILE only).</summary>
    public IEnumerable<string> Report()
    {
        lock (_lock)
            return _times.OrderByDescending(kv => kv.Value.Ticks)
                .Select(kv => $"worldgen time: {kv.Key}: {kv.Value.Ticks * 1000.0 / Stopwatch.Frequency:F0} ms over {kv.Value.Calls} columns, "
                    + $"{kv.Value.Ticks * 1000.0 / Stopwatch.Frequency / Math.Max(1, kv.Value.Calls):F1} ms each")
                .ToList();
    }

    private static string Name(ChunkColumnGenerationDelegate d) =>
        $"{d.Method.DeclaringType?.FullName}.{d.Method.Name}" + (d.Target != null && d.Target.GetType() != d.Method.DeclaringType ? $" ({d.Target.GetType().FullName})" : "");
}
