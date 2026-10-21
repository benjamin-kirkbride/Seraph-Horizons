using Atlas.Api;
using Atlas.XUnit;
using Newtonsoft.Json;
using SeraphHorizons.Mod.Eidolon;
using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Eidolon, a crew's mine window grown round a member standing outside it (README
/// "Eidolon", working together, *Routes*), on a player's hillside (fixtures/eidolon-hillside, a WorldEdit
/// export of the owner's world) with the owner's mine area: one eidolon 10 columns west of the area, outside
/// the window the area alone would have, the other on the hillside in it. The outside one gets stops in the
/// first routes solved, and both mine, whether the two are ordered together (the first plan's window takes it
/// in) or it joins the job running (the window grown and planned again over). On a floor in the sky (<see cref="Felling.Sky"/>), on the virtual clock
/// (<see cref="VirtualClock"/>).
/// </summary>
[AtlasWorld]
public class EidolonMineWindowScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;
    private EidolonSystem Mod => EidolonSystem.Of(World.Api)!;

    // the export's own corner, and the owner's area and eidolons in its coordinates
    private static readonly BlockPos Original = new(511862, 113, 512212);
    private static readonly (int X, int Y, int Z) AreaMin = (511878, 117, 512222), AreaMax = (511914, 128, 512249);
    private static readonly (int X, int Y, int Z) Inside = (511884, 122, 512233);
    private static readonly (int X, int Z) Outside = (511868, 512235);

    private void Log(string what)
    {
        W.Logger.Notification("[eidolon-minewindow-test] " + what);
        output.WriteLine(what);
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

    private async Task<EntityLaborEidolon> Spawn(BlockPos at, IPlayer owner)
    {
        var e = Mod.Spawn(W, new Vec3d(at.X + 0.5, at.Y, at.Z + 0.5), 0, owner, activate: true);
        Assert.NotNull(e);
        await World.Until(() => e!.CanWork, 400);
        return e!;
    }

    // the hillside placed in the sky, a commander joined over it: where the outside one stands, the area's corners
    private async Task<(System.Func<int, int, int, BlockPos> T, BlockPos Outside, BlockPos A1, BlockPos A2, IServerPlayer Player, ItemSlot Tool)> Hillside(
        string name, int dx)
    {
        var centre = World.Spawn.AddCopy(-720 + dx, 0, 1600);
        centre.Y = W.BlockAccessor.MapSizeY - 2 - 44;
        var site = await Felling.Sky(World, centre, reach: 40);
        var b = site.AddCopy(-31, 0, -24);
        BlockPos T(int x, int y, int z) => new(b.X + x - Original.X, b.Y + y - Original.Y, b.Z + z - Original.Z);
        var schematic = JsonConvert.DeserializeObject<BlockSchematic>(
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(typeof(EidolonMineWindowScenarios).Assembly.Location)!, "fixtures", "eidolon-hillside", "hillside.json")))!;
        schematic.Init(W.BlockAccessor);
        schematic.Place(W.BlockAccessor, W, b, EnumReplaceMode.ReplaceAll, true);
        await World.Ticks(20);
        // where the outside one stands: the top of its column
        var blocks = W.BlockAccessor;
        var outside = T(Outside.X, Original.Y + 27, Outside.Z);
        while (outside.Y > b.Y && blocks.GetBlock(outside.DownCopy()).CollisionBoxes is not { Length: > 0 })
            outside.Y--;
        var p = await World.JoinRetrying(name);
        var player = (IServerPlayer)p.Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Creative;
        await p.TeleportTo(T(511895, 140, 512235));
        var tool = player.InventoryManager.ActiveHotbarSlot;
        tool.Itemstack = new ItemStack(W.GetItem(new AssetLocation(EidolonCommanderSystem.CommanderCode)));
        tool.MarkDirty();
        return (T, outside, T(AreaMin.X, AreaMin.Y, AreaMin.Z), T(AreaMax.X, AreaMax.Y, AreaMax.Z), player, tool);
    }

    private static void OrderMine(IPlayer player, ItemSlot tool, IEnumerable<EntityLaborEidolon> eidolons, BlockPos a, BlockPos b)
    {
        foreach (var e in eidolons)
            RightClick(player, tool, entity: e);
        tool.Itemstack!.Collectible.SetToolMode(tool, player, null!, EidolonCommandModes.IndexOf(MineOrder.OrderCode));
        RightClick(player, tool, block: a);
        RightClick(player, tool, block: b);
    }

    private static string Describe(MarkArea? a) => a is { } w ? $"({w.Min.X},{w.Min.Y},{w.Min.Z})-({w.Max.X},{w.Max.Y},{w.Max.Z})" : "none";

    // The first routes swapped in after plan version planVersion: each member's stops, and the window they were solved on.
    private async Task<(Dictionary<long, int> Stops, MarkArea Window)> FirstRoutes(MineJobCells cells, int planVersion)
    {
        Dictionary<long, int>? first = null;
        MarkArea window = default;
        await World.Until(() =>
        {
            if (cells.PlanVersion <= planVersion || cells.Routes is not { } r || r.PlanVersion != cells.PlanVersion)
                return false;
            first = r.Routes.ToDictionary(x => x.Key, x => x.Value.Stops.Count(s => s.Kind == MineStopKind.Station));
            window = cells.Live!.Site.Window;
            Log($"first routes of plan {cells.PlanVersion} ({(r.Today ? "today's" : $"variant {r.VariantIndex}")}): stops "
                + string.Join("/", first.Values) + $"; window {Describe(window)}");
            Assert.False(r.Today, "the job was left to today's dispatch: no routes to check");
            return true;
        }, 3000);
        Assert.NotNull(first);
        return (first!, window);
    }

    private async Task BothMine(MineOrder o1, MineOrder o2, int from1, int from2)
    {
        await World.Until(() => o1.Mined > from1 && o2.Mined > from2, 6000);
        Log($"{o1.Mined} + {o2.Mined} mined");
        Assert.True(o1.Mined > from1 && o2.Mined > from2, $"{o1.Mined} and {o2.Mined}");
    }

    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task One_ten_columns_outside_the_area_gets_stops_on_the_first_solve_and_both_mine()
    {
        var (T, outside, a1, a2, player, tool) = await Hillside("minewindow", 0);
        var area = MarkArea.Between(new MarkPos(a1.X, a1.Y, a1.Z), new MarkPos(a2.X, a2.Y, a2.Z));
        var outsideNode = new PathCell(outside.X, outside.Y, outside.Z);
        Assert.Equal(10, MineSite.Off(area, outsideNode));
        Assert.False(MineSite.Holds(MineSite.Around(area), outsideNode));

        using var clock = VirtualClock.Start(World);
        var e1 = await Spawn(T(Inside.X, Inside.Y, Inside.Z), player);
        var e2 = await Spawn(outside, player);
        Log($"inside at {e1.Pos.XYZ}, outside at {e2.Pos.XYZ}; area {Describe(area)}");
        OrderMine(player, tool, [e1, e2], a1, a2);
        await World.Ticks(2);
        var o1 = Assert.IsType<MineOrder>(e1.Orders!.Current);
        var o2 = Assert.IsType<MineOrder>(e2.Orders!.Current);
        var cells = o1.Job!.Shared;
        Assert.Same(cells, o2.Job!.Shared);

        // the first routes: the outside one placed on the walk graph and given stops
        var (first, window) = await FirstRoutes(cells, 0);
        Assert.True(MineSite.Holds(window, outsideNode), $"window {Describe(window)} does not hold {outsideNode}");
        Assert.True(first[e1.EntityId] > 0, $"inside: {first[e1.EntityId]} stops");
        Assert.True(first[e2.EntityId] > 0, $"outside: {first[e2.EntityId]} stops");
        await BothMine(o1, o2, 0, 0);
        e1.Die(EnumDespawnReason.Removed);
        e2.Die(EnumDespawnReason.Removed);
    }

    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task One_outside_joining_the_job_running_grows_its_window_and_gets_stops()
    {
        var (T, outside, a1, a2, player, tool) = await Hillside("minewindowjoin", 160);
        var area = MarkArea.Between(new MarkPos(a1.X, a1.Y, a1.Z), new MarkPos(a2.X, a2.Y, a2.Z));
        var outsideNode = new PathCell(outside.X, outside.Y, outside.Z);

        using var clock = VirtualClock.Start(World);
        var e1 = await Spawn(T(Inside.X, Inside.Y, Inside.Z), player);
        var e2 = await Spawn(outside, player);
        e2.Orders!.ClearOrder();
        OrderMine(player, tool, [e1], a1, a2);
        await World.Ticks(2);
        var o1 = Assert.IsType<MineOrder>(e1.Orders!.Current);
        var cells = o1.Job!.Shared;
        // alone, on the area's own window, until it has mined a little
        await World.Until(() => o1.Mined >= 8, 3000);
        Assert.Equal(MineSite.Around(area), cells.Live!.Site.Window);
        int plan = cells.PlanVersion;

        // the outside one given the same area, as the tool gives it, from where it stands: the window grown round
        // it, planned again, then routed
        e2.Orders.SetOrder(MineOrder.OrderCode, MineOrder.Args(area, e2.Pos.XYZ));
        await World.Ticks(5);
        var o2 = Assert.IsType<MineOrder>(e2.Orders!.Current);
        Assert.Same(cells, o2.Job!.Shared);
        int mined1 = o1.Mined;
        var (first, window) = await FirstRoutes(cells, plan);
        Assert.True(MineSite.Holds(window, outsideNode), $"window {Describe(window)} does not hold {outsideNode}");
        Assert.True(first[e1.EntityId] > 0, $"first: {first[e1.EntityId]} stops");
        Assert.True(first[e2.EntityId] > 0, $"joined: {first[e2.EntityId]} stops");
        // nothing dug while the grown window was read is dug again or lost: what the first mined stays mined
        Assert.All(cells.Live!.Remaining, c => Assert.NotEqual(0, W.BlockAccessor.GetBlock(new BlockPos(c.X, c.Y, c.Z)).Id));
        await BothMine(o1, o2, mined1, 0);
        e1.Die(EnumDespawnReason.Removed);
        e2.Die(EnumDespawnReason.Removed);
    }
}
