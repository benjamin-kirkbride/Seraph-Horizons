using Atlas.Api;
using Atlas.XUnit;
using SeraphHorizons.Mod.Eidolon;
using SeraphHorizons.Mod.Eidolon.Core;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.TrunkEntities;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Eidolon: where an eidolon stands and walks, with the pack's default
/// settings: off a lying trunk (a trunk entity, solid by TrunkEntities' push, not a block) to the
/// floor beside it, alone (thin and thick) or two together; two spawned in one place keep apart
/// and each walks to its own point; and up and down a one-block step, alone and two together. Each
/// scenario logs where the eidolons are now and then, so a failure shows how they went wrong. And steps
/// of two (<see cref="EidolonClimb"/>): sent to a point on a ledge two blocks high it walks to its foot,
/// climbs it (playing <c>climb</c>, moved up and onto it by the navigator) and gets there, and sent back
/// it walks off the two-block drop; a ledge three high it neither climbs nor walks off, and says there is
/// no way. Each builds on a granite floor of its own high over the spawn.
/// </summary>
[AtlasWorld]
public class EidolonFootingScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;
    private ICoreServerAPI Sapi => (ICoreServerAPI)World.Api;
    private EidolonSystem Mod => EidolonSystem.Of(World.Api)!;

    /// <summary>The player <see cref="Floor"/> joined, for a scenario that has them followed.</summary>
    private ITestPlayer _watcher = null!;

    private void Log(string what)
    {
        W.Logger.Notification("[eidolon-footing-test] " + what);
        output.WriteLine(what);
    }

    /// <summary>A clear granite floor 40 above spawn at (<paramref name="dx"/>, 0), 12 each way,
    /// with 8 blocks of air over it, its chunks loaded, and a player watching (entities only
    /// simulate near one).</summary>
    private async Task<BlockPos> Floor(int dx, string watcher)
    {
        const int reach = 12;
        var origin = World.Spawn.AddCopy(dx, 40, 0);
        int size = Vintagestory.API.Config.GlobalConstants.ChunkSize;
        for (int cx = (origin.X - reach) / size; cx <= (origin.X + reach) / size; cx++)
            for (int cz = (origin.Z - reach) / size; cz <= (origin.Z + reach) / size; cz++)
                Sapi.WorldManager.LoadChunkColumnPriority(cx, cz);
        await World.Until(() => W.BlockAccessor.GetChunkAtBlockPos(origin.AddCopy(-reach, 0, -reach)) != null
                                && W.BlockAccessor.GetChunkAtBlockPos(origin.AddCopy(reach, 0, reach)) != null, 1200);
        int granite = W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;
        for (int x = -reach; x <= reach; x++)
        for (int z = -reach; z <= reach; z++)
        {
            W.BlockAccessor.SetBlock(granite, origin.AddCopy(x, -1, z));
            for (int y = 0; y <= 8; y++)
                W.BlockAccessor.SetBlock(0, origin.AddCopy(x, y, z));
        }
        _watcher = await World.JoinRetrying(watcher);
        ((IServerPlayer)_watcher.Player).WorldData.CurrentGameMode = EnumGameMode.Creative;
        await _watcher.TeleportTo(origin.AddCopy(-8, 0, -8));
        await World.Ticks(5);
        return origin;
    }

    /// <summary>An eidolon with no owner, awake and free to work, its feet at <paramref name="at"/>.</summary>
    private async Task<EntityLaborEidolon> Eidolon(Vec3d at)
    {
        var e = Mod.Spawn(W, at, 0, null, activate: true);
        Assert.NotNull(e);
        await World.Until(() => e!.CanWork, 400);
        return e!;
    }

    /// <summary>A thin (md) or thick (xl) trunk lying along z, its underside's middle at
    /// <paramref name="at"/>'s middle.</summary>
    private async Task<EntityTrunk> LyingTrunk(BlockPos at, string size)
    {
        var stack = new ItemStack(W.GetBlock(new AssetLocation($"loggingmod:treetrunk-oak-{size}-no-north"))!);
        var slots = new TreeAttribute();
        slots["0"] = new ItemstackAttribute(new ItemStack(W.GetBlock(new AssetLocation("game:log-placed-oak-ud"))!, 10));
        stack.Attributes["slots"] = slots;
        var trunk = TrunkSpawns.Spawn(W, stack, at.ToVec3d().Add(0.5, 0, 0.5), 0);
        Assert.NotNull(trunk);
        await World.Ticks(20);
        return trunk!;
    }

    private static double Apart(Entity a, Entity b) => Math.Sqrt(a.Pos.XYZ.HorizontalSquareDistanceTo(b.Pos.XYZ));

    private static string Where(EntityLaborEidolon e) =>
        $"({e.Pos.X:F2}, {e.Pos.Y:F3}, {e.Pos.Z:F2}) ground {e.OnGround} motion ({e.Pos.Motion.X:F3}, {e.Pos.Motion.Y:F3}, {e.Pos.Motion.Z:F3}) order {e.Orders?.OrderCode ?? "none"}";

    /// <summary>Whether it is walking a path, and what its order says of itself, if anything.</summary>
    private static string Walking(EntityLaborEidolon e) =>
        (e.TaskAi?.PathTraverser?.Active == true ? "walking" : "standing")
        + (e.WatchedAttributes.GetTreeAttribute(EntityBehaviorEidolonOrders.StatusKey)?.GetString("key") is { } key ? " (" + key + ")" : "");

    /// <summary>Waits at most <paramref name="ticks"/> for <paramref name="done"/>, logging where each
    /// of <paramref name="eidolons"/> is (relative to <paramref name="at"/>) every 40 ticks; whether it
    /// came.</summary>
    private async Task<bool> Watch(string what, BlockPos at, EntityLaborEidolon[] eidolons, Func<bool> done, int ticks, int every = 40)
    {
        for (int i = 0; ; i++)
        {
            bool ok = done();
            if (ok || i >= ticks || i % every == 0)
            {
                var each = eidolons.Select((e, n) =>
                    $"#{n} ({e.Pos.X - at.X:F2}, {e.Pos.Y - at.Y:F3}, {e.Pos.Z - at.Z:F2}) ground {e.OnGround} motion ({e.Pos.Motion.X:F3}, {e.Pos.Motion.Y:F3}, {e.Pos.Motion.Z:F3}) order {e.Orders?.OrderCode ?? "none"} {Walking(e)}");
                string apart = eidolons.Length == 2 ? $" apart {Apart(eidolons[0], eidolons[1]):F2}" : "";
                Log($"{what} tick {i}{(ok ? " done" : i >= ticks ? " timed out" : "")}:{apart} " + string.Join("; ", each));
            }
            if (ok)
                return true;
            if (i >= ticks)
                return false;
            await World.Ticks(1);
        }
    }

    private static void GoTo(EntityLaborEidolon e, Vec3d target) =>
        e.Orders!.SetOrder(GoToOrder.OrderCode, GoToOrder.Args(target, run: false));

    /// <summary>Whether <paramref name="e"/> has arrived at <paramref name="target"/>: its order done,
    /// standing within a block of it at its height.</summary>
    private static bool Arrived(EntityLaborEidolon e, Vec3d target) =>
        e.Orders!.OrderCode == null && e.OnGround && Math.Abs(e.Pos.Y - target.Y) < 0.1
        && e.Pos.XYZ.HorizontalSquareDistanceTo(target) < 1.0;

    private static void Remove(params Entity[] entities)
    {
        foreach (var e in entities)
            e.Die(EnumDespawnReason.Removed);
    }

    // ---- A: one eidolon off a trunk ----

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_alone_on_a_thin_trunk_it_walks_off_it_to_the_floor_beside() => await OffATrunk(-60, "md", "footingthin", 1);

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_alone_on_a_thick_trunk_it_walks_off_it_to_the_floor_beside() => await OffATrunk(-90, "xl", "footingthick", 1);

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_alone_on_a_thin_trunk_it_walks_off_it_to_a_point_close_beside() => await OffATrunk(-240, "md", "footingclose", 1, 1.5);

    // ---- E: two eidolons together off a trunk ----

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task E_two_together_on_a_thin_trunk_walk_off_it_to_the_floor_beside() => await OffATrunk(-120, "md", "footingtwotrunk", 2);

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task E_two_together_on_a_thin_trunk_follow_their_leader_off_it()
    {
        var at = await Floor(-270, "footingtwolead");
        using var clock = VirtualClock.Start(World);
        var eidolons = await OnATrunk(at, "md", 2);
        // Their leader stands on the floor ten blocks off; they follow, keeping their distance (four),
        // so both must come down off the trunk.
        await _watcher.TeleportTo(at.AddCopy(10, 0, 0));
        foreach (var e in eidolons)
            e.Orders!.SetOrder(FollowOrder.OrderCode, FollowOrder.Args(_watcher.Player));
        bool off = await Watch("following off the trunk", at, eidolons,
            () => eidolons.All(e => e.OnGround && Math.Abs(e.Pos.Y - at.Y) < 0.1 && e.Pos.X > at.X + 1.5), 900);
        Assert.True(off, string.Join("; ", eidolons.Select(Where)));
        Remove(eidolons);
    }

    /// <summary><paramref name="count"/> eidolons spawned in one place on a <paramref name="size"/>
    /// trunk lying along z at <paramref name="at"/>, a little off its axis. One stands on it; two
    /// part (<see cref="EidolonSpacing"/>), so one or both may step off it while they wake.</summary>
    private async Task<EntityLaborEidolon[]> OnATrunk(BlockPos at, string size, int count)
    {
        var trunk = await LyingTrunk(at, size);
        int height = TrunkBox.Size(trunk.TypeClass).Height;
        var top = new Vec3d(trunk.Pos.X + 0.1, at.Y + height + 0.05, trunk.Pos.Z + 0.3);
        var eidolons = Enumerable.Range(0, count).Select(_ => Mod.Spawn(W, top, 0, null, activate: true)!).ToArray();
        await World.Until(() => eidolons.All(e => e.CanWork), 400);
        await Watch($"{size} trunk, settling", at, eidolons, () => false, 40, 4);
        if (count == 1)
            Assert.InRange(eidolons[0].Pos.Y, at.Y + height - 0.05, at.Y + height + 0.1);
        return eidolons;
    }

    private async Task OffATrunk(int dx, string size, string watcher, int count, double outX = 3)
    {
        var at = await Floor(dx, watcher);
        using var clock = VirtualClock.Start(World);
        var eidolons = await OnATrunk(at, size, count);
        // Each to its own point on the floor, outX from the trunk's axis.
        var targets = eidolons.Select((_, i) => new Vec3d(at.X + 0.5 + outX, at.Y, at.Z + 0.5 + (count == 1 ? 0.5 : -1.5 + 3 * i))).ToArray();
        for (int i = 0; i < count; i++)
            GoTo(eidolons[i], targets[i]);
        bool off = await Watch($"off the {size} trunk", at, eidolons, () => eidolons.Select((e, i) => Arrived(e, targets[i])).All(a => a), 900);
        Assert.True(off, string.Join("; ", eidolons.Select(Where)));
        Remove(eidolons);
    }

    // ---- B: two spawned in one place ----

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task B_two_spawned_in_one_place_keep_apart_and_each_walks_to_its_point()
    {
        var at = await Floor(-150, "footingtwo");
        using var clock = VirtualClock.Start(World);
        var spot = new Vec3d(at.X, at.Y, at.Z);
        var a = await Eidolon(spot);
        var b = await Eidolon(spot);
        await Watch("two in one place, settling", at, [a, b], () => false, 80);
        double apart = Apart(a, b);
        var ta = new Vec3d(at.X - 5, at.Y, at.Z);
        var tb = new Vec3d(at.X + 5, at.Y, at.Z);
        GoTo(a, ta);
        GoTo(b, tb);
        bool there = await Watch("two to their points", at, [a, b], () => Arrived(a, ta) && Arrived(b, tb), 900);
        Assert.True(there, Where(a) + "; " + Where(b));
        Assert.True(apart >= 1.6, $"two spawned in one place still {apart:F2} apart after 80 ticks");
        Remove(a, b);
    }

    // ---- C and D: up and down a one-block step ----

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task C_two_together_walk_up_a_one_block_step_and_down_it() => await UpAndDown(-180, "footingtwostep", 2);

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task C_two_together_follow_their_leader_up_a_one_block_step_and_down_it()
    {
        var at = await Step(-300, "footingtwofollow");
        using var clock = VirtualClock.Start(World);
        var eidolons = new[] { await Eidolon(new Vec3d(at.X - 2, at.Y, at.Z)), await Eidolon(new Vec3d(at.X - 2, at.Y, at.Z)) };
        await Watch("following at the step, settling", at, eidolons, () => false, 40);
        var leader = _watcher.Player.Entity;
        foreach (var e in eidolons)
            e.Orders!.SetOrder(FollowOrder.OrderCode, FollowOrder.Args(_watcher.Player));
        // Up onto the step: the leader at x + 10, so they stand on it four blocks behind.
        await _watcher.TeleportTo(at.AddCopy(10, 1, 0));
        bool climbed = await Watch("following up the step", at, eidolons,
            () => eidolons.All(e => e.OnGround && Math.Abs(e.Pos.Y - at.Y - 1) < 0.1 && e.Pos.DistanceTo(leader.Pos.XYZ) < 7), 900);
        Assert.True(climbed, "up: " + string.Join("; ", eidolons.Select(Where)));
        // And down off it again: the leader at x - 8.
        await _watcher.TeleportTo(at.AddCopy(-8, 0, 0));
        bool descended = await Watch("following down the step", at, eidolons,
            () => eidolons.All(e => e.OnGround && Math.Abs(e.Pos.Y - at.Y) < 0.1 && e.Pos.DistanceTo(leader.Pos.XYZ) < 7), 900);
        Assert.True(descended, "down: " + string.Join("; ", eidolons.Select(Where)));
        Remove(eidolons);
    }

    /// <summary>A floor (<see cref="Floor"/>) with a soil step one block high across it from x + 3 on.</summary>
    private async Task<BlockPos> Step(int dx, string watcher)
    {
        var at = await Floor(dx, watcher);
        int soil = W.GetBlock(new AssetLocation("game:soil-medium-normal"))!.Id;
        for (int x = 3; x <= 12; x++)
            for (int z = -12; z <= 12; z++)
                W.BlockAccessor.SetBlock(soil, at.AddCopy(x, 0, z));
        await World.Ticks(5);
        return at;
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task D_alone_it_walks_up_a_one_block_step_and_down_it() => await UpAndDown(-210, "footingstep", 1);

    private async Task UpAndDown(int dx, string watcher, int count)
    {
        var at = await Step(dx, watcher);
        using var clock = VirtualClock.Start(World);
        var eidolons = new EntityLaborEidolon[count];
        for (int i = 0; i < count; i++)
            eidolons[i] = await Eidolon(new Vec3d(at.X - 2, at.Y, at.Z));
        await Watch("at the step, settling", at, eidolons, () => false, 40);
        // Up onto the step, each to its own point, then down again.
        var up = eidolons.Select((_, i) => new Vec3d(at.X + 7, at.Y + 1, at.Z + (count == 1 ? 0 : -2 + 4 * i))).ToArray();
        for (int i = 0; i < count; i++)
            GoTo(eidolons[i], up[i]);
        bool climbed = await Watch("up the step", at, eidolons, () => eidolons.Select((e, i) => Arrived(e, up[i])).All(a => a), 900);
        Assert.True(climbed, "up: " + string.Join("; ", eidolons.Select(Where)));
        var down = eidolons.Select((_, i) => new Vec3d(at.X - 3, at.Y, at.Z + (count == 1 ? 0 : -2 + 4 * i))).ToArray();
        for (int i = 0; i < count; i++)
            GoTo(eidolons[i], down[i]);
        bool descended = await Watch("down the step", at, eidolons, () => eidolons.Select((e, i) => Arrived(e, down[i])).All(a => a), 900);
        Assert.True(descended, "down: " + string.Join("; ", eidolons.Select(Where)));
        Remove(eidolons);
    }

    // ---- F: a ledge of two, climbed (EidolonClimb), and one of three, not ----

    /// <summary>A floor (<see cref="Floor"/>) with a granite ledge <paramref name="height"/> high across
    /// it from x + 3 on.</summary>
    private async Task<BlockPos> Ledge(int dx, int height, string watcher)
    {
        var at = await Floor(dx, watcher);
        int granite = W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;
        for (int x = 3; x <= 12; x++)
        for (int z = -12; z <= 12; z++)
        for (int y = 0; y < height; y++)
            W.BlockAccessor.SetBlock(granite, at.AddCopy(x, y, z));
        await World.Ticks(5);
        return at;
    }

    /// <summary>Sends it to <paramref name="target"/> and waits until the order is done (or the ticks
    /// run out), noting whether it played <c>climb</c> on the way and logging where it is now and then.</summary>
    private async Task<bool> ClimbingTo(EntityLaborEidolon e, Vec3d target, string what, int ticks = 1200)
    {
        bool climbed = false;
        int tick = 0;
        GoTo(e, target);
        await World.Until(() =>
        {
            climbed |= e.AnimManager.IsAnimationActive(EidolonClimb.Animation);
            if (++tick % 60 == 0)
                Log($"{what}, tick {tick}: {Where(e)}{(climbed ? ", has climbed" : "")}; {EidolonCommands.Describe(e)}");
            return e.Orders!.OrderCode == null;
        }, ticks);
        Log($"{what}: {Where(e)}, climbed {climbed}; {EidolonCommands.Describe(e)}");
        return climbed;
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task F_it_climbs_a_two_block_ledge_and_walks_back_down_off_it()
    {
        var at = await Ledge(-330, 2, "climbwatcher");
        using var clock = VirtualClock.Start(World);
        var e = await Eidolon(new Vec3d(at.X - 4, at.Y, at.Z));
        await World.Ticks(20);

        var top = new Vec3d(at.X + 8, at.Y + 2, at.Z);
        bool climbed = await ClimbingTo(e, top, "up the ledge");
        Assert.True(climbed, "it played climb: " + EidolonCommands.Describe(e));
        Assert.Null(e.Orders!.OrderCode);
        Assert.True(e.Pos.XYZ.HorizontalSquareDistanceTo(top) < 1.0, Where(e));
        Assert.Equal(at.Y + 2, e.Pos.Y, 1);
        Assert.False(e.AnimManager.IsAnimationActive(EidolonClimb.Animation));

        var below = new Vec3d(at.X - 4, at.Y, at.Z);
        Assert.False(await ClimbingTo(e, below, "down off the ledge"), "nothing to climb going down");
        Assert.Null(e.Orders!.OrderCode);
        Assert.True(e.Pos.XYZ.HorizontalSquareDistanceTo(below) < 1.0, Where(e));
        Assert.Equal(at.Y, e.Pos.Y, 1);
        Remove(e);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task F_a_ledge_three_high_it_neither_climbs_nor_walks_off()
    {
        var at = await Ledge(-360, 3, "climbwatcher3");
        using var clock = VirtualClock.Start(World);
        var below = await Eidolon(new Vec3d(at.X - 4, at.Y, at.Z - 4));
        var above = await Eidolon(new Vec3d(at.X + 8, at.Y + 3, at.Z + 4));
        GoTo(below, new Vec3d(at.X + 8, at.Y + 3, at.Z - 4));
        GoTo(above, new Vec3d(at.X - 4, at.Y, at.Z + 4));
        await World.Ticks(200);
        foreach (var (e, y, what) in new[] { (below, at.Y, "below"), (above, at.Y + 3, "above") })
        {
            Log($"{what}: {Where(e)}; {EidolonCommands.Describe(e)}");
            Assert.Equal(GoToOrder.OrderCode, e.Orders!.OrderCode);
            Assert.Contains("no way", e.GetInfoText());
            Assert.Equal(y, e.Pos.Y, 1);
            Assert.False(e.AnimManager.IsAnimationActive(EidolonClimb.Animation));
            Remove(e);
        }
    }
}
