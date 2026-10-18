using Atlas.XUnit;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.LiquationFurnace;
using SeraphHorizons.Mod.LiquationFurnace.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

// seraphhorizons, LiquationFurnace (#929) on the ore machines' shared game code (Machines/Game, #711): the liquation
// furnace in the pack's world, placed, built stage by stage with real items (its brickwork all of one fired brick, then
// the lining and the fittings), charged with teallite's tin-lead bars, stoked through the open fire door, lit through the
// shared ore machine fire, its damper and fire door worked from the back, sweated under a chimney (aged rather than
// waited on), its tin and lead taken out, then capped and broken back into everything that went in. Built on granite
// floors 40 above spawn at x 780, z -780; the gear cutter's player.
public partial class SharedWorldScenarios
{
    private const string LiquationFrame = "seraphhorizons:liquationfurnace-";
    private const string TealliteBar = "seraphhorizons:bullion-teallite-tin";

    private async Task<BELiquationFurnace> PlaceLiquationFurnace(BlockPos pos, string side = "south")
    {
        World.SetBlock(LiquationFrame + side, pos);
        await World.Ticks(5);
        var be = W.BlockAccessor.GetBlockEntity(pos) as BELiquationFurnace ?? throw new Xunit.Sdk.XunitException($"no liquation furnace at {pos}");
        be.PlaceCells();
        return be;
    }

    /// <summary>An empty-hand click on the furnace's back face at <paramref name="y"/> (blocks up from the ground, in
    /// the controller's row): the fire door above, the ash mouth's damper below.</summary>
    private bool LiquationBackClick(IPlayer player, BELiquationFurnace furnace, double y)
    {
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = null;
        slot.MarkDirty();
        var back = furnace.FurnaceRig!.BackSide;
        var cell = furnace.CellPos(new Int3(0, 0, 1));
        var face = BlockFacing.FromCode(CellFaces.ToWorld((CellFace)(int)back, furnace.Side).Code());
        var sel = new BlockSelection { Position = cell, Face = face, HitPosition = new Vec3d(0.5, y, 0.5) };
        return W.BlockAccessor.GetBlock(cell).OnBlockInteractStart(W, player, sel);
    }

    [AtlasScenario]
    public void Liquation_furnace_loads_with_its_frame_build_recipe_and_figures()
    {
        Assert.True(LiquationFurnaceSystem.Applies(World.Api));
        foreach (var side in new[] { "north", "east", "south", "west" })
            Assert.IsType<BlockOreMachine>(W.GetBlock(new AssetLocation(LiquationFrame + side)));
        var block = (BlockOreMachine)W.GetBlock(new AssetLocation(LiquationFrame + "north"));
        Assert.Equal("liquationfurnace", block.Machine);
        Assert.NotNull(block.Rig);
        Assert.NotNull(LiquationFurnaceSystem.Of(World.Api).Rig);
        Assert.Equal(LiquationRequires.Extra(2).Order(), block.ExtraRequires.Order());
        var build = OreMachinesSystem.Of(World.Api).Build("liquationfurnace")!;
        Assert.Equal(["brickwork", "lining", "fittings"], build.Stages.Select(s => s.Id).ToArray());
        foreach (var code in build.Stages.SelectMany(s => s.Materials).SelectMany(m => m.Codes))
            Assert.True(W.GetItem(new AssetLocation(code)) is { Id: > 0 }, code);
        foreach (var code in LiquationFurnacing.Fuels)
            Assert.True(W.GetItem(new AssetLocation(code)) is { Id: > 0 }, code);
        Assert.Equal("Liquation furnace frame", new ItemStack(block).GetName());
        var recipe = Assert.Single(W.GridRecipes, r => r.Output?.Code?.ToString() == LiquationFrame + "north" && r.Enabled);
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.Code?.ToString() == "game:rod-iron" && i.Quantity == 4);
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.IsTool == true && i.Code?.Path == "hammer-*");
        // the tin-lead bars it takes exist
        Assert.True(W.GetItem(new AssetLocation(TealliteBar)) is { Id: > 0 });
        Assert.True(W.GetItem(new AssetLocation("seraphhorizons:bullion-franckeite-tin")) is { Id: > 0 });
    }

    // Built, charged with four teallite bars, stoked, lit, its door shut and its damper worked, sweated under a chimney:
    // 400 units of tin into the pot and 152 of lead on the plate, taken out as ingots and bits; capped and uncapped;
    // broken, everything back.
    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task Liquation_furnace_builds_sweats_tin_lead_into_tin_and_lead_and_breaks_back()
    {
        var hand = await CutterHand();
        var player = hand.Player;
        var pos = await CutterSite(780, -780, reach: 6);
        var sapi = (ICoreServerAPI)World.Api;
        int size = GlobalConstants.ChunkSize;
        for (int cx = (pos.X - 6) / size; cx <= (pos.X + 6) / size; cx++)
            for (int cz = (pos.Z - 6) / size; cz <= (pos.Z + 6) / size; cz++)
                sapi.WorldManager.LoadChunkColumnPriority(cx, cz, new ChunkLoadOptions { KeepLoaded = true });

        var furnace = await PlaceLiquationFurnace(pos);
        var rig = furnace.FurnaceRig!;
        var cell = furnace.CellPos(new Int3(1, 0, 0));

        // out of order: the fittings before the brickwork
        Assert.Equal(2, CutterClick(player, pos, CutterItem("game:metalplate-iron", 2))?.StackSize);
        Assert.Equal(0, furnace.Assembly!.Build.StagesDone);
        // the brickwork: a stack fits the eight it takes, red the furnace's type; then the lining and the fittings
        Assert.Equal(56, CutterClick(player, cell, CutterItem("game:burnedbrick-red", 64))?.StackSize);
        Assert.Equal("red", furnace.BrickType);
        Assert.Null(CutterClick(player, pos, CutterItem("game:refractorybrick-fired-tier3", 7)));
        Assert.Null(CutterClick(player, cell, CutterItem("game:clay-fire", 3)));
        Assert.False(furnace.Built);
        Assert.Null(CutterClick(player, pos, CutterItem("game:metalplate-iron", 2)));
        Assert.Null(CutterClick(player, pos, CutterItem("game:metalnailsandstrips-iron", 1)));
        Assert.True(furnace.Built);
        Assert.True(furnace.ShowsPart("fittings") && furnace.ShowsPart("damperopen") && !furnace.ShowsPart("cakes"));

        // the charge: not galena's silver-lead; four teallite bars of six
        Assert.Equal(2, CutterClick(player, pos, CutterItem("seraphhorizons:bullion-galena-lead", 2))?.StackSize);
        Assert.Equal(0, furnace.Bars);
        Assert.Equal(2, CutterClick(player, pos, CutterItem(TealliteBar, 6))?.StackSize);
        Assert.Equal(4, furnace.Bars);
        Assert.Equal(LiquationPhase.Charged, furnace.Phase);
        Assert.True(furnace.ShowsPart("cakes"));

        // nothing to light yet; then coke through the open door, eight of ten
        Assert.Equal(EnumIgniteState.NotIgnitablePreventDefault, OreMachineFire.IgniteState(player.Entity, pos, 1.5f));
        Assert.Equal(2, CutterClick(player, pos, CutterItem("game:coke", 10))?.StackSize);
        Assert.Equal(8, furnace.Fuel);
        Assert.True(furnace.ShowsPart("fuel2") && !furnace.ShowsPart("fire1"));

        // the fire door shut and opened again, the damper cycled round, from the back
        Assert.True(LiquationBackClick(player, furnace, 0.65));
        Assert.True(furnace.DoorShut);
        Assert.Equal(1, furnace.PartDepth);
        Assert.Equal(4, CutterClick(player, pos, CutterItem("game:coke", 4))?.StackSize);
        Assert.True(LiquationBackClick(player, furnace, 0.65));
        Assert.False(furnace.DoorShut);
        for (int i = 0; i < 3; i++)
            Assert.True(LiquationBackClick(player, furnace, 0.2));
        Assert.Equal(Damper.Open, furnace.Damper);

        // lit as the forge is, with a firestarter or a torch held 1.5 s on any of its cells
        Assert.Equal(EnumIgniteState.IgniteNow, OreMachineFire.IgniteState(player.Entity, cell, 1.5f));
        var handling = EnumHandling.PassThrough;
        OreMachineFire.IgniteOver(player.Entity, cell, 1.5f, ref handling);
        Assert.True(furnace.Lit);
        Assert.True(furnace.ShowsPart("fire2") && !furnace.ShowsPart("fuel1"));

        // no chimney: nothing sweats, though the fire burns
        furnace.Age(3);
        await World.Until(() => furnace.Fuel < 8, 3000);
        Assert.Equal(0, furnace.SweatedUnits, 6);

        // a chimney of six fire brick blocks and a top on the outlet
        var b = furnace.CellPos(rig.ChimneyBase);
        for (int y = 0; y < 8; y++)
            World.SetBlock(y < 6 ? "game:claybricks-good-fire" : y == 6 ? "game:claybrickchimney-four-red-ns" : "game:air", b.AddCopy(0, y, 0));
        await World.Until(() => furnace.Row.Works, 5000);

        // the door open: half the pace; 2 hours sweat 100 units
        Assert.Equal(0.5, furnace.Pace, 6);
        furnace.Age(2);
        await World.Until(() => furnace.SweatedUnits >= 99, 3000);
        Assert.True(furnace.Sweating && furnace.ShowsPart("tinrun"));
        // shut, the full pace: 3 more hours make the charge's 400
        Assert.True(LiquationBackClick(player, furnace, 0.65));
        Assert.Equal(1, furnace.Pace, 6);
        furnace.Age(3.1);
        await World.Until(() => furnace.Phase == LiquationPhase.Sweated, 3000);
        Assert.Equal(0, furnace.Bars);
        Assert.Equal(400, furnace.TinUnits, 6);
        Assert.Equal(4 * 100 * 0.40 * 0.95, furnace.LeadUnits, 6);
        Assert.True(furnace.ShowsPart("tin") && furnace.ShowsPart("lead") && !furnace.ShowsPart("cakes"));
        // no charge on a sweated plate
        Assert.Equal(2, CutterClick(player, pos, CutterItem(TealliteBar, 2))?.StackSize);

        // the tin first, four ingots; then the lead, an ingot and ten bits, 2 units carried
        var tin = CutterClick(player, pos, null);
        Assert.Equal(("game:ingot-tin", 4), (tin?.Collectible.Code.ToString(), tin?.StackSize));
        Assert.Equal(0, furnace.TinUnits, 6);
        var leadIngot = CutterClick(player, pos, null);
        Assert.Equal(("game:ingot-lead", 1), (leadIngot?.Collectible.Code.ToString(), leadIngot?.StackSize));
        Assert.Equal(2, furnace.LeadUnits, 6);
        Assert.Equal(10, player.InventoryManager.Inventories.Values.Where(i => i.ClassName is "hotbar" or "backpack").SelectMany(i => i)
            .Where(s => s.Itemstack?.Collectible.Code.ToString() == "game:metalbit-lead").Sum(s => s.StackSize));
        Assert.Equal(LiquationPhase.Empty, furnace.Phase);

        // capped with an iron plate; the plate back from the outlet's top
        var outlet = furnace.CellPos(rig.OutletCell);
        Assert.Null(CutterClick(player, pos, CutterItem(rig.CapCode, 1)));
        Assert.True(furnace.Capped && furnace.ShowsPart("outletcap"));
        Assert.Equal(rig.CapCode, CutterClick(player, outlet, null)?.Collectible.Code.ToString());
        Assert.False(furnace.Capped);

        // broken: the frame, every stage's items, the fuel left
        var drops = W.BlockAccessor.GetBlock(pos).GetDrops(W, pos, player);
        Assert.Contains(drops, d => d.Collectible.Code.ToString() == LiquationFrame + "north");
        Assert.Equal(8, drops.Where(d => d.Collectible.Code.ToString() == "game:burnedbrick-red").Sum(d => d.StackSize));
        Assert.Equal(7, drops.Where(d => d.Collectible.Code.ToString() == "game:refractorybrick-fired-tier3").Sum(d => d.StackSize));
        Assert.Equal(2, drops.Where(d => d.Collectible.Code.ToString() == "game:metalplate-iron").Sum(d => d.StackSize));
        for (int y = 0; y < 8; y++)
            W.BlockAccessor.SetBlock(0, b.AddCopy(0, y, 0));
        W.BlockAccessor.BreakBlock(pos, null);
    }
}
