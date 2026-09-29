using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.RecipeExport.Items;

/// <summary>
/// Ways to get an item other than a recipe, as far as the data declares them: block drops,
/// block harvests, entity drops and harvests, and trader lists. Drops decided in code
/// (Block.GetDrops overrides, loot vessels, panning, ...) are not seen; see item-data.md.
/// </summary>
internal sealed class SourceIndex
{
    private readonly Dictionary<string, List<JObject>> _byItem = new();
    private readonly ICoreServerAPI _api;

    public SourceIndex(ICoreServerAPI api)
    {
        _api = api;
        AddBlocks();
        AddEntities();
    }

    public int Count => _byItem.Values.Sum(l => l.Count);

    /// <summary>Sources of one item code, in a stable order; null when there are none.</summary>
    public JArray? For(string code)
    {
        if (!_byItem.TryGetValue(code, out var list)) return null;
        var sorted = list
            .Select(s => (Key: s.ToString(Newtonsoft.Json.Formatting.None), Source: s))
            .DistinctBy(s => s.Key)
            .OrderBy(s => s.Source["type"]!.Value<string>(), StringComparer.Ordinal)
            .ThenBy(s => s.Source["from"]!.Value<string>(), StringComparer.Ordinal)
            .ThenBy(s => s.Key, StringComparer.Ordinal)
            .Select(s => s.Source);
        return new JArray(sorted);
    }

    private void AddBlocks()
    {
        foreach (var block in _api.World.Blocks)
        {
            if (block?.Code == null || block.IsMissing) continue;
            var from = block.Code.ToString();
            if (!Json.IsValidCode(from)) continue;
            string? fromName = null;
            string FromName() => fromName ??= ItemRecords.Name(block);

            foreach (var drop in block.Drops ?? [])
            {
                var stack = drop?.ResolvedItemstack;
                // A block dropping itself is how every placeable block works; not a source worth listing.
                if (stack?.Collectible?.Code == null || stack.Collectible == block) continue;
                var s = Source("blockDrop", from, FromName(), Json.Quantity(drop!.Quantity));
                if (drop.Tool != null) s["tool"] = Json.Lower(drop.Tool.Value);
                Add(stack.Collectible.Code.ToString(), s);
            }

            var harvested = block.GetBehavior<BlockBehaviorHarvestable>()?.harvestedStacks
                            ?? block.GetBehavior<BlockBehaviorFruitingBush>()?.harvestedStacks;
            foreach (var drop in harvested ?? [])
            {
                var stack = drop?.ResolvedItemstack;
                if (stack?.Collectible?.Code == null) continue;
                var s = Source("other", from, FromName(), Json.Quantity(drop!.Quantity));
                s["note"] = "Harvested";
                if (drop.Tool != null) s["tool"] = Json.Lower(drop.Tool.Value);
                Add(stack.Collectible.Code.ToString(), s);
            }
        }
    }

    private void AddEntities()
    {
        foreach (var entity in _api.World.EntityTypes)
        {
            if (entity?.Code == null) continue;
            var from = entity.Code.ToString();
            if (!Json.IsValidCode(from)) continue;
            var fromName = Lang.GetMatching(entity.Code.Domain + ":item-creature-" + entity.Code.Path);

            foreach (var drop in entity.Drops ?? [])
                AddEntityDrop(drop, from, fromName, null);

            // Butchering drops live in the "harvestable" server behavior (and in look-alike
            // behaviors of mods), each with a "drops" array of block drop stacks.
            foreach (var behavior in entity.Server?.BehaviorsAsJsonObj ?? [])
            {
                if (behavior?["drops"] is not { Exists: true } drops) continue;
                var code = behavior["code"].AsString("");
                BlockDropItemStack[]? stacks;
                try { stacks = drops.AsObject<BlockDropItemStack[]>(null, entity.Code.Domain); }
                catch (Exception) { continue; }
                foreach (var drop in stacks ?? [])
                {
                    if (drop == null) continue;
                    if (drop.ResolvedItemstack == null && !drop.Resolve(_api.World, "seraphexport", entity.Code)) continue;
                    AddEntityDrop(drop, from, fromName, code == "harvestable" ? "Harvested" : "Behavior " + code);
                }
            }

            AddTrader(entity, from, fromName);
        }
    }

    private void AddEntityDrop(BlockDropItemStack? drop, string from, string fromName, string? note)
    {
        var stack = drop?.ResolvedItemstack;
        if (stack?.Collectible?.Code == null) return;
        var s = Source("entityDrop", from, fromName, Json.Quantity(drop!.Quantity));
        if (drop.Tool != null) s["tool"] = Json.Lower(drop.Tool.Value);
        if (note != null) s["note"] = note;
        Add(stack.Collectible.Code.ToString(), s);
    }

    /// <summary>Same lookup as the game's TradeHandbookInfo: a trade list file, or inline tradeProps.</summary>
    private void AddTrader(EntityProperties entity, string from, string fromName)
    {
        var file = entity.Attributes?["tradePropsFile"].AsString(null);
        if (file == null && entity.Attributes?["tradeProps"].Exists != true) return;
        TradeProperties? props;
        try
        {
            props = file != null
                ? _api.Assets.TryGet(AssetLocation.Create(file, entity.Code.Domain).WithPathAppendixOnce(".json"))?.ToObject<TradeProperties>()
                : entity.Attributes!["tradeProps"].AsObject<TradeProperties>(null, entity.Code.Domain);
        }
        catch (Exception e)
        {
            _api.Logger.Warning("[seraphexport] could not read trade list of {0}: {1}", from, e.Message);
            return;
        }
        if (props == null) return;
        AddTrades(props.Selling?.List, "traderSells", from, fromName);
        AddTrades(props.Buying?.List, "traderBuys", from, fromName);
    }

    private void AddTrades(TradeItem[]? list, string type, string from, string fromName)
    {
        foreach (var trade in list ?? [])
        {
            if (trade == null || !trade.Resolve(_api.World, "seraphexport " + from, false)) continue;
            var stack = trade.ResolvedItemstack;
            if (stack?.Collectible?.Code == null) continue;
            var s = Source(type, from, fromName, new JObject { ["avg"] = stack.StackSize });
            if (trade.Price != null) s["price"] = Json.Round(trade.Price.avg);
            var extra = new JObject();
            if (trade.Price != null && trade.Price.var != 0) extra["priceVar"] = Json.Round(trade.Price.var);
            if (trade.Stock != null) extra["stock"] = Json.Quantity(trade.Stock);
            if (trade.Attributes is { Exists: true } && trade.Attributes.Token is JObject attrs && attrs.Count > 0)
                extra["attributes"] = attrs.DeepClone();
            if (extra.Count > 0) s["extra"] = extra;
            Add(stack.Collectible.Code.ToString(), s);
        }
    }

    private static JObject Source(string type, string from, string fromName, JObject quantity) => new()
    {
        ["type"] = type,
        ["from"] = from,
        ["fromName"] = fromName,
        ["quantity"] = quantity,
    };

    private void Add(string code, JObject source)
    {
        if (!_byItem.TryGetValue(code, out var list)) _byItem[code] = list = new();
        list.Add(source);
    }
}
