using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Ore;

/// <summary>
/// Single ore drops (<c>SingleOreDrops</c>): every loaded block, of any mod, whose drops make it an
/// ore block (<see cref="SingleOreDrops.Plan"/>) drops exactly one of its ore and no crystallised
/// ore. Run in <see cref="OreSystem.AssetsFinalize"/> on the server, after every mod's blocks are
/// loaded and patched; clients get the server's drops.
/// </summary>
public static class SingleOreDropsPass
{
    /// <summary>How many blocks were changed, by domain, in the last run; for the log and the scenarios.</summary>
    public static IReadOnlyDictionary<string, int> ChangedByDomain { get; private set; } = new Dictionary<string, int>();

    public static void Clear() => ChangedByDomain = new Dictionary<string, int>();

    public static void Apply(ICoreAPI api)
    {
        var world = api.World;
        var byDomain = new Dictionary<string, int>(StringComparer.Ordinal);
        int crystallised = 0;
        foreach (var block in world.Blocks)
        {
            if (block?.Code == null || block.Drops is not { Length: > 0 } drops) continue;
            var views = drops.Select(d => View(world, d)).ToArray();
            if (SingleOreDrops.Plan(views) is not { } plan) continue;
            var kept = new List<BlockDropItemStack>(drops.Length);
            for (int i = 0; i < drops.Length; i++)
            {
                switch (plan[i])
                {
                    case OreDropChange.Remove:
                        crystallised++;
                        continue;
                    case OreDropChange.One:
                        // A drop can be shared between variants; a fresh one keeps the change to this block.
                        var one = drops[i].Clone();
                        one.Quantity = NatFloat.createUniform(1, 0);
                        kept.Add(one);
                        break;
                    default:
                        kept.Add(drops[i]);
                        break;
                }
            }
            block.Drops = kept.ToArray();
            byDomain[block.Code.Domain] = byDomain.GetValueOrDefault(block.Code.Domain) + 1;
        }
        ChangedByDomain = byDomain;
        api.Logger.Notification("[seraphhorizons] Single ore drops: {0} ore blocks drop one raw ore ({1}), {2} crystallised ore drops removed",
            byDomain.Values.Sum(), byDomain.Count == 0 ? "none" : string.Join(", ", byDomain.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key} {kv.Value}")),
            crystallised);
    }

    private static OreDropView View(IWorldAccessor world, BlockDropItemStack? drop)
    {
        if (drop?.Code == null) return new OreDropView(false, "", false, 0);
        bool item = drop.Type == EnumItemClass.Item;
        var collectible = drop.ResolvedItemstack?.Collectible ?? (item ? world.GetItem(drop.Code) : world.GetBlock(drop.Code));
        return new OreDropView(item, drop.Code.Path, collectible?.Attributes?["metalUnits"].Exists == true, drop.Quantity?.avg ?? 0);
    }
}
