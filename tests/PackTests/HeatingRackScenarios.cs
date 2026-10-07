using System.Reflection;
using Atlas.Api;
using Atlas.XUnit;
using SeraphHorizons.Mod;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
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
    /// knowing that position (its firepit check and dirty-marking go by it). Put down on the floor's
    /// top face it stands on the floor, one cell up (<c>HeatingRackStandsOnBlock</c>,
    /// <see cref="HeatingRackPlacement"/>): Carry On reports that cell and restores the block
    /// entity's tree there.</summary>
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
        SetBankedResin(RackEntity(from)!, 250f);

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

        var lifted = to.UpCopy();
        Assert.Equal(lifted, placedAt);
        Assert.Equal("game:rock-granite", W.BlockAccessor.GetBlock(floor).Code.ToString());
        Assert.Equal("game:air", W.BlockAccessor.GetBlock(to).Code.ToString());
        Assert.StartsWith("loggingmod:resinrack-fire-", W.BlockAccessor.GetBlock(lifted).Code.ToString());
        Assert.Null(CarryOnApi.Carried(World.Api, shop.Player.Entity));
        var entity = RackEntity(lifted);
        Assert.NotNull(entity);
        Assert.Equal(lifted, entity.Pos);
        Assert.Equal(250f, BankedResin(entity));
    }

    private void SetBankedResin(BlockEntity rack, float ml)
    {
        var tree = new TreeAttribute();
        rack.ToTreeAttributes(tree);
        tree.SetFloat("bankedMl", ml);
        rack.FromTreeAttributes(tree, W);
    }

    private static float BankedResin(BlockEntity rack)
    {
        var tree = new TreeAttribute();
        rack.ToTreeAttributes(tree);
        return tree.GetFloat("bankedMl", -1f);
    }

    /// <summary>The selection the game hands the block's <c>TryPlaceBlock</c> for a click on the top
    /// face of the block under <paramref name="cell"/>: the cell over that face, offset to by the
    /// face, as the client's <c>OnBlockBuild</c> and Carry On's <c>TryPlaceDownAt</c> make it.</summary>
    private static BlockSelection OffsetOffTopFace(BlockPos cell) =>
        new() { Position = cell.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 1, 0.5), DidOffset = true };

    /// <summary>Places a rack from a stack through the block's own <c>TryPlaceBlock</c>, as the hotbar
    /// and a creative pick do, at <paramref name="sel"/>.</summary>
    private async Task PlaceRackFromStack(Woodshop shop, BlockSelection sel)
    {
        var stack = new ItemStack(shop.Block(HeatingRack));
        string failure = "";
        shop.Holding(stack);
        Assert.True(stack.Block.TryPlaceBlock(W, shop.P, stack, sel, ref failure), $"not placed: {failure}");
        shop.Holding(null);
        await World.Ticks(2);
    }

    /// <summary><c>HeatingRackStandsOnBlock</c> (<see cref="HeatingRackPlacement"/>): a rack placed
    /// from a stack onto a granite floor's top face stands on the floor, one cell up, with an empty
    /// cell under it for a firepit, its block entity knowing that position; the selection says where
    /// it went.</summary>
    [AtlasScenario]
    public async Task Heating_rack_placed_on_the_floor_stands_on_it()
    {
        var shop = await OpenRackShop(World.Spawn.AddCopy(120, 3, 290));
        var to = shop.Cell(2);
        var sel = OffsetOffTopFace(to);
        await PlaceRackFromStack(shop, sel);
        var lifted = to.UpCopy();
        output.WriteLine($"cell over the floor {Describe(to)}; above {Describe(lifted)}");

        Assert.Equal(lifted, sel.Position);
        Assert.Equal("game:air", W.BlockAccessor.GetBlock(to).Code.ToString());
        Assert.StartsWith("loggingmod:resinrack-fire-", W.BlockAccessor.GetBlock(lifted).Code.ToString());
        Assert.Equal(lifted, RackEntity(lifted)!.Pos);
    }

    /// <summary>Aimed at a firepit's top face the rack goes in the cell right above it, where its
    /// firepit check looks: no lift.</summary>
    [AtlasScenario]
    public async Task Heating_rack_placed_on_a_firepit_stands_right_above_it()
    {
        var shop = await OpenRackShop(World.Spawn.AddCopy(120, 3, 320));
        var firepit = shop.Cell(2);
        World.SetBlock("game:firepit-extinct", firepit);
        await World.Ticks(2);
        Assert.True(HeatingRackPlacement.IsFirepit(W.BlockAccessor, firepit), $"no firepit: {Describe(firepit)}");

        var above = firepit.UpCopy();
        await PlaceRackFromStack(shop, OffsetOffTopFace(above));
        output.WriteLine($"firepit {Describe(firepit)}; above {Describe(above)}; two up {Describe(above.UpCopy())}");

        Assert.StartsWith("game:firepit", W.BlockAccessor.GetBlock(firepit).Code.ToString());
        Assert.StartsWith("loggingmod:resinrack-fire-", W.BlockAccessor.GetBlock(above).Code.ToString());
        Assert.Equal(above, RackEntity(above)!.Pos);
        Assert.Equal("game:air", W.BlockAccessor.GetBlock(above.UpCopy()).Code.ToString());
    }

    /// <summary>With the cell above taken, the rack goes where the game puts it, in the cell over
    /// the face; aimed at a side face, it is not lifted either.</summary>
    [AtlasScenario]
    public async Task Heating_rack_not_lifted_into_a_taken_cell_or_off_a_side_face()
    {
        var shop = await OpenRackShop(World.Spawn.AddCopy(120, 3, 350));
        var to = shop.Cell(1);
        World.SetBlock("game:rock-granite", to.UpCopy());
        await World.Ticks(2);
        var sel = OffsetOffTopFace(to);
        await PlaceRackFromStack(shop, sel);
        output.WriteLine($"under a granite block: {Describe(to)}");
        Assert.Equal(to, sel.Position);
        Assert.StartsWith("loggingmod:resinrack-fire-", W.BlockAccessor.GetBlock(to).Code.ToString());
        Assert.Equal(to, RackEntity(to)!.Pos);

        // Off the east face of a granite block standing on the floor.
        var post = shop.Cell(3);
        World.SetBlock("game:rock-granite", post);
        await World.Ticks(2);
        var side = post.EastCopy();
        var sideSel = new BlockSelection { Position = side.Copy(), Face = BlockFacing.EAST, HitPosition = new Vec3d(1, 0.5, 0.5), DidOffset = true };
        await PlaceRackFromStack(shop, sideSel);
        output.WriteLine($"off a side face: {Describe(side)}");
        Assert.Equal(side, sideSel.Position);
        Assert.StartsWith("loggingmod:resinrack-fire-", W.BlockAccessor.GetBlock(side).Code.ToString());
        Assert.Equal("game:air", W.BlockAccessor.GetBlock(side.UpCopy()).Code.ToString());
    }

    /// <summary>The place-down by the stack alone, which is what Carry On's client runs (its
    /// TryPlaceDownAsPlayer: the block's TryPlaceBlock with the stack OnPickBlock gave at pickup;
    /// only the server restores the block entity's tree afterwards), and what a creative pick and
    /// place does. The rack's OnPickBlock writes its whole block entity tree into that stack, posx,
    /// posy and posz included, and its DoPlaceBlock loads the tree back, so on Logging Expanded 0.3.6
    /// the new block entity's Pos is where the rack was picked up (#374). The regression test for
    /// <c>HeatingRackKeepsPosition</c> (<see cref="HeatingRackPosition"/>): the stack carries no
    /// position, and the placed rack's Pos is the new position.</summary>
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
        Assert.False(stack.Attributes.HasAttribute("posx"), "the picked stack carries the rack's position");
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
