using Atlas.XUnit;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.CupellationFurnace;
using SeraphHorizons.Mod.CupellationFurnace.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

// seraphhorizons, CupellationFurnace (#740) on the ore machines' shared game code (Machines/Game, #711): the cupellation
// furnace in the pack's world, placed, built stage by stage with real items (its brickwork all of one fired brick), its
// test beaten in with bone meal, its fire lit on coal, charged with silver-lead, rich lead and argentiferous copper
// with lead, worked under a chimney (aged rather than waited on, its blast given: the world has no engine air blower to
// hand), its silver, copper and litharge taken out, then capped and broken back into everything that went in. Built on
// granite floors 40 above spawn at x 900, z 700; the gear cutter's player.
public partial class MillWorldScenarios
{
    private const string CupelFrame = "seraphhorizons:cupellationfurnace-";

    private async Task<BECupellationFurnace> PlaceCupellationFurnace(BlockPos pos, string side = "south")
    {
        World.SetBlock(CupelFrame + side, pos);
        await World.Ticks(5);
        var be = W.BlockAccessor.GetBlockEntity(pos) as BECupellationFurnace ?? throw new Xunit.Sdk.XunitException($"no cupellation furnace at {pos}");
        be.PlaceCells();
        return be;
    }

    private ItemStack? FurnaceClick(IPlayer player, BlockPos at, ItemStack? held, bool sneak = false)
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
    public void Cupellation_furnace_loads_with_its_frame_build_recipe_and_bullion()
    {
        Assert.True(CupellationFurnaceSystem.Applies(World.Api));
        foreach (var side in new[] { "north", "east", "south", "west" })
            Assert.IsType<BlockMachine>(W.GetBlock(new AssetLocation(CupelFrame + side)));
        var block = (BlockMachine)W.GetBlock(new AssetLocation(CupelFrame + "north"));
        Assert.Equal("cupellationfurnace", block.Machine);
        Assert.NotNull(block.Rig);
        Assert.NotNull(block.Rig!.Anchor("blast"));
        Assert.NotNull(CupellationFurnaceSystem.Of(World.Api).Rig);
        Assert.Equal(CupellationRequires.Extra.Order(), block.ExtraRequires.Order());
        var build = MachinesSystem.Of(World.Api).Build("cupellationfurnace")!;
        Assert.Equal(["brickwork", "lining", "fittings"], build.Stages.Select(s => s.Id).ToArray());
        foreach (var code in build.Stages[0].Materials[0].Codes)
            Assert.True(W.GetItem(new AssetLocation(code)) is { Id: > 0 }, code);
        Assert.True(W.GetItem(new AssetLocation(build.Wearing.Single().Codes.Single())) is { Id: > 0 });
        foreach (var fuel in Cupelling.Fuels)
            Assert.True(W.GetItem(new AssetLocation(fuel)) is { Id: > 0 }, fuel);
        Assert.Equal("Cupellation furnace frame", new ItemStack(block).GetName());
        var recipe = Assert.Single(W.GridRecipes, r => r.Output?.Code?.ToString() == CupelFrame + "north" && r.Enabled);
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.Code?.ToString() == "game:metalnailsandstrips-iron" && i.Quantity == 12);
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.IsTool == true && i.Code?.Path == "hammer-*");
    }

    // Built, its test beaten in, lit on coal, charged and worked under a chimney: silver-lead to its silver at 95 % and
    // 40 litharge, rich lead to all of its silver, argentiferous copper with lead to its copper; capped and uncapped;
    // broken, everything back.
    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task Cupellation_furnace_builds_cupels_bullion_and_breaks_back()
    {
        var hand = await CutterHand();
        var player = hand.Player;
        var pos = await CutterSite(900, 700, reach: 6);
        var sapi = (ICoreServerAPI)World.Api;
        int size = GlobalConstants.ChunkSize;
        for (int cx = (pos.X - 6) / size; cx <= (pos.X + 6) / size; cx++)
            for (int cz = (pos.Z - 6) / size; cz <= (pos.Z + 6) / size; cz++)
                sapi.WorldManager.LoadChunkColumnPriority(cx, cz, new ChunkLoadOptions { KeepLoaded = true });

        var furnace = await PlaceCupellationFurnace(pos);
        var rig = furnace.FurnaceRig!;
        var cell = furnace.CellPos(new Int3(1, 0, 0));
        string silverLead = "seraphhorizons:bullion-galena-lead";

        // the stages, in order: out of order fits nothing; the brickwork all red
        Assert.Equal(12, FurnaceClick(player, pos, CutterItem("game:refractorybrick-fired-tier3", 12))?.StackSize);
        Assert.Equal(47, FurnaceClick(player, cell, CutterItem("game:burnedbrick-red", 64))?.StackSize);
        Assert.Equal("red", furnace.BrickType);
        Assert.Null(FurnaceClick(player, pos, CutterItem("game:refractorybrick-fired-tier3", 12)));
        Assert.Null(FurnaceClick(player, pos, CutterItem("game:clay-fire", 5)));
        Assert.Null(FurnaceClick(player, pos, CutterItem("game:metalplate-iron", 3)));
        Assert.Null(FurnaceClick(player, pos, CutterItem("game:metalplate-copper", 1)));
        Assert.True(furnace.Built);
        Assert.True(furnace.ShowsPart("fittings") && furnace.ShowsPart("damperopen") && !furnace.ShowsPart("test"));

        // no charge before the test
        Assert.Equal(2, FurnaceClick(player, pos, CutterItem(silverLead, 2))?.StackSize);
        Assert.Equal(0, furnace.ChargeUnits);
        Assert.Equal(10, FurnaceClick(player, pos, CutterItem("game:bonemeal", 10))?.StackSize);
        Assert.Null(FurnaceClick(player, pos, CutterItem("game:bonemeal", 48)));
        Assert.True(furnace.TestIn && furnace.ShowsPart("test"));

        // coal at the fire door; nothing to light without it
        Assert.Equal(EnumIgniteState.NotIgnitablePreventDefault, MachineFire.IgniteState(player.Entity, pos, 1.5f));
        Assert.Null(FurnaceClick(player, pos, CutterItem("game:ore-bituminouscoal", 16)));
        Assert.Equal(16, furnace.Coal);
        Assert.Equal(EnumIgniteState.IgniteNow, MachineFire.IgniteState(player.Entity, cell, 1.5f));
        var handling = EnumHandling.PassThrough;
        MachineFire.IgniteOver(player.Entity, cell, 1.5f, ref handling);
        Assert.True(furnace.Lit && furnace.ShowsPart("fire"));

        // tin-lead is not cupelled; two bars of silver-lead fill a charge, the third stays in hand
        Assert.Equal(1, FurnaceClick(player, pos, CutterItem("seraphhorizons:bullion-teallite-tin", 1))?.StackSize);
        Assert.Equal(1, FurnaceClick(player, pos, CutterItem(silverLead, 3))?.StackSize);
        Assert.Equal(200, furnace.ChargeUnits);
        Assert.True(furnace.ShowsPart("lead"));

        // blown, but no chimney: nothing is worked
        furnace.TestBlast = 1;
        furnace.Age(2);
        await World.Ticks(10);
        Assert.Equal(0, furnace.Done, 6);
        Assert.Equal(CupellingRefusal.None, furnace.ChargeRefusal());

        // a chimney of six fire brick blocks and a top on the outlet
        var b = furnace.CellPos(rig.ChimneyBase);
        for (int y = 0; y < 8; y++)
            World.SetBlock(y < 6 ? "game:claybricks-good-fire" : y == 6 ? "game:claybrickchimney-four-red-ns" : "game:air", b.AddCopy(0, y, 0));
        await World.Until(() => furnace.Row.Works, 5000);

        // 200 units of lead at 55 an hour: about 3.6 hours
        furnace.Age(2);
        await World.Until(() => furnace.Done > 0, 3000);
        Assert.True(furnace.Working && furnace.ShowsPart("stream"));
        Assert.Equal(1, FurnaceClick(player, pos, CutterItem(silverLead, 1))?.StackSize);
        furnace.Age(2);
        await World.Until(() => !furnace.SilverCake.Empty, 5000);
        Assert.Equal(0, furnace.ChargeUnits);
        Assert.Equal(40, furnace.Litharge);
        // 6 silver at 95 %: 5.7 units, a bit and a 14 % chance of another
        Assert.InRange(furnace.SilverCake.StackSize, 1, 2);
        Assert.Equal("game:metalbit-silver", furnace.SilverCake.Itemstack!.Collectible.Code.ToString());
        Assert.Equal(14, furnace.Assembly!.Wearing.Fitted("test")!.Value.Left, 6);
        Assert.True(furnace.Coal < 16);

        // the cake first, then the litharge; no charge until the cake is out
        Assert.Equal(1, FurnaceClick(player, pos, CutterItem(silverLead, 1))?.StackSize);
        Assert.Equal("game:metalbit-silver", FurnaceClick(player, pos, null)?.Collectible.Code.ToString());
        var litharge = FurnaceClick(player, pos, null);
        Assert.Equal((OreProducts.LithargeCode, 40), (litharge?.Collectible.Code.ToString(), litharge?.StackSize));

        // rich lead: all of its silver, 20 units, 4 bits
        Assert.Null(FurnaceClick(player, pos, CutterItem(Bullion.RichLeadCode, 2)));
        furnace.Age(4);
        await World.Until(() => !furnace.SilverCake.Empty, 5000);
        Assert.Equal(4, furnace.SilverCake.StackSize);
        Assert.Equal(4, FurnaceClick(player, pos, null)?.StackSize);
        Assert.Equal(40, FurnaceClick(player, pos, null)?.StackSize);

        // argentiferous copper wants its lead: alone it waits; with a lead ingot the copper comes back whole
        Assert.Null(FurnaceClick(player, pos, CutterItem("seraphhorizons:bullion-tetrahedrite-copper", 1)));
        furnace.Age(2);
        await World.Ticks(10);
        Assert.Equal(CupellingRefusal.TooLittleLead, furnace.ChargeRefusal());
        Assert.Equal(0, furnace.Done, 6);
        Assert.Null(FurnaceClick(player, pos, CutterItem(Cupelling.LeadIngot, 1)));
        furnace.Age(3);
        await World.Until(() => !furnace.CopperBits.Empty, 5000);
        Assert.Equal(20, furnace.CopperBits.StackSize);
        Assert.Equal(20, furnace.Litharge);

        // the damper with an empty hand and sneak
        Assert.Null(FurnaceClick(player, pos, null, sneak: true));
        Assert.Equal(Damper.Half, furnace.Damper);
        Assert.True(furnace.ShowsPart("damperhalf") && !furnace.ShowsPart("damperopen"));

        // capped with an iron plate; the plate back from the outlet's top
        var outlet = furnace.CellPos(rig.OutletCell);
        Assert.Null(FurnaceClick(player, pos, CutterItem(rig.CapCode, 1)));
        Assert.True(furnace.Capped && furnace.ShowsPart("outletcap"));
        Assert.Equal(rig.CapCode, FurnaceClick(player, outlet, null)?.Collectible.Code.ToString());
        Assert.False(furnace.Capped);
        Assert.Null(FurnaceClick(player, pos, CutterItem(rig.CapCode, 1)));

        // broken: the frame, every stage's items, the test with use left and the cap
        var drops = W.BlockAccessor.GetBlock(pos).GetDrops(W, pos, player);
        int Count(string code) => drops.Where(d => d.Collectible.Code.ToString() == code).Sum(d => d.StackSize);
        Assert.Contains(drops, d => d.Collectible.Code.ToString() == CupelFrame + "north");
        Assert.Equal(17, Count("game:burnedbrick-red"));
        Assert.Equal(12, Count("game:refractorybrick-fired-tier3"));
        Assert.Equal(5, Count("game:clay-fire"));
        Assert.Equal(4, Count("game:metalplate-iron"));
        Assert.Equal(1, Count("game:metalplate-copper"));
        Assert.Equal(48, Count("game:bonemeal"));
        furnace.TestBlast = null;
        for (int y = 0; y < 8; y++)
            W.BlockAccessor.SetBlock(0, b.AddCopy(0, y, 0));
        W.BlockAccessor.BreakBlock(pos, null);
    }
}
