using Atlas.XUnit;
using SeraphHorizons.Mod.Eidolon;
using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Eidolon, several eidolons felling one area as one job (README "Eidolon",
/// working together), with the pack's default settings, ordered with the command tool as a player
/// orders them. Two given the same area never hold the same tree, fell them all between them and report
/// once with both shares, the job closing; one with no tree free waits at the area's rim. On floors of
/// their own in the sky, the trees grown by the game's generators (<see cref="Felling"/>).
/// </summary>
[AtlasWorld]
public class EidolonTeamFellScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;
    private EidolonSystem Mod => EidolonSystem.Of(World.Api)!;
    private EidolonJobSystem Jobs => EidolonJobSystem.Of(World.Api)!;
    private ItemAxe IronAxe => (ItemAxe)W.GetItem(new AssetLocation("game:axe-felling-iron"))!;

    private void Log(string what)
    {
        W.Logger.Notification("[eidolon-teamfell-test] " + what);
        output.WriteLine(what);
    }

    private async Task<EntityLaborEidolon> Spawn(BlockPos at, IPlayer owner)
    {
        var e = Mod.Spawn(W, new Vec3d(at.X, at.Y, at.Z), 0, owner, activate: true);
        Assert.NotNull(e);
        await World.Until(() => e!.CanWork, 400);
        return e!;
    }

    private async Task<(IServerPlayer Player, ItemSlot Tool)> Commander(string name, BlockPos at)
    {
        var p = await World.JoinPlayer(name);
        var player = (IServerPlayer)p.Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Creative;
        await p.TeleportTo(at);
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = new ItemStack(W.GetItem(new AssetLocation(EidolonCommanderSystem.CommanderCode)));
        slot.MarkDirty();
        return (player, slot);
    }

    private static void RightClick(IPlayer player, ItemSlot slot, Entity? entity = null, BlockPos? block = null)
    {
        var handling = EnumHandHandling.NotHandled;
        slot.Itemstack!.Collectible.OnHeldInteractStart(slot, player.Entity,
            block == null ? null : new BlockSelection { Position = block, Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 1, 0.5) },
            entity == null ? null : new EntitySelection { Entity = entity, Position = entity.Pos.XYZ.AddCopy(0, 2, 0) },
            true, ref handling);
        Assert.Equal(EnumHandHandling.PreventDefault, handling);
    }

    /// <summary>Binds the tool to each of <paramref name="eidolons"/>, picks "fell" and marks the area
    /// between the two floor blocks, which gives every bound eidolon the order.</summary>
    private static void OrderFell(IPlayer player, ItemSlot tool, IEnumerable<EntityLaborEidolon> eidolons, BlockPos a, BlockPos b)
    {
        foreach (var e in eidolons)
            RightClick(player, tool, entity: e);
        tool.Itemstack!.Collectible.SetToolMode(tool, player, null!, EidolonCommandModes.IndexOf(FellOrder.OrderCode));
        RightClick(player, tool, block: a);
        RightClick(player, tool, block: b);
    }

    /// <summary>Gives it an axe as a player does: a right-click on it with the axe.</summary>
    private void GiveAxe(IServerPlayer player, EntityLaborEidolon e)
    {
        var slot = new DummySlot(new ItemStack(IronAxe));
        e.OnInteract(player.Entity, slot, e.Pos.XYZ.AddCopy(0, 2, 0), EnumInteractMode.Interact);
        Assert.True(slot.Empty, "it did not take the axe");
    }

    /// <summary>The reports the orders made (<see cref="FellOrder.Reported"/>: what the owner was told;
    /// Atlas's chat capture fails on the felling's particles).</summary>
    private static List<string> Reports(params FellOrder[] orders) => orders.Select(o => o.Reported).OfType<string>().ToList();

    private static string State(EntityLaborEidolon e) => EidolonCommands.Describe(e) + " / " + e.GetInfoText().Replace('\n', ' ');

    private static string Status(EntityLaborEidolon e) =>
        e.WatchedAttributes.GetTreeAttribute(EntityBehaviorEidolonOrders.StatusKey)?.GetString("key") ?? "";

    private int Wood(BlockPos log) => Felling.Tree(W, IronAxe, log).Wood;

    /// <summary>Grows oaks at <paramref name="spots"/>, standing on their ground, each grown enough to fell.</summary>
    private async Task<List<BlockPos>> Oaks(IEnumerable<BlockPos> spots)
    {
        var logs = new List<BlockPos>();
        int i = 0;
        // the seeds the single felling scenario grows, each a tree well over the fewest logs
        foreach (var spot in spots)
            logs.Add(await Felling.Grow(World, spot, Felling.Oak, 1.0f, 101 + i++ % 3));
        // The generator grows a tree from the block above the one it is given: fill the gap, as a real
        // tree stands on its ground.
        foreach (var l in logs.Where(l => W.BlockAccessor.GetBlock(l.DownCopy()).Id == 0))
            World.SetBlock("game:soil-medium-normal", l.DownCopy());
        await World.Ticks(5);
        Log($"trees: {string.Join(", ", logs.Select(l => $"{l} {Wood(l)} logs"))}");
        Assert.All(logs, l => Assert.True(Wood(l) >= Mod.Config.FellMinLogs, $"{l}: {Wood(l)} logs"));
        return logs;
    }

    [AtlasScenario(TimeoutMs = 600_000)]
    public async Task Two_eidolons_fell_an_area_together_never_the_same_tree_and_report_once()
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-820, 90, 1600), reach: 18);
        var logs = await Oaks([site.AddCopy(-9, 0, -2), site.AddCopy(0, 0, 7), site.AddCopy(9, 0, -2), site.AddCopy(-10, 0, 11), site.AddCopy(10, 0, 11)]);

        var (player, tool) = await Commander("eidolonfellteam", site.AddCopy(0, 0, -16));
        var e1 = await Spawn(site.AddCopy(-4, 0, -12), player);
        var e2 = await Spawn(site.AddCopy(4, 0, -12), player);
        GiveAxe(player, e1);
        GiveAxe(player, e2);
        OrderFell(player, tool, [e1, e2], site.AddCopy(-13, -1, -8), site.AddCopy(13, -1, 12));
        var o1 = Assert.IsType<FellOrder>(e1.Orders!.Current);
        var o2 = Assert.IsType<FellOrder>(e2.Orders!.Current);
        await World.Ticks(2);
        Assert.NotNull(o1.Job);
        Assert.Same(o1.Job, o2.Job);
        var job = o1.Job!;

        // Watch each tick: never a tree held by both.
        var faults = new List<string>();
        int ticks = 0, lastFelled = -1;
        await World.Until(() =>
        {
            var held1 = Jobs.Felling.Held(e1.EntityId);
            var held2 = Jobs.Felling.Held(e2.EntityId);
            if (held1.Intersect(held2).Any())
                faults.Add($"both hold {string.Join(" ", held1.Intersect(held2))}");
            if (o1.Felled + o2.Felled != lastFelled || ++ticks % 300 == 0)
            {
                Log($"{o1.Felled} + {o2.Felled} felled, at {e1.Pos.XYZ} and {e2.Pos.XYZ}: {State(e1)} || {State(e2)}");
                lastFelled = o1.Felled + o2.Felled;
            }
            return e1.Orders.OrderCode == null && e2.Orders.OrderCode == null;
        }, 15000);
        var lines = Reports(o1, o2);
        Log($"after: {o1.Felled} + {o2.Felled}; reports: " + string.Join(" | ", lines));
        Assert.Empty(faults);
        Assert.All(logs, l => Assert.Equal(0, Wood(l)));
        Assert.Equal(logs.Count, o1.Felled + o2.Felled);
        Assert.True(o1.Felled > 0 && o2.Felled > 0, $"{o1.Felled} and {o2.Felled}");
        // One report, with both shares, and the job closed.
        var report = Assert.Single(lines);
        Assert.Contains("Your 2 eidolons", report);
        Assert.Contains($"{logs.Count} felled", report);
        Assert.Contains($"{o1.Felled} by eidolon {e1.EntityId}", report);
        Assert.Contains($"{o2.Felled} by eidolon {e2.EntityId}", report);
        Assert.True(job.Closed);
        Assert.Null(Jobs.Felling.JobOf(e1.EntityId));
        Assert.Null(Jobs.Felling.JobOf(e2.EntityId));
        Assert.Empty(Jobs.Felling.Held(e1.EntityId));
        Assert.Empty(Jobs.Felling.Held(e2.EntityId));
        e1.Die(EnumDespawnReason.Removed);
        e2.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 480_000)]
    public async Task One_with_no_tree_free_waits_at_the_rim()
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-820, 90, 1700), reach: 14);
        var logs = await Oaks([site.AddCopy(0, 0, 0)]);
        var a = site.AddCopy(-3, -1, -3);
        var b = site.AddCopy(3, -1, 3);
        var area = MarkArea.Between(new MarkPos(a.X, a.Y, a.Z), new MarkPos(b.X, b.Y, b.Z));

        var (player, tool) = await Commander("eidolonfellspare", site.AddCopy(0, 0, -12));
        var e1 = await Spawn(site.AddCopy(-3, 0, -6), player);
        var e2 = await Spawn(site.AddCopy(8, 0, 8), player);
        GiveAxe(player, e1);
        GiveAxe(player, e2);
        OrderFell(player, tool, [e1, e2], a, b);
        var o1 = Assert.IsType<FellOrder>(e1.Orders!.Current);
        var o2 = Assert.IsType<FellOrder>(e2.Orders!.Current);

        // Who waited, whether it stood clear of the area once there, and never waiting holding a tree.
        var waited = new HashSet<long>();
        var atRim = new HashSet<long>();
        var faults = new List<string>();
        int ticks = 0;
        await World.Until(() =>
        {
            foreach (var e in new[] { e1, e2 })
            {
                if (!Status(e).EndsWith("fell-waiting"))
                    continue;
                waited.Add(e.EntityId);
                var f = EidolonMining.Footprint(e.Pos.X, e.Pos.Z);
                bool over = f.MaxX >= area.Min.X && f.MinX <= area.Max.X && f.MaxZ >= area.Min.Z && f.MinZ <= area.Max.Z;
                if (e.Pos.Motion.HorLength() < 0.001 && !over)
                    atRim.Add(e.EntityId);
                if (Jobs.Felling.Held(e.EntityId).Count > 0)
                    faults.Add($"{e.EntityId} waits holding {string.Join(" ", Jobs.Felling.Held(e.EntityId))}");
            }
            if (++ticks % 120 == 0)
                Log($"{o1.Felled} + {o2.Felled}: {State(e1)} || {State(e2)}");
            return e1.Orders.OrderCode == null && e2.Orders.OrderCode == null;
        }, 9000);
        var lines = Reports(o1, o2);
        Log($"after: {o1.Felled} + {o2.Felled}, waited {string.Join(",", waited)}, at the rim {string.Join(",", atRim)}; reports: " + string.Join(" | ", lines));
        Assert.Empty(faults);
        Assert.Equal(0, Wood(logs[0]));
        Assert.Equal(1, o1.Felled + o2.Felled);
        Assert.NotEmpty(waited);
        Assert.NotEmpty(atRim);
        var report = Assert.Single(lines);
        Assert.Contains("Your 2 eidolons", report);
        Assert.Contains("1 felled", report);
        e1.Die(EnumDespawnReason.Removed);
        e2.Die(EnumDespawnReason.Removed);
    }
}
