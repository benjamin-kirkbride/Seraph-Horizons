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
/// mods-src/seraphhorizons/Eidolon, driving tunnels (README "Eidolon", Mining, *Tunnels* and *Headings*)
/// with the pack's default settings, ordered with the command tool as a player orders it. A tunnel 2
/// wide, 4 high and 8 long into a granite hillside: refused three high as it is marked, driven in from its
/// open face a slice at a time, each slice's top half before its bottom half, and walked back out; one 6
/// high benched, its top heading from a ledge in front of the cliff, then the lower one from the floor,
/// out by the lower mouth; and two driving one 4 wide, a 2-wide heading each, side by side. On floors of
/// their own in the sky (<see cref="Felling.Sky"/>).
/// </summary>
[AtlasWorld]
public class EidolonTunnelScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;
    private EidolonSystem Mod => EidolonSystem.Of(World.Api)!;
    private EidolonJobSystem Jobs => EidolonJobSystem.Of(World.Api)!;

    private void Log(string what)
    {
        W.Logger.Notification("[eidolon-tunnel-test] " + what);
        output.WriteLine(what);
    }

    private async Task<EntityLaborEidolon> Spawn(BlockPos at, IPlayer owner)
    {
        var e = Mod.Spawn(W, new Vec3d(at.X, at.Y, at.Z), 0, owner, activate: true);
        Assert.NotNull(e);
        await World.Until(() => e!.CanWork, 400);
        return e!;
    }

    private async Task<(ITestPlayer Test, IServerPlayer Player, ItemSlot Tool)> Commander(string name, BlockPos at)
    {
        var p = await World.JoinPlayer(name);
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

    /// <summary>Binds the tool to each of <paramref name="eidolons"/>, picks "mine" and marks the area
    /// between the two blocks, which gives every bound eidolon the order.</summary>
    private static void OrderMine(IPlayer player, ItemSlot tool, IEnumerable<EntityLaborEidolon> eidolons, BlockPos a, BlockPos b)
    {
        foreach (var e in eidolons)
            RightClick(player, tool, entity: e);
        int index = EidolonCommandModes.IndexOf(MineOrder.OrderCode);
        tool.Itemstack!.Collectible.SetToolMode(tool, player, null!, index);
        RightClick(player, tool, block: a);
        RightClick(player, tool, block: b);
    }

    /// <summary>The reports the orders made (<see cref="MineOrder.Reported"/>: what the owner was told;
    /// Atlas's chat capture fails on the strikes' broken-block particles).</summary>
    private static List<string> Reports(params MineOrder[] orders) => orders.Select(o => o.Reported).OfType<string>().ToList();

    private static string State(EntityLaborEidolon e) => EidolonCommands.Describe(e) + " / " + e.GetInfoText().Replace('\n', ' ');

    private static MarkArea Area(BlockPos a, BlockPos b) => MarkArea.Between(new MarkPos(a.X, a.Y, a.Z), new MarkPos(b.X, b.Y, b.Z));

    private static List<BlockPos> Cells(MarkArea area)
    {
        var cells = new List<BlockPos>();
        for (int x = area.Min.X; x <= area.Max.X; x++)
        for (int y = area.Min.Y; y <= area.Max.Y; y++)
        for (int z = area.Min.Z; z <= area.Max.Z; z++)
            cells.Add(new BlockPos(x, y, z));
        return cells;
    }

    private static bool OverArea(MarkArea area, EntityLaborEidolon e)
    {
        var f = EidolonMining.Footprint(e.Pos.X, e.Pos.Z);
        return f.MaxX >= area.Min.X && f.MinX <= area.Max.X && f.MaxZ >= area.Min.Z && f.MinZ <= area.Max.Z;
    }

    private static int Feet(Entity e) => (int)Math.Floor(e.Pos.Y + 0.01);

    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task It_drives_a_tunnel_into_a_hillside_from_its_open_face_a_face_at_a_time()
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-520, 90, 2000), reach: 14);
        var blocks = W.BlockAccessor;
        int granite = W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;
        // A hill of granite (x 0 to 10, seven high, z -3 to 4) with a cliff face to the west onto the floor.
        for (int x = 0; x <= 10; x++)
        for (int y = 0; y <= 6; y++)
        for (int z = -3; z <= 4; z++)
            blocks.SetBlock(granite, site.AddCopy(x, y, z));
        // A tunnel 2 wide, 4 high and 8 long into it from the face: the hill's top layers roof it.
        var a = site.AddCopy(0, 0, 0);
        var b = site.AddCopy(7, 3, 1);
        var area = MarkArea.Between(new MarkPos(a.X, a.Y, a.Z), new MarkPos(b.X, b.Y, b.Z));
        var cells = new List<BlockPos>();
        for (int x = area.Min.X; x <= area.Max.X; x++)
        for (int y = area.Min.Y; y <= area.Max.Y; y++)
        for (int z = area.Min.Z; z <= area.Max.Z; z++)
            cells.Add(new BlockPos(x, y, z));

        var (_, player, tool) = await Commander("eidolontunneller", site.AddCopy(-10, 0, 1));
        var home = site.AddCopy(-6, 0, 1);
        var e = await Spawn(home, player);
        // Three high, all of it under the hill, is refused as it is marked (an area covered all over that is
        // too low could not be dug at all), the first corner kept: the opposite corner one higher orders it.
        OrderMine(player, tool, [e], a, site.AddCopy(7, 2, 1));
        Assert.Null(e.Orders!.OrderCode);
        RightClick(player, tool, block: b);
        Assert.Equal(MineOrder.OrderCode, e.Orders.OrderCode);
        var order = Assert.IsType<MineOrder>(e.Orders.Current);
        var ordered = e.Pos.XYZ;

        // Watch each tick: never under its box; the face slice by slice from the mouth, each slice's top
        // half (layers 2, 3) before its bottom half.
        var standing = cells.ToHashSet();
        int ticks = 0, lastMined = -1;
        var faults = new List<string>();
        await World.Until(() =>
        {
            var broke = standing.Where(c => blocks.GetBlock(c).Id == 0).ToList();
            foreach (var c in broke)
            {
                standing.Remove(c);
                if (EidolonMining.UnderFoot(e.Pos.X, e.Pos.Z, new MarkPos(c.X, c.Y, c.Z)))
                    faults.Add($"{c} broke under it at {e.Pos.XYZ}");
            }
            foreach (var c in broke)
            {
                if (standing.Any(o => o.X < c.X))
                    faults.Add($"({c.X - site.X},{c.Y - site.Y},{c.Z - site.Z}) broke with a slice nearer the mouth standing");
                if (c.Y - site.Y <= 1 && standing.Any(o => o.X == c.X && o.Y - site.Y >= 2))
                    faults.Add($"({c.X - site.X},{c.Y - site.Y},{c.Z - site.Z}) broke under its slice's top half");
            }
            if (order.Mined != lastMined || ++ticks % 300 == 0)
            {
                Log($"{order.Mined} mined, {order.Strikes} strikes, at {e.Pos.XYZ}: " + State(e));
                lastMined = order.Mined;
            }
            return e.Orders.OrderCode == null;
        }, 9000);
        Log($"after: {order.Mined} mined, {order.Strikes} strikes, {order.Unreachable} out of reach, {order.EmptyStrikes} empty, "
            + $"{order.CutStrikes} cut: " + State(e) + " / " + order.Reported);
        Assert.Empty(faults);
        Assert.Null(e.Orders.OrderCode);
        Assert.NotNull(order.Tunnel);
        Assert.Equal((1, 0), (order.Tunnel!.DriveX, order.Tunnel.DriveZ));
        Assert.All(cells, c => Assert.Equal(0, blocks.GetBlock(c).Id));
        Assert.Equal(cells.Count, order.Mined);
        Assert.Equal(0, order.Unreachable);
        Assert.Equal(0, order.EmptyStrikes);
        // up to four a strike: two a slice
        Assert.InRange(order.Strikes, cells.Count / 4, cells.Count / 2);
        // the roof and the hill round the tunnel stand
        Assert.Equal(granite, blocks.GetBlock(site.AddCopy(3, 4, 0)).Id);
        Assert.Equal(granite, blocks.GetBlock(site.AddCopy(3, 2, 2)).Id);
        Assert.Equal(granite, blocks.GetBlock(site.AddCopy(8, 2, 0)).Id);
        // back out of the tunnel to where it was ordered
        Assert.True(e.Pos.XYZ.HorizontalSquareDistanceTo(ordered) <= 2.5 * 2.5, $"at {e.Pos.XYZ}, ordered at {ordered}");
        Assert.Equal(ordered.Y, e.Pos.Y, 0.6);
        e.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task It_benches_a_tunnel_six_high_a_heading_at_a_time_and_leaves_by_the_lower_mouth()
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-520, 90, 2100), reach: 14);
        var blocks = W.BlockAccessor;
        int granite = W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;
        // A hill of granite (x 0 to 10, nine high, z -3 to 4), its face stepped: a ledge two high in front
        // of it (x -6 to -3) for the top heading's mouth, the floor at its foot (x -2, -1) for the lower's.
        for (int x = -6; x <= 10; x++)
        for (int y = 0; y <= 8; y++)
        for (int z = -3; z <= 4; z++)
            if (x >= 0 || (x <= -3 && y <= 1))
                blocks.SetBlock(granite, site.AddCopy(x, y, z));
        // A tunnel 2 wide, 6 high and 6 long: its top heading layers 2 to 5, then layers 0 and 1.
        var a = site.AddCopy(0, 0, 0);
        var b = site.AddCopy(5, 5, 1);
        var area = MarkArea.Between(new MarkPos(a.X, a.Y, a.Z), new MarkPos(b.X, b.Y, b.Z));
        var cells = new List<BlockPos>();
        for (int x = area.Min.X; x <= area.Max.X; x++)
        for (int y = area.Min.Y; y <= area.Max.Y; y++)
        for (int z = area.Min.Z; z <= area.Max.Z; z++)
            cells.Add(new BlockPos(x, y, z));

        // Ordered on the floor at the cliff's foot, past the hill's side (z 8): from there it climbs onto the
        // ledge for the top heading, and walks home on the floor from the lower mouth.
        var (_, player, tool) = await Commander("eidolonbencher", site.AddCopy(-1, 0, 11));
        var e = await Spawn(site.AddCopy(-1, 0, 8), player);
        OrderMine(player, tool, [e], a, b);
        var order = Assert.IsType<MineOrder>(e.Orders!.Current);
        var ordered = e.Pos.XYZ;

        // Watch each tick: the lower heading only once the top one is gone; each a slice at a time from
        // the mouth; once all is mined, never back up on the ledge: out by the lower mouth.
        var standing = cells.ToHashSet();
        int ticks = 0, lastMined = -1;
        double highestAfter = double.MinValue;
        var faults = new List<string>();
        await World.Until(() =>
        {
            var broke = standing.Where(c => blocks.GetBlock(c).Id == 0).ToList();
            standing.ExceptWith(broke);
            foreach (var c in broke)
            {
                int y = c.Y - site.Y;
                if (EidolonMining.UnderFoot(e.Pos.X, e.Pos.Z, new MarkPos(c.X, c.Y, c.Z)))
                    faults.Add($"{c} broke under it at {e.Pos.XYZ}");
                if (y <= 1 && standing.Any(o => o.Y - site.Y >= 2))
                    faults.Add($"({c.X - site.X},{y},{c.Z - site.Z}) of the lower heading broke with the top one standing");
                if (standing.Any(o => o.X < c.X && (o.Y - site.Y >= 2) == (y >= 2)))
                    faults.Add($"({c.X - site.X},{y},{c.Z - site.Z}) broke with a slice of its heading nearer the mouth standing");
            }
            if (standing.Count == 0)
                highestAfter = Math.Max(highestAfter, e.Pos.Y - site.Y);
            if (order.Mined != lastMined || ++ticks % 300 == 0)
            {
                Log($"{order.Mined} mined, {order.Strikes} strikes, at {e.Pos.XYZ}: " + State(e));
                lastMined = order.Mined;
            }
            return e.Orders.OrderCode == null;
        }, 9000);
        Log($"after: {order.Mined} mined, {order.Strikes} strikes, {order.Unreachable} out of reach, {order.EmptyStrikes} empty, "
            + $"highest after {highestAfter:0.00}: " + State(e) + " / " + order.Reported);
        Assert.Empty(faults);
        Assert.Null(e.Orders.OrderCode);
        Assert.All(cells, c => Assert.Equal(0, blocks.GetBlock(c).Id));
        Assert.Equal(cells.Count, order.Mined);
        Assert.Equal(0, order.Unreachable);
        Assert.Equal(0, order.EmptyStrikes);
        // The last heading driven was the lower one, from its own face.
        Assert.Equal((a.Y + 1, a.Y), (order.Tunnel!.Top, order.Tunnel.Bottom));
        // Done on the lower floor, it walked straight out of the lower mouth, never up on the ledge, home.
        Assert.True(highestAfter < 0.5, $"up to {highestAfter} after it was done");
        Assert.True(e.Pos.XYZ.HorizontalSquareDistanceTo(ordered) <= 2.5 * 2.5, $"at {e.Pos.XYZ}, ordered at {ordered}");
        Assert.Equal(ordered.Y, e.Pos.Y, 0.6);
        e.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task Two_eidolons_drive_a_tunnel_4_wide_a_2_wide_heading_each_side_by_side()
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-720, 90, 2400), reach: 14);
        var blocks = W.BlockAccessor;
        int granite = W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;
        // A hill of granite (x 0 to 10, seven high, z -3 to 6), a cliff face to the west onto the floor.
        for (int x = 0; x <= 10; x++)
        for (int y = 0; y <= 6; y++)
        for (int z = -3; z <= 6; z++)
            blocks.SetBlock(granite, site.AddCopy(x, y, z));
        // A tunnel 4 wide, 4 high and 8 long into it: two headings, z 0 to 1 and 2 to 3.
        var a = site.AddCopy(0, 0, 0);
        var b = site.AddCopy(7, 3, 3);
        var area = Area(a, b);
        var toMine = Cells(area);

        var (_, player, tool) = await Commander("eidolontunnels2", site.AddCopy(-10, 0, 2));
        var e1 = await Spawn(site.AddCopy(-6, 0, 0), player);
        var e2 = await Spawn(site.AddCopy(-6, 0, 4), player);
        OrderMine(player, tool, [e1, e2], a, b);
        var o1 = Assert.IsType<MineOrder>(e1.Orders!.Current);
        var o2 = Assert.IsType<MineOrder>(e2.Orders!.Current);
        var home1 = e1.Pos.XYZ;
        var home2 = e2.Pos.XYZ;

        // Watch each tick: each strikes only in its own heading, never a column held by both, nothing
        // broken under either's box; and how near they come.
        var standing = toMine.ToHashSet();
        var faults = new List<string>();
        var headings = new HashSet<string>();
        double nearest = double.MaxValue;
        int ticks = 0, lastMined = -1;
        await World.Until(() =>
        {
            if (Jobs.Mining.Held(e1.EntityId).Intersect(Jobs.Mining.Held(e2.EntityId)).Any())
                faults.Add("both hold a column");
            foreach (var (o, e) in new[] { (o1, e1), (o2, e2) })
            {
                if (o.Driving && o.Sector is { } sector)
                    headings.Add($"{e.EntityId} {sector}");
                if (o.Driving && o.Group is { } g && !g.Cells.All(c => o.Sector is { } s && s.Contains(c.X, c.Z)))
                    faults.Add($"{e.EntityId} strikes {string.Join(" ", g.Cells)} out of its heading {o.Sector}");
            }
            var broke = standing.Where(c => blocks.GetBlock(c).Id == 0).ToList();
            foreach (var c in broke)
            {
                foreach (var e in new[] { e1, e2 })
                    if (EidolonMining.Undermines(e.Pos.X, Feet(e), e.Pos.Z, new MarkPos(c.X, c.Y, c.Z)) && c.Y < e.Pos.Y)
                        faults.Add($"{c} broke under {e.EntityId} at {e.Pos.XYZ}");
                standing.Remove(c);
            }
            if (OverArea(area, e1) && OverArea(area, e2))
                nearest = Math.Min(nearest, Math.Sqrt(e1.Pos.XYZ.HorizontalSquareDistanceTo(e2.Pos.XYZ)));
            if (o1.Mined + o2.Mined != lastMined || ++ticks % 300 == 0)
            {
                Log($"{o1.Mined} + {o2.Mined} mined, at {e1.Pos.XYZ} and {e2.Pos.XYZ}: {State(e1)} || {State(e2)}");
                lastMined = o1.Mined + o2.Mined;
            }
            return e1.Orders.OrderCode == null && e2.Orders.OrderCode == null;
        }, 9000);
        var lines = Reports(o1, o2);
        Log("reports: " + string.Join(" | ", lines));
        Log($"headings: {string.Join(", ", headings)}; nearest in the tunnel {nearest:0.00}");
        Log($"after: {o1.Mined} + {o2.Mined}, {o1.Unreachable} + {o2.Unreachable} out of reach, {o1.EmptyStrikes} + {o2.EmptyStrikes} empty; standing: "
            + string.Join(" ", toMine.Where(c => blocks.GetBlock(c).Id != 0).Select(c => $"({c.X - site.X},{c.Y - site.Y},{c.Z - site.Z})")));
        Assert.Empty(faults);
        Assert.Equal((1, 0), (o1.Tunnel!.DriveX, o1.Tunnel.DriveZ));
        Assert.All(toMine, c => Assert.Equal(0, blocks.GetBlock(c).Id));
        Assert.Equal(toMine.Count, o1.Mined + o2.Mined);
        Assert.Equal(0, o1.Unreachable + o2.Unreachable);
        Assert.Equal(0, o1.EmptyStrikes + o2.EmptyStrikes);
        // A heading each, the nearer: e1 the north one (z 0 to 1), e2 the south; both worked theirs.
        Assert.Contains($"{e1.EntityId} ({a.X},{a.Z})-({b.X},{a.Z + 1})", headings);
        Assert.Contains($"{e2.EntityId} ({a.X},{a.Z + 2})-({b.X},{b.Z})", headings);
        Assert.True(o1.Mined > 0 && o2.Mined > 0, $"{o1.Mined} and {o2.Mined}");
        // Side by side, never in each other's way: their boxes (1.7 wide) never overlapped.
        Assert.True(nearest >= 1.6, $"nearest {nearest}");
        var report = Assert.Single(lines);
        Assert.Contains("Your 2 eidolons", report);
        Assert.True(e1.Pos.XYZ.HorizontalSquareDistanceTo(home1) <= 2.5 * 2.5, $"{e1.EntityId} at {e1.Pos.XYZ}, ordered at {home1}");
        Assert.True(e2.Pos.XYZ.HorizontalSquareDistanceTo(home2) <= 2.5 * 2.5, $"{e2.EntityId} at {e2.Pos.XYZ}, ordered at {home2}");
        e1.Die(EnumDespawnReason.Removed);
        e2.Die(EnumDespawnReason.Removed);
    }
}
