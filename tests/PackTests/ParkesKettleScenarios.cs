using Atlas.XUnit;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using SeraphHorizons.Mod.ParkesKettle;
using SeraphHorizons.Mod.ParkesKettle.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.PackTests;

// seraphhorizons, ParkesKettle (#927) on the ore machines' shared game code (Machines/Game, #711): the Parkes kettle in
// the pack's world, placed, built stage by stage with real items (its brickwork all of one fired brick), charged with
// plain galena's silver-lead and zinc, its grate laid and lit through the shared ore machine fire under a chimney,
// stirred by a vanilla creative rotor on its power face (its load following the charge), stirred through, its crust
// skimmed and its lead ladled out, then capped and broken back into everything that went in. Built on granite floors
// 40 above spawn at x -900, z 920; the gear cutter's player.
public partial class SharedWorldScenarios
{
    private const string KettleFrame = "seraphhorizons:parkeskettle-";
    private const string GalenaBullion = "seraphhorizons:bullion-galena-lead";

    private async Task<BEParkesKettle> PlaceKettle(BlockPos pos, string side = "south")
    {
        World.SetBlock(KettleFrame + side, pos);
        await World.Ticks(5);
        var be = W.BlockAccessor.GetBlockEntity(pos) as BEParkesKettle ?? throw new Xunit.Sdk.XunitException($"no Parkes kettle at {pos}");
        be.PlaceCells();
        return be;
    }

    private ItemStack? KettleClick(IPlayer player, BlockPos at, ItemStack? held, bool sneak = false)
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

    private static int KettleHeld(IPlayer player, string code) =>
        new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName }
            .SelectMany(inv => player.InventoryManager.GetOwnInventory(inv) ?? Enumerable.Empty<ItemSlot>())
            .Where(s => s.Itemstack?.Collectible.Code.ToString() == code).Sum(s => s.StackSize);

    [AtlasScenario]
    public void Parkes_kettle_loads_with_its_frame_build_recipe_and_figures()
    {
        Assert.True(ParkesKettleSystem.Applies(World.Api));
        foreach (var side in new[] { "north", "east", "south", "west" })
            Assert.IsType<BlockMachine>(W.GetBlock(new AssetLocation(KettleFrame + side)));
        var block = (BlockMachine)W.GetBlock(new AssetLocation(KettleFrame + "north"));
        Assert.Equal("parkeskettle", block.Machine);
        Assert.NotNull(block.Rig);
        Assert.NotNull(ParkesKettleSystem.Of(World.Api).Rig);
        Assert.Equal(KettleRequires.Extra(4, 2).Order(), block.ExtraRequires.Order());
        var build = MachinesSystem.Of(World.Api).Build("parkeskettle")!;
        Assert.Equal(["brickwork", "lining", "kettle", "stirrer"], build.Stages.Select(s => s.Id).ToArray());
        foreach (var stage in build.Stages)
            foreach (var code in stage.Materials.SelectMany(m => m.Codes))
                Assert.True(W.GetItem(new AssetLocation(code)) is { Id: > 0 }, code);
        Assert.Equal("Parkes kettle frame", new ItemStack(block).GetName());
        var recipe = Assert.Single(W.GridRecipes, r => r.Output?.Code?.ToString() == KettleFrame + "north" && r.Enabled);
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.Code?.ToString() == "game:metalnailsandstrips-iron" && i.Quantity == 10);
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.IsTool == true && i.Code?.Path == "hammer-*");
        // what it takes and gives exists
        foreach (var code in new[] { GalenaBullion, Bullion.RichLeadCode, ParkesKettling.ZincBit, ParkesKettling.Lead })
            Assert.True(W.GetItem(new AssetLocation(code)) is { Id: > 0 }, code);
        foreach (var code in ParkesKettling.Fuels)
            Assert.True(W.GetItem(new AssetLocation(code)) is { Id: > 0 }, code);
    }

    // Built, charged with galena's silver-lead and zinc, its grate laid and lit under a chimney, stirred by a rotor, then
    // stirred through: the silver whole in rich lead skimmed with the crust and 80 % of the zinc, the rest of the lead
    // ladled out; the fire burns its coal by the hour; capped and uncapped; broken, everything back.
    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task Parkes_kettle_builds_desilverizes_galena_lead_and_breaks_back()
    {
        var hand = await CutterHand();
        var player = hand.Player;
        var pos = await CutterSite(-900, 920, reach: 6);
        var sapi = (ICoreServerAPI)World.Api;
        int size = GlobalConstants.ChunkSize;
        for (int cx = (pos.X - 6) / size; cx <= (pos.X + 6) / size; cx++)
            for (int cz = (pos.Z - 6) / size; cz <= (pos.Z + 6) / size; cz++)
                sapi.WorldManager.LoadChunkColumnPriority(cx, cz, new ChunkLoadOptions { KeepLoaded = true });

        var kettle = await PlaceKettle(pos);
        var rig = kettle.KettleRig!;
        var cell = kettle.CellPos(new Int3(1, 0, 0));
        var recovery = OreProcessingSystem.RecoveryFor(World.Api);

        // out of order: the kettle's plates before the brickwork; then every stage with real items
        Assert.Equal(6, KettleClick(player, pos, CutterItem("game:metalplate-iron", 6))?.StackSize);
        Assert.Equal(0, kettle.Assembly!.Build.StagesDone);
        Assert.Equal(44, KettleClick(player, cell, CutterItem("game:burnedbrick-red", 64))?.StackSize);
        Assert.Equal("red", kettle.BrickType);
        int i = 0;
        while (kettle.Assembly.Build.Next is { } _)
        {
            var (material, missing) = kettle.Assembly.Build.NextNeeds().First();
            Assert.True(KettleClick(player, i++ % 2 == 0 ? pos : cell, CutterItem(material.Codes[0], missing)) == null, $"{material.Codes[0]} x{missing} was not fitted");
        }
        Assert.True(kettle.Built);
        Assert.True(kettle.ShowsPart("kettle") && kettle.ShowsPart("stirrer") && kettle.ShowsPart("damperopen") && !kettle.ShowsPart("lead1"));
        // empty, the stirrer's load is its empty figure
        Assert.Equal(recovery.LoadAt(OreMachine.ParkesKettle, 0), kettle.Resistance, 3);

        // nothing to light yet
        Assert.Equal(EnumIgniteState.NotIgnitablePreventDefault, MachineFire.IgniteState(player.Entity, pos, 1.5f));
        // zinc goes in on the lead; argentiferous galena's bullion is refused; galena's, 24 bars of 30
        Assert.Equal(8, KettleClick(player, pos, CutterItem(ParkesKettling.ZincBit, 8))?.StackSize);
        Assert.Equal(4, KettleClick(player, pos, CutterItem("seraphhorizons:bullion-galena_nativesilver-lead", 4))?.StackSize);
        Assert.Equal(0, kettle.LeadUnits);
        Assert.Null(KettleClick(player, pos, CutterItem(GalenaBullion, 12)));
        Assert.Equal(6, KettleClick(player, cell, CutterItem(GalenaBullion, 18))?.StackSize);
        Assert.Equal(2400, kettle.LeadUnits);
        Assert.Equal(KettlePhase.Charging, kettle.Phase);
        Assert.True(kettle.ShowsPart("lead4"));
        // full, its load is the full figure
        Assert.Equal(recovery.LoadAt(OreMachine.ParkesKettle, 1), kettle.Resistance, 3);
        // 1 unit of zinc to 100 of lead: 5 bits of 8
        Assert.Equal(3, KettleClick(player, pos, CutterItem(ParkesKettling.ZincBit, 8))?.StackSize);
        Assert.Equal(KettlePhase.Stirring, kettle.Phase);
        Assert.True(kettle.ShowsPart("zinc"));
        Assert.Equal(1, KettleClick(player, pos, CutterItem(GalenaBullion, 1))?.StackSize);

        // the grate: 8 coal of 10; lit as the forge is
        Assert.Equal(2, KettleClick(player, pos, CutterItem("game:ore-bituminouscoal", 10))?.StackSize);
        Assert.Equal(8, kettle.Coal);
        Assert.True(kettle.ShowsPart("fuel2") && !kettle.ShowsPart("fire1"));
        Assert.Equal(EnumIgniteState.IgniteNow, MachineFire.IgniteState(player.Entity, cell, 1.5f));
        var handling = EnumHandling.PassThrough;
        MachineFire.IgniteOver(player.Entity, cell, 1.5f, ref handling);
        Assert.True(kettle.Lit);
        Assert.True(kettle.ShowsPart("fire2") && !kettle.ShowsPart("fuel1"));
        // no chimney: no heat, nothing to stir
        await World.Ticks(5);
        Assert.Equal(0, kettle.Heat);
        Assert.False(kettle.HasWork);

        // a chimney of six fire brick blocks and a top on the outlet
        var b = kettle.CellPos(rig.ChimneyBase);
        for (int y = 0; y < 8; y++)
            World.SetBlock(y < 6 ? "game:claybricks-good-fire" : y == 6 ? "game:claybrickchimney-four-red-ns" : "game:air", b.AddCopy(0, y, 0));
        await World.Until(() => kettle.Row.Works, 5000);
        Assert.Equal(1, kettle.Heat);
        // the damper half shut: half the heat; shut, banked
        Assert.Null(KettleClick(player, pos, null, sneak: true));
        Assert.Equal(Damper.Half, kettle.Damper);
        Assert.Equal(0.5, kettle.Heat);
        Assert.Null(KettleClick(player, pos, null, sneak: true));
        Assert.Equal(0, kettle.Heat);
        Assert.True(kettle.ShowsPart("dampershut") && !kettle.ShowsPart("damperopen"));
        Assert.Null(KettleClick(player, pos, null, sneak: true));
        Assert.Equal(Damper.Open, kettle.Damper);

        // the fire burns 1.2 coal an hour with the damper open
        kettle.Age(2.5);
        await World.Until(() => kettle.Coal < 7, 3000);
        Assert.InRange(kettle.Coal, 4.5, 5.0);

        // powered: the stirrer works the charge
        var power = kettle.CellPos(kettle.Rig!.Anchor("power")!.Value.Cell);
        var rotorPos = power.AddCopy(kettle.PowerFace);
        World.SetBlock($"game:creativerotor-{kettle.PowerFace.Code}", rotorPos);
        await World.Ticks(2);
        var rotor = W.BlockAccessor.GetBlockEntity(rotorPos)!.GetBehavior<BEBehaviorMPCreativeRotor>()!;
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "speedSetting").SetValue(rotor, 10);
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "powerSetting").SetValue(rotor, 10);
        rotor.Blockentity.MarkDirty(true);
        await World.Until(() => Math.Abs(kettle.ShaftSpeed) >= 0.3f, 15000);
        await World.Until(() => kettle.Progress > 0, 20000);
        Assert.Equal(MachineState.Working, kettle.State);
        // stirred through rather than waited on
        kettle.StirThrough();
        Assert.Equal(KettlePhase.Done, kettle.Phase);
        // 2,400 lead with 3 % silver: 72 silver, 7 rich bars of 10 (2 carried on), 17 lead; 25 zinc in, 20 back
        Assert.Equal((7, 17, 4), (kettle.RichLead, kettle.LeadOut, kettle.ZincBack));
        Assert.True(kettle.ShowsPart("crust") && !kettle.ShowsPart("zinc"));
        W.BlockAccessor.SetBlock(0, rotorPos);

        // an empty hand skims the crust, then ladles the lead out, 6 at a time
        KettleClick(player, pos, null);
        Assert.Equal(7, KettleHeld(player, Bullion.RichLeadCode));
        Assert.Equal(4, KettleHeld(player, ParkesKettling.ZincBit));
        Assert.True(kettle.Skimmed);
        Assert.False(kettle.ShowsPart("crust"));
        // (each click empties the hand first, so a ladling is counted as it comes)
        int ladled = 0;
        foreach (int left in new[] { 11, 5, 0 })
        {
            KettleClick(player, i++ % 2 == 0 ? pos : cell, null);
            Assert.Equal(left, kettle.LeadOut);
            ladled += KettleHeld(player, ParkesKettling.Lead);
            foreach (var inv in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
                foreach (var slot in player.InventoryManager.GetOwnInventory(inv) ?? Enumerable.Empty<ItemSlot>())
                    if (slot.Itemstack?.Collectible.Code.ToString() == ParkesKettling.Lead)
                        slot.Itemstack = null;
        }
        Assert.Equal(17, ladled);
        Assert.Equal(KettlePhase.Empty, kettle.Phase);

        // capped with an iron plate on the outlet; the plate back from it
        var outlet = kettle.CellPos(rig.OutletCell);
        Assert.Null(KettleClick(player, outlet, CutterItem(rig.CapCode, 1)));
        Assert.True(kettle.Capped && kettle.ShowsPart("outletcap"));
        Assert.Equal(rig.CapCode, KettleClick(player, outlet, null)?.Collectible.Code.ToString());
        Assert.False(kettle.Capped);
        Assert.Null(KettleClick(player, outlet, CutterItem(rig.CapCode, 1)));

        // broken: the frame, every stage's items (10 iron plates), the cap (1 more) and the coal left
        var drops = W.BlockAccessor.GetBlock(pos).GetDrops(W, pos, player);
        Assert.Contains(drops, d => d.Collectible.Code.ToString() == KettleFrame + "north");
        Assert.Equal(20, drops.Where(d => d.Collectible.Code.ToString() == "game:burnedbrick-red").Sum(d => d.StackSize));
        Assert.Equal(12, drops.Where(d => d.Collectible.Code.ToString() == "game:refractorybrick-fired-tier3").Sum(d => d.StackSize));
        Assert.Equal(11, drops.Where(d => d.Collectible.Code.ToString() == "game:metalplate-iron").Sum(d => d.StackSize));
        Assert.InRange(drops.Where(d => d.Collectible.Code.ToString() == "game:ore-bituminouscoal").Sum(d => d.StackSize), 4, 5);
        for (int y = 0; y < 8; y++)
            W.BlockAccessor.SetBlock(0, b.AddCopy(0, y, 0));
        W.BlockAccessor.BreakBlock(pos, null);
    }
}
