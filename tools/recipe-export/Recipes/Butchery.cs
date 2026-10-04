using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>
/// One creature's butchery with the Butchering mod: one entity type and one carcass item.
/// The body is picked up whole, skinned on a hook, left there to bleed out and butchered on
/// a table; or it is harvested where it lies at a reduced yield. Variants are the entity
/// variants that give the same (their harvestable drops differ).
/// </summary>
public sealed class ButcheryChain
{
    public required string EntityType;
    public required string Workload;

    /// <summary>The carcass as picked up: one item per coat texture the creature can have.</summary>
    public required List<Item> Dead;
    public required Item Skinned;
    public required Item BledOut;

    public double? BleedHours;
    public int? KnifeCost, CleaverCost;
    public Item? Blood;
    public int BloodAmount;
    public float? BloodLitres;

    public List<ButcheryVariant> Variants = new();
}

public sealed class ButcheryVariant
{
    /// <summary>Entity variant codes and English names, sorted by code.</summary>
    public List<(string Code, string Name)> Entities = new();
    public List<BlockDropItemStack> Skinning = new();
    public List<BlockDropItemStack> Butchering = new();
    public List<BlockDropItemStack> FieldHarvest = new();

    /// <summary>The block the table leaves behind: the entity's dead-decay block (bones).</summary>
    public Block? Remains;

    /// <summary>Equal keys give equal records; see <see cref="Butchery.Key"/>.</summary>
    internal string Key = "";
}

/// <summary>A station that works on carcasses of one state: hooks skin, tables butcher.</summary>
public sealed class ButcheryStation
{
    public required List<Block> Blocks;

    /// <summary>Loot multiplier per block: its butcheringEfficiency times the mod's config multiplier.</summary>
    public required Dictionary<string, float> Efficiency;
    public required Type EntityClass;
    public bool TakesCleaver;
}

public sealed class ButcheryData
{
    public required string Mod;
    public required string BehaviorClass;

    /// <summary>Behavior codes entity types use for it (normally just "butcherable").</summary>
    public required HashSet<string> BehaviorCodes;
    public required ButcheryStation Hook;
    public required ButcheryStation Table;
    public required List<Item> Knives, Cleavers;
    public required List<Block> Buckets;

    /// <summary>What field harvesting yields with the mod, against without it; null when it cannot be measured.</summary>
    public float? FieldHarvestMultiplier;

    /// <summary>Path prefixes of drops the creature's condition scales at a workstation even when they are not food.</summary>
    public string[] WeightPrefixes = [];
    public List<ButcheryChain> Chains = new();
}

/// <summary>
/// Reads the Butchering mod at export time. The exporter cannot reference the mod, so its
/// classes are found by name through the class registry and read by reflection; the engine
/// facts this follows are in docs/recipe-browser/exporter.md (Butchery).
/// </summary>
public static class Butchery
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                     BindingFlags.Static | BindingFlags.FlattenHierarchy;

    /// <summary>The mod's entity behavior: right-click a dead creature with an empty hand to pick it up.</summary>
    public const string BehaviorClassName = "EntityBehaviorButcherable";

    /// <summary>Null when no loaded mod registers the behavior.</summary>
    public static ButcheryData? Find(ICoreServerAPI api)
    {
        var behaviors = BehaviorCodes(api);
        if (behaviors.Count == 0) return null;
        var behaviorCode = behaviors.Order(StringComparer.Ordinal).First();
        var behaviorType = api.ClassRegistry.GetEntityBehaviorClass(behaviorCode);
        var mod = api.ModLoader.Mods.FirstOrDefault(m => m.Systems.Any(s => s.GetType().Assembly == behaviorType.Assembly))?.Info.ModID;
        if (mod == null) return null;

        var config = api.ModLoader.Mods.First(m => m.Info.ModID == mod).Systems
            .Select(s => s.GetType().GetField("Config")?.GetValue(s)).FirstOrDefault(c => c != null);
        var (hook, table) = Stations(api, config);
        if (hook == null || table == null)
        {
            api.Logger.Warning("[seraphexport] {0} is loaded but its hook or table was not found; butchery not exported", mod);
            return null;
        }
        var first = hook.Blocks[0];
        var data = new ButcheryData
        {
            Mod = mod,
            BehaviorClass = behaviorType.Name,
            BehaviorCodes = behaviors,
            Hook = hook,
            Table = table,
            Knives = List<Item>(first, "knifeList"),
            Cleavers = List<Item>(first, "cleaverList"),
            Buckets = api.World.Blocks.Where(b => b?.Code != null && !b.IsMissing && b.EntityClass != null &&
                                                  api.ClassRegistry.GetBlockEntity(b.EntityClass) is { } t &&
                                                  typeof(BlockEntityBucket).IsAssignableFrom(t))
                // The game's own buckets first, so the slot leads with the one most players have.
                .OrderBy(b => b.Code.Domain != "game").ThenBy(b => b.Code.ToString(), StringComparer.Ordinal).ToList(),
            FieldHarvestMultiplier = MeasureFieldHarvest(api, behaviorCode),
        };
        data.Chains = Chains(api, data);
        return data;
    }

    /// <summary>Codes under which the butchering behavior class is registered (normally just "butcherable").</summary>
    private static HashSet<string> BehaviorCodes(ICoreServerAPI api)
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in api.World.EntityTypes)
        foreach (var b in entity?.Server?.BehaviorsAsJsonObj ?? [])
        {
            var code = b?["code"].AsString();
            if (code == null || codes.Contains(code)) continue;
            if (api.ClassRegistry.GetEntityBehaviorClass(code)?.Name == BehaviorClassName) codes.Add(code);
        }
        return codes;
    }

    private static (ButcheryStation? Hook, ButcheryStation? Table) Stations(ICoreServerAPI api, object? config)
    {
        var byState = new Dictionary<string, List<Block>>(StringComparer.Ordinal);
        foreach (var block in api.World.Blocks.Where(b => b?.Code != null && !b.IsMissing)
                     .OrderBy(b => b.Code.ToString(), StringComparer.Ordinal))
        {
            // BlockButcherWorkstation.processesState: the carcass state the block works on.
            if (block.GetType().GetProperty("processesState")?.GetValue(block) is not string state) continue;
            if (!byState.TryGetValue(state, out var list)) byState[state] = list = new();
            list.Add(block);
        }
        // The hook takes the carcass as picked up (state "dead") and the table the bled-out one;
        // each reads its own loot multiplier from the mod's config.
        return (Station(api, byState.GetValueOrDefault("dead"), config, "SkinningRackLootMultiplier"),
                Station(api, byState.GetValueOrDefault("bledout"), config, "butcheringTableLootMultiplier"));
    }

    private static ButcheryStation? Station(ICoreServerAPI api, List<Block>? blocks, object? config, string multiplierField)
    {
        if (blocks == null || blocks.Count == 0 || blocks[0].EntityClass == null) return null;
        var entityClass = api.ClassRegistry.GetBlockEntity(blocks[0].EntityClass);
        if (entityClass == null) return null;
        var configured = config?.GetType().GetField(multiplierField)?.GetValue(config) as float? ?? 1f;
        return new ButcheryStation
        {
            Blocks = blocks,
            Efficiency = blocks.ToDictionary(b => b.Code.ToString(),
                b => (b.GetType().GetProperty("ButcheringEfficiency")?.GetValue(b) as float? ?? 1f) * configured),
            EntityClass = entityClass,
            // Only the table (BlockButcherTable) accepts a cleaver as well as a knife.
            TakesCleaver = Ancestors(blocks[0].GetType()).Any(t => t.Name == "BlockButcherTable"),
        };
    }

    private static List<T> List<T>(object o, string field) where T : CollectibleObject =>
        (o.GetType().GetField(field, Any)?.GetValue(o) as System.Collections.IEnumerable)?.OfType<T>()
        .Where(c => c.Code != null && !c.IsMissing).OrderBy(c => c.Code.ToString(), StringComparer.Ordinal).ToList() ?? new();

    private static IEnumerable<Type> Ancestors(Type? t)
    {
        for (; t != null; t = t.BaseType) yield return t;
    }

    private static object? Const(Type t, string name) => t.GetField(name, Any)?.GetRawConstantValue();

    /// <summary>
    /// The mod patches EntityBehaviorHarvestable.dropQuantityMultiplier to cut what a creature
    /// it can butcher gives when harvested where it lies. That getter needs an entity, so a
    /// bare one is built (never spawned or initialised) and asked twice: without and with
    /// the butchering behavior. Their ratio is the mod's cut, whatever its config says.
    /// </summary>
    private static float? MeasureFieldHarvest(ICoreServerAPI api, string behaviorCode)
    {
        try
        {
            var probe = api.ClassRegistry.CreateEntity("EntityAgent");
            probe.World = api.World;
            probe.Api = api;
            var props = new EntityProperties
            {
                Code = new AssetLocation("seraphexport", "probe"),
                Class = "EntityAgent",
                Server = new EntityServerProperties(Array.Empty<JsonObject>(), new Dictionary<string, JsonObject>()),
            };
            typeof(Entity).GetProperty(nameof(Entity.Properties))!.SetValue(probe, props);
            var harvestable = new EntityBehaviorHarvestable(probe);
            var getter = typeof(EntityBehaviorHarvestable).GetProperty("dropQuantityMultiplier", Any)!;
            var plain = (float)getter.GetValue(harvestable)!;
            props.Server.Behaviors.Add(api.ClassRegistry.CreateEntityBehavior(probe, behaviorCode));
            var butchered = (float)getter.GetValue(harvestable)!;
            return plain > 0 ? (float)Math.Round(butchered / plain, 4) : null;
        }
        catch (Exception e)
        {
            api.Logger.Warning("[seraphexport] could not measure the field harvesting multiplier: {0}", e.Message);
            return null;
        }
    }

    /// <summary>
    /// Field harvesting under the mod, without reading the rest of it: the behavior codes it
    /// is on and the measured multiplier. Null when the mod is not loaded or it cannot be measured.
    /// </summary>
    public static (HashSet<string> Codes, float Multiplier)? FieldHarvest(ICoreServerAPI api)
    {
        var codes = BehaviorCodes(api);
        if (codes.Count == 0) return null;
        return MeasureFieldHarvest(api, codes.Order(StringComparer.Ordinal).First()) is { } m ? (codes, m) : null;
    }

    public static bool IsButcherable(EntityProperties entity, HashSet<string> codes) =>
        entity.Server?.BehaviorsAsJsonObj?.Any(b => codes.Contains(b?["code"].AsString() ?? "")) == true;

    private static JsonObject? Behavior(EntityProperties entity, string code) =>
        entity.Server?.BehaviorsAsJsonObj?.FirstOrDefault(b => b?["code"].AsString() == code);

    private static List<ButcheryChain> Chains(ICoreServerAPI api, ButcheryData data)
    {
        // Exclusions the workstations apply to every drop (BlockEntityButcherWorkstation):
        // skinning takes the drops whose path starts with one of these, butchering the rest.
        var be = api.ClassRegistry.CreateBlockEntity(data.Hook.Blocks[0].EntityClass);
        var skinPrefixes = be.GetType().GetField("SkinningRackExclusives", Any)?.GetValue(be) as string[] ?? [];
        data.WeightPrefixes = be.GetType().GetField("AnimalWeightDoesApply", Any)?.GetValue(be) as string[] ?? [];

        var chains = new Dictionary<string, ButcheryChain>(StringComparer.Ordinal);
        using var english = new Items.EnglishLocale();
        foreach (var entity in api.World.EntityTypes.Where(e => e?.Code != null).OrderBy(e => e.Code.ToString(), StringComparer.Ordinal))
        {
            var behavior = entity.Server?.BehaviorsAsJsonObj?.FirstOrDefault(b => data.BehaviorCodes.Contains(b?["code"].AsString() ?? ""));
            if (behavior == null) continue;
            var itemCode = behavior["item"].AsString();
            if (itemCode == null) continue;
            var loc = new AssetLocation(itemCode);
            if (api.World.GetItem(loc) is not { IsMissing: false } item || Prop<string>(item, "ProcessingState") == null)
            {
                api.Logger.Warning("[seraphexport] {0}: butchering item {1} is not a carcass of the mod; skipped", entity.Code, itemCode);
                continue;
            }

            var type = Items.SourceIndex.EntityType(entity);
            var key = type + "|" + item.Code;
            if (!chains.TryGetValue(key, out var chain))
            {
                chain = NewChain(api, data, type, item);
                if (chain == null) continue;
                chains[key] = chain;
            }

            foreach (var dead in DeadItems(api, entity, item))
                if (!chain.Dead.Contains(dead)) chain.Dead.Add(dead);
            var harvest = HarvestDrops(api, entity);
            var exclude = Prop<string[]>(item, "ExcludeRewards") ?? [];
            bool Kept(BlockDropItemStack d) => !exclude.Any(x => d.Code!.Path.Equals(x));
            bool Skin(BlockDropItemStack d) => skinPrefixes.Any(p => d.Code!.Path.StartsWith(p));
            var variant = new ButcheryVariant
            {
                Skinning = Drops(api, item, "SkinningRewards").Concat(harvest).Where(d => Skin(d) && Kept(d)).ToList(),
                Butchering = Drops(api, item, "ButcheringRewards").Concat(harvest).Where(d => !Skin(d) && Kept(d)).ToList(),
                // Harvesting with a knife skips drops that ask for another tool.
                FieldHarvest = harvest.Where(d => d.Tool == null || d.Tool == EnumTool.Knife).ToList(),
                Remains = Behavior(entity, "deaddecay")?["decayedBlock"].AsString() is { } decayed
                    ? api.World.GetBlock(new AssetLocation(decayed)) : null,
            };
            variant.Key = Key(variant);
            var name = Lang.GetMatching(entity.Code.Domain + ":item-creature-" + entity.Code.Path);
            var same = chain.Variants.FirstOrDefault(v => v.Key == variant.Key);
            if (same == null) chain.Variants.Add(same = variant);
            same.Entities.Add((entity.Code.ToString(), name));
        }
        return chains.Values.ToList();
    }

    /// <summary>
    /// True when the creature's condition (animalWeight) scales the drop: food, what smelts into
    /// food, and the mod's own list (sinew, offal). <paramref name="workstation"/> is false for
    /// field harvesting, where only the first two count.
    /// </summary>
    public static bool ScaledByCondition(ButcheryData data, BlockDropItemStack drop, bool workstation)
    {
        var c = drop.ResolvedItemstack!.Collectible;
        if (c.NutritionProps != null || c.CombustibleProps?.SmeltedStack?.ResolvedItemstack?.Collectible?.NutritionProps != null) return true;
        return workstation && data.WeightPrefixes.Any(p => drop.Code!.Path.StartsWith(p));
    }

    private static ButcheryChain? NewChain(ICoreServerAPI api, ButcheryData data, string type, Item item)
    {
        var skinned = api.World.GetItem(item.CodeWithVariants(new[] { "texture", "state" }, new[] { "1", "skinned" }));
        var bledOut = skinned == null ? null : api.World.GetItem(skinned.CodeWithVariant("state", "bledout"));
        if (skinned == null || bledOut == null)
        {
            api.Logger.Warning("[seraphexport] {0} has no skinned or bled-out state; butchery not exported", item.Code);
            return null;
        }
        // Without the attribute the workstations never finish (ButcheringWorkLoad defaults only the property).
        var workload = item.Attributes?["butcheringWorkLoad"].Exists == true ? Prop<string>(item, "ButcheringWorkLoad") : null;
        if (workload == null)
        {
            api.Logger.Warning("[seraphexport] {0} has no butcheringWorkLoad, so no station takes it; butchery not exported", item.Code);
            return null;
        }
        var capitalised = workload.Length == 0 ? workload : char.ToUpperInvariant(workload[0]) + workload[1..];
        var chain = new ButcheryChain
        {
            EntityType = type,
            Workload = workload,
            Dead = new(),
            Skinned = skinned,
            BledOut = bledOut,
            BleedHours = Const(data.Hook.EntityClass, "hoursToBleedOut" + capitalised) as double?,
            KnifeCost = Const(data.Hook.EntityClass, "knifedurabilityloss" + workload) as int?,
            CleaverCost = Const(data.Table.EntityClass, "cleaverdurabilityloss" + workload) as int?,
            BloodAmount = Prop<int?>(item, "BloodAmount") ?? 0,
        };
        if (Prop<string>(item, "BloodType") is { } bloodType && api.World.GetItem(new AssetLocation(bloodType)) is { IsMissing: false } blood)
        {
            chain.Blood = blood;
            var perLitre = BlockLiquidContainerBase.GetContainableProps(new ItemStack(blood))?.ItemsPerLitre;
            if (perLitre is > 0) chain.BloodLitres = chain.BloodAmount / perLitre.Value;
        }
        return chain;
    }

    /// <summary>
    /// The carcass items a creature can give: EntityBehaviorButcherable picks the "texture"
    /// variant from the entity's texture index (0 to TexturesAlternatesCount), plus one, and
    /// falls back to texture 1 when that item does not exist.
    /// </summary>
    public static List<Item> DeadItems(ICoreServerAPI api, EntityProperties entity, Item item)
    {
        if (string.IsNullOrEmpty(item.Variant?["texture"])) return new() { item };
        var first = api.World.GetItem(item.CodeWithVariant("texture", "1")) ?? item;
        var found = new List<Item>();
        var alternates = entity.Client?.TexturesAlternatesCount ?? 0;
        for (int i = 0; i <= alternates; i++)
        {
            var c = api.World.GetItem(item.CodeWithVariant("texture", (i + 1).ToString())) ?? first;
            if (!c.IsMissing && !found.Contains(c)) found.Add(c);
        }
        return found;
    }

    /// <summary>The entity's harvestable drops: what it gives harvested where it lies, and what the workstations add.</summary>
    private static List<BlockDropItemStack> HarvestDrops(ICoreServerAPI api, EntityProperties entity)
    {
        var drops = Behavior(entity, "harvestable")?["drops"];
        if (drops == null || !drops.Exists) return new();
        BlockDropItemStack[]? stacks;
        try { stacks = drops.AsObject<BlockDropItemStack[]>(null, entity.Code.Domain); }
        catch (Exception) { return new(); }
        return Resolved(api, stacks, entity.Code);
    }

    private static List<BlockDropItemStack> Drops(ICoreServerAPI api, Item item, string property) =>
        Resolved(api, Prop<BlockDropItemStack[]>(item, property), item.Code);

    /// <summary>Resolved drops that can give anything: an average and spread of zero gives nothing.</summary>
    private static List<BlockDropItemStack> Resolved(ICoreServerAPI api, BlockDropItemStack[]? drops, AssetLocation owner) =>
        (drops ?? []).Where(d => d?.Code != null && (d.Quantity == null || d.Quantity.avg != 0 || d.Quantity.var != 0) &&
                                 (d.ResolvedItemstack != null || d.Resolve(api.World, "seraphexport", owner)))
            .ToList();

    private static T? Prop<T>(object o, string name) => o.GetType().GetProperty(name)?.GetValue(o) is T v ? v : default;

    /// <summary>Variants with equal keys give the same in every stage.</summary>
    private static string Key(ButcheryVariant v)
    {
        string Drops(List<BlockDropItemStack> list) => string.Join(",", list.Select(d =>
            $"{d.ResolvedItemstack!.Collectible.Code}:{d.Quantity?.avg}:{d.Quantity?.var}"));
        return string.Join("|", Drops(v.Skinning), Drops(v.Butchering), Drops(v.FieldHarvest), v.Remains?.Code);
    }
}
