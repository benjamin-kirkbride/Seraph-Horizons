using Atlas.XUnit;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using SeraphHorizons.Mod.StallRoaster;
using SeraphHorizons.Mod.StallRoaster.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

// seraphhorizons, StallRoaster (#925) on the ore machines' shared game code (Machines/Game, #711): the stall roaster in
// the pack's world, placed, built stage by stage with real items (its brickwork all of one fired brick), its bed laid,
// charged with raw and crushed galena, lit through the shared ore machine fire, walled up, burnt through under a
// chimney (aged rather than waited on), its sulfur taken and its stall-roasted rock raked out, then capped and broken
// back into everything that went in. Built on granite floors 40 above spawn at x 720 to 740, z 660; the gear cutter's
// player.
public partial class SharedWorldScenarios
{
    private const string StallFrame = "seraphhorizons:stallroaster-";

    private async Task<BEStallRoaster> PlaceStall(BlockPos pos, string side = "south")
    {
        World.SetBlock(StallFrame + side, pos);
        await World.Ticks(5);
        var be = W.BlockAccessor.GetBlockEntity(pos) as BEStallRoaster ?? throw new Xunit.Sdk.XunitException($"no stall roaster at {pos}");
        be.PlaceCells();
        return be;
    }

    private ItemStack? StallClick(IPlayer player, BlockPos at, ItemStack? held, bool sneak = false)
    {
        player.Entity.Controls.ShiftKey = sneak;
        try
        {
            return CutterClick(player, at, held);
        }
        finally
        {
            player.Entity.Controls.ShiftKey = false;
        }
    }

    [AtlasScenario]
    public void Stall_roaster_loads_with_its_frame_build_recipe_and_figures()
    {
        Assert.True(StallRoasterSystem.Applies(World.Api));
        foreach (var side in new[] { "north", "east", "south", "west" })
            Assert.IsType<BlockOreMachine>(W.GetBlock(new AssetLocation(StallFrame + side)));
        var block = (BlockOreMachine)W.GetBlock(new AssetLocation(StallFrame + "north"));
        Assert.Equal("stallroaster", block.Machine);
        Assert.NotNull(block.Rig);
        Assert.NotNull(StallRoasterSystem.Of(World.Api).Rig);
        Assert.Equal(StallRequires.Extra(4).Order(), block.ExtraRequires.Order());
        var build = OreMachinesSystem.Of(World.Api).Build("stallroaster")!;
        Assert.Equal(["brickwork", "lining"], build.Stages.Select(s => s.Id).ToArray());
        // every brick type the build takes is a fired brick the game has
        foreach (var code in build.Stages[0].Materials[0].Codes)
            Assert.True(W.GetItem(new AssetLocation(code)) is { Id: > 0 }, code);
        Assert.Equal("Stall roaster frame", new ItemStack(block).GetName());
        var recipe = Assert.Single(W.GridRecipes, r => r.Output?.Code?.ToString() == StallFrame + "north" && r.Enabled);
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.Code?.ToString() == "game:rod-iron" && i.Quantity == 2);
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.IsTool == true && i.Code?.Path == "hammer-*");

        // stall-roasted rock smelts at its grade's share x 92 %, the firepit's at 85 %
        var stall = W.GetItem(new AssetLocation(OreProducts.StallRoastedRockCode("galena", "rich")))!;
        var firepit = W.GetItem(new AssetLocation(OreProducts.RoastedRockCode("galena", "rich")))!;
        // the share of its units an item gives (a metal's ingot is 100 units)
        double Share(Item i) => 100.0 * i.CombustibleProps!.SmeltedStack!.StackSize / i.CombustibleProps.SmeltedRatio / ItemOreProduct.UnitsOf(i);
        Assert.Equal(7, ItemOreProduct.UnitsOf(stall));
        Assert.Equal(0.40 * 0.92, Share(stall), 4);
        Assert.Equal(0.40 * 0.85, Share(firepit), 4);
        Assert.StartsWith("Stall-roasted Galena ore", new ItemStack(stall).GetName(), StringComparison.OrdinalIgnoreCase);
    }

    // Built, bedded, charged with raw and crushed galena, lit, walled up and burnt through under a chimney: every rock
    // one for one into stall-roasted rock, the charge's sulfur as powder in the crust, raked out a course at a time;
    // capped and uncapped; broken, everything back.
    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task Stall_roaster_builds_roasts_sulfide_rock_gives_its_sulfur_and_breaks_back()
    {
        var hand = await CutterHand();
        var player = hand.Player;
        var pos = await CutterSite(720, 660, reach: 6);
        var sapi = (ICoreServerAPI)World.Api;
        int size = GlobalConstants.ChunkSize;
        for (int cx = (pos.X - 6) / size; cx <= (pos.X + 6) / size; cx++)
            for (int cz = (pos.Z - 6) / size; cz <= (pos.Z + 6) / size; cz++)
                sapi.WorldManager.LoadChunkColumnPriority(cx, cz, new ChunkLoadOptions { KeepLoaded = true });

        var stall = await PlaceStall(pos);
        var rig = stall.StallRig!;
        var cell = stall.CellPos(new Int3(1, 0, 0));

        // out of order: the lining before the brickwork
        Assert.Equal(9, StallClick(player, pos, CutterItem("game:refractorybrick-fired-tier3", 9))?.StackSize);
        Assert.True(_cutterHandled);
        Assert.Equal(0, stall.Assembly!.Build.StagesDone);
        // the brickwork: too few in hand fits nothing; a stack fits the seven it takes, and red is the stall's type
        Assert.Equal(3, StallClick(player, pos, CutterItem("game:burnedbrick-red", 3))?.StackSize);
        Assert.Equal(57, StallClick(player, cell, CutterItem("game:burnedbrick-red", 64))?.StackSize);
        Assert.True(stall.Assembly.Build.StageComplete(0));
        Assert.Equal("red", stall.BrickType);
        Assert.Null(StallClick(player, pos, CutterItem("game:refractorybrick-fired-tier3", 9)));
        Assert.Null(StallClick(player, cell, CutterItem("game:clay-fire", 4)));
        Assert.True(stall.Built);
        Assert.True(stall.ShowsPart("brickwork") && stall.ShowsPart("lining") && !stall.ShowsPart("wood"));

        // nothing to light yet
        Assert.Equal(EnumIgniteState.NotIgnitablePreventDefault, OreMachineFire.IgniteState(player.Entity, pos, 1.5f));
        // the bed: four firewood of ten, then no more
        Assert.Equal(6, StallClick(player, pos, CutterItem(StallRoasting.Firewood, 10))?.StackSize);
        Assert.Equal(StallPhase.Bed, stall.Phase);
        Assert.Equal(6, StallClick(player, pos, CutterItem(StallRoasting.Firewood, 6))?.StackSize);
        Assert.True(stall.ShowsPart("wood"));

        // the charge: not an oxide; raw and crushed galena, two stacks; then full
        var hematite = new ItemStack(W.SearchItems(new AssetLocation("game:ore-rich-hematite-*")).First(), 16);
        Assert.Equal(16, StallClick(player, pos, hematite)?.StackSize);
        Assert.Equal(0, stall.Rock);
        var rawGalena = new ItemStack(W.SearchItems(new AssetLocation("game:ore-rich-galena-*")).First(), 16);
        Assert.Null(StallClick(player, pos, rawGalena));
        Assert.Null(StallClick(player, cell, CutterItem(OreProducts.CrushedCode("galena", "rich"), 16)));
        Assert.Equal(32, stall.Rock);
        Assert.Equal(StallPhase.Charged, stall.Phase);
        Assert.Equal(4, StallRoasting.Courses(stall.Rock, rig.PerCourse, rig.Courses));
        Assert.True(stall.ShowsPart("ore4") && !stall.ShowsPart("fire1"));
        Assert.Equal(1, StallClick(player, pos, CutterItem(OreProducts.CrushedCode("galena", "rich"), 1))?.StackSize);

        // lit as the forge is, with a firestarter or a torch held 1.5 s on any of its cells
        Assert.Equal(EnumIgniteState.IgniteNow, OreMachineFire.IgniteState(player.Entity, cell, 1.5f));
        var handling = EnumHandling.PassThrough;
        OreMachineFire.IgniteOver(player.Entity, cell, 1.5f, ref handling);
        Assert.True(stall.Lit);
        Assert.Equal(0, stall.Firewood);
        Assert.Equal(StallPhase.Burning, stall.Phase);
        Assert.True(stall.ShowsPart("fire4") && stall.ShowsPart("kindling") && !stall.ShowsPart("ore1"));

        // no chimney, front open: it does not burn on
        stall.Age(10);
        await World.Ticks(10);
        Assert.Equal(0, stall.BurnedHours, 6);

        // a chimney of six fire brick blocks and a top on the outlet; the front walled up with its own brick
        var b = stall.CellPos(rig.ChimneyBase);
        for (int y = 0; y < 8; y++)
            World.SetBlock(y < 6 ? "game:claybricks-good-fire" : y == 6 ? "game:claybrickchimney-four-red-ns" : "game:air", b.AddCopy(0, y, 0));
        Assert.Equal(1, StallClick(player, pos, CutterItem("game:burnedbrick-cream", 1))?.StackSize);
        Assert.Equal(StallFront.Open, stall.Front);
        Assert.Equal(11, StallClick(player, pos, CutterItem("game:burnedbrick-red", 20))?.StackSize);
        Assert.Equal(StallFront.Half, stall.Front);
        Assert.Equal(2, StallClick(player, pos, CutterItem("game:burnedbrick-red", 11))?.StackSize);
        Assert.Equal(StallFront.Walled, stall.Front);
        Assert.Equal(18, stall.FrontBricks);
        Assert.True(stall.ShowsPart("frontwalled") && !stall.ShowsPart("fronthalf"));
        await World.Until(() => stall.Row.Works, 5000);
        Assert.Equal(1, stall.Draught);

        // burnt through: 39 hours, then the last
        stall.Age(39);
        await World.Until(() => stall.BurnedHours >= 38.9, 3000);
        Assert.False(stall.Roasted);
        stall.Age(2);
        await World.Until(() => stall.Roasted, 3000);
        Assert.Equal(StallPhase.Roasted, stall.Phase);
        string roasted = OreProducts.StallRoastedRockCode("galena", "rich");
        Assert.Equal(32, stall.ChargeSlots.Where(s => s.Itemstack?.Collectible.Code.ToString() == roasted).Sum(s => s.StackSize));
        // a third of 32 rich galena's sulfur: 32 x 7 x 0.155 / 3 = 11.6 units, 2 powders, the rest carried
        Assert.Equal(2, stall.SulfurPowders);
        Assert.True(stall.ShowsPart("roasted4") && stall.ShowsPart("sulfur") && !stall.ShowsPart("fire1"));

        // the sulfur first (into the empty hand), then raking out needs the front open
        var sulfur = StallClick(player, pos, null);
        Assert.Equal((StallRoasting.SulfurPowder, 2), (sulfur?.Collectible.Code.ToString(), sulfur?.StackSize));
        Assert.Equal(0, stall.SulfurPowders);
        Assert.Null(StallClick(player, pos, null));
        Assert.Equal(32, stall.Rock);
        Assert.Null(StallClick(player, pos, null, sneak: true));
        Assert.Null(StallClick(player, pos, null, sneak: true));
        Assert.Equal(StallFront.Open, stall.Front);
        // a course a click, into the hand
        int raked = 0;
        for (int i = 0; i < 4; i++)
        {
            var course = StallClick(player, i % 2 == 0 ? pos : cell, null);
            Assert.Equal(roasted, course?.Collectible.Code.ToString());
            raked += course!.StackSize;
            Assert.Equal(32 - raked, stall.Rock);
        }
        Assert.Equal(32, raked);
        Assert.Equal(0, stall.Rock);
        Assert.Equal(StallPhase.Empty, stall.Phase);
        // walled up again from the bricks it kept
        Assert.Equal(1, StallClick(player, pos, CutterItem("game:burnedbrick-red", 1))?.StackSize);
        Assert.Equal(StallFront.Half, stall.Front);

        // capped with an iron plate; the plate back from the outlet's top
        var outlet = stall.CellPos(rig.OutletCell);
        Assert.Null(StallClick(player, pos, CutterItem(rig.CapCode, 1)));
        Assert.True(stall.Capped && stall.ShowsPart("outletcap"));
        Assert.Equal(rig.CapCode, StallClick(player, outlet, null)?.Collectible.Code.ToString());
        Assert.False(stall.Capped);
        Assert.Null(StallClick(player, pos, CutterItem(rig.CapCode, 1)));

        // broken: the frame, every stage's items, the front's 18 bricks and the cap
        var drops = W.BlockAccessor.GetBlock(pos).GetDrops(W, pos, player);
        Assert.Contains(drops, d => d.Collectible.Code.ToString() == StallFrame + "north");
        Assert.Equal(25, drops.Where(d => d.Collectible.Code.ToString() == "game:burnedbrick-red").Sum(d => d.StackSize));
        Assert.Equal(9, drops.Where(d => d.Collectible.Code.ToString() == "game:refractorybrick-fired-tier3").Sum(d => d.StackSize));
        Assert.Equal(1, drops.Where(d => d.Collectible.Code.ToString() == rig.CapCode).Sum(d => d.StackSize));
        for (int y = 0; y < 8; y++)
            W.BlockAccessor.SetBlock(0, b.AddCopy(0, y, 0));
        W.BlockAccessor.BreakBlock(pos, null);
    }
}
