using Atlas.Api;
using Atlas.XUnit;
using SeraphHorizons.Mod.Eidolon;
using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Eidolon: steps of two (<see cref="EidolonClimb"/>), with the pack's default
/// settings. Sent to a point on a ledge two blocks high it walks to its foot, climbs it (playing
/// <c>climb</c>, moved up and onto it by the navigator) and gets there, and sent back it walks off the
/// two-block drop; a ledge three high it neither climbs nor walks off, and says there is no way. Each
/// builds on a granite floor of its own high over the spawn, a player watching (entities only simulate
/// near one).
/// </summary>
[AtlasWorld]
public class EidolonClimbScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;
    private ICoreServerAPI Sapi => (ICoreServerAPI)World.Api;
    private EidolonSystem Mod => EidolonSystem.Of(World.Api)!;

    private void Log(string what)
    {
        W.Logger.Notification("[eidolon-climb-test] " + what);
        output.WriteLine(what);
    }

    /// <summary>A clear granite floor 40 above spawn at (<paramref name="dx"/>, 0), 12 each way, with
    /// 8 blocks of air over it, and on it a granite ledge <paramref name="height"/> high from x + 3 on,
    /// right across; its chunks loaded and a player watching.</summary>
    private async Task<BlockPos> Ledge(int dx, int height, string watcher)
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
                W.BlockAccessor.SetBlock(x >= 3 && y < height ? granite : 0, origin.AddCopy(x, y, z));
        }
        var p = await World.JoinPlayer(watcher);
        ((IServerPlayer)p.Player).WorldData.CurrentGameMode = EnumGameMode.Creative;
        await p.TeleportTo(origin.AddCopy(-8, 0, -8));
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

    private static string State(EntityLaborEidolon e) => EidolonCommands.Describe(e);

    /// <summary>Sends it to <paramref name="target"/> and waits until the order is done (or the ticks
    /// run out), noting whether it played <c>climb</c> on the way and logging where it is now and then.</summary>
    private async Task<bool> GoTo(EntityLaborEidolon e, Vec3d target, string what, int ticks = 2400)
    {
        bool climbed = false;
        int tick = 0;
        e.Orders!.SetOrder(GoToOrder.OrderCode, GoToOrder.Args(target, run: false));
        await World.Until(() =>
        {
            climbed |= e.AnimManager.IsAnimationActive(EidolonClimb.Animation);
            if (++tick % 60 == 0)
                Log($"{what}, tick {tick}: at ({e.Pos.X:0.00}, {e.Pos.Y:0.00}, {e.Pos.Z:0.00}){(climbed ? ", has climbed" : "")}; {State(e)}");
            return e.Orders.OrderCode == null;
        }, ticks);
        Log($"{what}: at ({e.Pos.X:0.00}, {e.Pos.Y:0.00}, {e.Pos.Z:0.00}), climbed {climbed}; {State(e)}");
        return climbed;
    }

    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task It_climbs_a_two_block_ledge_and_walks_back_down_off_it()
    {
        var at = await Ledge(-120, 2, "climbwatcher");
        var e = await Eidolon(new Vec3d(at.X - 4, at.Y, at.Z));
        await World.Ticks(20);

        var top = new Vec3d(at.X + 8, at.Y + 2, at.Z);
        bool climbed = await GoTo(e, top, "up the ledge");
        Assert.True(climbed, "it played climb: " + State(e));
        Assert.Null(e.Orders!.OrderCode);
        Assert.True(e.Pos.XYZ.HorizontalSquareDistanceTo(top) < 1.0, State(e));
        Assert.Equal(at.Y + 2, e.Pos.Y, 1);
        Assert.False(e.AnimManager.IsAnimationActive(EidolonClimb.Animation));

        var below = new Vec3d(at.X - 4, at.Y, at.Z);
        Assert.False(await GoTo(e, below, "down off the ledge"), "nothing to climb going down");
        Assert.Null(e.Orders!.OrderCode);
        Assert.True(e.Pos.XYZ.HorizontalSquareDistanceTo(below) < 1.0, State(e));
        Assert.Equal(at.Y, e.Pos.Y, 1);
        e.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task A_ledge_three_high_it_neither_climbs_nor_walks_off()
    {
        var at = await Ledge(-180, 3, "climbwatcher3");
        var below = await Eidolon(new Vec3d(at.X - 4, at.Y, at.Z - 4));
        var above = await Eidolon(new Vec3d(at.X + 8, at.Y + 3, at.Z + 4));
        below.Orders!.SetOrder(GoToOrder.OrderCode, GoToOrder.Args(new Vec3d(at.X + 8, at.Y + 3, at.Z - 4), run: false));
        above.Orders!.SetOrder(GoToOrder.OrderCode, GoToOrder.Args(new Vec3d(at.X - 4, at.Y, at.Z + 4), run: false));
        await World.Ticks(200);
        foreach (var (e, y, what) in new[] { (below, at.Y, "below"), (above, at.Y + 3, "above") })
        {
            Log($"{what}: at ({e.Pos.X:0.00}, {e.Pos.Y:0.00}, {e.Pos.Z:0.00}); {State(e)}");
            Assert.Equal(GoToOrder.OrderCode, e.Orders!.OrderCode);
            Assert.Contains("no way", e.GetInfoText());
            Assert.Equal(y, e.Pos.Y, 1);
            Assert.False(e.AnimManager.IsAnimationActive(EidolonClimb.Animation));
            e.Die(EnumDespawnReason.Removed);
        }
    }
}
