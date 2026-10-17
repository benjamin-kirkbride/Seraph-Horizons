using Atlas.XUnit;
using SeraphHorizons.Mod.Jig;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.PackTests;

// seraphhorizons, Jig (#919) on the ore machines' shared game code (Machines/Game, #711): the jig in the pack's
// world, placed, built stage by stage with real items, driven by a vanilla creative rotor on its power face,
// fed by hand and by a vanilla chute onto its feed trough, stopped dry and started again with water, its
// concentrate into a chest at its concentrate face and its tailings into one at its tailings face (or onto a
// gravel heap there), the sluice's tailings worked on, and broken back into everything that went in. Built on
// granite floors 40 above spawn at x 780, z 780; the gear cutter's player.
public partial class SharedWorldScenarios
{
    private const string JigFrame = "seraphhorizons:jig-";

    private async Task<BEJig> PlaceJig(BlockPos pos, string side = "south")
    {
        World.SetBlock(JigFrame + side, pos);
        await World.Ticks(5);
        var be = W.BlockAccessor.GetBlockEntity(pos) as BEJig ?? throw new Xunit.Sdk.XunitException($"no jig at {pos}");
        be.PlaceCells();
        return be;
    }

    /// <summary>Every stage by right-clicks with real items, from the frame and a cell in turn.</summary>
    private void BuildJig(BEJig jig, IPlayer player, string pipe = "seraphhorizons:pipesection-copper")
    {
        var cell = jig.CellPos(new Int3(0, 1, 1));
        int i = 0;
        while (jig.Assembly!.Build.Next is { } _)
        {
            var (material, missing) = jig.Assembly.Build.NextNeeds().First();
            string code = material.Codes.Contains(pipe) ? pipe : material.Codes[0]
                .Replace("plank-*", "plank-oak").Replace("supportbeam-*", "supportbeam-oak").Replace("chute-straight-*", "chute-straight-we");
            Assert.True(CutterClick(player, i++ % 2 == 0 ? jig.Pos : cell, CutterItem(code, missing)) == null, $"{code} x{missing} was not fitted");
            Assert.True(_cutterHandled);
        }
        Assert.True(jig.Assembly.Ready);
    }

    private static int JigChestCount(BlockEntityContainer chest, string code) =>
        chest.Inventory.Where(s => s.Itemstack?.Collectible.Code.ToString() == code).Sum(s => s.StackSize);

    /// <summary>Waits on the virtual clock for <paramref name="done"/>, saying where the jig stands if it never is.</summary>
    private async Task JigUntil(BEJig jig, Func<bool> done, int ticks = 120000)
    {
        try
        {
            using (VirtualClock.Start(World))
                await World.Until(done, ticks);
        }
        catch (Exception e) when (e.GetType().Name.Contains("Timeout"))
        {
            string held = string.Join(", ", new[] { BEJig.Concentrate, BEOreMachine.Tailings }.SelectMany(jig.OutputSlots)
                .Where(s => !s.Empty).Select(s => $"{s.StackSize}x {s.Itemstack.Collectible.Code}"));
            throw new Xunit.Sdk.XunitException($"the jig never got there: {jig.State}, speed {jig.ShaftSpeed}, infeed {jig.InfeedSlot.StackSize}, " +
                $"water {jig.Water.Litres} of {jig.ReservoirCapacity}, turns {jig.Turns.Turns}, held [{held}]");
        }
    }

    private async Task<BlockEntityContainer> ChestAt(BEJig jig, string anchor)
    {
        var (cell, face) = MachineChutes.World(jig.Pos, jig.Side, jig.Rig!.Anchor(anchor)!.Value);
        var at = cell.AddCopy(face);
        World.SetBlock("game:chest-east", at);
        await World.Ticks(2);
        return (BlockEntityContainer)W.BlockAccessor.GetBlockEntity(at)!;
    }

    private ItemStack JigCrushed(string ore, string grade, int size, OreStream? stream = null)
    {
        var stack = CutterItem(OreProducts.CrushedCode(ore, grade), size);
        stack.Attributes.SetString(OreStreamAttributes.Rock, "granite");
        if (stream != null)
            OreStreamAttributes.Write(stack, stream);
        return stack;
    }

    [AtlasScenario]
    public void Jig_loads_with_its_frame_build_recipe_and_figures()
    {
        Assert.True(JigSystem.Applies(World.Api));
        foreach (var side in new[] { "north", "east", "south", "west" })
            Assert.IsType<BlockOreMachine>(W.GetBlock(new AssetLocation(JigFrame + side)));
        var block = (BlockOreMachine)W.GetBlock(new AssetLocation(JigFrame + "north"));
        Assert.Equal("jig", block.Machine);
        Assert.NotNull(block.Rig);
        Assert.Equal(["water", "bed"], block.ExtraRequires);
        var build = OreMachinesSystem.Of(World.Api).Build("jig")!;
        Assert.Equal(["timber", "sieve", "drive", "services"], build.Stages.Select(s => s.Id).ToArray());
        Assert.Empty(build.Wearing);
        Assert.Equal("Jig frame", new ItemStack(block).GetName());
        var recipe = Assert.Single(W.GridRecipes, r => r.Output?.Code?.ToString() == JigFrame + "north" && r.Enabled);
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.Code?.Path == "plank-*" && i.Quantity == 3);
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.Code?.Path == "supportbeam-*" && i.Quantity == 2);
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.IsTool == true && i.Code?.Path == "hammer-*");
        var spec = OreProcessingSystem.RecoveryFor(World.Api).Machine(OreMachine.Jig);
        Assert.Equal(8, spec.ItemsPerHour, 6);
        Assert.Equal(70, spec.WaterPerItem, 6);
        Assert.Equal(0.60, spec.Recovery, 6);
    }

    // Built stage by stage; then fed and run: its concentrate into a chest at the concentrate face and the rock,
    // carrying the rest, into a chest at the tailings face, on a steady load; dry it stops and resumes with
    // water; the sluice's tailings give the jig's share of what the sluice left; a chute onto the feed trough
    // feeds it; with nothing at the tailings face they go on a heap of gravel; broken, everything back.
    [AtlasScenario(TimeoutMs = 300_000)]
    public async Task Jig_builds_in_stages_and_jigs_crushed_ore_into_its_concentrate_and_tailings()
    {
        var hand = await CutterHand();
        var pos = await CutterSite(780, 780);
        // its chunk columns kept loaded, so the jig runs on the virtual clock with no player beside it
        var sapi = (ICoreServerAPI)World.Api;
        int size = GlobalConstants.ChunkSize;
        for (int cx = (pos.X - 5) / size; cx <= (pos.X + 5) / size; cx++)
            for (int cz = (pos.Z - 5) / size; cz <= (pos.Z + 5) / size; cz++)
                sapi.WorldManager.LoadChunkColumnPriority(cx, cz, new ChunkLoadOptions { KeepLoaded = true });
        await JigRun(hand.Player, pos);
    }

    private async Task JigRun(IPlayer player, BlockPos pos)
    {
        var jig = await PlaceJig(pos);
        var assembly = jig.Assembly!;
        Assert.Equal(MachineLoad.Unassembled, jig.Resistance);

        // out of order: the sieve's chain before the timber
        Assert.Equal(1, CutterClick(player, pos, CutterItem("game:metalchain-iron", 1))?.StackSize);
        Assert.True(_cutterHandled);
        Assert.Equal(0, assembly.Build.StagesDone);
        BuildJig(jig, player);
        Assert.True(jig.Built);
        Assert.True(jig.ShowsPart("pipecopper") && !jig.ShowsPart("pipelead"));
        // dry: no water drawn yet, nor the bed
        Assert.False(jig.ShowsPart("water"));
        Assert.False(jig.ShowsPart("bed"));
        Assert.Equal(0.1f, jig.Resistance, 3);

        var conc = await ChestAt(jig, BEJig.Concentrate);
        var tail = await ChestAt(jig, BEOreMachine.Tailings);

        // not what it jigs: raw ore and a stick
        Assert.False(jig.Takes(CutterItem("game:stick")));
        Assert.False(jig.Takes(new ItemStack(W.SearchItems(new AssetLocation("game:ore-rich-cassiterite-*")).First())));
        const string Feed = "seraphhorizons:crushedore-cassiterite-rich";
        Assert.Null(CutterClick(player, pos, JigCrushed("cassiterite", "rich", 6)));
        Assert.Equal(6, jig.InfeedSlot.StackSize);
        await World.Ticks(5);
        Assert.Equal(MachineState.NoPower, jig.State);

        // powered, but dry: it stops until water comes
        var power = jig.CellPos(jig.Rig!.Anchor("power")!.Value.Cell);
        var rotorPos = power.AddCopy(jig.PowerFace);
        World.SetBlock($"game:creativerotor-{jig.PowerFace.Code}", rotorPos);
        await World.Ticks(2);
        var rotor = W.BlockAccessor.GetBlockEntity(rotorPos)!.GetBehavior<BEBehaviorMPCreativeRotor>()!;
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "speedSetting").SetValue(rotor, 10);
        rotor.Blockentity.MarkDirty(true);
        await World.Until(() => Math.Abs(jig.ShaftSpeed) >= 0.3f, 15000);
        await World.Ticks(5);
        Assert.Equal(MachineState.Dry, jig.State);
        Assert.True(jig.ReservoirCapacity >= 70);

        // watered (as a ppex pipe would): 6 rocks at 70 L each, a reservoir at a time
        await JigUntil(jig, () =>
        {
            jig.FillWater(jig.ReservoirCapacity);
            return jig.InfeedSlot.Empty && jig.OutputSlots(BEOreMachine.Tailings).All(s => s.Empty);
        });
        Assert.True(jig.ShowsPart("water"));
        await World.Ticks(5);
        var recovery = OreProcessingSystem.RecoveryFor(World.Api);
        double units = OreProducts.RawUnits("cassiterite", "rich")!.Value;
        double share = recovery.PassShare(recovery.Ore("cassiterite"), OreMachine.Jig);
        int expected = (int)Math.Floor(6 * units * 0.85 * share / recovery.ConcentrateUnits + 1e-9);
        Assert.Equal(expected, JigChestCount(conc, "seraphhorizons:concentrate-cassiterite"));
        Assert.Equal(6, JigChestCount(tail, Feed));
        var stream = OreStreamAttributes.Read(tail.Inventory.First(s => !s.Empty).Itemstack)!;
        Assert.Equal(WorkedBy.Jig, stream.Worked);
        Assert.True(stream.Units < units);
        Assert.Equal(0.1f, jig.Resistance, 3);

        // the sluice's tailings: the jig catches its share of what the sluice left
        var sluiced = ConcentratorWash.Wash(recovery, OreMachine.Sluice, OreStream.FromRock("cassiterite", "rich", OreForm.Crushed, rock: "granite")).Tailings;
        foreach (var slot in tail.Inventory) { slot.Itemstack = null; slot.MarkDirty(); }
        foreach (var slot in conc.Inventory) { slot.Itemstack = null; slot.MarkDirty(); }
        Assert.Null(CutterClick(player, pos, JigCrushed("cassiterite", "rich", 4, sluiced)));
        await JigUntil(jig, () =>
        {
            jig.FillWater(jig.ReservoirCapacity);
            return jig.InfeedSlot.Empty && JigChestCount(tail, Feed) >= 4;
        });
        var after = OreStreamAttributes.Read(tail.Inventory.First(s => !s.Empty).Itemstack)!;
        Assert.Equal(WorkedBy.Sluice | WorkedBy.Jig, after.Worked);
        double catchable = sluiced.Units - recovery.LockedOf(sluiced);
        Assert.Equal(sluiced.Units - catchable * share, after.Units, 6);

        // a chute laid down onto the feed trough feeds the infeed: the chute leaves the rock in the trough
        // cell's buffer, and the jig draws it in on its own tick, which may come a pass later (waiting on the
        // chute alone, the tailings run below could start, and end, before the jig had it)
        var (inCell, inFace) = MachineChutes.World(pos, jig.Side, jig.Rig.Anchor("infeed")!.Value);
        var chutePos = inCell.AddCopy(inFace);
        World.SetBlock("game:chute-straight-ud", chutePos);
        await World.Ticks(2);
        var chute = (BlockEntityItemFlow)W.BlockAccessor.GetBlockEntity(chutePos)!;
        var trough = (BEOreMachineCell)W.BlockAccessor.GetBlockEntity(inCell)!;
        chute.Inventory[0].Itemstack = JigCrushed("cassiterite", "rich", 1);
        chute.Inventory[0].MarkDirty();
        await World.Until(() => chute.Inventory.Empty && trough.Buffer.Empty && !jig.InfeedSlot.Empty, 20000);

        // with nothing at the tailings face (in the upper row) they fall to the ground beside it, a heap of
        // gravel, a layer a rock
        var tailPos = tail.Pos.Copy();
        W.BlockAccessor.SetBlock(0, tailPos);
        await JigUntil(jig, () =>
        {
            jig.FillWater(jig.ReservoirCapacity);
            return jig.InfeedSlot.Empty && jig.OutputSlots(BEOreMachine.Tailings).All(s => s.Empty);
        });
        await World.Ticks(3);
        Assert.StartsWith("game:gravel-granite", W.BlockAccessor.GetBlock(tailPos.DownCopy()).Code.ToString());

        // broken: the frame and every stage's items
        W.BlockAccessor.SetBlock(0, rotorPos);
        var drops = W.BlockAccessor.GetBlock(pos).GetDrops(W, pos, player);
        Assert.Contains(drops, d => d.Collectible.Code.ToString() == JigFrame + "north");
        Assert.Equal(20, drops.Where(d => d.Collectible.Code.ToString() == "game:plank-oak").Sum(d => d.StackSize));
        Assert.Equal(1, drops.Where(d => d.Collectible.Code.ToString() == "seraphhorizons:pipesection-copper").Sum(d => d.StackSize));
        Assert.Equal(3, drops.Where(d => d.Collectible.Code.ToString() == "game:chute-straight-we").Sum(d => d.StackSize));
        foreach (var p in new[] { conc.Pos, chutePos, tailPos, tailPos.DownCopy() })
            W.BlockAccessor.SetBlock(0, p);
        W.BlockAccessor.BreakBlock(pos, null);
    }
}
