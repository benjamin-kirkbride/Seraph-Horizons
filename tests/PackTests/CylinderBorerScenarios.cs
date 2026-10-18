using Atlas.XUnit;
using SeraphHorizons.Mod.CylinderBorer;
using SeraphHorizons.Mod.CylinderBorer.Core;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.PackTests;

// seraphhorizons, CylinderBorer (mods-src/seraphhorizons/CylinderBorer/; #639): the cylinder borer in the
// plain world, driven by a real mechanical power network (a vanilla creative rotor against its power face).
// A bore is 72 axle turns for iron and 108 for steel, so each scenario that runs it under power waits for
// the shaft's own turning to move the cutter head, then finishes the bore through BECylinderBorer.Bore, as
// the gear cutter's scenarios finish a gear through Cut; the powered part runs on the virtual clock
// (tests/PackTests/README.md). Every scenario builds on a granite floor of its
// own 40 above spawn at x -680 to -740, z -680 to -720 (clear of the eidolon gantry's at x -560 to -620),
// and they share the gear cutter's player, gearcutterhand (the server takes 16 players at most, and the
// other scenarios use the rest).
public partial class SharedWorldScenarios
{
    private CylinderBorerSystem BorerMod => CylinderBorerSystem.Of(World.Api);
    private CylinderBorerRig BorerRig => BorerMod.Rig ?? throw new Xunit.Sdk.XunitException("the cylinder borer's rig did not load");

    private async Task<BECylinderBorer> PlaceBorer(BlockPos pos, string side = "north")
    {
        World.SetBlock($"seraphhorizons:cylinderborer-frame-{side}", pos);
        await World.Ticks(5);
        var be = W.BlockAccessor.GetBlockEntity(pos) as BECylinderBorer ?? throw new Xunit.Sdk.XunitException($"no cylinder borer at {pos}");
        be.PlaceGhosts();
        return be;
    }

    /// <summary>The stages before the cutter head as the stacks a player puts in, a material a click:
    /// the bushings' metal parts, the bar's three rods, metal parts and large stainless gear, the feed's
    /// two oak planks, rod, two ropes and plate (the rods and the plate of mixed metals).</summary>
    private static readonly (string Code, int Count)[] BorerMaterials =
    [
        (CylinderBorerParts.BushingsCode, 1),
        ("game:rod-iron", 3), (CylinderBorerParts.BushingsCode, 1), ("seraphhorizons:largegear-stainless", 1),
        ("game:plank-oak", 2), ("game:rod-steel", 1), ("game:rope", 2), ("game:metalplate-meteoriciron", 1),
    ];

    /// <summary>What breaking a borer built of <see cref="BorerMaterials"/> gives back of them, by code.</summary>
    private static Dictionary<string, int> BorerKept() =>
        BorerMaterials.GroupBy(m => m.Code).ToDictionary(g => g.Key, g => g.Sum(m => m.Count));

    /// <summary>A cutter head with <paramref name="durability"/> left (full when null).</summary>
    private ItemStack BorerHead(int? durability = null)
    {
        var head = CutterItem(CylinderBorerParts.HeadCode);
        if (durability is { } left)
            head.Attributes.SetInt("durability", left);
        return head;
    }

    /// <summary>Every stage by right-clicks with real items, alternately on the frame and a ghost, the
    /// cutter head last; then a full oil tank.</summary>
    private void AssembleBorer(BECylinderBorer borer, IPlayer player, ItemStack? head = null)
    {
        var ghost = borer.GhostCells().First().Pos;
        int i = 0;
        foreach (var part in BorerMaterials.Select(m => CutterItem(m.Code, m.Count)).Append(head ?? BorerHead()))
            Assert.True(CutterClick(player, i++ % 2 == 0 ? borer.Pos : ghost, part) == null, $"{part.StackSize} {part.Collectible.Code} did not go in");
        Assert.True(borer.Complete);
        FillBorerOil(borer, 1);
    }

    private static void FillBorerOil(BECylinderBorer borer, double fill)
    {
        Assert.NotNull(borer.Oiling);
        var tank = borer.Oiling.Tank;
        borer.Oiling.Tank = tank with { Points = tank.Capacity * fill };
    }

    /// <summary>A creative rotor at full speed against the power face; waits until the shaft turns fast.</summary>
    private async Task<BlockPos> PowerBorer(BECylinderBorer borer)
    {
        var ghost = borer.CellPos(BorerRig.PowerCell);
        var face = Assert.IsType<BlockCylinderBorerGhostPower>(W.BlockAccessor.GetBlock(ghost)).PowerFace;
        var rotorPos = ghost.AddCopy(face);
        World.SetBlock($"game:creativerotor-{face.Code}", rotorPos);
        await World.Ticks(2);
        var rotor = W.BlockAccessor.GetBlockEntity(rotorPos)!.GetBehavior<BEBehaviorMPCreativeRotor>()!;
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "speedSetting").SetValue(rotor, 10);
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "powerSetting").SetValue(rotor, 10);
        rotor.Blockentity.MarkDirty(true);
        await World.Until(() => borer.ShaftSpeed >= 0.3f, 15000);
        return rotorPos;
    }

    /// <summary>Radians that finish the cylinder in the cradle from where it is.</summary>
    private static double RestOfBore(BECylinderBorer borer) =>
        (borer.Job.End - borer.Job.Work) * 2 * Math.PI * borer.TurnsPerBlock(borer.Job.Class) + 1e-6;

    private static string BorerInfo(BECylinderBorer borer, IPlayer player)
    {
        var sb = new System.Text.StringBuilder();
        borer.GetBlockInfo(player, sb);
        return sb.ToString();
    }

    private static ItemStack? BorerHeld(IPlayer player, string code) => player.InventoryManager.Inventories.Values
        .Where(inv => inv.ClassName is GlobalConstants.hotBarInvClassName or GlobalConstants.backpackInvClassName)
        .SelectMany(inv => inv).FirstOrDefault(s => s.Itemstack?.Collectible.Code.ToString() == code)?.Itemstack;

    // ---- Loading ----

    [AtlasScenario]
    public void Cylinder_borer_loads_with_its_blocks_parts_mold_and_recipes()
    {
        Assert.True(CylinderBorerSystem.Applies(World.Api));
        Assert.NotNull(BorerMod.Rig);
        foreach (var side in new[] { "north", "east", "south", "west" })
        {
            Assert.IsType<BlockCylinderBorer>(W.GetBlock(new AssetLocation($"seraphhorizons:cylinderborer-frame-{side}")));
            Assert.IsType<BlockCylinderBorerGhostPower>(W.GetBlock(new AssetLocation($"seraphhorizons:cylinderborer-ghostpower-{side}")));
        }
        Assert.IsType<BlockCylinderBorerGhost>(W.GetBlock(new AssetLocation("seraphhorizons:cylinderborer-ghost")));
        foreach (var stage in CylinderBorerRequires.Stages)
            foreach (var code in CylinderBorerParts.MaterialsFor(stage).SelectMany(m => m.Codes))
                Assert.True(BECylinderBorer.PartStack(W, code) is { StackSize: 1 }, $"no {code} for {stage}");   // metal parts are a block
        foreach (var k in new[] { 1, 2 })
        {
            Assert.True(W.GetItem(new AssetLocation(Boring.BlankFor(k)!)) is { Id: > 0, IsMissing: false }, $"no {Boring.BlankFor(k)}");
            Assert.True(W.GetItem(new AssetLocation(Boring.CylinderFor(k)!)) is { Id: > 0, IsMissing: false }, $"no {Boring.CylinderFor(k)}");
        }
        var head = BorerHead();
        Assert.Equal(8, head.Collectible.GetMaxDurability(head));
        Assert.Equal("Cylinder borer frame", new ItemStack(W.GetBlock(BlockCylinderBorer.ItemCode)).GetName());
        Assert.Equal("Steel cutter head", head.GetName());
        Assert.Equal("Rough cast iron cylinder", CutterItem(Boring.BlankIron).GetName());
        Assert.Equal("Steel engine cylinder", CutterItem(Boring.CylinderSteel).GetName());

        // the grid: the frame, of oak and iron (two plates in one slot, two rods in one, 12 nails and
        // strips), two stainless gears and a hammer
        var frame = Assert.Single(W.GridRecipes, r => r.Output?.Code?.ToString() == "seraphhorizons:cylinderborer-frame-north" && r.Enabled);
        Assert.Single(frame.ResolvedIngredients!, i => i?.Code?.Path == "metalplate-*" && i.AllowedVariants!.Contains("meteoriciron") && i.Quantity == 2);
        Assert.Single(frame.ResolvedIngredients!, i => i?.Code?.Path == "rod-*" && i.Quantity == 2);
        Assert.Single(frame.ResolvedIngredients!, i => i?.Code?.Path == "metalnailsandstrips-*" && i.Quantity == 12);
        Assert.Equal(2, frame.ResolvedIngredients!.Count(i => i?.Code?.ToString() == "game:log-placed-oak-ud"));
        Assert.Equal(2, frame.ResolvedIngredients!.Count(i => i?.Code?.ToString() == "game:plank-oak"));
        Assert.Contains(frame.ResolvedIngredients!, i => i?.Code?.ToString() == "seraphhorizons:gear-stainless" && i.Quantity == 2);
        Assert.Contains(frame.ResolvedIngredients!, i => i?.IsTool == true && i.Code?.Path == "hammer-*");
        // the bushings are the game's metal parts, and the bar and the feed take their materials at the
        // frame: none has an item or a recipe of its own
        Assert.Equal("game:metal-parts", CylinderBorerParts.BushingsCode);
        Assert.NotNull(W.GetBlock(new AssetLocation(CylinderBorerParts.BushingsCode)));
        foreach (var gone in new[] { CylinderBorerParts.LegacyBushingsCode, CylinderBorerParts.LegacyBarCode, CylinderBorerParts.LegacyFeedCode })
        {
            Assert.Null(W.GetItem(new AssetLocation(gone)));
            Assert.DoesNotContain(W.GridRecipes, r => r.Output?.Code?.ToString() == gone);
        }
        // the anvil: the cutter head from one steel ingot, steel only, by hand
        var smith = Assert.Single(World.Api.GetSmithingRecipes(), r => r.Output?.ResolvedItemstack?.Collectible.Code.ToString() == CylinderBorerParts.HeadCode);
        Assert.True(smith.Ingredient!.SatisfiesAsIngredient(CutterItem("game:ingot-steel")));
        Assert.False(smith.Ingredient.SatisfiesAsIngredient(CutterItem("game:ingot-iron")));
        Assert.DoesNotContain(smith.Name?.Path, new[] { "plate", "blistersteel" });
        Assert.InRange(smith.Voxels.Cast<bool>().Count(v => v), 1, 42);
        // the mold: clay formed, raw and fired
        Assert.Contains(World.Api.GetClayformingRecipes(), r => r.Output?.ResolvedItemstack?.Collectible.Code.ToString() == "seraphhorizons:cylindermold-blue-raw");
        Assert.NotNull(W.GetBlock(new AssetLocation("seraphhorizons:cylindermold-fire-raw")));
        Assert.IsType<BlockToolMold>(W.GetBlock(new AssetLocation("seraphhorizons:cylindermold-tan-fired")));
        // the settings and the rig's pace agree
        Assert.Equal(BorerRig.TurnsPerBlock[1], BorerMod.Config.TurnsPerBlockIron, 0.01);
        Assert.Equal(BorerRig.TurnsPerBlock[2], BorerMod.Config.TurnsPerBlockSteel, 0.01);
        Assert.Equal(10f, SeraphHorizons.Mod.MachineOil.MachineOilSystem.Of(World.Api).Config.CylinderBorer.DrainPerJob);
        Assert.Contains("cylinder borer", Lang.Get("seraphhorizons:machineoil-text"));
    }

    // The fired mold takes 600 units of molten iron or steel, six ingots, from a crucible or the canal,
    // and casts the blank of that metal; Steelmaking Expanded's canal pedestal takes it as a small tool
    // mold (its own MoldKinds, by reflection: nothing of smex is referenced).
    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task Cylinder_mold_takes_six_ingots_of_iron_or_steel_and_casts_a_blank()
    {
        var pos = await CutterSite(-740, -720);
        var player = await CutterPlayer();
        World.SetBlock("seraphhorizons:cylindermold-blue-fired", pos);
        await World.Ticks(3);
        var mold = Assert.IsType<BlockEntityToolMold>(W.BlockAccessor.GetBlockEntity(pos));
        Assert.True(mold.CanReceiveAny);
        Assert.False(mold.CanReceive(CutterItem("game:ingot-copper")));
        foreach (var metal in new[] { "iron", "steel" })
        {
            var ingot = CutterItem("game:ingot-" + metal);
            Assert.True(mold.CanReceive(ingot));
            // as the canal fills it, or a crucible pours: six ingots' worth, what is over left in the pour
            for (int poured = 0; poured < 600; poured += 100)
            {
                Assert.False(mold.IsFull);
                int amount = 100;
                mold.ReceiveLiquidMetal(ingot, ref amount, 1600f);
                Assert.Equal(0, amount);
            }
            int more = 100;
            mold.ReceiveLiquidMetal(ingot, ref more, 1600f);
            Assert.Equal(100, more);
            Assert.True(mold.IsFull);
            Assert.Equal(600, mold.FillLevel);
            Assert.Null(mold.GetStateAwareMoldedStacks()); // still hot
            mold.MetalContent.Collectible.SetTemperature(W, mold.MetalContent, 20f, false);
            Assert.True(mold.IsHardened);
            // the cast cylinder to an empty hand; the mold stays, empty, for the next
            CutterClick(player, pos, null);
            Assert.NotNull(BorerHeld(player, "seraphhorizons:cylinderblank-" + metal));
            Assert.Equal(0, mold.FillLevel);
            Assert.Same(mold, W.BlockAccessor.GetBlockEntity(pos));
        }
        var kinds = HarmonyLib.AccessTools.TypeByName("SteelmakingExpanded.BlockNetworkMolten.Blocks.MoldKinds");
        Assert.NotNull(kinds);
        var fits = HarmonyLib.AccessTools.Method(kinds, "FitsPedestal");
        Assert.True((bool)fits.Invoke(null, [W.GetBlock(new AssetLocation("seraphhorizons:cylindermold-blue-fired"))])!);
        CutterKillItems(pos);
    }

    // ---- Placing ----

    // On all four facings: room for every cell, a ghost stamped in each, the borer running away along
    // the facing from the bench end, the power ghost's axle face native south turned to the facing, at
    // the far end; broken through a ghost, every cell is cleared and the frame drops.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Cylinder_borer_places_on_all_four_facings_with_ghosts()
    {
        var player = await CutterPlayer();
        int i = 0;
        foreach (var side in Sides.All)
        {
            var pos = await CutterSite(-680 - 12 * i++, -680);
            var borer = await PlaceBorer(pos, side.Code());
            Assert.Equal(side, borer.Side);
            var cells = borer.GhostCells().ToList();
            Assert.Equal(7, cells.Count);
            var n = side.Normal();
            foreach (var cell in cells.Select(c => c.Pos).Append(pos))
            {
                if (!cell.Equals(pos))
                    Assert.Equal(pos, Assert.IsType<BECylinderBorerGhost>(W.BlockAccessor.GetBlockEntity(cell)).Principal);
                var block = W.BlockAccessor.GetBlock(cell);
                Assert.NotNull(block.GetSelectionBoxes(W.BlockAccessor, cell));
                Assert.NotNull(block.GetCollisionBoxes(W.BlockAccessor, cell));
            }
            var powerGhost = Assert.Single(cells, c => c.Power).Pos;
            Assert.Equal(pos.AddCopy(n.X * 3, 0, n.Z * 3), powerGhost);
            var face = Assert.IsType<BlockCylinderBorerGhostPower>(W.BlockAccessor.GetBlock(powerGhost)).PowerFace;
            Assert.Equal(side.Code(), face.Code);
            Assert.DoesNotContain(cells, c => c.Pos.Equals(powerGhost.AddCopy(face)));
            Assert.Equal("Cylinder borer frame", W.BlockAccessor.GetBlock(pos).GetPlacedBlockName(W, pos));

            W.BlockAccessor.GetBlock(cells[3].Pos).OnBlockBroken(W, cells[3].Pos, player);
            await World.Ticks(2);
            Assert.Equal(0, W.BlockAccessor.GetBlock(pos).Id);
            Assert.All(cells, c => Assert.Equal(0, W.BlockAccessor.GetBlock(c.Pos).Id));
            Assert.Equal(1, CutterItemsNear(pos).GetValueOrDefault("seraphhorizons:cylinderborer-frame-north"));
            CutterKillItems(pos);
        }
    }

    // ---- Building ----

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Cylinder_borer_is_built_in_order_and_refuses_parts_out_of_order()
    {
        var pos = await CutterSite(-680, -700);
        var player = await CutterPlayer();
        var borer = await PlaceBorer(pos, "east");
        var ghost = borer.GhostCells().Last().Pos;
        Assert.Contains("Build: stage 1 of 4, bushings", BorerInfo(borer, player));
        Assert.Contains("Still takes: 1 × ", BorerInfo(borer, player));

        // a later stage's material is refused and stays in hand, from the frame or a ghost
        Assert.Equal(2, CutterClick(player, pos, CutterItem("game:rope", 2))?.StackSize);
        Assert.True(_cutterHandled);
        Assert.Equal(1, CutterClick(player, ghost, BorerHead())?.StackSize);
        Assert.False(borer.Parts.Has(CylinderBorerStage.Bushings));
        // no casting before the borer is built
        Assert.Equal(1, CutterClick(player, pos, CutterItem(Boring.BlankIron))?.StackSize);
        Assert.False(borer.BlankOn);
        // an item that is no part of it is the item's own business
        Assert.Equal(3, CutterClick(player, pos, CutterItem("game:gear-rusty", 3))?.StackSize);
        Assert.False(_cutterHandled);
        Assert.Equal(3, CutterClick(player, pos, CutterItem("game:rod-copper", 3))?.StackSize);
        Assert.False(_cutterHandled);

        // the bushings: one lot of metal parts from a stack of two, the second already fitted
        Assert.Equal(1, CutterClick(player, pos, CutterItem(CylinderBorerParts.BushingsCode, 2))?.StackSize);
        Assert.True(borer.Parts.Has(CylinderBorerStage.Bushings));
        string info = BorerInfo(borer, player);
        Assert.Contains("Build: stage 2 of 4, boring bar", info);
        Assert.Contains("Still takes: 3 × Iron rod or ", info);
        Assert.Contains("Steel rod, 1 × ", info);

        // the bar: two rods are too few (and not a full stack), so nothing goes in; three of five do
        Assert.Equal(2, CutterClick(player, ghost, CutterItem("game:rod-iron", 2))?.StackSize);
        Assert.Equal(0, borer.Parts.Count(CylinderBorerStage.Bar, 0));
        Assert.Equal(2, CutterClick(player, ghost, CutterItem("game:rod-iron", 5))?.StackSize);
        Assert.Equal(3, borer.Parts.Count(CylinderBorerStage.Bar, 0));
        Assert.Equal(1, CutterClick(player, ghost, CutterItem("game:rod-iron"))?.StackSize);   // all the rods it takes are in
        Assert.False(borer.Parts.Has(CylinderBorerStage.Bar));
        Assert.False(borer.PartFitted("bar"));   // drawn only once complete
        Assert.DoesNotContain("Iron rod", BorerInfo(borer, player));
        // a stage partly in keeps what it holds through a save
        var partly = new Vintagestory.API.Datastructures.TreeAttribute();
        borer.ToTreeAttributes(partly);
        borer.FromTreeAttributes(partly, W);
        Assert.Equal(3, borer.Parts.Count(CylinderBorerStage.Bar, 0));
        Assert.Equal(CylinderBorerStage.Bar, borer.Parts.Next);
        Assert.Null(CutterClick(player, pos, CutterItem(CylinderBorerParts.BushingsCode)));
        Assert.Null(CutterClick(player, pos, CutterItem("seraphhorizons:largegear-stainless")));
        Assert.True(borer.Parts.Has(CylinderBorerStage.Bar));
        Assert.True(borer.PartFitted("bar"));

        // the feed: its planks from a stack of four, a steel rod, the ropes, a meteoric iron plate
        Assert.Contains("Build: stage 3 of 4, feed", BorerInfo(borer, player));
        Assert.Equal(2, CutterClick(player, ghost, CutterItem("game:plank-oak", 4))?.StackSize);
        foreach (var (code, count) in BorerMaterials.Skip(5))
            Assert.Null(CutterClick(player, pos, CutterItem(code, count)));
        Assert.True(borer.Parts.Has(CylinderBorerStage.Feed));
        Assert.False(borer.Complete);
        Assert.Contains("Build: stage 4 of 4, cutter head", BorerInfo(borer, player));
        Assert.Equal(1, CutterClick(player, pos, CutterItem(Boring.BlankSteel))?.StackSize);
        // a spent head is refused
        Assert.Equal(1, CutterClick(player, ghost, BorerHead(0))?.StackSize ?? 0);
        Assert.Null(CutterClick(player, ghost, BorerHead()));
        Assert.True(borer.Complete);
        Assert.Equal("Cylinder borer", W.BlockAccessor.GetBlock(pos).GetPlacedBlockName(W, pos));
        Assert.DoesNotContain("Build:", BorerInfo(borer, player));
        // built dry, the next cylinder would break the head; a full tank gives it all eight
        Assert.Contains("Cutter head: 8 of 8, about 1 more cylinders", BorerInfo(borer, player));
        Assert.Contains("No oil: the next cylinder breaks the cutter head", BorerInfo(borer, player));
        FillBorerOil(borer, 1);
        Assert.Contains("Cutter head: 8 of 8, about 8 more cylinders", BorerInfo(borer, player));
        Assert.Contains("Cutter head wear: 1× per cylinder", BorerInfo(borer, player));
        Assert.Contains("Cradle empty", BorerInfo(borer, player));

        // a save keeps every fitted code
        var tree = new Vintagestory.API.Datastructures.TreeAttribute();
        borer.ToTreeAttributes(tree);
        borer.FromTreeAttributes(tree, W);
        Assert.True(borer.Complete);
        Assert.Equal(9, borer.Parts.Returns().Count);
        Assert.Equal(BorerMaterials.Sum(m => m.Count) + 1, borer.Parts.Returns().Sum(d => d.Count));
    }

    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task Cylinder_borer_creative_shortcut_fits_the_next_stage_free()
    {
        var pos = await CutterSite(-692, -700);
        var player = await CutterPlayer();
        var borer = await PlaceBorer(pos);
        int stages = 0;
        while (!borer.Complete && stages < 10)
        {
            var next = borer.Parts.Next!.Value;
            Assert.Null(CutterClick(player, pos, null, ctrl: true, creative: true));
            Assert.True(borer.Parts.Has(next), $"the shortcut did not fit {next}");
            stages++;
        }
        Assert.Equal(4, stages);
        Assert.Equal(8, borer.Parts.HeadLeft);
        // complete, a creative Ctrl click is the take-back again
        CutterClick(player, pos, null, ctrl: true, creative: true);
        Assert.False(borer.Parts.Has(CylinderBorerStage.CutterHead));
    }

    // ---- Boring ----

    // Under power a cast iron cylinder is bored in one traverse into the Watt engine's cylinder, beyond
    // the output face; the head moves by the shaft's own turning, wears a point with a full tank and the
    // tank drains 10 points; a bored cylinder is never taken back on.
    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task Cylinder_borer_bores_an_iron_cylinder_under_power()
    {
        var pos = await CutterSite(-704, -700);
        var player = await CutterPlayer();
        var borer = await PlaceBorer(pos, "south");
        AssembleBorer(borer, player);
        using var clock = VirtualClock.Start(World);
        var mp = W.BlockAccessor.GetBlockEntity(borer.CellPos(BorerRig.PowerCell))!.GetBehavior<BEBehaviorCylinderBorerMP>()!;
        Assert.Equal(BorerMod.Config.Resistance, mp.GetResistance(), 4);

        foreach (var code in new[] { Boring.CylinderIron, Boring.CylinderSteel, "game:ingot-iron" })
        {
            Assert.Equal(1, CutterClick(player, pos, CutterItem(code))?.StackSize);
            Assert.False(_cutterHandled, $"{code} was the borer's");
        }
        Assert.Equal(1, CutterClick(player, pos, CutterItem(Boring.BlankIron, 2))?.StackSize);
        Assert.True(borer.BlankOn);
        Assert.Equal(1, borer.Job.Class);
        // one at a time
        Assert.Equal(1, CutterClick(player, pos, CutterItem(Boring.BlankIron))?.StackSize);
        // the casting has a collision box while it is in the cradle
        var cradle = borer.CellPos(new Int3(0, 0, 2));
        int loaded = W.BlockAccessor.GetBlock(cradle).GetCollisionBoxes(W.BlockAccessor, cradle).Length;

        await World.Ticks(10);
        Assert.Equal(0, borer.Job.Work);
        Assert.Contains("Stopped with a rough cast iron cylinder in: 0%", BorerInfo(borer, player));
        var rotor = await PowerBorer(borer);
        await World.Until(() => borer.Job.Work > 0.002, 60000);
        Assert.True(borer.Running);
        Assert.Contains("Boring a rough cast iron cylinder:", BorerInfo(borer, player));

        Assert.True(borer.Bore(RestOfBore(borer)));
        Assert.False(borer.BlankOn);
        Assert.True(W.BlockAccessor.GetBlock(cradle).GetCollisionBoxes(W.BlockAccessor, cradle).Length < loaded);
        await World.Ticks(5);
        Assert.Equal(1, CutterItemsNear(pos).GetValueOrDefault(Boring.CylinderIron));
        Assert.Equal(0, CutterItemsNear(pos).GetValueOrDefault(Boring.BlankIron));
        // it dropped beyond the output face (native west), not inside the borer
        var dropped = World.EntitiesIn(new Cuboidi(pos.X - 6, pos.Y - 3, pos.Z - 6, pos.X + 6, pos.Y + 6, pos.Z + 6))
            .OfType<EntityItem>().First(e => e.Itemstack.Collectible.Code.ToString() == Boring.CylinderIron);
        var local = Footprint.ToLocal(new Float3((float)(dropped.Pos.X - pos.X), (float)(dropped.Pos.Y - pos.Y), (float)(dropped.Pos.Z - pos.Z)), borer.Side);
        Assert.True(local.X < 0.01f, $"the cylinder is at native {local}");
        Assert.Equal(7, borer.Parts.HeadLeft);
        Assert.Equal(990, borer.Oiling!.Tank.Points, 6);
        Assert.Contains("Cutter head: 7 of 8", BorerInfo(borer, player));
        W.BlockAccessor.SetBlock(0, rotor);
        CutterKillItems(pos);
    }

    // Steel takes 96 turns a block to iron's 64; at half a tank the head wears two points a cylinder,
    // and dry the cylinder breaks it, the load the same either way.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Cylinder_borer_bores_steel_slower_and_its_head_wears_by_the_oil()
    {
        var pos = await CutterSite(-716, -700);
        var player = await CutterPlayer();
        var borer = await PlaceBorer(pos, "west");
        AssembleBorer(borer, player, BorerHead(3));
        var mp = W.BlockAccessor.GetBlockEntity(borer.CellPos(BorerRig.PowerCell))!.GetBehavior<BEBehaviorCylinderBorerMP>()!;

        Assert.Null(CutterClick(player, pos, CutterItem(Boring.BlankSteel)));
        Assert.Equal(2, borer.Job.Class);
        Assert.Equal(borer.TurnsPerBlock(1) * 1.5, borer.TurnsPerBlock(2), 0.01);
        Assert.False(borer.Bore(2 * Math.PI * borer.TurnsPerBlock(2) * 0.5));
        Assert.Equal(0.5, borer.Job.Work, 3);
        FillBorerOil(borer, 0.5);
        Assert.True(borer.Bore(RestOfBore(borer)));
        Assert.Equal(1, borer.Parts.HeadLeft);
        await World.Ticks(5);
        Assert.Equal(1, CutterItemsNear(pos).GetValueOrDefault(Boring.CylinderSteel));

        // dry: the same load, and the cylinder breaks the head after it is bored
        FillBorerOil(borer, 0);
        Assert.Equal(BorerMod.Config.Resistance, mp.GetResistance(), 4);
        Assert.Contains("No oil: the next cylinder breaks the cutter head", BorerInfo(borer, player));
        Assert.Null(CutterClick(player, pos, CutterItem(Boring.BlankIron)));
        Assert.True(borer.Bore(RestOfBore(borer)));
        Assert.False(borer.Parts.Has(CylinderBorerStage.CutterHead));
        Assert.False(borer.Complete);
        await World.Ticks(5);
        Assert.Equal(1, CutterItemsNear(pos).GetValueOrDefault(Boring.CylinderIron));   // the last one still came out
        // no casting goes in without a head
        Assert.Equal(1, CutterClick(player, pos, CutterItem(Boring.BlankIron))?.StackSize);
        Assert.Null(CutterClick(player, pos, BorerHead()));
        Assert.True(borer.Complete);
        CutterKillItems(pos);
    }

    // ---- Taking back and breaking ----

    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task Cylinder_borer_gives_back_the_head_the_casting_and_everything_when_broken()
    {
        var pos = await CutterSite(-728, -700);
        var player = await CutterPlayer();
        var borer = await PlaceBorer(pos, "west");
        AssembleBorer(borer, player, BorerHead(5));
        var ghost = borer.GhostCells().First().Pos;
        Assert.Null(CutterClick(player, pos, CutterItem(Boring.BlankSteel)));
        Assert.False(borer.Bore(2 * Math.PI * borer.TurnsPerBlock(2) * 0.3));
        // Ctrl: the head first, with its durability, unworn by it; then the casting, its bore lost
        CutterClick(player, ghost, null, ctrl: true);
        var head = BorerHeld(player, CylinderBorerParts.HeadCode)!;
        Assert.Equal(5, head.Collectible.GetRemainingDurability(head));
        Assert.True(borer.BlankOn);
        CutterClick(player, ghost, null, ctrl: true);
        Assert.False(borer.BlankOn);
        Assert.NotNull(BorerHeld(player, Boring.BlankSteel));
        Assert.True(borer.Parts.Has(CylinderBorerStage.Feed));

        // refit and load; broken mid-bore, every part, the head with its wear, and the casting come back
        Assert.Null(CutterClick(player, pos, BorerHead(5)));
        Assert.Null(CutterClick(player, pos, CutterItem(Boring.BlankIron)));
        Assert.False(borer.Bore(2 * Math.PI * borer.TurnsPerBlock(1) * 0.5));
        CutterKillItems(pos);
        W.BlockAccessor.GetBlock(ghost).OnBlockBroken(W, ghost, player);
        await World.Ticks(3);
        Assert.Equal(0, W.BlockAccessor.GetBlock(pos).Id);
        var drops = CutterItemsNear(pos);
        var want = BorerKept();
        foreach (var code in new[] { CylinderBorerParts.HeadCode, Boring.BlankIron, "seraphhorizons:cylinderborer-frame-north" })
            want[code] = 1;
        foreach (var (code, count) in want)
            Assert.True(drops.GetValueOrDefault(code) == count, $"{code}: {drops.GetValueOrDefault(code)}, want {count}");
        Assert.Equal(0, drops.GetValueOrDefault(Boring.CylinderIron));
        var dropped = World.EntitiesIn(new Cuboidi(pos.X - 6, pos.Y - 3, pos.Z - 6, pos.X + 6, pos.Y + 6, pos.Z + 6))
            .OfType<EntityItem>().Single(e => e.Itemstack.Collectible.Code.ToString() == CylinderBorerParts.HeadCode).Itemstack;
        Assert.Equal(5, dropped.Collectible.GetRemainingDurability(dropped));
        CutterKillItems(pos);
    }

    // A borer saved when the bar and the feed were items of their own (and the bushings before them) restores
    // with those stages complete and is saved anew without them; breaking it gives back what the items were made
    // of, the rods and the plate in iron.
    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task Cylinder_borer_saved_with_the_old_bar_and_feed_items_keeps_its_stages()
    {
        var pos = await CutterSite(-680, -720);
        var player = await CutterPlayer();
        var borer = await PlaceBorer(pos);
        var tree = new Vintagestory.API.Datastructures.TreeAttribute();
        borer.ToTreeAttributes(tree);
        tree.RemoveAttribute("fitted");
        tree.SetString("part-bushings", CylinderBorerParts.LegacyBushingsCode);
        tree.SetString("part-bar", CylinderBorerParts.LegacyBarCode);
        tree.SetString("part-feed", CylinderBorerParts.LegacyFeedCode);
        borer.FromTreeAttributes(tree, W);
        Assert.Equal(CylinderBorerStage.CutterHead, borer.Parts.Next);
        Assert.True(borer.PartFitted("bar"));
        Assert.True(borer.PartFitted("feed"));
        var again = new Vintagestory.API.Datastructures.TreeAttribute();
        borer.ToTreeAttributes(again);
        Assert.Null(again.GetString("part-bar"));
        Assert.NotNull(again["fitted"]);

        CutterKillItems(pos);
        var ghost = borer.GhostCells().First().Pos;
        W.BlockAccessor.GetBlock(ghost).OnBlockBroken(W, ghost, player);
        await World.Ticks(3);
        var drops = CutterItemsNear(pos);
        var want = new Dictionary<string, int>
        {
            ["game:metal-parts"] = 2, ["game:rod-iron"] = 4, ["seraphhorizons:largegear-stainless"] = 1, ["game:plank-oak"] = 2,
            ["game:rope"] = 2, ["game:metalplate-iron"] = 1, ["seraphhorizons:cylinderborer-frame-north"] = 1,
        };
        Assert.Equal(want.OrderBy(kv => kv.Key), drops.OrderBy(kv => kv.Key));
        CutterKillItems(pos);
    }

    // ---- Infeed and outfeed ----

    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task Cylinder_borer_takes_castings_from_a_chest_and_puts_cylinders_in_one()
    {
        var pos = await CutterSite(-740, -700);
        var player = await CutterPlayer();
        var borer = await PlaceBorer(pos, "north");
        AssembleBorer(borer, player);
        var infeed = borer.CellPos(BorerRig.InfeedNeighbours().First(p => p.Y == 0));
        var outfeed = borer.CellPos(BorerRig.OutputNeighbour());
        World.SetBlock("game:chest-east", infeed);
        World.SetBlock("game:chest-east", outfeed);
        await World.Ticks(3);
        var source = Assert.IsAssignableFrom<BlockEntityContainer>(W.BlockAccessor.GetBlockEntity(infeed));
        var sink = Assert.IsAssignableFrom<BlockEntityContainer>(W.BlockAccessor.GetBlockEntity(outfeed));
        // a bored cylinder and an ingot it never takes, then a cast iron and a cast steel cylinder
        source.Inventory[0].Itemstack = CutterItem(Boring.CylinderIron);
        source.Inventory[1].Itemstack = CutterItem("game:ingot-iron");
        source.Inventory[2].Itemstack = CutterItem(Boring.BlankIron);
        source.Inventory[3].Itemstack = CutterItem(Boring.BlankSteel);
        source.MarkDirty(true);

        using var clock = VirtualClock.Start(World);
        // not taken while the shaft stands
        await World.Ticks(30);
        Assert.False(borer.BlankOn);
        await PowerBorer(borer);
        await World.Until(() => borer.BlankOn, 5000);
        Assert.Equal(1, borer.Job.Class);
        Assert.True(source.Inventory[2].Empty);
        Assert.True(borer.Bore(RestOfBore(borer)));
        Assert.Equal(1, sink.Inventory.Where(s => s.Itemstack?.Collectible.Code.ToString() == Boring.CylinderIron).Sum(s => s.StackSize));
        // the next casting goes in once the head has eased back
        Assert.False(borer.BlankOn);
        await World.Until(() => borer.BlankOn, 5000);
        Assert.Equal(2, borer.Job.Class);
        Assert.True(borer.Bore(RestOfBore(borer)));
        Assert.Equal(1, sink.Inventory.Where(s => s.Itemstack?.Collectible.Code.ToString() == Boring.CylinderSteel).Sum(s => s.StackSize));
        Assert.DoesNotContain(Boring.CylinderIron, CutterItemsNear(pos).Keys);
        await World.Ticks(40);
        Assert.False(borer.BlankOn);   // only the bored cylinder and the ingot are left
        Assert.Equal(Boring.CylinderIron, source.Inventory[0].Itemstack?.Collectible.Code.ToString());
        Assert.Equal("game:ingot-iron", source.Inventory[1].Itemstack?.Collectible.Code.ToString());
    }

    // ---- The engines ----

    [AtlasScenario]
    public void Ppex_engines_take_the_bored_cylinder_in_place_of_plates_and_pipes()
    {
        foreach (var (engine, cylinder) in new[] { ("ppex:enginewatt-north", Boring.CylinderIron), ("ppex:enginecornish-north", Boring.CylinderSteel) })
        {
            var recipes = W.GridRecipes.Where(r => r.Output?.Code?.ToString() == engine && r.Enabled).ToList();
            Assert.NotEmpty(recipes);
            foreach (var r in recipes)
            {
                var codes = GearConsumerUses.Codes(r).ToList();
                Assert.Contains(cylinder, codes);
                Assert.DoesNotContain(codes, c => c?.StartsWith("ppex:pipe-") == true);
                Assert.Single(r.ResolvedIngredients!, i => i?.Code?.ToString() == cylinder && i.Quantity == 1);
            }
        }
    }
}
