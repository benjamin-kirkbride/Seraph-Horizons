using System.Reflection;
using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.PackTests;

/// <summary>Carry On 2.0's manager, by reflection: its API ships inside the pinned CarryOnLib zip,
/// which this project doesn't reference. Same calls as its network handlers make for a player's
/// pickup and place-down.</summary>
internal static class CarryOnApi
{
    private static object Manager(ICoreAPI api)
    {
        var lib = api.ModLoader.Systems.FirstOrDefault(s => s.GetType().FullName == "CarryOn.CarryOnLib.CarryOnLibSystem")
            ?? throw new Xunit.Sdk.XunitException("CarryOnLib's mod system is not loaded");
        return lib.GetType().GetProperty("CarryManager")!.GetValue(lib)
            ?? throw new Xunit.Sdk.XunitException("CarryOnLib has no CarryManager (Carry On did not register one)");
    }

    private static MethodInfo Method(object manager, string name) =>
        manager.GetType().GetMethod(name) ?? throw new Xunit.Sdk.XunitException($"ICarryManager.{name} not found");

    /// <summary>CarrySlot.Hands; the enum is CarryOnLib's, the manager's class Carry On's.</summary>
    private static object Hands(object manager) =>
        Enum.Parse(Method(manager, "TryPickUp").GetParameters()[2].ParameterType, "Hands");

    /// <summary>Picks the block at <paramref name="pos"/> up into the entity's hands.</summary>
    public static (bool Ok, string Failure) TryPickUp(ICoreAPI api, Entity entity, BlockPos pos)
    {
        var m = Manager(api);
        var args = new object?[] { entity, pos, Hands(m), "__ignore__", true, true, null };
        bool ok = (bool)Method(m, "TryPickUp").Invoke(m, args)!;
        return (ok, (string)args[3]!);
    }

    /// <summary>Puts what the player's hands carry down at <paramref name="selection"/>, the block
    /// face the player is looking at: Carry On offsets the position by the face itself.</summary>
    public static (bool Ok, BlockPos? PlacedAt, string Failure) TryPlaceDownAt(ICoreAPI api, IPlayer player, BlockSelection selection)
    {
        var m = Manager(api);
        var args = new object?[] { player, Hands(m), selection, null, "__ignore__" };
        bool ok = (bool)Method(m, "TryPlaceDownAt").Invoke(m, args)!;
        return (ok, (BlockPos?)args[3], (string)args[4]!);
    }

    public static object? Carried(ICoreAPI api, Entity entity)
    {
        var m = Manager(api);
        return Method(m, "GetCarried").Invoke(m, new object?[] { entity, Hands(m) });
    }
}

/// <summary>
/// Logging Expanded's trunk heating rack (<c>loggingmod:resinrack</c>) picked up and put back down
/// with Carry On, and broken: #374 (the rack ends up in the floor after a carry) and #383 (it
/// drops nothing when broken). Each scenario drives the pinned mods' own code, on the woodshop's
/// granite floor, and records what the game does so the issues can say what is and isn't the case.
/// </summary>
public partial class WoodworkingScenarios
{
    private const string HeatingRack = "loggingmod:resinrack-fire-north";

    private BlockEntity? RackEntity(BlockPos pos) => W.BlockAccessor.GetBlockEntity(pos);

    /// <summary>Opens a woodshop whose floor is certainly there: Open lays its granite before its
    /// player teleports in, and in a chunk nothing had loaded yet (these shops are off on their
    /// own) that floor is lost, so it is laid again under the cells with the player present.</summary>
    private async Task<Woodshop> OpenRackShop(BlockPos at)
    {
        var shop = await Woodshop.Open(World, at);
        for (int cell = 0; cell <= 4; cell++)
        for (int dz = -1; dz <= 1; dz++)
            W.BlockAccessor.SetBlock(shop.Block("game:rock-granite").Id, shop.Cell(cell).AddCopy(0, -1, dz));
        await World.Ticks(2);
        Assert.Equal("game:rock-granite", W.BlockAccessor.GetBlock(shop.Cell(4).DownCopy()).Code.ToString());
        return shop;
    }

    private string Describe(BlockPos pos) =>
        $"{W.BlockAccessor.GetBlock(pos).Code} (entity {RackEntity(pos)?.GetType().Name ?? "none"}, Pos {RackEntity(pos)?.Pos?.ToString() ?? "-"})";

    /// <summary>The whole route Carry On's server takes for a player's pickup and place-down: the
    /// rack must stand where it was put down, on the floor and not in it, with its block entity
    /// knowing that position (its firepit check and dirty-marking go by it).</summary>
    [AtlasScenario]
    public async Task Heating_rack_carried_with_carry_on_stands_where_it_is_put_down()
    {
        var shop = await OpenRackShop(World.Spawn.AddCopy(120, 3, 200));
        var from = shop.Cell(1);
        var to = shop.Cell(4);
        W.BlockAccessor.SetBlock(shop.Block(HeatingRack).Id, from);
        await World.Ticks(2);
        output.WriteLine($"placed: {Describe(from)}");
        Assert.Equal(from, RackEntity(from)!.Pos);

        var (picked, pickFailure) = CarryOnApi.TryPickUp(World.Api, shop.Player.Entity, from);
        Assert.True(picked, $"pickup failed: {pickFailure}");
        Assert.NotNull(CarryOnApi.Carried(World.Api, shop.Player.Entity));
        await World.Ticks(2);
        output.WriteLine($"after pickup, old spot: {Describe(from)}");
        Assert.Equal("game:air", W.BlockAccessor.GetBlock(from).Code.ToString());

        // Looking at the top face of the floor block under the target cell, as a player does.
        var floor = to.DownCopy();
        Assert.False(W.BlockAccessor.GetBlock(floor).IsReplacableBy(shop.Block(HeatingRack)));
        var sel = new BlockSelection { Position = floor.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 1, 0.5) };
        var (placed, placedAt, placeFailure) = CarryOnApi.TryPlaceDownAt(World.Api, shop.P, sel);
        Assert.True(placed, $"place-down failed: {placeFailure}");
        await World.Ticks(2);
        output.WriteLine($"placed at {placedAt}: target cell {Describe(to)}; floor below {Describe(floor)}; old spot {Describe(from)}");

        Assert.Equal(to, placedAt);
        Assert.Equal("game:rock-granite", W.BlockAccessor.GetBlock(floor).Code.ToString());
        Assert.StartsWith("loggingmod:resinrack-fire-", W.BlockAccessor.GetBlock(to).Code.ToString());
        Assert.Null(CarryOnApi.Carried(World.Api, shop.Player.Entity));
        var entity = RackEntity(to);
        Assert.NotNull(entity);
        Assert.Equal(to, entity.Pos);
    }

    /// <summary>The place-down by the stack alone, which is what Carry On's client runs (its
    /// TryPlaceDownAsPlayer: the block's TryPlaceBlock with the stack OnPickBlock gave at pickup;
    /// only the server restores the block entity's tree afterwards), and what a creative pick and
    /// place does. The rack's OnPickBlock writes its whole block entity tree into that stack, posx,
    /// posy and posz included, and its DoPlaceBlock loads the tree back, so the new block entity's
    /// Pos should still be the new position. Fails on Logging Expanded 0.3.6 (#374): Pos is where
    /// the rack was picked up.</summary>
    [AtlasScenario]
    public async Task Heating_rack_placed_from_its_picked_up_stack_keeps_the_new_position()
    {
        var shop = await OpenRackShop(World.Spawn.AddCopy(120, 3, 230));
        var from = shop.Cell(1);
        var to = shop.Cell(4);
        W.BlockAccessor.SetBlock(shop.Block(HeatingRack).Id, from);
        await World.Ticks(2);

        var stack = W.BlockAccessor.GetBlock(from).OnPickBlock(W, from);
        output.WriteLine($"picked stack attributes: {stack.Attributes}");
        W.BlockAccessor.SetBlock(0, from);
        await World.Ticks(2);

        string failure = "";
        var sel = new BlockSelection { Position = to.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0, 0.5) };
        shop.Holding(stack);
        Assert.True(stack.Block.TryPlaceBlock(W, shop.P, stack, sel, ref failure), $"not placed: {failure}");
        shop.Holding(null);
        await World.Ticks(2);
        output.WriteLine($"placed: target cell {Describe(to)}; old spot {Describe(from)}");

        Assert.StartsWith("loggingmod:resinrack-fire-", W.BlockAccessor.GetBlock(to).Code.ToString());
        var entity = RackEntity(to);
        Assert.NotNull(entity);
        Assert.Equal(to, entity.Pos);
    }

    /// <summary>Breaking the rack in survival: its blocktype says <c>drops: []</c>, but its class
    /// returns materials from code. Records exactly what lands.</summary>
    [AtlasScenario]
    public async Task Heating_rack_broken_drops_its_materials()
    {
        var shop = await OpenRackShop(World.Spawn.AddCopy(120, 3, 260));
        var pos = shop.Cell(2);
        W.BlockAccessor.SetBlock(shop.Block(HeatingRack).Id, pos);
        await World.Ticks(2);

        var block = W.BlockAccessor.GetBlock(pos);
        var drops = block.GetDrops(W, pos, shop.P);
        output.WriteLine("GetDrops: " + string.Join(", ", drops.Select(d => $"{d.StackSize}x {d.Collectible.Code}")));

        block.OnBlockBroken(W, pos, shop.P);
        var made = await shop.Collect();
        foreach (var (code, n) in made)
            output.WriteLine($"dropped {n}x {code}");

        Assert.Equal("game:air", W.BlockAccessor.GetBlock(pos).Code.ToString());
        Assert.NotEmpty(made);
        Assert.True(made.GetValueOrDefault("game:stick") > 0, "no sticks dropped");
        Assert.True(made.GetValueOrDefault("game:rope") > 0, "no rope dropped");
        Assert.Contains(made.Keys, k => k.StartsWith("game:bowl-"));
    }
}
