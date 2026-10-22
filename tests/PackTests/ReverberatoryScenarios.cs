using Atlas.XUnit;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using SeraphHorizons.Mod.ReverberatoryRoaster;
using SeraphHorizons.Mod.ReverberatoryRoaster.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.PackTests;

// seraphhorizons, ReverberatoryRoaster (#736) on the ore machines' shared game code (Machines/Game, #711): the
// reverberatory roaster in the pack's world, placed, built stage by stage with real items (its masonry all of one fired
// brick), coal fed by a vanilla chute into its bunker (the fuel side feed), lit through the shared ore machine fire,
// drawn by a chimney, driven by a creative rotor, watered, fed sulfide concentrate by hand and by chute into its hopper,
// its roasted concentrate and native sulfur's powder pushed into a chest at its output face; the damper shut stops it,
// the coal running short puts the fire out, a furnace back to back with it shares the chimney once capped; broken, every
// item back. Built on granite floors 40 above spawn at x 780, z 700; the gear cutter's player.
public partial class MillWorldScenarios
{
    private const string ReverbFrame = "seraphhorizons:reverberatory-";

    private async Task<BEReverberatory> PlaceReverb(BlockPos pos, string side)
    {
        World.SetBlock(ReverbFrame + side, pos);
        await World.Ticks(5);
        var be = W.BlockAccessor.GetBlockEntity(pos) as BEReverberatory ?? throw new Xunit.Sdk.XunitException($"no reverberatory roaster at {pos}");
        be.PlaceCells();
        return be;
    }

    private static int ReverbChestCount(BlockEntityContainer chest, string code) =>
        chest.Inventory.Where(s => s.Itemstack?.Collectible.Code.ToString() == code).Sum(s => s.StackSize);

    private async Task ReverbUntil(BEReverberatory f, Func<bool> done, int ticks = 90000)
    {
        try
        {
            using (VirtualClock.Start(World))
                await World.Until(done, ticks);
        }
        catch (Exception e) when (e.GetType().Name.Contains("Timeout"))
        {
            string held = string.Join(", ", f.OutputSlots("output").Where(s => !s.Empty).Select(s => $"{s.StackSize}x {s.Itemstack.Collectible.Code}"));
            throw new Xunit.Sdk.XunitException($"the furnace never got there: {f.State}, lit {f.Lit}, pace {f.Pace}, row {f.Row.Problem}, speed {f.ShaftSpeed}, " +
                $"bed {f.InfeedSlot.StackSize}, coal {f.Coal}, water {f.Water.Litres}, progress {f.Progress}, held [{held}]");
        }
    }

    [AtlasScenario]
    public void Reverberatory_loads_with_its_frame_build_recipe_and_figures()
    {
        Assert.True(ReverberatorySystem.Applies(World.Api));
        foreach (var side in new[] { "north", "east", "south", "west" })
            Assert.IsType<BlockMachine>(W.GetBlock(new AssetLocation(ReverbFrame + side)));
        var block = (BlockMachine)W.GetBlock(new AssetLocation(ReverbFrame + "north"));
        Assert.Equal("reverberatory", block.Machine);
        Assert.NotNull(block.Rig);
        Assert.NotNull(ReverberatorySystem.Of(World.Api).Rig);
        var build = MachinesSystem.Of(World.Api).Build("reverberatory")!;
        Assert.Equal(["masonry", "hopperdoors", "drive", "feeding", "services"], build.Stages.Select(s => s.Id).ToArray());
        // every item the build names is in the game
        foreach (var code in build.Stages.SelectMany(s => s.Materials).SelectMany(m => m.Codes))
            Assert.True(code.Contains('*') ? W.SearchBlocks(new AssetLocation(code)).Length > 0 : W.GetItem(new AssetLocation(code)) is { Id: > 0 }, code);
        Assert.Equal("Reverberatory roaster frame", new ItemStack(block).GetName());
        var recipe = Assert.Single(W.GridRecipes, r => r.Output?.Code?.ToString() == ReverbFrame + "north" && r.Enabled);
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.Code?.ToString() == "game:metalplate-iron" && i.Quantity == 8);
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.IsTool == true && i.Code?.Path == "hammer-*");
        var spec = OreProcessingSystem.RecoveryFor(World.Api).Machine(OreMachine.Reverberatory);
        Assert.Equal(76, spec.ItemsPerHour, 6);
        Assert.Equal(2, spec.WaterPerItem, 6);
        // every sulfide's concentrate has its roasted concentrate, and every fuel the bunker takes is in the game
        foreach (var conc in W.SearchItems(new AssetLocation(OreProducts.Domain, "concentrate-*")))
            if (OreProcessingSystem.RecoveryFor(World.Api).Ore(conc.Variant["ore"]!).IsSulfide)
                Assert.True(W.GetItem(new AssetLocation(OreProducts.RoastedCode(conc.Variant["ore"]!))) is { Id: > 0 }, conc.Code.ToString());
        foreach (var fuel in ReverberatoryRoasting.Fuels)
            Assert.True(W.GetItem(new AssetLocation(fuel)) is { Id: > 0 }, fuel);
        Assert.True(W.GetItem(new AssetLocation(ReverberatoryRoasting.NativeSulfurCode)) is { Id: > 0 });
    }

    // Built, coal by chute, lit under a chimney, powered and watered; fed chalcopyrite concentrate by hand and by chute:
    // roasted concentrate one for one into a chest; native sulfur rock melted into powder; the damper shut stops it, the
    // coal running short puts the fire out; a furnace back to back shares the chimney once its outlet is capped; broken,
    // everything back.
    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task Reverberatory_builds_roasts_concentrate_and_native_sulfur_by_chute_and_breaks_back()
    {
        var hand = await CutterHand();
        var player = hand.Player;
        var pos = await CutterSite(780, 700, reach: 9);
        var sapi = (ICoreServerAPI)World.Api;
        int size = GlobalConstants.ChunkSize;
        for (int cx = (pos.X - 9) / size; cx <= (pos.X + 9) / size; cx++)
            for (int cz = (pos.Z - 9) / size; cz <= (pos.Z + 9) / size; cz++)
                sapi.WorldManager.LoadChunkColumnPriority(cx, cz, new ChunkLoadOptions { KeepLoaded = true });

        var f = await PlaceReverb(pos, "south");
        var rig = f.FurnaceRig!;
        var assembly = f.Assembly!;
        var cell = f.CellPos(new Int3(1, 0, 0));
        Assert.Equal(MachineLoad.Unassembled, f.Resistance);

        // out of order: the drive's gears before the masonry
        Assert.Equal(4, CutterClick(player, pos, CutterItem("seraphhorizons:gear-stainless", 4))?.StackSize);
        Assert.True(_cutterHandled);
        Assert.Equal(0, assembly.Build.StagesDone);
        // the masonry's brick, all one type: red first, cream refused
        Assert.Equal(38, CutterClick(player, pos, CutterItem("game:burnedbrick-red", 64))?.StackSize);
        Assert.Equal(64, CutterClick(player, pos, CutterItem("game:burnedbrick-cream", 64))?.StackSize);
        Assert.Equal("red", f.BrickType);
        Assert.True(f.ShowsPart("brickwork") && !f.ShowsPart("lining"));
        // every stage after it with real items
        int i = 0;
        while (assembly.Build.Next is { } _)
        {
            var (material, missing) = assembly.Build.NextNeeds().First();
            string code = material.Codes.Contains("seraphhorizons:pipesection-copper") ? "seraphhorizons:pipesection-copper"
                : material.Codes[0].Replace("chute-straight-*", "chute-straight-ud");
            Assert.True(CutterClick(player, i++ % 2 == 0 ? pos : cell, CutterItem(code, missing)) == null, $"{code} x{missing} was not fitted");
        }
        Assert.True(f.Built);
        MachineTest.FillOil(f);
        Assert.True(f.ShowsPart("arch") && f.ShowsPart("rabbles") && f.ShowsPart("pipecopper") && !f.ShowsPart("pipelead"));
        Assert.True(f.ShowsPart("damperopen") && !f.ShowsPart("dampershut") && !f.ShowsPart("fire") && !f.ShowsPart("charge"));
        // the rabbles' load follows the bed: empty 0.1
        Assert.Equal(0.1f, f.Resistance, 3);

        // nothing to light it on: no coal
        Assert.Equal(EnumIgniteState.NotIgnitablePreventDefault, MachineFire.IgniteState(player.Entity, pos, 1.5f));
        // coal by a chute into the bunker (the fuel side feed); not firewood
        var (fuelCell, fuelFace) = MachineChutes.World(pos, f.Side, f.Rig!.Anchor("fuel")!.Value);
        var fuelChutePos = fuelCell.AddCopy(fuelFace);
        World.SetBlock("game:chute-straight-ud", fuelChutePos);
        await World.Ticks(2);
        var fuelChute = (BlockEntityItemFlow)W.BlockAccessor.GetBlockEntity(fuelChutePos)!;
        fuelChute.Inventory[0].Itemstack = CutterItem("game:firewood", 4);
        fuelChute.Inventory[0].MarkDirty();
        await World.Ticks(40);
        Assert.Equal(0, f.Coal);
        fuelChute.Inventory[0].Itemstack = null;
        fuelChute.Inventory[0].MarkDirty();
        fuelChute.Inventory[0].Itemstack = CutterItem("game:ore-bituminouscoal", 2);
        fuelChute.Inventory[0].MarkDirty();
        await World.Until(() => f.Coal == 2, 20000);

        // lit with a firestarter or a torch on any cell
        Assert.Equal(EnumIgniteState.IgniteNow, MachineFire.IgniteState(player.Entity, cell, 1.5f));
        var handling = EnumHandling.PassThrough;
        MachineFire.IgniteOver(player.Entity, cell, 1.5f, ref handling);
        Assert.True(f.Lit && f.ShowsPart("fire"));

        // a chest at the output face
        var (outCell, outFace) = MachineChutes.World(pos, f.Side, f.Rig!.Anchor("output")!.Value);
        var chestPos = outCell.AddCopy(outFace);
        World.SetBlock("game:chest-east", chestPos);
        await World.Ticks(2);
        var chest = (BlockEntityContainer)W.BlockAccessor.GetBlockEntity(chestPos)!;

        // not what it roasts: an oxide's concentrate, crushed galena
        Assert.False(f.Takes(CutterItem(OreProducts.ConcentrateCode("hematite"), 1)));
        Assert.False(f.Takes(CutterItem(OreProducts.CrushedCode("galena", "rich"), 1)));
        // by hand: 16 chalcopyrite concentrate, half the bed
        string conc = OreProducts.ConcentrateCode("chalcopyrite"), roasted = OreProducts.RoastedCode("chalcopyrite");
        Assert.Null(CutterClick(player, pos, CutterItem(conc, 16)));
        Assert.Equal(16, f.InfeedSlot.StackSize);
        Assert.True(f.ShowsPart("charge"));
        Assert.Equal(0.3f, f.Resistance, 3);
        // the bed holds 32
        Assert.Equal(48, CutterClick(player, pos, CutterItem(conc, 64))?.StackSize);
        Assert.Equal(0.5f, f.Resistance, 3);

        // powered and watered, but no chimney: idle
        var power = f.CellPos(f.Rig.Anchor("power")!.Value.Cell);
        var rotorPos = power.AddCopy(f.PowerFace);
        World.SetBlock($"game:creativerotor-{f.PowerFace.Code}", rotorPos);
        await World.Ticks(2);
        var rotor = W.BlockAccessor.GetBlockEntity(rotorPos)!.GetBehavior<BEBehaviorMPCreativeRotor>()!;
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "speedSetting").SetValue(rotor, 10);
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "powerSetting").SetValue(rotor, 30);
        rotor.Blockentity.MarkDirty(true);
        await World.Until(() => Math.Abs(f.ShaftSpeed) >= 0.3f, 15000);
        f.FillWater(f.ReservoirCapacity);
        await World.Ticks(20);
        Assert.Equal(MachineState.Idle, f.State);
        Assert.Equal(0, ReverbChestCount(chest, roasted));
        // a wrench reads its own turns, a concentrate's at its pace (weight 1), not the shared count it never advances
        var wrench = MachineTest.WrenchInfo(f, player);
        Assert.Contains(Lang.Get("seraphhorizons:reverberatory-info-turns", "0", f.Spec!.TurnsPerItem.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)), wrench);
        Assert.DoesNotContain("shaft turns toward the next item", wrench);

        // a chimney of six fire brick blocks and a top on the outlet
        var b = f.CellPos(rig.ChimneyBase);
        for (int y = 0; y < 8; y++)
            World.SetBlock(y < 6 ? "game:claybricks-good-fire" : y == 6 ? "game:claybrickchimney-four-red-ns" : "game:air", b.AddCopy(0, y, 0));
        await World.Until(() => f.Row.Works, 5000);
        Assert.Equal(1, f.Pace);

        // roasting: a coal every six; 12 concentrate take the two coal, the 13th puts the fire out
        // (no pipe here: the reservoir is topped up by hand as it goes; 2 L a concentrate, 20 L held)
        await ReverbUntil(f, () => Watered(f) && ReverbChestCount(chest, roasted) >= 12 && !f.Lit);
        Assert.Equal(12, ReverbChestCount(chest, roasted));
        Assert.Equal(0, f.Coal);
        Assert.Equal(20, f.InfeedSlot.StackSize);
        Assert.Equal(MachineState.Idle, await MillStateSoon(f));

        // coal by hand, lit again; the damper shut banks the fire, open it roasts on
        f.FillWater(f.ReservoirCapacity);
        Assert.Null(CutterClick(player, pos, CutterItem("game:coke", 8)));
        Assert.Equal(8, f.Coal);
        MachineFire.IgniteOver(player.Entity, cell, 1.5f, ref handling);
        Assert.True(f.Lit);
        var ashFace = BlockFacing.FromCode(Footprint.ToWorld(rig.AshSide, f.Side).Code());
        ReverbClickFace(player, pos, ashFace);
        Assert.Equal(Damper.Half, f.Damper);
        Assert.Equal(0.5, f.Pace, 6);
        ReverbClickFace(player, pos, ashFace);
        Assert.Equal(Damper.Shut, f.Damper);
        Assert.True(f.ShowsPart("dampershut") && !f.ShowsPart("damperopen"));
        int before = ReverbChestCount(chest, roasted);
        await World.Ticks(60);
        Assert.Equal(before, ReverbChestCount(chest, roasted));
        Assert.Equal(MachineState.Idle, f.State);
        ReverbClickFace(player, pos, ashFace);
        Assert.Equal(Damper.Open, f.Damper);

        // a chute into the hopper feeds it; the bed roasts out
        var (inCell, inFace) = MachineChutes.World(pos, f.Side, f.Rig.Anchor("infeed")!.Value);
        var chutePos = inCell.AddCopy(inFace);
        World.SetBlock("game:chute-straight-ud", chutePos);
        await World.Ticks(2);
        var chute = (BlockEntityItemFlow)W.BlockAccessor.GetBlockEntity(chutePos)!;
        chute.Inventory[0].Itemstack = CutterItem(conc, 4);
        chute.Inventory[0].MarkDirty();
        await World.Until(() => chute.Inventory.Empty, 20000);
        await ReverbUntil(f, () => Watered(f) && f.InfeedSlot.Empty && ReverbChestCount(chest, roasted) == 36);
        Assert.Equal(0.1f, f.Resistance, 3);
        Assert.False(f.ShowsPart("charge"));

        // native sulfur rock: 13 units x 0.65 a rock, 5 a powder; 10 rock give 16 powders (16.9)
        Assert.Null(CutterClick(player, pos, CutterItem("game:coke", 8)));
        Assert.Null(CutterClick(player, pos, CutterItem(ReverberatoryRoasting.NativeSulfurCode, 10)));
        await ReverbUntil(f, () => Watered(f) && f.InfeedSlot.Empty);
        Assert.True(f.Lit);
        Assert.Equal(16, ReverbChestCount(chest, ReverberatoryRoasting.SulfurPowderCode));

        // a second furnace back to back (west walls shared, turned half round): both outlets open is a leak; capped,
        // the one chimney serves both
        var partnerPos = f.CellPos(rig.Partner);
        var partner = await PlaceReverb(partnerPos, "north");
        while (partner.Assembly!.FillNext()) { }
        partner.MarkDirty(true);
        Assert.True(partner.Built);
        MachineTest.FillOil(partner);
        Assert.Same(f, partner.Partner());
        Assert.Same(partner, f.Partner());
        await World.Until(() => f.Row.Problem == RowProblem.OutletOpen, 5000);
        Assert.Null(CutterClick(player, partner.CellPos(rig.OutletCell), CutterItem(rig.CapCode, 1)));
        Assert.True(partner.Capped && partner.ShowsPart("outletcap"));
        await World.Until(() => f.Row.Works && partner.Row.Works && f.Row.Count == 2, 5000);
        W.BlockAccessor.BreakBlock(partnerPos, null);

        // broken: the frame and every stage's items
        W.BlockAccessor.SetBlock(0, rotorPos);
        var drops = W.BlockAccessor.GetBlock(pos).GetDrops(W, pos, player);
        Assert.Contains(drops, d => d.Collectible.Code.ToString() == ReverbFrame + "north");
        Assert.Equal(26, drops.Where(d => d.Collectible.Code.ToString() == "game:burnedbrick-red").Sum(d => d.StackSize));
        Assert.Equal(26, drops.Where(d => d.Collectible.Code.ToString() == "game:refractorybrick-fired-tier3").Sum(d => d.StackSize));
        Assert.Equal(4, drops.Where(d => d.Collectible.Code.ToString() == "seraphhorizons:gear-stainless").Sum(d => d.StackSize));
        Assert.Equal(2, drops.Where(d => d.Collectible.Code.ToString() == "seraphhorizons:pipesection-copper").Sum(d => d.StackSize));
        foreach (var p in new[] { chestPos, chutePos, fuelChutePos })
            W.BlockAccessor.SetBlock(0, p);
        for (int y = 0; y < 8; y++)
            W.BlockAccessor.SetBlock(0, b.AddCopy(0, y, 0));
        W.BlockAccessor.BreakBlock(pos, null);
    }

    /// <summary>Tops the reservoir up (true, for a wait's condition).</summary>
    private static bool Watered(BEReverberatory f)
    {
        f.FillWater(f.ReservoirCapacity);
        return true;
    }

    /// <summary>A right-click with an empty hand on <paramref name="face"/> of the block at <paramref name="at"/>.</summary>
    private void ReverbClickFace(IPlayer player, BlockPos at, BlockFacing face)
    {
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = null;
        slot.MarkDirty();
        var sel = new BlockSelection { Position = at.Copy(), Face = face, HitPosition = new Vec3d(0.5, 0.2, 0.5) };
        Assert.True(W.BlockAccessor.GetBlock(at).OnBlockInteractStart(W, player, sel));
    }
}
