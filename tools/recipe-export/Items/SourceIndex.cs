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
/// block harvests, panning, entity drops and harvests, and trader lists. Drops decided in code
/// (Block.GetDrops overrides, loot vessels, fishing, ...) are not seen; see item-data.md.
/// </summary>
internal sealed class SourceIndex
{
    private readonly Dictionary<string, List<JObject>> _byItem = new();
    private readonly ICoreServerAPI _api;

    public SourceIndex(ICoreServerAPI api)
    {
        _api = api;
        AddBlocks();
        AddPanning();
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

    /// <summary>
    /// One source per pannable block and drop of a pan's table. Only full blocks are listed:
    /// panning one leaves a block with layers 7 to 1 (pannedBlock), and each layer pans as the
    /// full block named by its first two code parts, so it repeats that block's drops. A full
    /// block whose layers pan as another block (sand-wavy leaves sand) also gets that block's
    /// drops, with extra.material. A layered block whose drops no full block lists is listed
    /// itself.
    /// </summary>
    private void AddPanning()
    {
        var pans = Panning.Pans(_api);
        int count = 0;
        var skipped = new List<string>();
        var leftover = new List<string>();
        foreach (var pan in pans)
        {
            var label = pans.Count > 1 ? pan.Pan.Code.ToString() : null;
            // (drop list, rock) pairs listed so far: what a layered block's pan would repeat.
            var listed = new HashSet<(PanningDrop[], string?)>();
            var layered = new List<Block>();
            foreach (var block in _api.World.Blocks)
            {
                if (block?.Code == null || block.IsMissing || !pan.Pan.IsPannableMaterial(block)) continue;
                if (!Json.IsValidCode(block.Code.ToString())) continue;
                if (block.Variant["layer"] != null) { layered.Add(block); continue; }
                var panned = pan.PannedBlock(block);
                var drops = pan.DropsFor(block);
                // The pan refuses a block with no panned block, and fails on one with no drops.
                if (panned == null || drops == null) { skipped.Add(block.Code.ToShortString()); continue; }
                count += AddPanned(pan, block, block, drops, label);
                listed.Add((drops, block.Variant["rock"]));
                if (panned.Variant["layer"] == null || !pan.Pan.IsPannableMaterial(panned) || pan.Material(panned) is not { } rest) continue;
                var restDrops = pan.DropsFor(rest);
                if (restDrops == null || listed.Contains((restDrops, rest.Variant["rock"]))) continue;
                count += AddPanned(pan, block, rest, restDrops, label);
                listed.Add((restDrops, rest.Variant["rock"]));
            }
            foreach (var block in layered)
            {
                if (pan.Material(block) is not { } material || pan.DropsFor(material) is not { } drops) continue;
                if (listed.Contains((drops, material.Variant["rock"]))) continue;
                count += AddPanned(pan, block, material, drops, label);
                leftover.Add(block.Code.ToShortString());
            }
        }
        _api.Logger.Notification("[seraphexport] {0} panning source(s) from {1} pan(s)", count, pans.Count);
        if (skipped.Count > 0)
            _api.Logger.Notification("[seraphexport] {0} pannable block(s) cannot be panned (no panned block or no drops): {1}",
                skipped.Count, string.Join(", ", skipped));
        if (leftover.Count > 0)
            _api.Logger.Notification("[seraphexport] {0} layered block(s) pan drops no full block lists: {1}",
                leftover.Count, string.Join(", ", leftover));
    }

    private int AddPanned(Panning pan, Block block, Block material, PanningDrop[] drops, string? label)
    {
        var from = block.Code.ToString();
        var fromName = ItemRecords.Name(block);
        var stacks = drops.Select(d => pan.Stack(d, material)).ToArray();
        // A drop that does not resolve still rolls, but a hit on it does not end the pan.
        var chances = drops.Select((d, i) => stacks[i] == null ? 0 : Panning.RollChance(d.Chance)).ToArray();
        int added = 0;
        for (var i = 0; i < drops.Length; i++)
        {
            var stack = stacks[i];
            if (stack?.Collectible?.Code == null) continue;
            var drop = drops[i];
            var quantity = new JObject { ["avg"] = Json.Significant((decimal)drop.Chance.avg) };
            if (drop.Chance.var != 0) quantity["var"] = Json.Significant((decimal)drop.Chance.var);
            var s = Source("other", from, fromName, quantity);
            s["note"] = "Panned";
            var extra = new JObject { ["chancePerPan"] = Json.Significant((decimal)Panning.ChancePerPan(chances, i)) };
            if (drop.DropModbyStat != null) extra["stat"] = drop.DropModbyStat;
            if (drop.Attributes is { Exists: true } && drop.Attributes.Token is JObject attrs && attrs.Count > 0)
                extra["attributes"] = attrs.DeepClone();
            if (stack.StackSize != 1) extra["stackSize"] = stack.StackSize;
            if (material != block) extra["material"] = material.Code.ToString();
            if (label != null) extra["pan"] = label;
            s["extra"] = extra;
            Add(stack.Collectible.Code.ToString(), s);
            added++;
        }
        return added;
    }

    private void AddEntities()
    {
        foreach (var entity in _api.World.EntityTypes)
        {
            if (entity?.Code == null) continue;
            var from = entity.Code.ToString();
            if (!Json.IsValidCode(from)) continue;
            var fromName = Lang.GetMatching(entity.Code.Domain + ":item-creature-" + entity.Code.Path);
            var type = EntityType(entity);

            foreach (var drop in entity.Drops ?? [])
                AddEntityDrop(drop, from, fromName, type, null);

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
                    AddEntityDrop(drop, from, fromName, type, code == "harvestable" ? "Harvested" : "Behavior " + code);
                }
            }

            AddTrader(entity, from, fromName, type);
        }
    }

    /// <summary>
    /// The code of the entity type file, which a variant's code extends with its variant
    /// states: game:wolf for game:wolf-eurasian-adult-male. The site shows a type's variants
    /// on one page.
    /// </summary>
    internal static string EntityType(EntityProperties entity)
    {
        var path = entity.Code.Path;
        if (entity.Variant is { Count: > 0 })
        {
            var suffix = "-" + string.Join("-", entity.Variant.Values);
            if (path.Length > suffix.Length && path.EndsWith(suffix, StringComparison.Ordinal)) path = path[..^suffix.Length];
        }
        return entity.Code.Domain + ":" + path;
    }

    private void AddEntityDrop(BlockDropItemStack? drop, string from, string fromName, string type, string? note)
    {
        var stack = drop?.ResolvedItemstack;
        if (stack?.Collectible?.Code == null) return;
        var s = Source("entityDrop", from, fromName, Json.Quantity(drop!.Quantity));
        if (drop.Tool != null) s["tool"] = Json.Lower(drop.Tool.Value);
        if (note != null) s["note"] = note;
        s["extra"] = new JObject { ["entityType"] = type };
        Add(stack.Collectible.Code.ToString(), s);
    }

    /// <summary>Same lookup as the game's TradeHandbookInfo: a trade list file, or inline tradeProps.</summary>
    private void AddTrader(EntityProperties entity, string from, string fromName, string type)
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
        AddTrades(props.Selling?.List, "traderSells", from, fromName, type);
        AddTrades(props.Buying?.List, "traderBuys", from, fromName, type);
    }

    private void AddTrades(TradeItem[]? list, string type, string from, string fromName, string entityType)
    {
        foreach (var trade in list ?? [])
        {
            if (trade == null || !trade.Resolve(_api.World, "seraphexport " + from, false)) continue;
            var stack = trade.ResolvedItemstack;
            if (stack?.Collectible?.Code == null) continue;
            var s = Source(type, from, fromName, new JObject { ["avg"] = stack.StackSize });
            if (trade.Price != null) s["price"] = Json.Round(trade.Price.avg);
            var extra = new JObject { ["entityType"] = entityType };
            if (trade.Price != null && trade.Price.var != 0) extra["priceVar"] = Json.Round(trade.Price.var);
            if (trade.Stock != null) extra["stock"] = Json.Quantity(trade.Stock);
            if (trade.Attributes is { Exists: true } && trade.Attributes.Token is JObject attrs && attrs.Count > 0)
                extra["attributes"] = attrs.DeepClone();
            s["extra"] = extra;
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
