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
/// mods-src/seraphhorizons/Eidolon, two eidolons mining an area cut into a hillside whose edge wanders
/// across it (README "Eidolon", mining, *Stations*, *Tunnels*, *Out of reach*), as a player's world has it:
/// the hill's roof over part of the area, bare columns of rock standing in front of its face, a ledge to
/// one side the way up. Told nothing out of reach up front, they never come to have any as they dig, and
/// finish the area. The ground is the unit tests' (mods-src/seraphhorizons/tests/Eidolon/MineHillsideTests.cs,
/// <c>Hillside(34, 0)</c>), built here the same way; on a floor of its own in the sky
/// (<see cref="Felling.Sky"/>), on the virtual clock (<see cref="VirtualClock"/>).
/// </summary>
[AtlasWorld]
public class EidolonOverhangScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;
    private EidolonSystem Mod => EidolonSystem.Of(World.Api)!;
    private EidolonJobSystem Jobs => EidolonJobSystem.Of(World.Api)!;

    private void Log(string what)
    {
        W.Logger.Notification("[eidolon-overhang-test] " + what);
        output.WriteLine(what);
    }

    private async Task<EntityLaborEidolon> Spawn(BlockPos at, IPlayer owner)
    {
        var e = Mod.Spawn(W, new Vec3d(at.X + 0.5, at.Y, at.Z + 0.5), 0, owner, activate: true);
        Assert.NotNull(e);
        await World.Until(() => e!.CanWork, 400);
        return e!;
    }

    private async Task<(ITestPlayer Test, IServerPlayer Player, ItemSlot Tool)> Commander(string name, BlockPos at)
    {
        var p = await World.JoinRetrying(name);
        var player = (IServerPlayer)p.Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Creative;
        await p.TeleportTo(at);
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = new ItemStack(W.GetItem(new AssetLocation(EidolonCommanderSystem.CommanderCode)));
        slot.MarkDirty();
        return (p, player, slot);
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

    private static string State(EntityLaborEidolon e) => EidolonCommands.Describe(e) + " / " + e.GetInfoText().Replace('\n', ' ');

    private static int Feet(Entity e) => (int)Math.Floor(e.Pos.Y + 0.01);

    /// <summary>
    /// The unit tests' hillside (<c>MineHillsideTests.Hillside</c>) in granite, its block (x, y, z) at
    /// <paramref name="origin"/> + (x - 5, y, z - 5), y 0 the floor's top: a hill 3 over an area 8 high, its
    /// edge wandering 2 either way a column; bare columns the area's height in front of it; ground
    /// <paramref name="step"/> high for 3 rows; and to the west a ledge 4 high with a step 2 high in front.
    /// Only what lies within <paramref name="reach"/> of the origin is set (the sky cleared there).
    /// </summary>
    private void Hillside(BlockPos origin, int granite, int seed, int step, int reach)
    {
        var blocks = W.BlockAccessor;
        var rng = new Random(seed);
        int top = 7;
        var edge = new int[40];
        int e = 5;
        for (int x = 0; x < 40; x++)
            edge[x] = e = Math.Clamp(e + rng.Next(-2, 3), 2, 9);
        int bare = rng.Next(2, 5);
        for (int x = -12; x <= 27; x++)
        for (int z = -12; z <= 30; z++)
        {
            int ex = edge[Math.Clamp(x + 6, 0, 39)];
            int h = z >= ex ? top + 3
                : z >= ex - bare && rng.Next(4) == 0 ? top
                : x <= 2 && z >= -2 ? 3
                : x <= 2 && z >= -4 ? 1
                : z >= ex - 3 ? step - 1
                : -1;
            if (Math.Abs(x - 5) > reach || Math.Abs(z - 5) > reach)
                continue;
            blocks.SetBlock(granite, origin.AddCopy(x - 5, -2, z - 5));
            for (int y = 0; y <= h; y++)
                blocks.SetBlock(granite, origin.AddCopy(x - 5, y, z - 5));
        }
    }

    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task Two_eidolons_finish_a_hillside_with_bare_columns_in_front_of_its_face_and_nothing_comes_out_of_reach()
    {
        const int reach = 17;
        var origin = await Felling.Sky(World, World.Spawn.AddCopy(-1120, 90, 2400), reach: reach);
        int granite = W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;
        Hillside(origin, granite, 34, 0, reach);
        BlockPos At(int x, int y, int z) => origin.AddCopy(x - 5, y, z - 5);
        var a = At(0, 0, 0);
        var b = At(9, 7, 9);
        var area = MarkArea.Between(new MarkPos(a.X, a.Y, a.Z), new MarkPos(b.X, b.Y, b.Z));
        var toMine = new List<BlockPos>();
        for (int x = area.Min.X; x <= area.Max.X; x++)
        for (int y = area.Min.Y; y <= area.Max.Y; y++)
        for (int z = area.Min.Z; z <= area.Max.Z; z++)
            if (W.BlockAccessor.GetBlock(new BlockPos(x, y, z)).Id != 0)
                toMine.Add(new BlockPos(x, y, z));
        Log($"{toMine.Count} blocks to mine");

        var (_, player, tool) = await Commander("eidolonoverhang", At(4, 0, -11));
        using var clock = VirtualClock.Start(World);
        var e1 = await Spawn(At(2, 0, -8), player);
        var e2 = await Spawn(At(7, 0, -8), player);
        foreach (var e in new[] { e1, e2 })
            RightClick(player, tool, entity: e);
        tool.Itemstack!.Collectible.SetToolMode(tool, player, null!, EidolonCommandModes.IndexOf(MineOrder.OrderCode));
        RightClick(player, tool, block: a);
        RightClick(player, tool, block: b);
        var o1 = Assert.IsType<MineOrder>(e1.Orders!.Current);
        var o2 = Assert.IsType<MineOrder>(e2.Orders!.Current);
        await World.Ticks(2);
        Assert.NotNull(o1.Job);
        Assert.Same(o1.Job, o2.Job);

        // Watch each tick: what is told out of reach (the plan's count, as each new plan comes in) never
        // more than nothing; nothing broken under either's box at or below its feet.
        var standing = toMine.ToHashSet();
        var faults = new List<string>();
        int ticks = 0, lastMined = -1, plans = 0, mostOut = 0;
        MinePlan? seen = null;
        await World.Until(() =>
        {
            if (o1.Plan is { } plan && plan != seen)
            {
                seen = plan;
                plans++;
                mostOut = Math.Max(mostOut, plan.OutOfReach.Count);
                Log($"plan {plans}: {plan.Stations.Count} stations, {plan.OutOfReach.Count} out of reach, {plan.Held.Count} kept under those, {plan.Free.Count} free, {plan.Reruns} reruns");
            }
            mostOut = Math.Max(mostOut, Math.Max(o1.Unreachable, o2.Unreachable));
            foreach (var c in standing.Where(c => W.BlockAccessor.GetBlock(c).Id == 0).ToList())
            {
                foreach (var e in new[] { e1, e2 })
                    if (EidolonMining.Undermines(e.Pos.X, Feet(e), e.Pos.Z, new MarkPos(c.X, c.Y, c.Z)) && c.Y < e.Pos.Y)
                        faults.Add($"{c} broke under {e.EntityId} at {e.Pos.XYZ}");
                standing.Remove(c);
            }
            if (o1.Mined + o2.Mined != lastMined || ++ticks % 600 == 0)
            {
                if (ticks % 600 == 0 || (o1.Mined + o2.Mined) / 100 != lastMined / 100)
                    Log($"{o1.Mined} + {o2.Mined} mined, {standing.Count} standing, {o1.Unreachable} out of reach: {State(e1)} || {State(e2)}");
                lastMined = o1.Mined + o2.Mined;
            }
            return e1.Orders.OrderCode == null && e2.Orders.OrderCode == null;
        }, 60_000);
        Log($"after: {o1.Mined} + {o2.Mined} mined in {plans} plans, at most {mostOut} out of reach; reports: "
            + string.Join(" | ", new[] { o1.Reported, o2.Reported }.OfType<string>()));
        Log("standing: " + string.Join(" ", toMine.Where(c => W.BlockAccessor.GetBlock(c).Id != 0)
            .Select(c => $"({c.X - origin.X + 5},{c.Y - origin.Y},{c.Z - origin.Z + 5})")));
        Assert.Empty(faults);
        Assert.Equal(0, mostOut);
        Assert.Null(o1.ToldOutOfReach);
        Assert.All(toMine, c => Assert.Equal(0, W.BlockAccessor.GetBlock(c).Id));
        // what they mined is every block of it but what the game moved with no strike of theirs (granite caves in, at
        // random, where a strike leaves it hanging; a block that falls into a dug cell is mined there again), bounded
        // by the job's count of those (MineJobCells.Gone)
        int gone = o1.Job!.Shared.Gone;
        Log($"{gone} gone with no strike of theirs");
        Assert.InRange(toMine.Count - (o1.Mined + o2.Mined), -gone, gone);
        Assert.True(o1.Mined > 0 && o2.Mined > 0, $"{o1.Mined} and {o2.Mined}");
        e1.Die(EnumDespawnReason.Removed);
        e2.Die(EnumDespawnReason.Removed);
    }
}
