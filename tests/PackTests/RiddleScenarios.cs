using Atlas.XUnit;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Riddle;
using SeraphHorizons.Mod.Riddle.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.PackTests;

// seraphhorizons, Riddle (mods-src/seraphhorizons/Riddle/, #714): the riddle in the plain world, worked by hand.
// The player's right-click held on it is played through the block's own interaction calls (start, a step each
// tick, stop), as the game makes them (the squaring shear's ShearHold), so W moves only while held; a charge
// takes 9 seconds of holding, so the scenario holds for less and finishes it through BERiddle.Shake. Every
// scenario builds on a granite floor of its own 40 above spawn at x 840 to 876, z -840 (clear of the other
// machines' sites), and shares the gear cutter's player, gearcutterhand.
public partial class SharedWorldScenarios
{
    private RiddleSystem RiddleMod => RiddleSystem.Of(World.Api);

    private const string RiddleFrame = "seraphhorizons:riddle-frame-north";
    private const string RiddleCrushed = "seraphhorizons:crushedore-hematite-medium";
    private const string RiddleMesh = "game:metalnailsandstrips-iron";

    private async Task<BERiddle> PlaceRiddle(BlockPos pos, string side = "north")
    {
        World.SetBlock($"seraphhorizons:riddle-frame-{side}", pos);
        await World.Ticks(5);
        return W.BlockAccessor.GetBlockEntity(pos) as BERiddle ?? throw new Xunit.Sdk.XunitException($"no riddle at {pos}");
    }

    private static string RiddleInfo(BERiddle riddle, IPlayer player)
    {
        var sb = new System.Text.StringBuilder();
        riddle.GetBlockInfo(player, sb);
        return sb.ToString();
    }

    /// <summary>The stacks of <paramref name="code"/> the player holds, as (count, classified).</summary>
    private static List<(int Count, bool Classified)> RiddleHeld(IPlayer player, string code) => player.InventoryManager.Inventories.Values
        .Where(inv => inv.ClassName is GlobalConstants.hotBarInvClassName or GlobalConstants.backpackInvClassName)
        .SelectMany(inv => inv).Where(s => s.Itemstack?.Collectible.Code.ToString() == code)
        .Select(s => (s.StackSize, global::SeraphHorizons.Mod.Riddle.RiddleOre.IsClassified(s.Itemstack))).ToList();

    private static void RiddleClearInventory(IPlayer player)
    {
        foreach (var inv in player.InventoryManager.Inventories.Values
                     .Where(inv => inv.ClassName is GlobalConstants.hotBarInvClassName or GlobalConstants.backpackInvClassName))
            foreach (var slot in inv)
            {
                slot.Itemstack = null;
                slot.MarkDirty();
            }
    }

    [AtlasScenario]
    public void Riddle_loads_with_its_block_recipe_and_rig()
    {
        Assert.True(RiddleSystem.Applies(World.Api));
        var rig = RiddleMod.Rig ?? throw new Xunit.Sdk.XunitException("the riddle's rig did not load");
        foreach (var side in new[] { "north", "east", "south", "west" })
            Assert.IsType<BlockRiddle>(W.GetBlock(new AssetLocation($"seraphhorizons:riddle-frame-{side}")));
        Assert.Equal("Riddle box", new ItemStack(W.GetBlock(BlockRiddle.ItemCode)).GetName());
        foreach (var code in RiddleParts.MeshCodes)
            Assert.True(W.GetItem(new AssetLocation(code)) is { Id: > 0, IsMissing: false }, $"no {code}");
        var frame = Assert.Single(W.GridRecipes, r => r.Output?.Code?.ToString() == RiddleFrame && r.Enabled);
        Assert.Contains(frame.ResolvedIngredients!, i => i?.Code?.ToString() == "game:plank-oak");
        Assert.Contains(frame.ResolvedIngredients!, i => i?.Code?.Path == "metalnailsandstrips-*" && i.AllowedVariants!.Contains("iron"));
        Assert.Contains(frame.ResolvedIngredients!, i => i?.IsTool == true && i.Code?.Path == "hammer-*");
        Assert.Equal(rig.LumpsPerCharge, RiddleMod.Config.LumpsPerCharge);
        Assert.Equal(rig.ShakesPerCharge, RiddleMod.Config.ShakesPerCharge, 3);
    }

    // The whole round: no charge before the mesh, the mesh fitted with two nails and strips, a charge of three
    // crushed ore riddled by holding right-click (W moves only while held), then finished: two classified crushed
    // ore in the box and one raw ore of the granite under it on the mesh. The next charge waits for both to come
    // out, the oversize first; a classified charge is refused; breaking returns the frame and the mesh.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Riddle_riddles_a_charge_into_classified_fines_and_raw_oversize()
    {
        var pos = await CutterSite(840, -840);
        var player = await CutterPlayer();
        RiddleClearInventory(player);
        var riddle = await PlaceRiddle(pos, "east");
        Assert.Equal(Side.East, riddle.Side);
        Assert.Contains("Next part", RiddleInfo(riddle, player));

        // no charge on a bare frame, and one nails and strips is too few for the mesh
        Assert.Equal(3, CutterClick(player, pos, CutterItem(RiddleCrushed, 3))?.StackSize);
        Assert.False(riddle.Riddling);
        Assert.Equal(1, CutterClick(player, pos, CutterItem(RiddleMesh, 1))?.StackSize);
        Assert.False(riddle.Complete);
        Assert.Equal(1, CutterClick(player, pos, CutterItem(RiddleMesh, 3))?.StackSize);
        Assert.True(riddle.Complete);

        // two crushed ore are not a charge; three are, from a stack of five
        Assert.Equal(2, CutterClick(player, pos, CutterItem(RiddleCrushed, 2))?.StackSize);
        Assert.False(riddle.Riddling);
        Assert.Equal(2, CutterClick(player, pos, CutterItem(RiddleCrushed, 5))?.StackSize);
        Assert.True(riddle.Riddling);
        Assert.Equal(3, riddle.Charge!.StackSize);
        Assert.True(riddle.BoxHoldsFines);

        // held, W moves; let go, it stops
        var (started, steps) = await ShearHold(player, pos, 20);
        Assert.True(started);
        Assert.True(steps >= 15, $"held for only {steps} steps");
        await World.Ticks(2);
        double work = riddle.Job.Work;
        Assert.InRange(work, 0.03, 0.3);
        await World.Ticks(20);
        Assert.Equal(work, riddle.Job.Work);
        Assert.Contains("riddled", RiddleInfo(riddle, player));

        // finish it: two fines in the box, classified, and the oversize on the mesh as raw ore of the floor's rock
        Assert.True(riddle.Shake(2 * Math.PI * RiddleMod.Config.ShakesPerCharge));
        Assert.False(riddle.Riddling);
        Assert.NotNull(riddle.Fines);
        Assert.Equal((RiddleCrushed, 2, true), (riddle.Fines!.Collectible.Code.ToString(), riddle.Fines.StackSize, global::SeraphHorizons.Mod.Riddle.RiddleOre.IsClassified(riddle.Fines)));
        Assert.Equal(("game:ore-medium-hematite-granite", 1), (riddle.Oversize!.Collectible.Code.ToString(), riddle.Oversize.StackSize));
        Assert.True(riddle.ChargeOn);
        Assert.Equal(1, riddle.RiddleWork);

        // the next charge waits: the oversize first, then the fines
        Assert.Equal(3, CutterClick(player, pos, CutterItem(RiddleCrushed, 3))?.StackSize);
        CutterClick(player, pos, null);
        Assert.Null(riddle.Oversize);
        Assert.Equal([(1, false)], RiddleHeld(player, "game:ore-medium-hematite-granite"));
        Assert.Equal(3, CutterClick(player, pos, CutterItem(RiddleCrushed, 3))?.StackSize);
        Assert.NotNull(riddle.Fines);
        CutterClick(player, pos, null);
        Assert.Null(riddle.Fines);
        Assert.Contains((2, true), RiddleHeld(player, RiddleCrushed));
        Assert.False(riddle.BoxHoldsFines);

        // classified crushed ore is not riddled again
        var classified = CutterItem(RiddleCrushed, 3);
        global::SeraphHorizons.Mod.Riddle.RiddleOre.MarkClassified(classified);
        Assert.Equal(3, CutterClick(player, pos, classified)?.StackSize);
        Assert.False(riddle.Riddling);

        // a charge nothing has been done to comes back with Ctrl
        Assert.Null(CutterClick(player, pos, CutterItem(RiddleCrushed, 3)));
        Assert.True(riddle.Job.Untouched);
        CutterClick(player, pos, null, ctrl: true);
        Assert.False(riddle.Riddling);

        // broken with a charge on: the frame, the mesh and the charge drop
        Assert.Null(CutterClick(player, pos, CutterItem(RiddleCrushed, 3)));
        CutterKillItems(pos);
        W.BlockAccessor.GetBlock(pos).OnBlockBroken(W, pos, player);
        await World.Ticks(2);
        Assert.Equal(0, W.BlockAccessor.GetBlock(pos).Id);
        var near = CutterItemsNear(pos);
        Assert.Equal(1, near.GetValueOrDefault(RiddleFrame));
        Assert.Equal(2, near.GetValueOrDefault(RiddleMesh));
        Assert.Equal(3, near.GetValueOrDefault(RiddleCrushed));
        CutterKillItems(pos);
        RiddleClearInventory(player);
    }
}
