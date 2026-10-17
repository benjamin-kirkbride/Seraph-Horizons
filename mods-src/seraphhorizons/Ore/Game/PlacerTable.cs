using System.Reflection;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Ore;

/// <summary>
/// The gold pan's table for a placer gravel block (#701): what the machines that wash rich gravel
/// (<see cref="ConcentratorWash"/>) scale from the pan. Read from the wooden pan's
/// <c>dropsBySourceMat</c> (the game's <c>BlockPan</c>, as Wilderlands Panning and <see cref="PanningDrops"/>
/// leave it), the list whose key matches the gravel's code, <c>{rocktype}</c> filled in with its rock, each
/// drop's chance a pan (<see cref="PlacerLine.ChancePerPan"/>) and whether it is metal (a nugget). Cached per
/// gravel code for the world.
/// </summary>
public static class PlacerTable
{
    /// <summary>The gold pan whose table placer gravel follows.</summary>
    public static readonly AssetLocation PanCode = new("game", "pan-wooden");

    private static readonly FieldInfo? PanDropsField =
        typeof(BlockPan).GetField("dropsBySourceMat", BindingFlags.NonPublic | BindingFlags.Instance);

    private const string CacheKey = "seraphhorizons-placertable";

    /// <summary>The table for <paramref name="gravelCode"/>; empty when the pan or the gravel is missing or
    /// the table cannot be read.</summary>
    public static IReadOnlyList<(string Code, double ChancePerPan, bool Metal)> For(ICoreAPI api, string gravelCode)
    {
        var cache = ObjectCacheUtil.GetOrCreate(api, CacheKey, () => new Dictionary<string, IReadOnlyList<(string, double, bool)>>());
        lock (cache)
        {
            if (!cache.TryGetValue(gravelCode, out var table))
                cache[gravelCode] = table = Read(api.World, gravelCode);
            return table;
        }
    }

    private static List<(string, double, bool)> Read(IWorldAccessor world, string gravelCode)
    {
        var result = new List<(string, double, bool)>();
        if (world.GetBlock(PanCode) is not BlockPan pan || world.GetBlock(new AssetLocation(gravelCode)) is not { Id: > 0 } gravel
            || PanDropsField?.GetValue(pan) is not Dictionary<string, PanningDrop[]> table)
            return result;
        string material = gravel.Code.ToShortString();
        PanningDrop[]? drops = null;
        foreach (var (key, list) in table)
            if (WildcardUtil.Match(key, material))
                drops = list;
        if (drops == null || drops.Length == 0)
            return result;
        var chances = drops.Select(d => (double)Math.Clamp(d.Chance.avg + d.Chance.offset, 0, 1)).ToList();
        for (int i = 0; i < drops.Length; i++)
        {
            var code = drops[i].Code.Clone();
            if (code.Path.Contains("{rocktype}"))
                code.Path = code.Path.Replace("{rocktype}", gravel.Variant["rock"]);
            CollectibleObject? c = drops[i].Type == EnumItemClass.Block ? world.GetBlock(code) : world.GetItem(code);
            if (c == null || c.IsMissing)
                continue;
            bool metal = c.Code.Path.StartsWith("nugget-", StringComparison.Ordinal);
            result.Add((c.Code.ToString(), PlacerLine.ChancePerPan(chances, i), metal));
        }
        return result;
    }
}
