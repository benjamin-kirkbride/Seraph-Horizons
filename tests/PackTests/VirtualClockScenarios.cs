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

/// <summary>The virtual clock's own tests (<see cref="VirtualClock"/>, #960): windows of ticks in real time and
/// on the virtual clock, wall time against server time; the switch back to real time, also when the scenario
/// throws; holds; and the same small mining order both ways. One boot.</summary>
[AtlasWorld]
public class VirtualClockScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;
    private ServerMain Server => (ServerMain)W;
    private EidolonSystem Mod => EidolonSystem.Of(World.Api)!;

    private sealed class Boom() : Exception("the scenario failed");

    private void Log(string what)
    {
        W.Logger.Notification("[virtual-clock-test] {0}", what);
        output.WriteLine(what);
    }

    private sealed record Span(long Wall, long Server, long Sim, double GameMinutes);

    private async Task<Span> Window(string name, int ticks)
    {
        var wall = Stopwatch.StartNew();
        long ms = W.ElapsedMilliseconds, sim = World.EntitySimulationTicks;
        double hours = W.Calendar.TotalHours;
        await World.Ticks(ticks);
        var span = new Span(wall.ElapsedMilliseconds, W.ElapsedMilliseconds - ms, World.EntitySimulationTicks - sim, (W.Calendar.TotalHours - hours) * 60);
        Log($"{name}, {ticks} ticks: wall {span.Wall} ms, server {span.Server} ms, entity sim {span.Sim} ticks, calendar {span.GameMinutes:0.00} game min");
        return span;
    }

    // Real time: server time is wall time (a pass sleeps to the engine's tick).
    private static void Real(Span s, int ticks)
    {
        Assert.InRange(s.Server, s.Wall - 50 - s.Wall / 20, s.Wall + 50);
        Assert.True(s.Server >= ticks * VirtualClock.StepMs * 0.9, $"{s.Server} ms of server time for {ticks} paced ticks");
    }

    // The virtual clock: exactly a step a tick, far faster than real time, the calendar on the same clock.
    private void Virtual(Span s, int ticks)
    {
        double expected = ticks * VirtualClock.StepMs;
        Assert.InRange(s.Server, expected - 2, expected + 2);
        Assert.True(s.Wall < s.Server / 4, $"{ticks} virtual ticks took {s.Wall} ms of wall time for {s.Server} ms of server time");
        double minutes = expected / 60000 * W.Calendar.SpeedOfTime * W.Calendar.CalendarSpeedMul;
        Assert.InRange(s.GameMinutes, minutes * 0.99, minutes * 1.01 + 0.01);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task The_virtual_clock_steps_exactly_and_switches_back_cleanly()
    {
        Real(await Window("real", 150), 150);
        var clock = VirtualClock.Start(World);
        long before;
        try
        {
            Assert.Same(clock, VirtualClock.Current);
            Virtual(await Window("virtual", 300), 300);
            Virtual(await Window("virtual", 3000), 3000);
            Assert.True(clock.Passes >= 3300, $"{clock.Passes} passes advanced for 3300 ticks");
        }
        finally
        {
            before = W.ElapsedMilliseconds;
            clock.Dispose();
        }
        Assert.Null(VirtualClock.Current);
        Assert.True(Server.totalUnpausedTime.IsRunning, "the master clock was not started again");
        // no jump: real time goes on from the virtual clock's time
        Assert.InRange(W.ElapsedMilliseconds - before, 0, 50);
        Real(await Window("real again", 60), 60);
        clock.Dispose();
    }

    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task Dispose_restores_real_time_when_the_scenario_throws_and_clocks_do_not_nest()
    {
        await Assert.ThrowsAsync<Boom>(async () =>
        {
            using var clock = VirtualClock.Start(World);
            Assert.Throws<InvalidOperationException>(() => VirtualClock.Start(World));
            Assert.Same(clock, VirtualClock.Current);
            await World.Ticks(30);
            throw new Boom();
        });
        Assert.Null(VirtualClock.Current);
        Assert.True(Server.totalUnpausedTime.IsRunning, "the master clock was not started again");
        Real(await Window("real after a throw", 60), 60);

        // the leak guard: a clock left on is stopped, and the scenario fails
        VirtualClock.Start(World);
        await World.Ticks(5);
        var leaked = Assert.Throws<InvalidOperationException>(() => VirtualClock.StopLeaked(nameof(Dispose_restores_real_time_when_the_scenario_throws_and_clocks_do_not_nest)));
        Log("leak guard: " + leaked.Message);
        Assert.Null(VirtualClock.Current);
        Assert.True(Server.totalUnpausedTime.IsRunning, "the master clock was not started again");
        Real(await Window("real after a leak", 60), 60);
    }

    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task A_hold_keeps_game_time_still_and_one_that_never_clears_fails()
    {
        using (var clock = VirtualClock.Start(World))
        {
            await World.Ticks(5);
            using var gate = new ManualResetEventSlim();
            var wall = Stopwatch.StartNew();
            long ms = W.ElapsedMilliseconds;
            using (clock.HoldWhile(() => !gate.IsSet, "the test's gate"))
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(600);
                    gate.Set();
                });
                await World.Ticks(3);
            }
            long held = W.ElapsedMilliseconds - ms;
            Log($"held: wall {wall.ElapsedMilliseconds} ms, server {held} ms for 3 ticks");
            Assert.True(wall.ElapsedMilliseconds >= 550, $"the hold let the ticks through after {wall.ElapsedMilliseconds} ms of wall time");
            Assert.InRange(held, 2 * VirtualClock.StepMs, 5 * VirtualClock.StepMs);
            Virtual(await Window("virtual after a hold", 60), 60);
        }

        var stuck = VirtualClock.Start(World, holdMinePlans: false);
        stuck.HoldWhile(() => true, "a hold that never clears", TimeSpan.FromMilliseconds(300));
        await World.Ticks(3);
        var fault = Assert.Throws<InvalidOperationException>(stuck.Dispose);
        Log("timed-out hold: " + fault.Message);
        Assert.Contains("a hold that never clears", fault.Message);
        Assert.Null(VirtualClock.Current);
        Assert.True(Server.totalUnpausedTime.IsRunning, "the master clock was not started again");
    }

    private async Task<(int Mined, int Strikes, string Line)> MineRow(string name, BlockPos origin)
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
        string line = $"mined {order.Mined} in {order.Strikes} strikes: wall {wall.ElapsedMilliseconds} ms, server {W.ElapsedMilliseconds - ms} ms, {ticks} ticks";
        e.Die(EnumDespawnReason.Removed);
        return (order.Mined, order.Strikes, line);
    }

    private static void RightClick(IPlayer player, ItemSlot slot, Entity? entity = null, BlockPos? block = null)
    {
        var handling = EnumHandHandling.NotHandled;
        slot.Itemstack!.Collectible.OnHeldInteractStart(slot, player.Entity,
            block == null ? null : new BlockSelection { Position = block, Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 1, 0.5) },
            entity == null ? null : new EntitySelection { Entity = entity, Position = entity.Pos.XYZ.AddCopy(0, 2, 0) },
            true, ref handling);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task The_same_dig_in_real_time_and_on_the_virtual_clock()
    {
        var real = await MineRow("vclockreal", World.Spawn.AddCopy(-520, 90, 1800));
        Log("real mine: " + real.Line);
        (int Mined, int Strikes, string Line) virt;
        using (VirtualClock.Start(World))
            virt = await MineRow("vclockvirt", World.Spawn.AddCopy(-520, 90, 1900));
        Log("virtual mine: " + virt.Line);
        Assert.Equal(8, real.Mined);
        Assert.Equal(8, virt.Mined);
        Assert.Equal(real.Strikes, virt.Strikes);
    }
}
