using Atlas.Api;
using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.TrunkEntities;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;

namespace SeraphHorizons.PackTests;

/// <summary>Trees the game grows, felled the way the server fells them: Logging Expanded's felling
/// listener (the server's BreakBlock event) and then the axe's own break of the same block. For
/// the scenarios of seraphhorizons' <c>FlatFellingWear</c> and its off check.</summary>
internal static class Felling
{
    /// <summary>The game's tree generators: a one-block oak and the two-by-two redwood.</summary>
    public const string Oak = "englishoak";
    public const string Redwood = "redwoodpine";

    /// <summary>A stone floor in the sky at <paramref name="origin"/>, cleared <paramref name="reach"/>
    /// around and 40 high (to the world's top at most: a tree only grows into air), its chunks
    /// loaded.</summary>
    public static async Task<BlockPos> Sky(IWorldSession world, BlockPos origin, int reach = 14)
    {
        var sapi = (ICoreServerAPI)world.Api;
        var w = sapi.World;
        int size = Vintagestory.API.Config.GlobalConstants.ChunkSize;
        var columns = new List<BlockPos>();
        for (int cx = (origin.X - reach) / size; cx <= (origin.X + reach) / size; cx++)
            for (int cz = (origin.Z - reach) / size; cz <= (origin.Z + reach) / size; cz++)
                columns.Add(new BlockPos(cx * size, origin.Y, cz * size));
        foreach (var c in columns)
            sapi.WorldManager.LoadChunkColumnPriority(c.X / size, c.Z / size);
        await world.Until(() => columns.All(c => w.BlockAccessor.GetChunkAtBlockPos(c) != null), 30000);
        int floor = w.GetBlock(new AssetLocation("game:rock-granite")).Id;
        int height = Math.Min(40, w.BlockAccessor.MapSizeY - 2 - origin.Y);
        for (int x = -reach; x <= reach; x++)
        for (int z = -reach; z <= reach; z++)
        {
            w.BlockAccessor.SetBlock(floor, origin.AddCopy(x, -1, z));
            for (int y = 0; y <= height; y++)
                w.BlockAccessor.SetBlock(0, origin.AddCopy(x, y, z));
        }
        return origin;
    }

    /// <summary>Grows a tree of <paramref name="generator"/> at <paramref name="pos"/> and returns
    /// a log block at its base.</summary>
    public static async Task<BlockPos> Grow(IWorldSession world, BlockPos pos, string generator, float size, int seed)
    {
        var sapi = (ICoreServerAPI)world.Api;
        var w = sapi.World;
        world.SetBlock("game:soil-medium-normal", pos.DownCopy());
        var key = sapi.World.TreeGenerators.Keys.First(k => k.Path == generator);
        var bulk = sapi.World.GetBlockAccessorBulkUpdate(true, true);
        sapi.World.TreeGenerators[key].GrowTree(bulk, pos,
            new TreeGenParams { size = size, skipForestFloor = true, hemisphere = EnumHemisphere.North }, new LCGRandom(seed));
        bulk.Commit();
        await world.Ticks(2);
        // The lowest upright log nearest the base: the axe's tree search only spreads up and out
        // from where it starts, so a start on a branch or up the trunk finds a fraction of the tree.
        var logs = new List<BlockPos>();
        for (int y = -1; y <= 4; y++)
        for (int x = -2; x <= 2; x++)
        for (int z = -2; z <= 2; z++)
        {
            var at = pos.AddCopy(x, y, z);
            if (w.BlockAccessor.GetBlock(at).Code?.Path is { } path && (path.StartsWith("log-") || path.StartsWith("logsection-")))
                logs.Add(at);
        }
        if (logs.Count == 0)
            throw new Xunit.Sdk.XunitException($"no log at the base of the {generator} at {pos}: {w.BlockAccessor.GetBlock(pos).Code}");
        return logs.OrderBy(l => l.Y).ThenBy(l => Math.Abs(l.X - pos.X) + Math.Abs(l.Z - pos.Z))
            .ThenByDescending(l => w.BlockAccessor.GetBlock(l).Code!.Path.EndsWith("-ud")).First();
    }

    /// <summary>What stands around <paramref name="pos"/>: every non-air block within 4 across and
    /// 12 up, for a failure message.</summary>
    public static string Dump(IWorldAccessor w, BlockPos pos)
    {
        var lines = new List<string>();
        for (int y = -1; y <= 12; y++)
        for (int x = -4; x <= 4; x++)
        for (int z = -4; z <= 4; z++)
            if (w.BlockAccessor.GetBlock(pos.AddCopy(x, y, z)) is { Id: > 0 } b)
                lines.Add($"({x},{y},{z}) {b.Code}");
        return string.Join("\n", lines);
    }

    /// <summary>The tree's blocks as the axe finds them from <paramref name="log"/>, and how many
    /// of them are wood (what the game's felling charges).</summary>
    public static (List<Block> Blocks, int Wood) Tree(IWorldAccessor w, ItemAxe axe, BlockPos log)
    {
        var blocks = axe.FindTree(w, log, out _, out _).Select(p => w.BlockAccessor.GetBlock(p)).ToList();
        return (blocks, blocks.Count(b => b.BlockMaterial == EnumBlockMaterial.Wood));
    }

    /// <summary>A survival player holding an axe of <paramref name="durability"/> (its full one when
    /// null) in the active hotbar slot.</summary>
    public static async Task<(IServerPlayer Player, ItemSlot Slot, ItemAxe Axe)> Feller(IWorldSession world, string name, int? durability = null)
    {
        var w = world.Api.World;
        var player = (IServerPlayer)(await world.JoinPlayer(name)).Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        var axe = (ItemAxe)w.GetItem(new AssetLocation("game:axe-felling-iron"));
        var slot = player.InventoryManager.ActiveHotbarSlot;
        var stack = new ItemStack(axe);
        if (durability is { } d)
            axe.SetDurability(stack, d);
        slot.Itemstack = stack;
        slot.MarkDirty();
        return (player, slot, axe);
    }

    /// <summary>Fells the tree at <paramref name="log"/> as the server does on a player's break:
    /// Logging Expanded's listener first (unless <paramref name="withListener"/> is off: then no
    /// trunk, as for a wood it does not know), then the axe's break. Returns the durability the
    /// axe lost (its whole remaining one if it shattered).</summary>
    public static async Task<int> Fell(IWorldSession world, IServerPlayer player, ItemSlot slot, ItemAxe axe, BlockPos log, bool withListener = true)
    {
        var sapi = (ICoreServerAPI)world.Api;
        int before = axe.GetRemainingDurability(slot.Itemstack);
        var sel = new BlockSelection { Position = log.Copy(), Face = BlockFacing.NORTH, HitPosition = new Vec3d(0.5, 0.5, 0) };
        if (withListener)
        {
            var core = sapi.ModLoader.GetModSystem("LoggingMod.Core") ?? throw new Xunit.Sdk.XunitException("no LoggingMod.Core");
            var listener = AccessTools.Field(core.GetType(), "_fellingListener").GetValue(core)!;
            object[] args = [player, sel, 1f, EnumHandling.PassThrough];
            AccessTools.Method(listener.GetType(), "OnBreakBlock").Invoke(listener, args);
        }
        axe.OnBlockBrokenWith(sapi.World, player.Entity, slot, sel);
        await world.Ticks(5);
        return before - (slot.Itemstack == null ? 0 : axe.GetRemainingDurability(slot.Itemstack));
    }

    /// <summary>The trunks lying around <paramref name="around"/>: trunk entities' stacks, and trunk
    /// item entities' (what Logging Expanded throws, with <c>TrunkEntities</c> off).</summary>
    public static List<ItemStack> TrunksNear(IWorldSession world, BlockPos around, int radius = 20) =>
        world.EntitiesIn(new Cuboidi(around.X - radius, around.Y - 40, around.Z - radius, around.X + radius, around.Y + 60, around.Z + radius))
            .Where(e => e.Alive).Select(e => e switch
            {
                EntityItem item when SeraphHorizons.Mod.Machines.Trunks.IsTrunk(item.Itemstack) => item.Itemstack,
                EntityTrunk trunk => trunk.Trunk,
                _ => null,
            }).OfType<ItemStack>().ToList();
}

// seraphhorizons, FlatFellingWear (mods-src/seraphhorizons/FellingWear.cs). The trees stand in the
// sky far south of the woodworking rooms, one site per scenario.
public partial class WoodworkingScenarios
{
    private async Task<BlockPos> FellingSite(int n) => await Felling.Sky(World, World.Spawn.AddCopy(-300, 90, 1200 + 80 * n));

    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task Felling_an_oak_costs_the_axe_the_thin_figure_and_leaves_a_trunk()
    {
        Assert.True(FellingWear.Patched);
        var site = await FellingSite(0);
        var log = await Felling.Grow(World, site, Felling.Oak, 1.0f, 101);
        var (blocks, wood) = Felling.Tree(W, (ItemAxe)W.GetItem(new AssetLocation("game:axe-felling-iron")), log);
        Assert.False(FellingWearRules.IsThick(blocks.Select(b => b.Code.Path)));
        Assert.True(wood > FellingWearConfig.Defaults.ThinTree, $"an oak of {wood} logs");
        var (player, slot, axe) = await Felling.Feller(World, "oakfeller");

        int lost = await Felling.Fell(World, player, slot, axe, log);

        Assert.Equal(FellingWearConfig.Defaults.ThinTree, lost);
        Assert.Equal(0, Felling.Tree(W, axe, log).Wood);
        var trunks = Felling.TrunksNear(World, site);
        Assert.NotEmpty(trunks);
        Assert.Equal(wood, trunks.Sum(t => SeraphHorizons.Mod.Machines.Trunks.StoredLogs(t, W)));
    }

    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task Felling_a_redwood_costs_the_axe_the_thick_figure()
    {
        var site = await FellingSite(1);
        var log = await Felling.Grow(World, site, Felling.Redwood, 0.5f, 102);
        var (blocks, wood) = Felling.Tree(W, (ItemAxe)W.GetItem(new AssetLocation("game:axe-felling-iron")), log);
        Assert.True(FellingWearRules.IsThick(blocks.Select(b => b.Code.Path)));
        Assert.True(wood > FellingWearConfig.Defaults.ThickTree, $"a redwood of {wood} logs");
        var (player, slot, axe) = await Felling.Feller(World, "redwoodfeller");

        int lost = await Felling.Fell(World, player, slot, axe, log);

        Assert.Equal(FellingWearConfig.Defaults.ThickTree, lost);
        Assert.Equal(0, Felling.Tree(W, axe, log).Wood);
        Assert.NotEmpty(Felling.TrunksNear(World, site));
    }

    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task Felling_that_leaves_no_trunk_costs_the_games_one_per_log()
    {
        var site = await FellingSite(2);
        var log = await Felling.Grow(World, site, Felling.Oak, 1.0f, 103);
        var (_, wood) = Felling.Tree(W, (ItemAxe)W.GetItem(new AssetLocation("game:axe-felling-iron")), log);
        var (player, slot, axe) = await Felling.Feller(World, "logfeller");

        int lost = await Felling.Fell(World, player, slot, axe, log, withListener: false);

        Assert.Equal(wood, lost);
        Assert.Empty(Felling.TrunksNear(World, site));
    }

    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task A_worn_axe_fells_the_whole_tree_and_shatters_after()
    {
        var site = await FellingSite(3);
        var log = await Felling.Grow(World, site, Felling.Oak, 1.0f, 101);
        var (_, wood) = Felling.Tree(W, (ItemAxe)W.GetItem(new AssetLocation("game:axe-felling-iron")), log);
        Assert.True(wood > 2, $"an oak of {wood} logs from {log} {W.BlockAccessor.GetBlock(log).Code}\n{Felling.Dump(W, site)}");
        var (player, slot, axe) = await Felling.Feller(World, "wornfeller", durability: 2);

        await Felling.Fell(World, player, slot, axe, log);

        Assert.Null(slot.Itemstack);
        Assert.Equal(0, Felling.Tree(W, axe, log).Wood);
        var trunks = Felling.TrunksNear(World, site);
        Assert.Equal(wood, trunks.Sum(t => SeraphHorizons.Mod.Machines.Trunks.StoredLogs(t, W)));
    }
}
