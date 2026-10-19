using Atlas.XUnit;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using SeraphHorizons.Mod.ShakingTable;
using SeraphHorizons.Mod.ShakingTable.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.PackTests;

// seraphhorizons, ShakingTable (mods-src/seraphhorizons/ShakingTable/; #921) on the ore machines' shared game
// code: the shaking table in the pack's world, built stage by stage with real items, driven by a vanilla creative
// rotor on its controller's power face, watered by code (FillWater, as a ppex pipe would), fed by hand, washing on
// the virtual clock into chests at its concentrate, middlings and tailings faces, its tailings then discarded as
// sand; and broken back into everything that went in. Built 40 above spawn at x 880 to 900, z 880.
public partial class MillWorldScenarios
{
    private const string TableFrame = "seraphhorizons:shakingtable-";

    [AtlasScenario, ReadsBootLog]
    public void Shaking_table_loads_with_its_frame_build_and_recipe()
    {
        Assert.True(ShakingTableSystem.Applies(World.Api));
        foreach (var side in new[] { "north", "east", "south", "west" })
            Assert.IsType<BlockMachine>(W.GetBlock(new AssetLocation(TableFrame + side)));
        var block = (BlockMachine)W.GetBlock(new AssetLocation(TableFrame + "north"));
        Assert.Equal("shakingtable", block.Machine);
        Assert.Equal("Shaking table frame", new ItemStack(block).GetName());
        var rig = block.Rig!;
        Assert.Equal(Int3.Zero, rig.Anchor(OreMachineRig.Power)!.Value.Cell);
        foreach (var outlet in new[] { ShakingTabling.Concentrate, ShakingTabling.Middlings, ShakingTabling.Tailings })
            Assert.NotNull(rig.Anchor(outlet));
        var build = MachinesSystem.Of(World.Api).Build("shakingtable");
        Assert.Equal(["deck", "rectifier", "head", "services"], build!.Stages.Select(s => s.Id));
        var recipe = Assert.Single(W.GridRecipes, r => r.Output?.Code?.ToString() == TableFrame + "north" && r.Enabled);
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.Code?.Path == "plank-*" && i.Quantity == 9);
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.Code?.ToString() == "game:metalnailsandstrips-iron" && i.Quantity == 4);
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.IsTool == true && i.Code?.Path == "hammer-*");
        // what it takes: crushed and ground ore and what the sluice and jig take; not raw ore or concentrate
        Assert.True(ShakingTableSystem.TakesFeed(SluiceStack(W, "seraphhorizons:crushedore-hematite-rich", 1)));
        Assert.True(ShakingTableSystem.TakesFeed(SluiceStack(W, "seraphhorizons:groundore-hematite-poor", 1)));
        Assert.False(ShakingTableSystem.TakesFeed(SluiceStack(W, "seraphhorizons:concentrate-hematite", 1)));
        Assert.False(ShakingTableSystem.TakesFeed(SluiceStack(W, "game:ore-rich-hematite-granite", 1)));
        var spec = OreProcessingSystem.RecoveryFor(World.Api).Machine(OreMachine.Table);
        Assert.Equal(7, spec.ItemsPerHour, 6);
        Assert.Equal(20, spec.WaterPerItem, 6);
        var logged = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error)
            .Where(e => e.Message.Contains("shakingtable", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("shaking table", StringComparison.OrdinalIgnoreCase))
            .Select(e => $"[{e.Level}] {e.Message}").ToList();
        Assert.True(logged.Count == 0, "Logged:\n" + string.Join("\n", logged));
    }

    /// <summary>Waters the table every server tick, as a pipe would, until <paramref name="done"/>.</summary>
    private async Task TableUntil(BEShakingTable be, Func<bool> done, int ms)
    {
        long listener = W.RegisterGameTickListener(_ => be.FillWater(100), 20);
        try
        {
            await World.Until(done, ms);
        }
        catch (Exception e) when (e is not Xunit.Sdk.XunitException)
        {
            var sb = new System.Text.StringBuilder();
            be.GetBlockInfo(null!, sb);
            throw new Xunit.Sdk.XunitException($"the shaking table stalled ({e.Message}): state {be.State}, speed {be.ShaftSpeed}, infeed {be.InfeedSlot.StackSize}\n{sb}");
        }
        finally
        {
            W.UnregisterGameTickListener(listener);
        }
    }

    private BlockPos OutletBeyond(BEShakingTable be, string anchor)
    {
        var (cell, face) = MachineChutes.World(be.Pos, be.Side, be.Rig!.Anchor(anchor)!.Value);
        return cell.AddCopy(face);
    }

    // Placed facing south; out of order refused; every stage by right-click with real items (the water line in lead);
    // the axle at the controller's power face turns it; crushed ore unscreened: its concentrate, one middlings rock
    // carrying the locked units and one tailings rock carrying the rest, each into its chest; with no chest at the
    // tailings face the tailings go on a heap of sand; broken, everything fitted comes back.
    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task Shaking_table_builds_washes_into_its_three_outlets_and_breaks_back()
    {
        var player = await CutterPlayer();
        var origin = await SluiceSite(880, 880, 6);
        World.SetBlock(TableFrame + "south", origin);
        await World.Ticks(5);
        var table = W.BlockAccessor.GetBlockEntity(origin) as BEShakingTable ?? throw new Xunit.Sdk.XunitException($"no shaking table at {origin}");
        table.PlaceCells();
        Assert.Equal(MachineLoad.Unassembled, table.Resistance);
        Assert.NotNull(table.Power);

        // out of order: the head's spring before the deck
        Assert.Equal(1, CutterClick(player, origin, CutterItem("seraphhorizons:spring-steel", 1))?.StackSize);
        Assert.True(_cutterHandled);
        Assert.Equal(0, table.Assembly!.Build.StagesDone);
        var cell = table.CellPos(new Int3(1, 0, 1));
        int clicks = 0;
        while (table.Assembly.Build.Next is { } _)
        {
            var (material, missing) = table.Assembly.Build.NextNeeds().First();
            string code = material.Codes.Last().Replace("*", "oak").Replace("chute-straight-oak", "chute-straight-ud");
            Assert.True(CutterClick(player, clicks++ % 2 == 0 ? origin : cell, CutterItem(code, missing)) == null, $"{code} x{missing} was not fitted");
            Assert.True(_cutterHandled);
        }
        Assert.True(table.Built);
        OilUp(table);
        Assert.True(table.ShowsPart("pipelead"));
        Assert.False(table.ShowsPart("pipecopper"));
        Assert.Equal(0.1f, table.Resistance, 3);

        // a chest at each outlet
        var conc = OutletBeyond(table, ShakingTabling.Concentrate);
        var mid = OutletBeyond(table, ShakingTabling.Middlings);
        var tail = OutletBeyond(table, ShakingTabling.Tailings);
        foreach (var p in new[] { conc, mid, tail })
            World.SetBlock("game:chest-east", p);
        await World.Ticks(2);

        const string feed = "seraphhorizons:crushedore-hematite-rich";
        Assert.Null(CutterClick(player, origin, SluiceStack(W, feed, 4)));
        Assert.Equal(4, table.InfeedSlot.StackSize);
        Assert.Equal(MachineState.NoPower, await StateSoon(table));

        // the axle on the controller's west face (native), a creative rotor
        var rotorPos = origin.AddCopy(table.PowerFace);
        World.SetBlock($"game:creativerotor-{table.PowerFace.Code}", rotorPos);
        await World.Ticks(2);
        var rotor = W.BlockAccessor.GetBlockEntity(rotorPos)!.GetBehavior<BEBehaviorMPCreativeRotor>()!;
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "speedSetting").SetValue(rotor, 10);
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "powerSetting").SetValue(rotor, 10);
        rotor.Blockentity.MarkDirty(true);
        await World.Until(() => Math.Abs(table.ShaftSpeed) >= 0.3f, 15000);
        using (VirtualClock.Start(World))
            await TableUntil(table, () => table.InfeedSlot.Empty && ChestCount(tail, feed) == 4, 120_000);
        await World.Ticks(5);

        // the concentrate: 4 rocks × units × 0.85 (unscreened) × 0.70, in whole concentrates
        var recovery = OreProcessingSystem.RecoveryFor(World.Api);
        double units = OreProducts.RawUnits("hematite", "rich")!.Value;
        int expected = (int)Math.Floor(4 * units * 0.85 * recovery.PassShare(recovery.Ore("hematite"), OreMachine.Table) / recovery.ConcentrateUnits + 1e-9);
        Assert.Equal(expected, ChestCount(conc, "seraphhorizons:concentrate-hematite"));
        // the middlings: a rock each, carrying only the locked 15 %, for the ball mill
        Assert.Equal(4, ChestCount(mid, feed));
        var m = OreStreamAttributes.Read((W.BlockAccessor.GetBlockEntity(mid) as BlockEntityContainer)!.Inventory.First(s => !s.Empty).Itemstack)!;
        Assert.Equal(units * 0.15, m.Units, 6);
        Assert.Equal(WorkedBy.None, m.Worked);
        // the tailings: the rest, worked by the table, nothing locked
        var t = OreStreamAttributes.Read((W.BlockAccessor.GetBlockEntity(tail) as BlockEntityContainer)!.Inventory.First(s => !s.Empty).Itemstack)!;
        Assert.True(t.Worked.HasFlag(WorkedBy.Table));
        Assert.Equal(0, t.Locked!.Value, 9);
        Assert.Equal(units * 0.85 * (1 - recovery.PassShare(recovery.Ore("hematite"), OreMachine.Table)), t.Units, 6);

        // with nothing at the tailings face: sand of the ore's rock, three quarters of a layer a rock
        W.BlockAccessor.SetBlock(0, tail);
        await World.Ticks(2);
        table.InfeedSlot.Itemstack = SluiceStack(W, "seraphhorizons:groundore-hematite-rich", 4);
        table.InfeedSlot.MarkDirty();
        using (VirtualClock.Start(World))
            await TableUntil(table, () => table.InfeedSlot.Empty, 120_000);
        await World.Ticks(5);
        var heap = W.BlockAccessor.GetBlock(tail).Code.ToString();
        Assert.StartsWith("game:sand-", heap);
        Assert.EndsWith("-3", heap);

        // broken: the frame and every stage's items
        W.BlockAccessor.SetBlock(0, rotorPos);
        var drops = W.BlockAccessor.GetBlock(origin).GetDrops(W, origin, player);
        Assert.Contains(drops, d => d.Collectible.Code.ToString() == TableFrame + "north");
        Assert.Equal(3, drops.Where(d => d.Collectible.Code.ToString() == "seraphhorizons:gear-stainless").Sum(d => d.StackSize));
        Assert.Equal(1, drops.Where(d => d.Collectible.Code.ToString() == "seraphhorizons:pipesection-lead").Sum(d => d.StackSize));
        Assert.Equal(4, drops.Where(d => d.Collectible.Code.ToString().StartsWith("game:chute-straight-")).Sum(d => d.StackSize));
        foreach (var p in new[] { conc, mid, tail })
            W.BlockAccessor.SetBlock(0, p);
        W.BlockAccessor.BreakBlock(origin, null);
    }
}
