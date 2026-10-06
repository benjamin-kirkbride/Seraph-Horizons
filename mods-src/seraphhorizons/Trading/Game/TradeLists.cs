using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Trading.Core;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Trading;

/// <summary>
/// The eleven trade lists (<c>seraphhorizons:config/tradelists/trader-{type}.json</c>), loaded on the
/// server once the world's items and blocks exist. Each entry is checked against the registry here,
/// once: one the game can't resolve (a mod missing, a code wrong) is dropped with a warning naming
/// it, so a restock never meets it. Each kept entry also gets its game TradeItem, made the way
/// vanilla makes its own (Newtonsoft from the entry's JSON), so stack, attributes, price and stock
/// mean exactly what they mean in a vanilla list.
/// </summary>
public sealed class TradeLists
{
    public const string Folder = "config/tradelists/";

    private readonly Dictionary<string, TradeListDef> _lists = new();
    // Each entry's JSON as vanilla's TradeItem reads it. A TradeItem is made afresh for every
    // restock: TradeItem.Resolve keeps state (it appends to AttributesToIgnore each time).
    private readonly Dictionary<TradeEntry, JObject> _items = new(ReferenceEqualityComparer.Instance);

    /// <summary>Entries the game could not resolve, as <c>type: key</c>; empty in the pack.</summary>
    public List<string> Unresolved { get; } = [];

    /// <summary>Problems <see cref="TradeListResolver.Problems"/> found, as <c>type: problem</c>.</summary>
    public List<string> Problems { get; } = [];

    public IReadOnlyDictionary<string, TradeListDef> Lists => _lists;

    public TradeListDef? For(string type) => _lists.GetValueOrDefault(type);

    /// <summary>A new game TradeItem for an entry this loaded and kept.</summary>
    public TradeItem ItemFor(TradeEntry entry) => _items[entry].ToObject<TradeItem>()!;

    public IReadOnlyDictionary<string, double> CampWeights => _lists.ToDictionary(kv => kv.Key, kv => kv.Value.CampWeight);

    public static TradeLists Load(ICoreAPI api)
    {
        var lists = new TradeLists();
        foreach (string type in TraderTypes.All)
        {
            var loc = new AssetLocation(SeraphHorizonsSystem.HarmonyId, $"{Folder}trader-{type}.json");
            var asset = api.Assets.TryGet(loc);
            if (asset is null)
            {
                lists.Problems.Add($"{type}: {loc} is missing");
                continue;
            }
            TradeListDef def;
            try
            {
                var token = JToken.Parse(asset.ToText());
                def = token.ToObject<TradeListDef>()!;
            }
            catch (Exception e)
            {
                lists.Problems.Add($"{type}: {loc} does not parse: {e.Message}");
                continue;
            }
            if (string.IsNullOrEmpty(def.Type)) def.Type = type;
            foreach (var entry in AllEntries(def))
                entry.AttributesKey = entry.Attributes is JToken attrs ? attrs.ToString(Formatting.None) : "";
            lists.Problems.AddRange(TradeListResolver.Problems(def).Select(p => $"{type}: {p}"));
            lists.Resolve(api.World, def);
            lists._lists[type] = def;
        }
        return lists;
    }

    private void Resolve(IWorldAccessor world, TradeListDef def)
    {
        foreach (var side in new[] { def.Selling, def.Buying })
        {
            Keep(world, def.Type, side.Core);
            Keep(world, def.Type, side.Rotating.List);
            foreach (var regional in side.Regional.Values)
            {
                Keep(world, def.Type, regional.Core);
                Keep(world, def.Type, regional.Rotating);
            }
        }
    }

    private void Keep(IWorldAccessor world, string type, List<TradeEntry> entries)
    {
        entries.RemoveAll(entry =>
        {
            if (ToJson(world, entry) is { } json)
            {
                _items[entry] = json;
                return false;
            }
            Unresolved.Add($"{type}: {entry.Key}");
            return true;
        });
    }

    private static JObject? ToJson(IWorldAccessor world, TradeEntry entry)
    {
        var json = new JObject
        {
            ["type"] = entry.Type,
            ["code"] = entry.Code,
            ["stacksize"] = entry.StackSize,
            ["price"] = new JObject { ["avg"] = entry.Price?.Avg ?? 1, ["var"] = entry.Price?.Var ?? 0 },
            ["stock"] = new JObject { ["avg"] = entry.Stock?.Avg ?? 1, ["var"] = entry.Stock?.Var ?? 0 },
        };
        if (entry.Attributes is JToken attributes) json["attributes"] = attributes.DeepClone();
        TradeItem item;
        try
        {
            item = json.ToObject<TradeItem>()!;
        }
        catch (Exception)
        {
            return null;
        }
        var code = item.Code;
        bool exists = item.Type == EnumItemClass.Block ? world.GetBlock(code) is { Id: > 0 } : world.GetItem(code) is { Id: > 0 };
        // Quietly: what fails is reported once, as a list, by the caller.
        return exists && item.Resolve(world, "seraphhorizons trade list", printWarningOnError: false) ? json : null;
    }

    private static IEnumerable<TradeEntry> AllEntries(TradeListDef def) =>
        new[] { def.Selling, def.Buying }.SelectMany(s =>
            s.Core.Concat(s.Rotating.List).Concat(s.Regional.Values.SelectMany(r => r.Core.Concat(r.Rotating))));
}
