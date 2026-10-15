using System.Diagnostics;
using Atlas.XUnit;
using SeraphHorizons.Mod.Eidolon;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Server;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>PROTOTYPE (<see cref="VirtualClock"/>): the same windows of ticks, and the same small mining
/// order, in real time and on the virtual clock, wall time against server time logged for each.</summary>
[AtlasWorld]
public class VirtualClockScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;
    private EidolonSystem Mod => EidolonSystem.Of(World.Api)!;

    private void Log(string what)
    {
        W.Logger.Notification("[virtual-clock-test] " + what);
        output.WriteLine(what);
    }

    private async Task<string> Window(int ticks)
    {
        var wall = Stopwatch.StartNew();
        long ms = W.ElapsedMilliseconds, sim = World.EntitySimulationTicks;
        double hours = W.Calendar.TotalHours;
        await World.Ticks(ticks);
        return $"{ticks} ticks: wall {wall.ElapsedMilliseconds} ms, server {W.ElapsedMilliseconds - ms} ms, "
            + $"entity sim {World.EntitySimulationTicks - sim} ticks, calendar {(W.Calendar.TotalHours - hours) * 60:0.0} game min";
    }

    private async Task<string> MineRow(string name, BlockPos origin)
    {
        var site = await Felling.Sky(World, origin, reach: 10);
        var blocks = W.BlockAccessor;
        int granite = W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;
        for (int x = -8; x <= 8; x++)
        for (int z = -8; z <= 8; z++)
            blocks.SetBlock(granite, site.AddCopy(x, -2, z));
        var a = site.AddCopy(-2, 0, 3);
        var b = site.AddCopy(1, 0, 4);
        for (int x = -2; x <= 1; x++)
        for (int z = 3; z <= 4; z++)
            blocks.SetBlock(granite, site.AddCopy(x, 0, z));
        var p = await World.JoinPlayer(name);
        var player = (IServerPlayer)p.Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Creative;
        await p.TeleportTo(site.AddCopy(0, 0, -8));
        var tool = player.InventoryManager.ActiveHotbarSlot;
        tool.Itemstack = new ItemStack(W.GetItem(new AssetLocation(EidolonCommanderSystem.CommanderCode)));
        tool.MarkDirty();
        var e = Mod.Spawn(W, new Vec3d(site.X, site.Y, site.Z - 2), 0, player, activate: true)!;
        await World.Until(() => e.CanWork, 400);

        var wall = Stopwatch.StartNew();
        long ms = W.ElapsedMilliseconds;
        int ticks = 0;
        if (ItemEidolonCommander.Bound(tool.Itemstack!).Count == 0)
            RightClick(player, tool, entity: e);
        tool.Itemstack!.Collectible.SetToolMode(tool, player, null!, EidolonCommandModes.IndexOf(MineOrder.OrderCode));
        RightClick(player, tool, block: a);
        RightClick(player, tool, block: b);
        var order = Assert.IsType<MineOrder>(e.Orders!.Current);
        await World.Until(() => { ticks++; return e.Orders.OrderCode == null; }, 4000);
        string result = $"mined {order.Mined} in {order.Strikes} strikes: wall {wall.ElapsedMilliseconds} ms, server {W.ElapsedMilliseconds - ms} ms, {ticks} ticks";
        Assert.Equal(8, order.Mined);
        e.Die(EnumDespawnReason.Removed);
        return result;
    }

    private static void RightClick(IPlayer player, ItemSlot slot, Entity? entity = null, BlockPos? block = null)
    {
        var handling = EnumHandHandling.NotHandled;
        slot.Itemstack!.Collectible.OnHeldInteractStart(slot, player.Entity,
            block == null ? null : new BlockSelection { Position = block, Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 1, 0.5) },
            entity == null ? null : new EntitySelection { Entity = entity, Position = entity.Pos.XYZ.AddCopy(0, 2, 0) },
            true, ref handling);
    }

    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task Real_time_and_the_virtual_clock_compared()
    {
        Log("real: " + await Window(300));
        Log("real mine: " + await MineRow("vclockreal", World.Spawn.AddCopy(-520, 90, 1800)));
        VirtualClock.Enable((ServerMain)W);
        try
        {
            Log("virtual: " + await Window(300));
            Log("virtual mine: " + await MineRow("vclockvirt", World.Spawn.AddCopy(-520, 90, 1900)));
            Log("virtual: " + await Window(3000));
        }
        finally
        {
            VirtualClock.Disable();
        }
        Log("real again: " + await Window(60));
    }
}
