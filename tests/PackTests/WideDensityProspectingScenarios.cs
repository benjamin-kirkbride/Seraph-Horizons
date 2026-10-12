using System.Diagnostics;
using System.Reflection;
using Atlas.Api;
using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod.Ore;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>BetterEr Prospecting's density mode as the pick uses it (its private <c>ProbeDensity</c>,
/// by reflection, as <c>OnBlockBrokenWith</c> calls it), for seraphhorizons' <c>WideDensityProspecting</c>
/// and its off check.</summary>
internal sealed class DensityPick
{
    private readonly IWorldSession _world;
    private readonly Item _pick;
    private readonly MethodInfo _probe;

    public IServerPlayer Player { get; }
    public ItemSlot Slot { get; }
    public Assembly BetterEr { get; }

    private DensityPick(IWorldSession world, IServerPlayer player, Assembly betterEr, Item pick)
    {
        _world = world;
        Player = player;
        BetterEr = betterEr;
        _pick = pick;
        _probe = AccessTools.Method(pick.GetType(), "ProbeDensity")!;
        Slot = player.InventoryManager.ActiveHotbarSlot;
        Slot.Itemstack = new ItemStack(pick);
    }

    /// <summary>Joins <paramref name="name"/> at spawn with an iron pick in hand, with the chunks
    /// <paramref name="reach"/> chunks around spawn loaded.</summary>
    public static async Task<DensityPick> Join(IWorldSession world, string name, int reach)
    {
        var w = world.Api.World;
        var player = (IServerPlayer)(await world.JoinAtSpawn(name)).Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        var spawn = w.DefaultSpawnPosition.AsBlockPos;
        int size = GlobalConstants.ChunkSize, cx0 = spawn.X / size, cz0 = spawn.Z / size;
        var columns = new List<BlockPos>();
        for (int dx = -reach; dx <= reach; dx++)
            for (int dz = -reach; dz <= reach; dz++)
            {
                world.Api.WorldManager.LoadChunkColumnPriority(cx0 + dx, cz0 + dz);
                columns.Add(new BlockPos((cx0 + dx) * size + 1, 1, (cz0 + dz) * size + 1));
            }
        await world.Until(() => columns.All(c => w.BlockAccessor.GetChunkAtBlockPos(c) != null), 300_000);
        var betterEr = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "BetterErProspecting");
        var pick = w.GetItem(new AssetLocation("game:prospectingpick-iron"))!;
        Assert.Equal("BetterErProspecting.Item.ItemBetterErProspectingPick", pick.GetType().FullName);
        return new DensityPick(world, player, betterEr, pick);
    }

    /// <summary>A rock block 20 below the surface at spawn.</summary>
    public BlockPos Underground()
    {
        var w = _world.Api.World;
        var spawn = w.DefaultSpawnPosition.AsBlockPos;
        return new BlockPos(spawn.X, Math.Max(1, w.BlockAccessor.GetTerrainMapheightAt(spawn) - 20), spawn.Z);
    }

    /// <summary>One hit in density mode on <paramref name="at"/>: the damage the pick takes.</summary>
    public int Hit(BlockPos at) => (int)_probe.Invoke(_pick, [_world.Api.World, Player, Slot,
        new BlockSelection { Position = at.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 1, 0.5) }])!;

    /// <summary>BetterEr's settings object (<c>ModConfig.Instance</c>).</summary>
    public object Config => AccessTools.Property(BetterEr.GetType("BetterErProspecting.Config.ModConfig", true), "Instance")!.GetValue(null)!;

    public T Setting<T>(string name) => (T)AccessTools.Field(Config.GetType(), name)!.GetValue(Config)!;

    public MethodInfo SystemMethod(string name) =>
        AccessTools.Method(BetterEr.GetType("BetterErProspecting.Prospecting.ProspectingSystem", true), name)!;

    /// <summary>BetterEr's walk at <paramref name="at"/>, here (the main thread).</summary>
    public Dictionary<string, int> Walk(BlockPos at) =>
        (Dictionary<string, int>)SystemMethod("GenerateBlockData").Invoke(null, [_world.Api, at, null])!;

    /// <summary>BetterEr's reading of <paramref name="counts"/> at <paramref name="at"/>: parts per
    /// thousand by deposit code.</summary>
    public Dictionary<string, double> Ppt(BlockPos at, Dictionary<string, int> counts)
    {
        object?[] args = [_world.Api, at, counts, null, null, null];
        Assert.True((bool)SystemMethod("generateReadigs").Invoke(null, args)!);
        return ((PropickReading)args[3]!).OreReadings.ToDictionary(kv => kv.Key, kv => kv.Value.PartsPerThousand);
    }

    /// <summary>The player's readings on the ore map layer (where <c>DidProbe</c> puts them).</summary>
    public List<PropickReading> Readings()
    {
        var layer = _world.Api.ModLoader.GetModSystem<WorldMapManager>().MapLayers.OfType<OreMapLayer>().Single();
        return layer.PropickReadingsByPlayer.TryGetValue(Player.PlayerUID, out var list) ? list : [];
    }
}

/// <summary>
/// <c>WideDensityProspecting</c>: BetterEr Prospecting's density mode reads 64 blocks each way,
/// normalised over that area, and walks off the main thread, its reading arriving a few ticks after
/// the hit. A standard world, so the walk has real ground under it.
/// </summary>
[AtlasWorld(Seed = 424242, WorldType = "standard")]
public class WideDensityProspectingScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private static DensityPick? _pick;

    private async Task<DensityPick> Pick() => _pick ??= await DensityPick.Join(World, "densitypick", 3);

    [AtlasScenario]
    public void Every_rewrite_found_and_applied()
    {
        Assert.Null(WideDensityProspectingSystem.Unsupported());
        Assert.True(WideDensityProspectingSystem.Patched);
        var betterEr = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "BetterErProspecting");
        var system = betterEr.GetType("BetterErProspecting.Prospecting.ProspectingSystem", true)!;
        var pick = betterEr.GetType("BetterErProspecting.Item.ItemBetterErProspectingPick", true)!;
        foreach (var (method, prefix, transpiler) in new (MethodBase, bool, bool)[]
                 {
                     (AccessTools.Method(system, "GenerateBlockData"), true, true),
                     (AccessTools.Method(system, "generateReadigs"), false, true),
                     (AccessTools.AsyncMoveNext(AccessTools.Method(system, "ReprospectTask")), false, true),
                     (AccessTools.Method(pick, "ProbeDensity"), true, false),
                 })
        {
            var info = Harmony.GetPatchInfo(method);
            Assert.NotNull(info);
            Assert.Equal(prefix, info.Prefixes.Any(p => p.owner == WideDensityProspectingSystem.HarmonyId));
            Assert.Equal(transpiler, info.Transpilers.Any(p => p.owner == WideDensityProspectingSystem.HarmonyId));
        }
        // Reprospect loads two chunks each way around a reading.
        Assert.Equal([-64, -32, 0, 32, 64], WideDensityProspectingSystem.ReprospectOffsets());
    }

    [AtlasScenario(TimeoutMs = 600_000)]
    public async Task The_wide_walk_counts_four_times_the_blocks_at_a_comparable_ppt()
    {
        var p = await Pick();
        var at = p.Underground();
        Dictionary<string, int> narrow, wide;
        Dictionary<string, double> narrowPpt, widePpt;
        try
        {
            WideDensityProspectingSystem.Radius = DensityProspecting.ShippedRadius;
            narrow = p.Walk(at);
            narrowPpt = p.Ppt(at, narrow);
        }
        finally
        {
            WideDensityProspectingSystem.Radius = DensityProspecting.WideRadius;
        }
        wide = p.Walk(at);
        widePpt = p.Ppt(at, wide);

        double ratio = (double)wide.Values.Sum() / narrow.Values.Sum();
        output.WriteLine($"r32: {narrow.Values.Sum()} blocks, r64: {wide.Values.Sum()} blocks, ratio {ratio:F2}");
        foreach (var key in wide.Keys.Union(narrow.Keys).OrderByDescending(k => wide.GetValueOrDefault(k)))
            output.WriteLine($"  {key,-28} r32 {narrow.GetValueOrDefault(key),7} ({narrowPpt.GetValueOrDefault(key),8:F3} ppt)   r64 {wide.GetValueOrDefault(key),7} ({widePpt.GetValueOrDefault(key),8:F3} ppt)");
        // (129 / 65)^2 = 3.94 the area; the ground's own variation on top.
        Assert.InRange(ratio, 3.0, 5.0);
        // Normalised over the wider zone, the deposits read on the same scale (the reading's other
        // entries, such as Hydrate or Diedrate's aquifer, are not the walk's).
        Assert.True(narrow.Values.Sum() > 0, "no deposit blocks around the sample");
        double narrowTotal = narrow.Keys.Sum(k => narrowPpt[k]), wideTotal = wide.Keys.Sum(k => widePpt[k]);
        output.WriteLine($"deposit ppt r32 {narrowTotal:F3}, r64 {wideTotal:F3}");
        Assert.InRange(wideTotal / narrowTotal, 0.5, 2.0);
    }

    [AtlasScenario(TimeoutMs = 600_000)]
    public async Task A_hit_is_charged_at_once_and_its_reading_arrives_from_a_walk_off_the_main_thread()
    {
        var p = await Pick();
        var at = p.Underground();
        Assert.True(p.Setting<bool>("NewDensityMode"));
        var walks = new List<(DensityWalk Walk, int MainThread)>();
        void OnDelivered(DensityWalk walk) => walks.Add((walk, Environment.CurrentManagedThreadId));
        WideDensityProspectingSystem.Delivered += OnDelivered;
        try
        {
            int before = p.Readings().Count;
            var watch = Stopwatch.StartNew();
            int damage = p.Hit(at);
            double hitMs = watch.Elapsed.TotalMilliseconds;
            // A second hit while the first is still being walked is turned away, at no cost.
            int second = p.Hit(at);
            output.WriteLine($"hit returned in {hitMs:F2} ms");

            Assert.Equal(p.Setting<int>("NewDensityDmg"), damage);
            Assert.Equal(0, second);
            Assert.Equal(1, WideDensityProspectingSystem.WalksInFlight);
            Assert.Equal(before, p.Readings().Count);

            await World.Until(() => WideDensityProspectingSystem.WalksInFlight == 0, 30_000);
            await World.Ticks(2);
            var (walk, mainThread) = Assert.Single(walks);
            output.WriteLine($"walk took {walk.Milliseconds:F1} ms on thread {walk.ThreadId}; delivered on thread {mainThread}");
            Assert.NotEqual(mainThread, walk.ThreadId);
            Assert.Equal(at, walk.Pos);
            Assert.Equal(p.Player.PlayerUID, walk.PlayerUid);

            var readings = p.Readings();
            Assert.Equal(before + 1, readings.Count);
            var reading = readings[^1];
            Assert.Equal(at.X, reading.Position.XInt);
            Assert.Equal(at.Z, reading.Position.ZInt);

            // The walk off the main thread counted what the same walk here counts.
            var here = p.Walk(at);
            Assert.Equal(here.OrderBy(kv => kv.Key), walk.Counts.OrderBy(kv => kv.Key));
            // And the reading is BetterEr's of those counts.
            var ppt = p.Ppt(at, here);
            Assert.Equal(ppt.Keys.OrderBy(k => k), reading.OreReadings.Keys.Where(ppt.ContainsKey).OrderBy(k => k));
            foreach (var (code, value) in ppt)
                Assert.Equal(value, reading.OreReadings[code].PartsPerThousand, 9);

            // Free again: the next hit walks.
            Assert.Equal(damage, p.Hit(at));
            await World.Until(() => WideDensityProspectingSystem.WalksInFlight == 0, 30_000);
            Assert.Equal(2, walks.Count);
        }
        finally
        {
            WideDensityProspectingSystem.Delivered -= OnDelivered;
        }
    }

    [AtlasScenario(TimeoutMs = 600_000)]
    public async Task With_the_old_density_mode_the_hit_is_as_BetterEr_ships_it()
    {
        var p = await Pick();
        var at = p.Underground();
        var config = p.Config;
        var mode = AccessTools.Field(config.GetType(), "NewDensityMode")!;
        mode.SetValue(config, false);
        try
        {
            try
            {
                p.Hit(at);
            }
            catch (TargetInvocationException e)
            {
                // The game's own density search, which BetterEr then runs, is not this tweak's.
                output.WriteLine("the old density mode threw: " + e.InnerException?.Message);
            }
            Assert.Equal(0, WideDensityProspectingSystem.WalksInFlight);
        }
        finally
        {
            mode.SetValue(config, true);
        }
    }
}
