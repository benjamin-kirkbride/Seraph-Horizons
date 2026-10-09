using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Power;

/// <summary>
/// Writes the export's optional <c>power</c> section: the mechanical power producers and
/// consumers of the pack with the parameters of their models, and the wind (contract:
/// site/src/lib/power-data.ts; docs/recipe-browser/power.md). Only parameters: torque at a
/// speed, equilibria and wind averages are computed by the app. A mod that is not loaded
/// leaves its entries out.
/// </summary>
public static class PowerSection
{
    public static void Fill(ICoreServerAPI api, JObject root)
    {
        var live = new Live(api);
        var blocks = new BlockIndex(api, root["items"] as JObject);
        var producers = Producers.Build(live, blocks);
        var consumers = Consumers.Build(live, blocks);
        root["power"] = new JObject
        {
            ["producers"] = new JArray(producers.OrderBy(p => (string)p["id"]!, StringComparer.Ordinal)),
            ["consumers"] = new JArray(consumers.OrderBy(c => (string)c["id"]!, StringComparer.Ordinal)),
            ["wind"] = WindSimulation.Build(api),
        };
        int fallbacks = root["power"]!.SelectTokens("$..fallback").Count(t => t.Type == JTokenType.Boolean && (bool)t!);
        api.Logger.Notification("[seraphexport] power: {0} producers, {1} consumers, {2} fallbacks",
            producers.Count, consumers.Count, fallbacks);
    }

    /// <summary>A number rounded to 6 places, as an integer when it is one (as RecipeSection writes them).</summary>
    public static JToken Num(double d)
    {
        d = Math.Round(d, 6);
        return d == Math.Floor(d) && Math.Abs(d) < long.MaxValue ? new JValue((long)d) : new JValue(d);
    }

    public static JArray Sources(IEnumerable<Figure> figures) =>
        new(figures.GroupBy(f => (f.What, f.From)).Select(g => g.First().Source()));
}

/// <summary>Blocks by the class of their block entity behaviors, and the item code a power entry
/// links to (one the export's <c>items</c> holds).</summary>
public sealed class BlockIndex
{
    private readonly ICoreServerAPI _api;
    private readonly JObject? _items;
    private readonly Dictionary<string, List<Block>> _byBehavior = new(StringComparer.Ordinal);

    public BlockIndex(ICoreServerAPI api, JObject? items)
    {
        _api = api;
        _items = items;
        foreach (var block in api.World.Blocks)
        {
            if (block?.Code == null || block.IsMissing || block.BlockEntityBehaviors == null) continue;
            foreach (var b in block.BlockEntityBehaviors)
            {
                Type? type = null;
                try { type = api.ClassRegistry.GetBlockEntityBehaviorClass(b.Name); } catch { }
                if (type == null) continue;
                foreach (var key in new[] { type.Name, type.FullName ?? type.Name })
                {
                    if (!_byBehavior.TryGetValue(key, out var list)) _byBehavior[key] = list = new();
                    if (!list.Contains(block)) list.Add(block);
                }
            }
        }
        foreach (var list in _byBehavior.Values)
            list.Sort((a, b) => string.CompareOrdinal(a.Code.ToString(), b.Code.ToString()));
    }

    /// <summary>Blocks with a behavior of exactly this class (short or full name), in code order.</summary>
    public IReadOnlyList<Block> WithBehavior(string typeName) =>
        _byBehavior.TryGetValue(typeName, out var list) ? list : Array.Empty<Block>();

    /// <summary>The behavior entry of that class on the block.</summary>
    public BlockEntityBehaviorType? Behavior(Block block, string typeName) =>
        block.BlockEntityBehaviors?.FirstOrDefault(b =>
        {
            try
            {
                var t = _api.ClassRegistry.GetBlockEntityBehaviorClass(b.Name);
                return t != null && (t.Name == typeName || t.FullName == typeName);
            }
            catch { return false; }
        });

    public Block? Get(string code) =>
        _api.World.GetBlock(new AssetLocation(code)) is { IsMissing: false, Code: not null } b ? b : null;

    /// <summary>Registered blocks whose code is <paramref name="prefix"/> or starts with it and a dash, in code order.</summary>
    public List<Block> Matching(string prefix)
    {
        var loc = new AssetLocation(prefix);
        return _api.World.Blocks
            .Where(b => b?.Code != null && !b.IsMissing && b.Code.Domain == loc.Domain
                        && (b.Code.Path == loc.Path || b.Code.Path.StartsWith(loc.Path + "-", StringComparison.Ordinal)))
            .OrderBy(b => b.Code.ToString(), StringComparer.Ordinal).ToList();
    }

    /// <summary>The code to link to among <paramref name="candidates"/>: one in the export's items,
    /// a north-facing variant first; null when none is.</summary>
    public string? ItemFor(IEnumerable<Block> candidates)
    {
        var codes = candidates.Select(b => b.Code.ToString()).ToList();
        var inItems = codes.Where(c => _items == null || _items.ContainsKey(c)).ToList();
        return inItems.FirstOrDefault(c => c.EndsWith("-north", StringComparison.Ordinal)) ?? inItems.FirstOrDefault();
    }

    /// <summary>The code to link to for <paramref name="prefix"/>, an item or a block: the code
    /// itself, its north variant, or the first variant in code order that the export's items hold.</summary>
    public string? ItemFor(string prefix)
    {
        if (_items == null) return ItemFor(Matching(prefix));
        if (_items.ContainsKey(prefix)) return prefix;
        if (_items.ContainsKey(prefix + "-north")) return prefix + "-north";
        return _items.Properties().Select(p => p.Name)
            .Where(k => k.StartsWith(prefix + "-", StringComparison.Ordinal))
            .OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault();
    }

    /// <summary>The English name the export gives <paramref name="code"/>, or <paramref name="fallback"/>.</summary>
    public string NameOf(string? code, string fallback) =>
        code != null && _items?[code]?["name"]?.ToString() is { Length: > 0 } name ? name : fallback;
}
