using Atlas.XUnit;
using SeraphHorizons.Mod.AcidParting;
using SeraphHorizons.Mod.AcidParting.Core;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

// seraphhorizons, AcidParting (#931) on the ore machines' shared game code (Machines/Game, #711): the acid parting vessel
// in the pack's world, placed, built stage by stage with real items, its copper plate hung, charged with dore, nitric
// acid and wash water, its fire laid and lit, parted under a chimney (aged rather than waited on), its silver drawn
// from the tub and its gold taken out of the vessel, then capped and broken back into everything that went in. Built
// on granite floors 40 above spawn at x -940, z 940; the gear cutter's player.
public partial class MillWorldScenarios
{
    private const string VesselFrame = "seraphhorizons:acidparting-";
    private const string Dore = "seraphhorizons:bullion-quartz_nativegold-gold";

    private async Task<BEAcidParting> PlaceVessel(BlockPos pos, string side = "south")
    {
        World.SetBlock(VesselFrame + side, pos);
        await World.Ticks(5);
        var be = W.BlockAccessor.GetBlockEntity(pos) as BEAcidParting ?? throw new Xunit.Sdk.XunitException($"no acid parting vessel at {pos}");
        be.PlaceCells();
        return be;
    }

    /// <summary>A right-click holding <paramref name="held"/> at the vessel's native point <paramref name="native"/> (the
    /// vessel's or the tub's mouth, the ash mouth), on the cell it is in.</summary>
    private ItemStack? VesselClick(IPlayer player, BEAcidParting vessel, Float3 native, ItemStack? held)
    {
        var world = vessel.WorldPoint(native);
        var cell = new BlockPos((int)Math.Floor(world.X), (int)Math.Floor(world.Y), (int)Math.Floor(world.Z));
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = held;
        slot.MarkDirty();
        var sel = new BlockSelection { Position = cell, Face = BlockFacing.UP, HitPosition = new Vec3d(world.X - cell.X, world.Y - cell.Y, world.Z - cell.Z) };
        _cutterHandled = W.BlockAccessor.GetBlock(cell).OnBlockInteractStart(W, player, sel);
        return slot.Itemstack;
    }

    /// <summary>A wooden bucket holding <paramref name="litres"/> of the game's nitric acid.</summary>
    private ItemStack AcidBucket(double litres)
    {
        var bucket = Assert.IsAssignableFrom<BlockLiquidContainerBase>(W.GetBlock(new AssetLocation("game:woodbucket")));
        var stack = new ItemStack(bucket);
        bucket.SetContent(stack, new ItemStack(W.GetItem(new AssetLocation(AcidPartingRules.Acid))!, (int)Math.Round(litres * 100)));
        return stack;
    }

    private static double BucketLitres(ItemStack? bucket) =>
        bucket?.Collectible is BlockLiquidContainerBase b && b.GetContent(bucket) is { } c ? c.StackSize / 100.0 : 0;

    [AtlasScenario]
    public void Acid_parting_loads_with_its_frame_build_recipes_and_figures()
    {
        Assert.True(AcidPartingSystem.Applies(World.Api));
        foreach (var side in new[] { "north", "east", "south", "west" })
            Assert.IsType<BlockOreMachine>(W.GetBlock(new AssetLocation(VesselFrame + side)));
        var block = (BlockOreMachine)W.GetBlock(new AssetLocation(VesselFrame + "north"));
        Assert.Equal("acidparting", block.Machine);
        Assert.NotNull(block.Rig);
        Assert.NotNull(AcidPartingSystem.Of(World.Api).Rig);
        Assert.Equal(VesselRequires.Extra.Order(), block.ExtraRequires.Order());
        var build = OreMachinesSystem.Of(World.Api).Build("acidparting")!;
        Assert.Equal(["brickwork", "lining", "sandbath", "vesseltub", "services"], build.Stages.Select(s => s.Id).ToArray());
        foreach (var code in build.Stages[0].Materials[0].Codes)
            Assert.True(W.GetItem(new AssetLocation(code)) is { Id: > 0 }, code);
        Assert.NotEmpty(W.SearchBlocks(new AssetLocation("game:crucible-*-fired")));
        Assert.True(W.GetItem(new AssetLocation("seraphhorizons:pipesection-lead")) is { Id: > 0 });
        Assert.Equal("Acid parting vessel frame", new ItemStack(block).GetName());
        var recipe = Assert.Single(W.GridRecipes, r => r.Output?.Code?.ToString() == VesselFrame + "north" && r.Enabled);
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.Code?.ToString() == "game:rod-iron" && i.Quantity == 2);
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.IsTool == true && i.Code?.Path == "hammer-*");
        // the acid it takes is in the game (Expanded Matter enables the game's nitric acid), and the pack cooks it
        Assert.True(W.GetItem(new AssetLocation(AcidPartingRules.Acid)) is { Id: > 0 });
        var cooking = World.Api.ModLoader.GetModSystem<RecipeRegistrySystem>().CookingRecipes;
        Assert.Contains(cooking, r => r.Code == "aquafortis" && r.CooksInto?.Code?.ToString() == AcidPartingRules.Acid);
        // the dore it takes is gold quartz's bullion
        Assert.True(W.GetItem(new AssetLocation(Dore)) is { Id: > 0 }, Dore);
    }

    // Built, charged with two bars of dore, its acid and its water poured in, its copper plate hung and its fire lit
    // under a chimney: twenty hours part both bars, the gold whole (40 gold bits) and the silver at 95 % (5 silver bits
    // and 0.7 carried); the acid down by 40 %, the water spent, the copper worn by the silver.
    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task Acid_parting_builds_parts_dore_into_gold_and_silver_and_breaks_back()
    {
        var hand = await CutterHand();
        var player = hand.Player;
        var pos = await CutterSite(-940, 940, reach: 6);
        var sapi = (ICoreServerAPI)World.Api;
        int size = GlobalConstants.ChunkSize;
        for (int cx = (pos.X - 6) / size; cx <= (pos.X + 6) / size; cx++)
            for (int cz = (pos.Z - 6) / size; cz <= (pos.Z + 6) / size; cz++)
                sapi.WorldManager.LoadChunkColumnPriority(cx, cz, new ChunkLoadOptions { KeepLoaded = true });

        var vessel = await PlaceVessel(pos);
        var rig = vessel.VesselRig!;
        var hood = vessel.CellPos(rig.HoodCell);

        // out of order: the lining before the brickwork; then each stage in turn with real items
        Assert.Equal(4, CutterClick(player, pos, CutterItem("game:refractorybrick-fired-tier3", 4))?.StackSize);
        Assert.Equal(0, vessel.Assembly!.Build.StagesDone);
        Assert.Equal(60, CutterClick(player, hood, CutterItem("game:burnedbrick-red", 64))?.StackSize);
        Assert.Equal("red", vessel.BrickType);
        Assert.Null(CutterClick(player, pos, CutterItem("game:refractorybrick-fired-tier3", 4)));
        Assert.Null(CutterClick(player, pos, CutterItem("game:clay-fire", 2)));
        Assert.Null(CutterClick(player, pos, CutterItem("game:sand-granite", 1)));
        Assert.Null(CutterClick(player, pos, CutterItem("game:metalnailsandstrips-iron", 5)));
        Assert.True(vessel.ShowsPart("sandbath") && vessel.ShowsPart("damperopen") && !vessel.ShowsPart("damperhalf"));
        Assert.Null(CutterClick(player, pos, CutterItem("game:crucible-blue-fired", 1)));
        Assert.Null(CutterClick(player, pos, CutterItem("game:metalnailsandstrips-iron", 1)));
        Assert.Null(CutterClick(player, pos, CutterItem("game:plank-oak", 1)));
        Assert.False(vessel.Built);
        Assert.Null(CutterClick(player, pos, CutterItem("seraphhorizons:pipesection-lead", 1)));
        Assert.True(vessel.Built);
        Assert.True(vessel.ShowsPart("services") && !vessel.ShowsPart("copper"));
        await World.Until(() => vessel.State == MachineState.Worn, 3000);

        // the copper plate in the tub: the wearing part, hung by right-click
        Assert.Null(VesselClick(player, vessel, rig.Tub, CutterItem(AcidPartingRules.CopperPlate, 1)));
        Assert.Equal(680, vessel.CopperLeft, 6);
        Assert.True(vessel.ShowsPart("copper"));

        // two bars of dore of three; the acid, 4 L of a bucket's 10; a bucket of wash water and another
        Assert.Equal(1, VesselClick(player, vessel, rig.Vessel, CutterItem(Dore, 3))?.StackSize);
        Assert.Equal(2, vessel.Bars);
        Assert.Equal(VesselPhase.Charged, vessel.Phase);
        Assert.True(vessel.ShowsPart("charge") && !vessel.ShowsPart("acid"));
        Assert.Equal(6, BucketLitres(VesselClick(player, vessel, rig.Vessel, AcidBucket(10))), 6);
        Assert.Equal(4, vessel.Acid, 6);
        Assert.True(vessel.ShowsPart("acid"));
        Assert.Equal(0, BucketLitres(VesselClick(player, vessel, rig.Vessel, WaterBucket(10))), 6);
        Assert.Equal(0, BucketLitres(VesselClick(player, vessel, rig.Vessel, WaterBucket(10))), 6);
        Assert.Equal(20, vessel.Water.Litres, 6);

        // fuel at the fire door: six charcoal, 24 hours; lit as the forge is
        Assert.Null(VesselClick(player, vessel, rig.FireDoor with { Z = 0.02f }, CutterItem("game:charcoal", 6)));
        Assert.Equal(6, vessel.FuelSlot.StackSize);
        Assert.True(vessel.ShowsPart("fuel") && !vessel.ShowsPart("fire"));
        var handling = EnumHandling.PassThrough;
        OreMachineFire.IgniteOver(player.Entity, pos, 1.5f, ref handling);
        Assert.True(vessel.Lit);
        Assert.Equal(5, vessel.FuelSlot.StackSize);
        Assert.True(vessel.ShowsPart("fire") && !vessel.ShowsPart("fuel"));

        // no chimney: nothing parts, no fuel burns
        vessel.Age(5);
        await World.Ticks(10);
        Assert.Equal(VesselPhase.Charged, vessel.Phase);
        Assert.Equal(4, vessel.FireLeft, 6);

        // a chimney of six fire brick blocks and a top on the outlet
        var b = vessel.CellPos(rig.ChimneyBase);
        for (int y = 0; y < 8; y++)
            World.SetBlock(y < 6 ? "game:claybricks-good-fire" : y == 6 ? "game:claybrickchimney-four-red-ns" : "game:air", b.AddCopy(0, y, 0));
        await World.Until(() => vessel.Row.Works, 5000);
        Assert.Equal(1, vessel.Draught);

        // the damper on the ash mouth: half, shut, open again
        Assert.Null(VesselClick(player, vessel, rig.Ash with { Z = 0.02f }, null));
        Assert.Equal(Damper.Half, vessel.Damper);
        Assert.True(vessel.ShowsPart("damperhalf"));
        Assert.Null(VesselClick(player, vessel, rig.Ash with { Z = 0.02f }, null));
        Assert.Null(VesselClick(player, vessel, rig.Ash with { Z = 0.02f }, null));
        Assert.Equal(Damper.Open, vessel.Damper);

        // two hours: it starts (the bars dissolve) and parts 20 units
        vessel.Age(2);
        await World.Until(() => vessel.Parted >= 19.9, 3000);
        Assert.Equal(VesselPhase.Parting, vessel.Phase);
        Assert.Equal(200, vessel.ChargeUnits, 6);
        Assert.Equal(0, vessel.Bars);
        Assert.True(vessel.ShowsPart("solution") && vessel.ShowsPart("liquor") && vessel.ShowsPart("gold") && !vessel.ShowsPart("charge"));
        // nothing more goes in while it parts, and the gold stays in it
        Assert.Equal(1, VesselClick(player, vessel, rig.Vessel, CutterItem(Dore, 1))?.StackSize);
        // the silver comes down in the tub as it parts, a bit for each 5 units of it
        int expected = (int)Math.Floor(vessel.Parted * 0.15 * 0.95 / 5 + 1e-9);
        Assert.InRange(vessel.SilverBits, expected - 1, expected + 1);

        // eighteen more: the charge parted, five charcoal burned
        vessel.Age(18);
        await World.Until(() => vessel.Phase == VesselPhase.Parted, 3000);
        Assert.Equal(40, vessel.GoldBits);
        Assert.Equal(5, vessel.SilverBits);
        Assert.Equal(4 - 0.4 * 4, vessel.Acid, 6);
        Assert.Equal(0, vessel.Water.Litres, 6);
        Assert.Equal(680 - 200 * 0.15 * 0.95, vessel.CopperLeft, 6);
        Assert.True(vessel.ShowsPart("gold") && !vessel.ShowsPart("solution") && !vessel.ShowsPart("wash"));

        // the silver at the tub, then the gold at the vessel; the vessel then empty, its weak acid kept
        var silver = VesselClick(player, vessel, rig.Tub, null);
        Assert.Equal(("game:metalbit-silver", 5), (silver?.Collectible.Code.ToString(), silver?.StackSize));
        var gold = VesselClick(player, vessel, rig.Vessel, null);
        Assert.Equal(("game:metalbit-gold", 40), (gold?.Collectible.Code.ToString(), gold?.StackSize));
        Assert.Equal(VesselPhase.Empty, vessel.Phase);
        Assert.Equal(2.4, vessel.Acid, 6);
        Assert.True(vessel.ShowsPart("acid"));

        // capped with an iron plate; the plate back from the outlet's top
        var outlet = vessel.CellPos(rig.OutletCell);
        Assert.Null(CutterClick(player, outlet, CutterItem(rig.CapCode, 1)));
        Assert.True(vessel.Capped && vessel.ShowsPart("outletcap"));
        Assert.Equal(rig.CapCode, CutterClick(player, outlet, null)?.Collectible.Code.ToString());
        Assert.False(vessel.Capped);
        Assert.Null(CutterClick(player, outlet, CutterItem(rig.CapCode, 1)));

        // broken: the frame, every stage's items, the copper plate (use left) and the cap
        var drops = W.BlockAccessor.GetBlock(pos).GetDrops(W, pos, player);
        Assert.Contains(drops, d => d.Collectible.Code.ToString() == VesselFrame + "north");
        Assert.Equal(4, drops.Where(d => d.Collectible.Code.ToString() == "game:burnedbrick-red").Sum(d => d.StackSize));
        Assert.Equal(4, drops.Where(d => d.Collectible.Code.ToString() == "game:refractorybrick-fired-tier3").Sum(d => d.StackSize));
        Assert.Equal(1, drops.Where(d => d.Collectible.Code.ToString() == "game:crucible-blue-fired").Sum(d => d.StackSize));
        Assert.Equal(1, drops.Where(d => d.Collectible.Code.ToString() == "seraphhorizons:pipesection-lead").Sum(d => d.StackSize));
        Assert.Equal(1, drops.Where(d => d.Collectible.Code.ToString() == AcidPartingRules.CopperPlate).Sum(d => d.StackSize));
        Assert.Equal(1, drops.Where(d => d.Collectible.Code.ToString() == rig.CapCode).Sum(d => d.StackSize));
        for (int y = 0; y < 8; y++)
            W.BlockAccessor.SetBlock(0, b.AddCopy(0, y, 0));
        W.BlockAccessor.BreakBlock(pos, null);
    }
}
