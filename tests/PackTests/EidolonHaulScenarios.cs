using System.IO;
using Atlas.XUnit;
using SeraphHorizons.Mod.Eidolon;
using SeraphHorizons.Mod.Eidolon.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Rosser.Core;
using SeraphHorizons.Mod.TrunkEntities;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Eidolon (#678): hauling. On the woodworking world (trunk entities with
/// Carry On, a rosser by the shared helpers): an eidolon is told with the command tool to haul from
/// an area (two corners) to a running rosser (any of its cells; a block that is no machine is refused
/// and the area kept). It takes a thin and a thick trunk lying in the area, one at a time, carries
/// each, its trunk kept through a save of the entity, and lays them in the rosser's infeed cells, the
/// second once the first is off them, and the rosser takes both; oil is spent per trunk.
/// </summary>
public partial class WoodworkingScenarios
{
    [AtlasScenario(TimeoutMs = 600_000)]
    public async Task An_eidolon_hauls_a_thin_and_a_thick_trunk_to_a_rosser()
    {
        var pos = await RosserSky(-520, -240, reach: 34);
        var player = await Player("hauler");
        await StandBy(player, pos);
        var rosser = await PlaceRosser(pos, "east");
        RosserReady(rosser, player);
        await PowerRosser(rosser);
        var cells = RosserRig.InfeedNeighbours().Select(rosser.CellPos).ToList();
        var outward = Footprint.ToWorld(RosserRig.InfeedSide, rosser.Side).Normal();
        // The area: 9 × 9 blocks, its middle 10 beyond the infeed cells' middle.
        double mx = cells.Average(c => c.X + 0.5), mz = cells.Average(c => c.Z + 0.5);
        var centre = new BlockPos((int)Math.Floor(mx + 10 * outward.X), pos.Y, (int)Math.Floor(mz + 10 * outward.Z));
        var a = centre.AddCopy(-4, -1, -4);
        var b = centre.AddCopy(4, -1, 4);
        // Across the line (perpendicular to outward), one each side of the middle.
        var across = new Vec3d(-outward.Z, 0, outward.X);
        var thin = TrunkSpawns.Spawn(W, RosserTrunk("oak", 4, 2), new Vec3d(centre.X + 0.5 - 2.5 * across.X, centre.Y, centre.Z + 0.5 - 2.5 * across.Z), 0.4f)!;
        var thick = TrunkSpawns.Spawn(W, RosserTrunk("oak", 30, 0, "xl"), new Vec3d(centre.X + 0.5 + 2.5 * across.X, centre.Y, centre.Z + 0.5 + 2.5 * across.Z), 1.9f)!;
        Assert.Equal(TrunkClass.Thin, thin.TypeClass);
        Assert.Equal(TrunkClass.Thick, thick.TypeClass);
        long thinId = thin.EntityId, thickId = thick.EntityId;
        await World.Ticks(20);

        // The player beside the area, an eidolon of theirs at its edge, bound to a command tool.
        var sp = (IServerPlayer)player;
        var back = sp.Entity.Pos.XYZ;
        sp.Entity.TeleportTo(centre.ToVec3d().Add(0.5 + 6 * outward.X, 0, 0.5 + 6 * outward.Z));
        var eidolon = EidolonSystem.Of(World.Api)!.Spawn(W, centre.ToVec3d().Add(0.5 - 6 * across.X, 0, 0.5 - 6 * across.Z), 0, player, activate: true);
        Assert.NotNull(eidolon);
        await World.Until(() => eidolon!.CanWork, 600);
        var item = W.GetItem(new AssetLocation(EidolonCommanderSystem.CommanderCode));
        var commander = Assert.IsType<ItemEidolonCommander>(item);
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = new ItemStack(item);
        try
        {
            Assert.True(commander.Bind(sp, slot, eidolon!));
            ItemEidolonCommander.SetMode(slot.Itemstack, HaulOrder.OrderCode);
            Assert.Equal(EidolonMarkKind.AreaThenBlock, ItemEidolonCommander.ModeOf(slot.Itemstack)!.Mark);

            // Two corners, then a block that is no machine (refused, the area kept), then a ghost of the rosser.
            Assert.Equal(0, commander.Use(sp, slot, a));
            Assert.Equal(0, commander.Use(sp, slot, b));
            Assert.Equal(0, commander.Use(sp, slot, a));
            Assert.NotNull(ItemEidolonCommander.GetMarks(slot.Itemstack, HaulOrder.OrderCode).Area);
            Assert.Null(ItemEidolonCommander.GetMarks(slot.Itemstack, HaulOrder.OrderCode).Third);
            Assert.Equal(1, commander.Use(sp, slot, rosser.GhostCells().First().Pos));
        }
        finally
        {
            slot.Itemstack = null;
            sp.Entity.TeleportTo(back);
        }
        var e = eidolon!;
        Assert.Equal(HaulOrder.OrderCode, e.Orders!.OrderCode);
        var carry = e.GetBehavior<EntityBehaviorEidolonTrunk>()!;
        double Oil() => e.Oil?.Tank?.Points ?? 0;
        double oil = Oil();
        string State() => $"{EidolonCommands.Describe(e)}; at {e.Pos.XYZ}; carrying {carry.Trunk?.Block?.Code}; rosser {rosser.State}";

        // It takes the nearer trunk (the thin one) up, and its stack goes with it through a save.
        await World.Until(() => carry.Carrying, 4000);
        Assert.False(W.GetEntityById(thinId) is { Alive: true } && W.GetEntityById(thickId) is { Alive: true }, State());
        var first = carry.Trunk!;
        Assert.False(EntityBehaviorEidolonTrunk.IsThick(first), State());
        using (var ms = new MemoryStream())
        {
            using (var writer = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                e.ToBytes(writer, false);
            ms.Position = 0;
            var copy = (EntityLaborEidolon)W.ClassRegistry.CreateEntity(W.GetEntityType(EidolonSystem.EntityCode)!);
            using var reader = new BinaryReader(ms);
            copy.FromBytes(reader, false);
            var saved = copy.WatchedAttributes.GetItemstack(EntityBehaviorEidolonTrunk.TrunkKey);
            Assert.NotNull(saved);
            Assert.True(saved!.ResolveBlockOrItem(W));
            Assert.Equal(first.Block!.Code, saved.Block!.Code);
            Assert.Equal(Trunks.StoredLogs(first, W), Trunks.StoredLogs(saved, W));
            Assert.Equal(HaulOrder.OrderCode, copy.WatchedAttributes.GetTreeAttribute(EntityBehaviorEidolonOrders.OrderKey)?.GetString("code"));
        }
        output.WriteLine("took up " + State());

        // The rosser takes the first; the second waits for it to come off the bed.
        await World.Until(() => rosser.Trunk != null, 6000);
        Assert.Equal(Trunks.StoredLogs(first, W), Trunks.StoredLogs(rosser.Trunk!, W));
        output.WriteLine("first on the rosser: " + State());
        await World.Until(() => carry.Carrying, 4000);
        var second = carry.Trunk!;
        Assert.NotEqual(EntityBehaviorEidolonTrunk.IsThick(first), EntityBehaviorEidolonTrunk.IsThick(second));
        await World.Until(() => rosser.State == RosserState.Delivered, 90_000);
        // The second is laid as soon as the infeed is clear, and waits there for the bed.
        await World.Until(() => !carry.Carrying, 6000);
        Assert.NotNull(TrunkStations.FindInCells(W, cells, _ => true));
        Assert.NotNull(rosser.TakeFinished());
        await World.Until(() => rosser.Trunk != null, 2000);
        Assert.Equal(Trunks.StoredLogs(second, W), Trunks.StoredLogs(rosser.Trunk!, W));
        Assert.Null(TrunkStations.FindInCells(W, cells, _ => true));
        Assert.False(W.GetEntityById(thinId) is { Alive: true });
        Assert.False(W.GetEntityById(thickId) is { Alive: true });
        output.WriteLine("both delivered: " + State());

        // Two trunks' oil spent, and with the area empty it says so and waits.
        Assert.Equal(oil - 2 * EidolonSystem.Of(World.Api)!.Config.OilPerTrunkDelivered, Oil(), 3);
        await World.Until(() => e.GetInfoText().Contains("No trunk left"), 400);
        Assert.Equal(HaulOrder.OrderCode, e.Orders.OrderCode);

        e.Die(EnumDespawnReason.Removed);
        W.BlockAccessor.SetBlock(0, RosserRotorPos(rosser));
        KillItemsNear(pos, 40);
    }
}
