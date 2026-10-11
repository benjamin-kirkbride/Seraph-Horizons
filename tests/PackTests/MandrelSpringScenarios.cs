using Atlas.XUnit;
using SeraphHorizons.Mod.MandrelStation;
using SeraphHorizons.Mod.MandrelStation.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

// seraphhorizons, MandrelStation's spring job and RodsOnForge: a hot steel rod wound into a spring on
// the station by right-click held with tongs, and every metal rod heated on the game's forge. The holds
// are the block's own interaction calls, as the game makes them while right-click is held. Every
// scenario builds on a granite floor of its own 40 above spawn at x -460 to -508, z -520 (a row south of
// the station's other scenarios), with the gear cutter's player.
public partial class SharedWorldScenarios
{
    private const string SteelRod = "game:rod-steel";
    private const string Spring = "seraphhorizons:spring-steel";

    /// <summary>A steel rod at <paramref name="temperature"/> °C, as the forge leaves it.</summary>
    private ItemStack HotRod(float temperature, string code = SteelRod, int size = 1)
    {
        var rod = CutterItem(code, size);
        rod.Collectible.SetTemperature(W, rod, temperature);
        return rod;
    }

    private void HoldTongs(IPlayer player, string? code = "game:tongs")
    {
        player.Entity.LeftHandItemSlot.Itemstack = code == null ? null : CutterItem(code);
        player.Entity.LeftHandItemSlot.MarkDirty();
    }

    /// <summary>Right-click held on the station: a click, then the game's steps a tick apart, for
    /// <paramref name="ticks"/> ticks or until the rod is used up; returns whether the step was taken last.</summary>
    private async Task<bool> MandrelHold(BEMandrelStation station, IPlayer player, int ticks, BlockPos? at = null)
    {
        var pos = at ?? station.Pos;
        CutterClick(player, pos, null);
        var sel = new BlockSelection { Position = pos.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.5, 0.5) };
        var block = W.BlockAccessor.GetBlock(pos);
        bool going = _cutterHandled;
        float held = 0;
        for (int i = 0; i < ticks && going && station.RodOn; i++)
        {
            await World.Ticks(1);
            held += 0.05f;
            going = block.OnBlockInteractStep(held, W, player, sel);
        }
        block.OnBlockInteractStop(held, W, player, sel);
        return going;
    }

    // ---- Loading ----

    [AtlasScenario]
    public void Mandrel_station_spring_and_rods_on_forge_load()
    {
        Assert.True(MandrelStationSystem.SpringsApply(World.Api));
        var spring = W.GetItem(new AssetLocation(Spring));
        Assert.True(spring is { Id: > 0, IsMissing: false }, "no spring");
        // steel's: it melts back into a steel ingot at the rod's melting point
        Assert.Equal(1502, spring!.CombustibleProps!.MeltingPoint);
        Assert.Equal("game:ingot-steel", spring.CombustibleProps.SmeltedStack!.Code.ToString());
        Assert.Equal("Steel spring", new ItemStack(spring).GetName());
        Assert.Contains("items", spring.CreativeInventoryTabs ?? []);
        Assert.NotNull(MandrelRig.Wind);
        Assert.Equal(MandrelRig.Wind!.TurnsPerRod, MandrelMod.Config.TurnsPerRod);
        // every metal rod is forgeable now, and drawn on the coals by its own transform
        foreach (var rod in W.Items.Where(i => i?.Code is { Domain: "game" } c && c.Path.StartsWith("rod-", StringComparison.Ordinal)))
        {
            Assert.True(rod.Attributes?["forgable"].AsBool() == true, $"{rod.Code} is not forgable");
            Assert.True(rod.Attributes!["inForgeTransform"].Exists, $"{rod.Code} has no inForgeTransform");
        }
    }

    // ---- The forge ----

    // A rod goes on a lit forge as an ingot does (Shift + right-click), one at a time, heats there, and
    // comes off hot (right-click).
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Rods_go_on_the_forge_and_heat_there()
    {
        var pos = await CutterSite(-460, -520);
        var player = await CutterPlayer();
        World.SetBlock("game:forge", pos);
        await World.Ticks(5);
        // the pack's forge is Crucibulum's, which passes what is not a crucible to the game's
        var forge = Assert.IsAssignableFrom<BlockEntityForge>(W.BlockAccessor.GetBlockEntity(pos));
        player.Entity.Controls.ShiftKey = true;
        try
        {
            Assert.Equal(7, CutterClick(player, pos, CutterItem("game:charcoal", 8))?.StackSize);
            Assert.True(forge.FuelLevel > 0);
            // a copper rod, one from a stack of two; a second does not stack on it
            Assert.Equal(1, CutterClick(player, pos, CutterItem("game:rod-copper", 2))?.StackSize);
            Assert.Equal("game:rod-copper", forge.WorkItemStack?.Collectible.Code.ToString());
            Assert.Equal(1, CutterClick(player, pos, CutterItem("game:rod-copper"))?.StackSize);
            Assert.Equal(1, forge.WorkItemStack!.StackSize);
        }
        finally
        {
            player.Entity.Controls.ShiftKey = false;
        }
        forge.TryIgnite();
        float before = forge.WorkItemStack!.Collectible.GetTemperature(W, forge.WorkItemStack);
        await World.Until(() => forge.WorkItemStack!.Collectible.GetTemperature(W, forge.WorkItemStack) > before + 15, 30000);
        // taken off by hand, hot (with tongs: the game drops a hot item held without them)
        HoldTongs(player);
        CutterClick(player, pos, null);
        Assert.Null(forge.WorkItemStack);
        float hottest = player.InventoryManager.Inventories.Values.Where(inv => inv.ClassName is GlobalConstants.hotBarInvClassName or GlobalConstants.backpackInvClassName).SelectMany(inv => inv)
            .Where(s => s.Itemstack?.Collectible.Code.ToString() == "game:rod-copper")
            .Max(s => s.Itemstack!.Collectible.GetTemperature(W, s.Itemstack));
        Assert.True(hottest > before + 10);
        HoldTongs(player, null);
        CutterKillItems(pos);
        W.BlockAccessor.SetBlock(0, pos);
    }

    // ---- Winding ----

    // On a fitted mandrel a cold steel rod is refused and stays in hand; a hot one goes on. Without tongs
    // the hold does nothing; with tongs in the off hand it winds a turn a second, from the stump or the
    // ghost, and in about four seconds one spring comes off beyond the tip as hot as the rod was, the
    // rod used up and the tongs a point the worse.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Mandrel_station_winds_a_hot_steel_rod_into_a_hot_spring()
    {
        var pos = await CutterSite(-472, -520);
        var player = await CutterPlayer();
        var station = await PlaceMandrelStation(pos, "south");
        FitMandrel(station, player);
        // hot metal is held with tongs (the game drops it from a hand without them)
        HoldTongs(player);

        // cold: refused, it stays in hand
        Assert.Equal(1, CutterClick(player, pos, HotRod(400))?.StackSize);
        Assert.False(station.RodOn);
        // iron is never wound, hot or not
        Assert.Equal(1, CutterClick(player, pos, HotRod(1100, "game:rod-iron"))?.StackSize);
        Assert.False(station.RodOn);
        // hot: one from the stack goes on, its temperature with it
        Assert.Equal(1, CutterClick(player, pos, HotRod(1100, size: 2))?.StackSize);
        Assert.True(station.RodOn);
        Assert.Equal(3, station.JobClass);
        Assert.False(station.HollowOn);
        Assert.InRange(station.RodTemperature, 1000, 1100);
        Assert.Contains("A steel rod on the mandrel", MandrelInfo(station, player));
        // no tongs: nothing else goes on (a click is a refused hold), the hold is refused and nothing winds
        HoldTongs(player, null);
        Assert.Equal(1, CutterClick(player, pos, CutterItem(Forging.LeadHollow))?.StackSize);
        Assert.Equal(1, CutterClick(player, pos, CutterItem(SteelRod))?.StackSize);
        Assert.False(_cutterHandled);
        player.InventoryManager.ActiveHotbarSlot.Itemstack = null;
        Assert.False(await MandrelHold(station, player, 20));
        Assert.Equal(0, station.Wind.Work);

        // a save keeps the rod and its heat
        var tree = new Vintagestory.API.Datastructures.TreeAttribute();
        station.ToTreeAttributes(tree);
        station.FromTreeAttributes(tree, W);
        Assert.True(station.RodOn);
        Assert.InRange(station.RodTemperature, 1000, 1100);

        // with tongs: about a second of holding is about a quarter of the rod
        HoldTongs(player);
        var tongs = player.Entity.LeftHandItemSlot.Itemstack!;
        int durability = tongs.Collectible.GetRemainingDurability(tongs);
        await MandrelHold(station, player, 20, MandrelGhost(station));
        Assert.True(station.RodOn);
        Assert.InRange(station.Wind.Work, 0.01, 0.9);
        float temperature = station.RodTemperature;
        // held on to the end
        await MandrelHold(station, player, 400);
        Assert.False(station.RodOn);
        Assert.Equal(0, station.JobClass);
        await World.Ticks(5);
        var near = CutterItemsNear(pos);
        Assert.Equal(1, near.GetValueOrDefault(Spring));
        Assert.Equal(0, near.GetValueOrDefault(SteelRod));
        var spring = World.EntitiesIn(new Cuboidi(pos.X - 6, pos.Y - 3, pos.Z - 6, pos.X + 6, pos.Y + 6, pos.Z + 6))
            .OfType<EntityItem>().Single(e => e.Itemstack.Collectible.Code.ToString() == Spring).Itemstack;
        Assert.InRange(spring.Collectible.GetTemperature(W, spring), temperature - 100, temperature + 1);
        Assert.True(spring.Collectible.GetTemperature(W, spring) > 751);
        Assert.Equal(durability - MandrelMod.Config.TongsWearPerRod, tongs.Collectible.GetRemainingDurability(tongs));
        Assert.Contains("Mandrel bare", MandrelInfo(station, player));
        CutterKillItems(pos);
        HoldTongs(player, null);
    }

    // A rod that cools below working heat stops winding and says so; Ctrl + right-click takes it back,
    // straight and as hot as it is, and it goes on again once reheated. Breaking the station gives back a
    // rod not yet wound, and loses a part-wound one.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Mandrel_station_stops_a_cooled_rod_and_gives_it_back_to_reheat()
    {
        var pos = await CutterSite(-484, -520);
        var player = await CutterPlayer();
        var station = await PlaceMandrelStation(pos, "east");
        FitMandrel(station, player);
        HoldTongs(player);
        Assert.Null(CutterClick(player, pos, HotRod(1000)));
        await MandrelHold(station, player, 10);
        double wound = station.Wind.Work;
        Assert.True(wound > 0);
        // it cools to 700 °C on the station: no more winding
        station.Rod!.Collectible.SetTemperature(W, station.Rod, 700);
        await MandrelHold(station, player, 20);
        Assert.Equal(wound, station.Wind.Work, 6);
        Assert.Contains("too cold to wind", MandrelInfo(station, player));
        // taken back, straight, at 700 °C
        CutterClick(player, pos, null, ctrl: true);
        Assert.False(station.RodOn);
        float back = player.InventoryManager.Inventories.Values.Where(inv => inv.ClassName is GlobalConstants.hotBarInvClassName or GlobalConstants.backpackInvClassName).SelectMany(inv => inv)
            .Where(s => s.Itemstack?.Collectible.Code.ToString() == SteelRod)
            .Max(s => s.Itemstack!.Collectible.GetTemperature(W, s.Itemstack));
        Assert.InRange(back, 650, 701);
        // reheated, it goes on again from the start
        Assert.Null(CutterClick(player, pos, HotRod(1050)));
        Assert.True(station.RodOn);
        Assert.Equal(0, station.Wind.Work);

        // broken with an unwound rod: the rod comes back with the frame and the mandrel
        CutterKillItems(pos);
        W.BlockAccessor.GetBlock(pos).OnBlockBroken(W, pos, player);
        await World.Ticks(3);
        var drops = CutterItemsNear(pos);
        Assert.Equal(1, drops.GetValueOrDefault(SteelRod));
        Assert.Equal(1, drops.GetValueOrDefault("game:rod-iron"));
        CutterKillItems(pos);

        // broken part-wound: the rod is lost
        station = await PlaceMandrelStation(pos, "east");
        FitMandrel(station, player);
        Assert.Null(CutterClick(player, pos, HotRod(1050)));
        await MandrelHold(station, player, 10);
        Assert.True(station.Wind.Work > 0);
        W.BlockAccessor.GetBlock(pos).OnBlockBroken(W, pos, player);
        await World.Ticks(3);
        Assert.Equal(0, CutterItemsNear(pos).GetValueOrDefault(SteelRod));
        CutterKillItems(pos);
        HoldTongs(player, null);
    }

    // On a bare station a steel rod, hot or cold, is the mandrel (the rule for the steel rod being both).
    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task Mandrel_station_takes_a_steel_rod_as_its_mandrel_when_bare()
    {
        var pos = await CutterSite(-496, -520);
        var player = await CutterPlayer();
        var station = await PlaceMandrelStation(pos);
        HoldTongs(player);
        Assert.Null(CutterClick(player, pos, HotRod(1100)));
        Assert.Equal(SteelRod, station.Mandrel);
        Assert.False(station.RodOn);
        // and a hot one on that steel mandrel is a spring job
        Assert.Null(CutterClick(player, pos, HotRod(1100)));
        Assert.True(station.RodOn);
        HoldTongs(player, null);
        CutterKillItems(pos);
    }
}
