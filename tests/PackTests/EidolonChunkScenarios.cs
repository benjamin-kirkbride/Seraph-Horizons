using Atlas.XUnit;
using SeraphHorizons.Mod.Eidolon;
using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Eidolon, chunk loading (README "Eidolon", chunk loading): an eidolon at
/// work keeps its chunks loaded and works with no player near. Each scenario on a floor of its own in
/// the sky, far from spawn, with no player joined, so the game would make the eidolon inactive (no AI,
/// no physics) were it not at work. Atlas pauses the chunk unloader for its world snapshots, so the
/// scenarios read the server's list of forced columns (what the unloader honours), not unloading itself.
/// </summary>
[AtlasWorld]
public class EidolonChunkScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;
    private EidolonSystem Mod => EidolonSystem.Of(World.Api)!;
    private EidolonChunkSystem Chunks => EidolonChunkSystem.Of(World.Api)!;
    private ItemAxe IronAxe => (ItemAxe)W.GetItem(new AssetLocation("game:axe-felling-iron"))!;

    private void Log(string what)
    {
        W.Logger.Notification("[eidolon-chunk-test] " + what);
        output.WriteLine(what);
    }

    private async Task<EntityLaborEidolon> Spawn(BlockPos at)
    {
        var e = Mod.Spawn(W, new Vec3d(at.X, at.Y, at.Z), 0, null, activate: true);
        Assert.NotNull(e);
        await World.Until(() => e!.CanWork, 600);
        e!.GetBehavior<EntityBehaviorEidolonAxe>()!.Hold(new ItemStack(IronAxe));
        return e;
    }

    private async Task<BlockPos> Tree(BlockPos at, int seed)
    {
        var log = await Felling.Grow(World, at, Felling.Oak, 1.0f, seed);
        // The generator grows from the block above the one it is given (EidolonFellScenarios).
        if (W.BlockAccessor.GetBlock(log.DownCopy()).Id == 0)
            World.SetBlock("game:soil-medium-normal", log.DownCopy());
        return log;
    }

    private static MarkArea Area(BlockPos a, BlockPos b) =>
        MarkArea.Between(new MarkPos(a.X, a.Y, a.Z), new MarkPos(b.X, b.Y, b.Z));

    private IReadOnlyCollection<ChunkColumn> Held(EntityLaborEidolon e) => Chunks.Holds.Of(e.EntityId);

    private string State(EntityLaborEidolon e) =>
        $"{EidolonCommands.Describe(e)}; at {e.Pos.XYZ}; {e.State}; keeps {e.KeepsChunksLoaded}; holds {Held(e).Count}; "
        + e.GetInfoText().Replace('\n', ' ');

    private void AssertNoPlayerNear(EntityLaborEidolon e) =>
        Assert.DoesNotContain(W.AllOnlinePlayers, p => p.Entity != null && p.Entity.Pos.DistanceTo(e.Pos) < 1000);

    [AtlasScenario]
    public void It_is_on_with_the_eidolon_and_holds_a_column_and_the_eight_round_it()
    {
        Assert.True(Chunks.Running);
        Assert.True(Mod.Config.KeepWorkChunksLoaded);
        Assert.Equal(1, Mod.Config.ChunkLoadRadius);
    }

    [AtlasScenario(TimeoutMs = 480_000)]
    public async Task Felling_with_no_player_near_it_keeps_its_chunks_loaded_fells_and_then_lets_them_go()
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-1800, 90, -1600), reach: 18);
        // Two of EidolonFellScenarios' three oaks, as far apart (their crowns clear of each other).
        var logs = new[] { await Tree(site.AddCopy(-9, 0, -2), 101), await Tree(site.AddCopy(9, 0, -2), 103) };
        await World.Ticks(5);
        Assert.All(logs, l => Assert.True(Felling.Tree(W, IronAxe, l).Wood >= Mod.Config.FellMinLogs, $"the tree at {l} is not grown"));
        var e = await Spawn(site.AddCopy(0, 0, -11));
        AssertNoPlayerNear(e);
        // No player near and no work: the game leaves it inactive and nothing holds its chunks.
        Chunks.Pass();
        Assert.Equal(EnumEntityState.Inactive, e.State);
        Assert.Empty(Held(e));
        Assert.False(e.KeepsChunksLoaded);

        var area = Area(site.AddCopy(-13, -1, -8), site.AddCopy(13, -1, 12));
        e.Orders!.SetOrder(FellOrder.OrderCode, FellOrder.Args(area));
        Chunks.Pass();
        Log("ordered: " + State(e));
        Assert.True(e.KeepsChunksLoaded);
        var own = EidolonChunks.At(e.Pos.X, e.Pos.Z);
        var held = Held(e);
        Assert.Contains(own, held);
        Assert.Contains(new ChunkColumn(own.X + 1, own.Z + 1), held);
        Assert.Contains(new ChunkColumn(own.X - 1, own.Z - 1), held);
        Assert.All(EidolonChunks.Covering(area), c => Assert.Contains(c, held));
        Assert.All(held, c => Assert.True(Chunks.IsForced(c), $"{c} not forced"));
        await World.Until(() => e.State == EnumEntityState.Active, 200);

        // It walks and fells both trees with no one near (the order is done and cleared after).
        int ticks = 0;
        var columnsSeen = new HashSet<ChunkColumn>(held);
        await World.Until(() =>
        {
            columnsSeen.UnionWith(Held(e));
            if (++ticks % 300 == 0)
                Log(State(e));
            return e.Orders.OrderCode == null;
        }, 9000);
        Log("done: " + State(e));
        AssertNoPlayerNear(e);
        Assert.All(logs, l => Assert.True(Felling.Tree(W, IronAxe, l).Wood == 0, $"the tree at {l} stands"));

        // Done, it lets every column go.
        Chunks.Pass();
        Assert.False(e.KeepsChunksLoaded);
        Assert.Empty(Held(e));
        Assert.All(columnsSeen, c => Assert.False(Chunks.IsForced(c) && !Chunks.Holds.Holds(c), $"{c} still forced"));
    }

    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task It_lets_its_chunks_go_dry_down_staying_or_without_an_axe_and_holds_them_again_at_work()
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(1700, 90, -1900), reach: 30);
        // One tree far across the area: walking to it takes a while, so the order keeps work throughout.
        await Tree(site.AddCopy(0, 0, 26), 301);
        await World.Ticks(5);
        var e = await Spawn(site.AddCopy(0, 0, -26));
        var area = Area(site.AddCopy(-6, -1, 20), site.AddCopy(6, -1, 29));
        var hand = e.GetBehavior<EntityBehaviorEidolonAxe>()!;

        async Task Work()
        {
            e.Orders!.SetOrder(FellOrder.OrderCode, FellOrder.Args(area));
            await World.Ticks(3);
            Chunks.Pass();
            Log("at work: " + State(e));
            Assert.True(e.KeepsChunksLoaded, State(e));
            Assert.NotEmpty(Held(e));
        }

        void LetGo(string why)
        {
            var had = Held(e).ToList();
            Chunks.Pass();
            Log(why + ": " + State(e));
            Assert.False(e.KeepsChunksLoaded, why);
            Assert.Empty(Held(e));
            Assert.All(had, c => Assert.False(Chunks.IsForced(c), $"{why}: {c} still forced"));
        }

        await Work();
        // Dry: it stands and waits for oil, so its chunks go.
        if (e.Oil is { Tank: not null } oil)
        {
            oil.SetPoints(0);
            Assert.False(e.CanWork);
            LetGo("dry");
            oil.SetPoints(500);
            await World.Ticks(2);
            Chunks.Pass();
            Assert.True(e.KeepsChunksLoaded, "oiled again: " + State(e));
        }
        else
            Log("MachineOil is off: no reservoir, never dry");

        // Ordered to stay: it holds a place, no work.
        e.Orders!.SetOrder(StayOrder.OrderCode);
        await World.Ticks(2);
        LetGo("staying");

        // No axe: the fell order waits on its player.
        await Work();
        hand.Hold(null);
        await World.Until(() => e.Orders.Current is FellOrder { WaitsForAxe: true }, 200);
        LetGo("no axe");
        hand.Hold(new ItemStack(IronAxe));
        await World.Until(() => e.Orders.Current is FellOrder { WaitsForAxe: false }, 200);
        Chunks.Pass();
        Assert.True(e.KeepsChunksLoaded, "given an axe again: " + State(e));

        // Down at 0 HP.
        e.GetBehavior<EntityBehaviorHealth>()!.Health = 0;
        e.Check();
        Assert.False(e.CanWork);
        LetGo("slumped");

        // Gone from the world: its holds go with it.
        e.GetBehavior<EntityBehaviorHealth>()!.Health = e.GetBehavior<EntityBehaviorHealth>()!.MaxHealth;
        e.Check();
        await World.Until(() => e.CanWork, 600);
        await Work();
        var had = Held(e).ToList();
        e.Die(EnumDespawnReason.Removed);
        await World.Ticks(2);
        Chunks.Pass();
        Assert.Empty(Held(e));
        Assert.All(had, c => Assert.False(Chunks.IsForced(c), $"removed: {c} still forced"));
    }

    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task A_restart_loads_the_chunks_it_held_again_and_lets_go_of_a_hold_whose_eidolon_never_loads()
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-1900, 90, 1800), reach: 30);
        await Tree(site.AddCopy(0, 0, 26), 401);
        await World.Ticks(5);
        var e = await Spawn(site.AddCopy(0, 0, -26));
        e.Orders!.SetOrder(FellOrder.OrderCode, FellOrder.Args(Area(site.AddCopy(-6, -1, 20), site.AddCopy(6, -1, 29))));
        await World.Ticks(3);
        Chunks.Pass();
        var held = Held(e).ToHashSet();
        Assert.NotEmpty(held);

        // What the savegame keeps: its holds, which the server's start loads again.
        var saved = ChunkHolds.FromBytes(Chunks.SavedNow());
        Assert.Equal(held, saved.Single(h => h.EntityId == e.EntityId).Columns.ToHashSet());

        // A hold saved for an eidolon that never loads (it was removed meanwhile, say): its columns are
        // forced and loaded at start, kept while it may yet load, then let go.
        var far = EidolonChunks.At(site.X + 3000, site.Z);
        var orphan = new ChunkHolds();
        orphan.Set(-4242, [far], _ => false);
        Chunks.RestoreFrom(orphan.ToBytes(), seconds: 3);
        Assert.True(Chunks.IsForced(far));
        await World.Until(() => W.BlockAccessor.GetChunk(far.X, 0, far.Z) != null, 1200);
        Chunks.Pass();
        Assert.True(Chunks.IsForced(far), "let go before its time");
        await World.Ticks(120);
        Chunks.Pass();
        Assert.False(Chunks.IsForced(far), "the orphan's column is still forced");
        Assert.Empty(Chunks.Holds.Of(-4242));

        // The working eidolon's own hold restored over itself changes nothing, and it carries on holding.
        Chunks.Pass();
        held = Held(e).ToHashSet();
        Chunks.RestoreFrom(Chunks.SavedNow(), seconds: 3);
        Chunks.Pass();
        Assert.Equal(held, Held(e).ToHashSet());
        Assert.All(held, c => Assert.True(Chunks.IsForced(c)));
    }
}
