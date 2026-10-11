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
/// mods-src/seraphhorizons/Eidolon, mining a marked area with the pack's default settings, ordered with
/// the command tool as a player orders it. A pit three layers deep in granite with no ramp: it quarries
/// it top down (no block broken while one two layers higher is left), never under its own box, the
/// stone's drops lying in the world, half a point of oil a strike; a block touching water and a chest are
/// left, and the column under the chest; done, it climbs out two blocks up onto a shelf and walks back to
/// where it was ordered. An area wider than it reaches from the rim, with a worn path across it: the
/// path is mined, the middle too. On a floor of its own in the sky
/// (<see cref="Felling.Sky"/>).
/// </summary>
[AtlasWorld]
public class EidolonMineScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;
    private EidolonSystem Mod => EidolonSystem.Of(World.Api)!;

    private void Log(string what)
    {
        W.Logger.Notification("[eidolon-mine-test] " + what);
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

    /// <summary>Binds the tool to <paramref name="e"/>, picks "mine" and marks the area between the
    /// two blocks, which gives the order.</summary>
    private void OrderMine(IPlayer player, ItemSlot tool, EntityLaborEidolon e, BlockPos a, BlockPos b)
    {
        if (ItemEidolonCommander.Bound(tool.Itemstack!).Count == 0)
            RightClick(player, tool, entity: e);
        int index = EidolonCommandModes.IndexOf(MineOrder.OrderCode);
        Assert.True(index >= 0, "no mine mode");
        tool.Itemstack!.Collectible.SetToolMode(tool, player, null!, index);
        RightClick(player, tool, block: a);
        RightClick(player, tool, block: b);
    }

    private static string State(EntityLaborEidolon e) => EidolonCommands.Describe(e) + " / " + e.GetInfoText().Replace('\n', ' ');

    [AtlasScenario]
    public void The_mine_mode_is_on_the_wheel_right_after_fell_and_marks_an_area()
    {
        var mode = EidolonCommandModes.Get(MineOrder.OrderCode);
        Assert.NotNull(mode);
        Assert.Equal(EidolonMarkKind.Area, mode!.Mark);
        Assert.Equal(EidolonMining.MaxAreaSide, mode.MaxAreaSide);
        Assert.Equal(EidolonMining.MaxAreaBlocks, mode.MaxAreaBlocks);
        Assert.Equal(EidolonCommandModes.IndexOf("fell") + 1, EidolonCommandModes.IndexOf(MineOrder.OrderCode));
        Assert.True(EidolonOrders.Exists(MineOrder.OrderCode));
        // The steel pickaxe it strikes at is the game's, with a speed for stone.
        var pick = W.GetItem(new AssetLocation(EidolonMining.PickCode));
        Assert.NotNull(pick);
        Assert.Equal(9, pick!.MiningSpeed[EnumBlockMaterial.Stone]);
        Assert.Equal(EnumBlockMaterial.Stone, W.GetBlock(new AssetLocation("game:rock-granite"))!.BlockMaterial);
    }

    [AtlasScenario(TimeoutMs = 360_000)]
    public async Task It_quarries_a_pit_three_deep_top_down_leaving_water_and_a_chest_and_climbs_out_two_blocks_up_to_walk_back()
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-520, 90, 1600), reach: 13);
        var blocks = W.BlockAccessor;
        int granite = W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;
        // Ground five blocks thick on a base; to the north of the pit (z -6 to -4) a shelf a block
        // lower than the ground and two over the pit's floor: no ramp, so it climbs out two blocks up
        // onto the shelf, then one to the ground.
        for (int x = -10; x <= 10; x++)
        for (int z = -13; z <= 10; z++)
        for (int y = -5; y <= -1; y++)
            blocks.SetBlock(y == -1 && z is >= -6 and <= -4 ? 0 : granite, site.AddCopy(x, y, z));
        var a = site.AddCopy(-3, -3, -3);
        var b = site.AddCopy(2, -1, 2);
        var area = MarkArea.Between(new MarkPos(a.X, a.Y, a.Z), new MarkPos(b.X, b.Y, b.Z));
        // Still water just outside the area's bottom layer, walled in: the area's block beside it is left.
        var water = site.AddCopy(3, -3, 0);
        World.SetBlock("game:water-still-7", water);
        var wet = site.AddCopy(2, -3, 0);
        // A chest on the area's top layer: left, and the column under it, which holds it up.
        var chest = site.AddCopy(-3, -1, -3);
        World.SetBlock("game:chest-east", chest);
        await World.Ticks(5);
        Assert.NotNull(blocks.GetBlockEntity(chest));

        var cells = new List<BlockPos>();
        for (int x = area.Min.X; x <= area.Max.X; x++)
        for (int y = area.Min.Y; y <= area.Max.Y; y++)
        for (int z = area.Min.Z; z <= area.Max.Z; z++)
            cells.Add(new BlockPos(x, y, z));
        var kept = new[] { wet, chest, chest.DownCopy(), chest.DownCopy(2) };
        var toMine = cells.Where(c => !kept.Contains(c)).ToList();

        var (player, tool) = await Commander("eidolonminer", site.AddCopy(0, 0, -12));
        var e = await Spawn(site.AddCopy(0, 0, -10), player);
        double oilBefore = e.Oil?.Tank?.Points ?? 0;
        OrderMine(player, tool, e, a, b);
        Assert.Equal(MineOrder.OrderCode, e.Orders!.OrderCode);
        var order = Assert.IsType<MineOrder>(e.Orders.Current);
        Assert.True(order.HasWorkLeft);
        Assert.Equal([area], order.WorkAreas);
        var ordered = e.Pos.XYZ;

        // Watch each tick: what broke since the last, never under its box, never with a block left two
        // layers higher; how low it went and whether it climbed.
        var standing = toMine.ToHashSet();
        int ticks = 0, lastMined = -1;
        double lowest = e.Pos.Y;
        bool climbed = false;
        var faults = new List<string>();
        await World.Until(() =>
        {
            lowest = Math.Min(lowest, e.Pos.Y);
            climbed |= e.AnimManager.IsAnimationActive(EidolonClimb.Animation);
            var broke = standing.Where(c => blocks.GetBlock(c).Id == 0).ToList();
            foreach (var c in broke)
            {
                if (EidolonMining.UnderFoot(e.Pos.X, e.Pos.Z, new MarkPos(c.X, c.Y, c.Z)) && c.Y < e.Pos.Y)
                    faults.Add($"{c} broke under it at {e.Pos.XYZ}");
                standing.Remove(c);
            }
            if (broke.Count > 0 && standing.Count > 0 && broke.Max(c => c.Y) < standing.Max(c => c.Y) - 1)
                faults.Add($"{string.Join(" ", broke)} broke with {standing.Max(c => c.Y)} left");
            if (order.Mined != lastMined || ++ticks % 300 == 0)
            {
                Log($"{order.Mined} mined, {order.Strikes} strikes, at {e.Pos.XYZ}: " + State(e));
                lastMined = order.Mined;
            }
            return e.Orders.OrderCode == null;
        }, 9000);
        Log($"after: lowest {lowest - site.Y}, climbed {climbed}: " + State(e));
        Log($"{order.Mined} mined, {order.Unreachable} out of reach; standing: "
            + string.Join(" ", toMine.Where(c => blocks.GetBlock(c).Id != 0).Select(c => $"({c.X - site.X},{c.Y - site.Y},{c.Z - site.Z})")));
        Assert.Empty(faults);
        Assert.Null(e.Orders.OrderCode);
        Assert.False(order.HasWorkLeft);
        Assert.Equal(0, order.UnloadedColumns);

        // Every block but those it leaves is gone, and they are not.
        Assert.All(toMine, c => Assert.Equal(0, blocks.GetBlock(c).Id));
        Assert.Equal(granite, blocks.GetBlock(wet).Id);
        Assert.NotNull(blocks.GetBlockEntity(chest));
        Assert.Equal(granite, blocks.GetBlock(chest.DownCopy()).Id);
        Assert.True(blocks.GetBlock(water).IsLiquid());
        Assert.Equal(toMine.Count, order.Mined);
        Assert.Equal(0, order.Unreachable);
        // Up to four a strike, and half a point of oil each.
        Assert.InRange(order.Strikes, (toMine.Count + 3) / 4, toMine.Count);
        if (e.Oil?.Tank is { } tank)
            Assert.Equal(oilBefore - order.Strikes * Mod.Config.OilPerMiningStrike, tank.Points, 3);
        // The granite's drops lie in the world.
        var drops = W.GetEntitiesAround(site.AddCopy(0, -2, 0).ToVec3d(), 12, 6, x => x is EntityItem)
            .Sum(x => ((EntityItem)x).Itemstack?.Collectible.Code.Path.Contains("granite") == true ? ((EntityItem)x).Itemstack.StackSize : 0);
        Log($"{drops} granite drops");
        Assert.True(drops > 0, "no drops");
        // It was down on the pit's floor, three below the ground, and climbed out to where it was ordered.
        Assert.True(lowest <= site.Y - 3 + 0.1, $"lowest {lowest - site.Y}");
        Assert.True(climbed, "it never climbed");
        Assert.True(e.Pos.XYZ.HorizontalSquareDistanceTo(ordered) <= 2.5 * 2.5, $"at {e.Pos.XYZ}, ordered at {ordered}");
        Assert.Equal(ordered.Y, e.Pos.Y, 0.6);
        e.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task It_mines_worn_paths_and_the_middle_of_a_wide_area_and_leaves_a_chest()
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-520, 90, 1900), reach: 14);
        var blocks = W.BlockAccessor;
        int granite = W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;
        int soil = W.GetBlock(new AssetLocation("game:soil-medium-normal"))!.Id;
        var path = W.GetBlock(new AssetLocation("desirepaths:soilpath-low-5"));
        Assert.NotNull(path);
        Assert.NotNull(path!.EntityClass);
        // Soil two blocks deep on granite.
        for (int x = -11; x <= 11; x++)
        for (int z = -12; z <= 11; z++)
        {
            blocks.SetBlock(granite, site.AddCopy(x, -3, z));
            blocks.SetBlock(soil, site.AddCopy(x, -2, z));
            blocks.SetBlock(soil, site.AddCopy(x, -1, z));
        }
        // 8 × 8, the top layer (the pit's depth and the chest's column are EidolonMine's pit): its
        // middle is out of reach from the rim (two blocks past its box), so it walks in.
        var a = site.AddCopy(-4, -1, -4);
        var b = site.AddCopy(3, -1, 3);
        var area = MarkArea.Between(new MarkPos(a.X, a.Y, a.Z), new MarkPos(b.X, b.Y, b.Z));
        // A worn path across the top layer (Desire Paths' soil, with a block entity), a chest in a corner.
        for (int x = -4; x <= 3; x++)
            blocks.SetBlock(path.Id, site.AddCopy(x, -1, 0));
        var chest = site.AddCopy(-4, -1, -4);
        World.SetBlock("game:chest-east", chest);
        await World.Ticks(5);
        Assert.NotNull(blocks.GetBlockEntity(site.AddCopy(0, -1, 0)));
        Assert.NotNull(blocks.GetBlockEntity(chest));
        var underChest = chest.DownCopy();
        var cells = new List<BlockPos>();
        for (int x = area.Min.X; x <= area.Max.X; x++)
        for (int y = area.Min.Y; y <= area.Max.Y; y++)
        for (int z = area.Min.Z; z <= area.Max.Z; z++)
            cells.Add(new BlockPos(x, y, z));
        var kept = new[] { chest };
        var toMine = cells.Where(c => !kept.Contains(c)).ToList();

        var p = await World.JoinPlayer("eidolonpathminer");
        var player = (IServerPlayer)p.Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Creative;
        await p.TeleportTo(site.AddCopy(0, 0, -11));
        var tool = player.InventoryManager.ActiveHotbarSlot;
        tool.Itemstack = new ItemStack(W.GetItem(new AssetLocation(EidolonCommanderSystem.CommanderCode)));
        tool.MarkDirty();
        var e = await Spawn(site.AddCopy(0, 0, -9), player);
        OrderMine(player, tool, e, a, b);
        var order = Assert.IsType<MineOrder>(e.Orders!.Current);
        // Exposed soil may grow tallgrass in a cell already mined, which it mines too: count what grew
        // back, so the count is exact.
        var mined = new HashSet<BlockPos>();
        int regrown = 0, ticks = 0;
        await World.Until(() =>
        {
            foreach (var c in toMine)
            {
                bool air = blocks.GetBlock(c).Id == 0;
                if (air)
                    mined.Add(c);
                else if (mined.Remove(c))
                {
                    regrown++;
                    Log($"grew back at ({c.X - site.X},{c.Y - site.Y},{c.Z - site.Z}): {blocks.GetBlock(c).Code}");
                }
            }
            if (++ticks % 300 == 0)
                Log($"{order.Mined} mined, at {e.Pos.XYZ}: " + State(e));
            return e.Orders.OrderCode == null;
        }, 6000);
        Log($"after: {order.Mined} mined, {order.Unreachable} out of reach; report: {order.Reported}; standing: "
            + string.Join(" ", toMine.Where(c => blocks.GetBlock(c).Id != 0).Select(c => $"({c.X - site.X},{c.Y - site.Y},{c.Z - site.Z})")));
        Assert.All(toMine, c => Assert.Equal(0, blocks.GetBlock(c).Id));
        Assert.NotNull(blocks.GetBlockEntity(chest));
        Assert.Equal(soil, blocks.GetBlock(underChest).Id);
        Assert.Equal(toMine.Count + regrown, order.Mined);
        Assert.Equal(0, order.Unreachable);
        // the chest left, nothing out of reach
        Assert.Contains("1 left as they were, 0 out of its reach", order.Reported);
        e.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task A_dry_reservoir_stops_it_and_oil_starts_it_again()
    {
        var site = await Felling.Sky(World, World.Spawn.AddCopy(-520, 90, 1700), reach: 10);
        var blocks = W.BlockAccessor;
        int granite = W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;
        for (int x = -8; x <= 8; x++)
        for (int z = -8; z <= 8; z++)
            blocks.SetBlock(granite, site.AddCopy(x, -2, z));
        // A row of granite on the floor to mine, eight blocks.
        var a = site.AddCopy(-2, 0, 3);
        var b = site.AddCopy(1, 0, 4);
        for (int x = -2; x <= 1; x++)
        for (int z = 3; z <= 4; z++)
            blocks.SetBlock(granite, site.AddCopy(x, 0, z));
        var (player, tool) = await Commander("eidolondryminer", site.AddCopy(0, 0, -8));
        var e = await Spawn(site.AddCopy(0, 0, -2), player);
        if (e.Oil?.Tank == null)
            return; // MachineOil off: never dry.
        // Oil for one strike only.
        e.Oil.SetPoints(Mod.Config.OilPerMiningStrike);
        OrderMine(player, tool, e, a, b);
        var order = Assert.IsType<MineOrder>(e.Orders!.Current);
        await World.Until(() => order.Strikes >= 1 && !e.CanWork, 2000);
        await World.Ticks(200);
        Log("dry: " + State(e));
        Assert.Equal(1, order.Strikes);
        Assert.Equal(MineOrder.OrderCode, e.Orders.OrderCode);
        e.Oil.SetPoints(Mod.Config.OilTank / 2);
        await World.Until(() => e.Orders.OrderCode == null, 4000);
        Log("after: " + State(e));
        Assert.Equal(8, order.Mined);
        e.Die(EnumDespawnReason.Removed);
    }

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

        var (player, tool) = await Commander("eidolontunneller", site.AddCopy(-10, 0, 1));
        var home = site.AddCopy(-6, 0, 1);
        var e = await Spawn(home, player);
        // Three high is refused as it is marked, the first corner kept: the opposite corner one higher orders it.
        OrderMine(player, tool, e, a, site.AddCopy(7, 2, 1));
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
        var (player, tool) = await Commander("eidolonbencher", site.AddCopy(-1, 0, 11));
        var e = await Spawn(site.AddCopy(-1, 0, 8), player);
        OrderMine(player, tool, e, a, b);
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
}
