using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One tool mold cast with one metal: the stacks it drops.</summary>
public sealed record Cast(string Metal, Item Ingot, List<ItemStack> Drops);

/// <summary>Tool molds that differ only in colour, and what each metal poured into them makes.</summary>
public sealed class MoldGroup
{
    public required List<Block> Molds;
    public required int Units;

    /// <summary>The drops as the mold names them, with <c>{metal}</c> left in.</summary>
    public required List<(string Code, EnumItemClass Type, int Quantity)> Templates;
    public List<Cast> Casts = new();
}

/// <summary>
/// Casting in the game's tool molds (<see cref="BlockToolMold"/>, decompiled 1.22): a crucible (or
/// any <c>ILiquidMetalSink</c> source, such as Steelmaking Expanded's canal) pours
/// <c>requiredUnits</c> (default 100, an ingot) of molten metal in, and once it has hardened the
/// mold gives its <c>drop</c> (or each of its <c>drops</c>), with <c>{metal}</c> the poured metal's
/// last code part. The poured metal is what smelts to that metal's ingot, so each
/// <c>ingot-{metal}</c> stands for it; a metal whose drop is not registered cannot be poured (the
/// mold's CanReceive asks for it). Molds that differ only in their colour are one group.
/// </summary>
public static class Casting
{
    public static List<MoldGroup> Find(ICoreServerAPI api)
    {
        var ingots = api.World.Items
            .Where(i => i?.Code != null && !i.IsMissing && i.Code.Path.StartsWith("ingot-") && i.Code.Path.Count(c => c == '-') == 1)
            .GroupBy(i => i.Code.Path["ingot-".Length..])
            .Select(g => g.OrderBy(i => i.Code.Domain == "game" ? 0 : 1).ThenBy(i => i.Code.ToString(), StringComparer.Ordinal).First())
            .OrderBy(i => i.Code.ToString(), StringComparer.Ordinal).ToList();

        var groups = new Dictionary<string, MoldGroup>(StringComparer.Ordinal);
        foreach (var block in api.World.Blocks
                     .Where(b => b is BlockToolMold && b.Code != null && !b.IsMissing)
                     .OrderBy(b => b.Code.ToString(), StringComparer.Ordinal))
        {
            var drops = Drops(block);
            if (drops.Count == 0) continue;
            var key = block.Code.Domain + ":" + string.Join("-", block.Variant.Where(kv => kv.Key != "color").Select(kv => kv.Value))
                      + "|" + block.Code.Path.Split('-')[0];
            if (!groups.TryGetValue(key, out var group))
            {
                group = new MoldGroup
                {
                    Molds = new(),
                    Units = block.Attributes?["requiredUnits"].AsInt(100) ?? 100,
                    Templates = drops.Select(d => (d.Code.Domain + ":" + Fill(d.Code.Path, block, "{metal}"), d.Type, Math.Max(1, d.Quantity))).ToList(),
                };
                foreach (var ingot in ingots)
                {
                    var metal = ingot.Code.Path["ingot-".Length..];
                    var stacks = new List<ItemStack>();
                    foreach (var drop in drops)
                    {
                        var d = drop.Clone();
                        d.Code = new AssetLocation(d.Code.Domain, Fill(d.Code.Path, block, metal));
                        if (d.Resolve(api.World, "tool mold drop", false) && d.ResolvedItemstack is { } s) stacks.Add(s);
                    }
                    if (stacks.Count == drops.Count) group.Casts.Add(new Cast(metal, ingot, stacks));
                }
                groups[key] = group;
            }
            group.Molds.Add(block);
        }
        return groups.Values.Where(g => g.Casts.Count > 0).ToList();
    }

    private static List<JsonItemStack> Drops(Block block)
    {
        var a = block.Attributes;
        if (a == null) return new();
        if (a["drop"].Exists && a["drop"].AsObject<JsonItemStack>() is { Code: not null } one) return new() { one };
        if (a["drops"].Exists && a["drops"].AsObject<JsonItemStack[]>() is { } many) return many.Where(d => d?.Code != null).ToList();
        return new();
    }

    private static string Fill(string path, Block block, string metal)
    {
        path = path.Replace("{metal}", metal);
        foreach (var (k, v) in block.Variant) path = path.Replace("{" + k + "}", v);
        return path;
    }
}
