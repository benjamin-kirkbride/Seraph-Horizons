using Atlas.XUnit;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using SeraphHorizons.Mod.StampBattery;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.PackTests;

// seraphhorizons, StampBattery (#728): the stamp battery in the plain world on the shared ore machine code:
// built stage by stage with real items, then driven by a vanilla creative rotor against its power face,
// fed by hand and by a chute from above, its classified crushed ore taken on by a chute against its front;
// dry it stops, with its shoes worn out it stops until a new set goes in. Each scenario builds on a
// granite floor of its own 40 above spawn at x 620 to 660, z 620, and they share the gear cutter's player.
public partial class SharedWorldScenarios
{
    private const string BatteryFrame = "seraphhorizons:stampbattery-north";
    private const string BatteryOre = "game:ore-rich-galena-granite";
    private const string BatteryCrushed = "seraphhorizons:crushedore-galena-rich";

    private async Task<BEStampBattery> PlaceBattery(BlockPos pos)
    {
        World.SetBlock(BatteryFrame, pos);
        await World.Ticks(5);
        var be = W.BlockAccessor.GetBlockEntity(pos) as BEStampBattery ?? throw new Xunit.Sdk.XunitException($"no stamp battery at {pos}");
        be.PlaceCells();
        return be;
    }

    /// <summary>Every stage at once (the creative shortcut's fill), the shoes with the stamps.</summary>
    private static void FillBattery(BEStampBattery battery)
    {
        while (!battery.Built)
            Assert.True(battery.Assembly!.FillNext());
        Assert.True(battery.Assembly!.Ready);
        battery.MarkDirty(true);
    }

    /// <summary>A creative rotor at full speed against the power face; waits until the shaft turns.</summary>
    private async Task<BlockPos> PowerBattery(BEStampBattery battery)
    {
        var anchor = battery.Rig!.Anchor(OreMachineRig.Power)!.Value;
        var cell = battery.CellPos(anchor.Cell);
        var face = battery.PowerFace;
        var rotorPos = cell.AddCopy(face);
        World.SetBlock($"game:creativerotor-{face.Code}", rotorPos);
        await World.Ticks(2);
        var rotor = W.BlockAccessor.GetBlockEntity(rotorPos)!.GetBehavior<BEBehaviorMPCreativeRotor>()!;
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "speedSetting").SetValue(rotor, 10);
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "powerSetting").SetValue(rotor, 10);
        rotor.Blockentity.MarkDirty(true);
        await World.Until(() => battery.ShaftSpeed >= 0.3f, 15000);
        return rotorPos;
    }

    /// <summary>A straight chute against the output face, laid to take from the battery.</summary>
    private BlockPos BatteryOutputChute(BEStampBattery battery)
    {
        var anchor = battery.Rig!.Anchor("output")!.Value;
        var (cell, face) = MachineChutes.World(battery.Pos, battery.Side, anchor);
        var at = cell.AddCopy(face);
        World.SetBlock(face.Axis == EnumAxis.X ? "game:chute-straight-we" : "game:chute-straight-ns", at);
        return at;
    }

    /// <summary>A chest at the far end of the output chute. A chute with an open end drops what it carries
    /// into the world a few ticks after taking it (#1259), too soon for a wait to be sure of seeing it there.</summary>
    private async Task<BlockEntityContainer> BatteryOutputChest(BEStampBattery battery, BlockPos outChute)
    {
        var (_, face) = MachineChutes.World(battery.Pos, battery.Side, battery.Rig!.Anchor("output")!.Value);
        var at = outChute.AddCopy(face);
        World.SetBlock("game:chest-east", at);
        await World.Ticks(2);
        return (BlockEntityContainer)W.BlockAccessor.GetBlockEntity(at)!;
    }

    /// <summary>Keeps the site's chunk columns loaded, so the battery runs on the virtual clock with no
    /// player beside it (the shared player stays where the other scenarios count on it).</summary>
    private void KeepBatteryLoaded(BlockPos origin, int reach = 5)
    {
        var sapi = (ICoreServerAPI)World.Api;
        int size = GlobalConstants.ChunkSize;
        for (int cx = (origin.X - reach) / size; cx <= (origin.X + reach) / size; cx++)
            for (int cz = (origin.Z - reach) / size; cz <= (origin.Z + reach) / size; cz++)
                sapi.WorldManager.LoadChunkColumnPriority(cx, cz, new ChunkLoadOptions { KeepLoaded = true });
    }

    private void ClearBattery(BlockPos origin, int reach = 4)
    {
        for (int x = -reach; x <= reach; x++)
        for (int z = -reach; z <= reach; z++)
        for (int y = 0; y <= 5; y++)
            W.BlockAccessor.SetBlock(0, origin.AddCopy(x, y, z));
        CutterKillItems(origin);
    }

    [AtlasScenario]
    public void Stamp_battery_loads_with_its_frame_build_and_recipe()
    {
        Assert.True(StampBatterySystem.Applies(World.Api));
        foreach (var side in new[] { "north", "east", "south", "west" })
            Assert.IsType<BlockOreMachine>(W.GetBlock(new AssetLocation($"seraphhorizons:stampbattery-{side}")));
        var build = OreMachinesSystem.Of(World.Api).Build("stampbattery")!;
        Assert.Equal(["timber", "mortar", "stamps", "camshaft", "services"], build.Stages.Select(s => s.Id));
        Assert.Equal(1000, build.Wearing.Single(w => w.Id == "shoes").Uses);
        Assert.Contains(W.GridRecipes, r => r.Output?.Code?.ToString() == BatteryFrame);
        var spec = OreProcessingSystem.RecoveryFor(World.Api).Machine(OreMachine.StampBattery);
        Assert.Equal(6, spec.ItemsPerHour);
        Assert.Equal(40, spec.WaterPerItem);
    }

    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task Stamp_battery_is_built_by_right_click_in_order_and_gives_everything_back()
    {
        var origin = await CutterSite(620, 620, 4);
        var player = await CutterPlayer();
        var battery = await PlaceBattery(origin);
        Assert.False(battery.Built);
        // the stamps' steel rods before the timber: out of order, nothing taken
        Assert.Equal(5, CutterClick(player, origin, CutterItem("game:rod-steel", 5))?.StackSize);
        // the timber: a full stack of support beams a click, then the rest
        var beams = CutterItem("game:supportbeam-oak", 32);
        Assert.Null(CutterClick(player, origin, beams));
        Assert.Equal(23, CutterClick(player, origin, CutterItem("game:supportbeam-oak", 32))?.StackSize);
        foreach (var stage in battery.Assembly!.Spec.Stages)
            foreach (var material in stage.Materials)
            {
                int missing = battery.Assembly.Build.Missing(battery.Assembly.Spec.Stages.ToList().IndexOf(stage), stage.Materials.ToList().IndexOf(material));
                if (missing == 0)
                    continue;
                string pattern = material.Codes[0];
                string code = pattern.Replace("*", pattern.Contains("leather") ? "plain" : pattern.Contains("chute") ? "ns" : "oak");
                // a material held in several stacks goes in a full stack a click
                var item = CutterItem(code);
                int max = item.Collectible.MaxStackSize;
                while (missing > 0)
                {
                    int n = missing >= max ? max : missing;
                    var left = CutterClick(player, origin, CutterItem(code, n));
                    Assert.True(left == null, $"{code} x{n} was not fitted in {stage.Id}");
                    missing -= n;
                }
            }
        Assert.True(battery.Built);
        Assert.True(battery.Assembly.Ready);
        Assert.True(battery.ShowsPart("pipecopper") ^ battery.ShowsPart("pipelead"));

        // breaking it gives every part back, the shoes and the frame with them
        W.BlockAccessor.BreakBlock(origin, player);
        await World.Ticks(5);
        var drops = CutterItemsNear(origin, 6);
        Assert.Equal(41 + 5 + 1, drops.GetValueOrDefault("game:supportbeam-oak"));
        Assert.Equal(5, drops.GetValueOrDefault("game:poundercap-steel"));
        Assert.Equal(1, drops.GetValueOrDefault(BatteryFrame));
        ClearBattery(origin);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Stamp_battery_crushes_and_classifies_fed_by_hand_and_by_chute()
    {
        var origin = await CutterSite(640, 620, 4);
        var player = await CutterPlayer();
        KeepBatteryLoaded(origin);
        var battery = await PlaceBattery(origin);
        FillBattery(battery);
        battery.FillWater(battery.ReservoirCapacity);
        Assert.Equal(80, battery.ReservoirCapacity, 6);
        var outChute = BatteryOutputChute(battery);
        var chest = await BatteryOutputChest(battery, outChute);
        var rotor = await PowerBattery(battery);

        // by hand: right-click with raw ore puts it in the feed box's mouth
        Assert.Null(CutterClick(player, origin, CutterItem(BatteryOre, 1)));
        Assert.Equal(BatteryOre, battery.InfeedSlot.Itemstack?.Collectible.Code.ToString());
        // classified ore does not go in
        var classified = CutterItem(BatteryCrushed, 1);
        OreStreamAttributes.AddState(classified, OreState.Classified);
        Assert.False(battery.Takes(classified));

        using (VirtualClock.Start(World))
        {
            try
            {
                await World.Until(() => !chest.Inventory.Empty, 6_000);
            }
            catch (Exception e)
            {
                throw new Xunit.Sdk.XunitException($"nothing out: state {battery.State}, speed {battery.ShaftSpeed}, water {battery.Water.Litres}, "
                    + $"infeed {battery.InfeedSlot.Itemstack?.Collectible.Code} x{battery.InfeedSlot.StackSize}, turns {battery.Turns.Turns}, "
                    + $"held {string.Join(",", battery.OutputSlots("output").Where(s => !s.Empty).Select(s => s.Itemstack.Collectible.Code))} ({e.Message})");
            }
        }
        var crushed = chest.Inventory.First(s => !s.Empty).Itemstack;
        Assert.Equal(BatteryCrushed, crushed.Collectible.Code.ToString());
        Assert.True(OreStreamAttributes.StateOf(crushed).HasFlag(OreState.Classified));
        Assert.Equal("granite", OreStreamAttributes.Read(crushed)!.Rock);
        Assert.Equal(battery.ReservoirCapacity - 40, battery.Water.Litres, 6);
        Assert.Equal(1 - 1.0 / 1000, battery.Assembly!.Wearing.ShareLeft("shoes"), 9);

        // by chute from above: a chute standing on the infeed face takes ore straight down into the mouth
        var infeed = battery.Rig!.Anchor(OreMachineRig.Infeed)!.Value;
        var (inCell, inFace) = MachineChutes.World(battery.Pos, battery.Side, infeed);
        var inChutePos = inCell.AddCopy(inFace);
        World.SetBlock("game:chute-straight-ud", inChutePos);
        await World.Ticks(2);
        var inChute = (BlockEntityItemFlow)W.BlockAccessor.GetBlockEntity(inChutePos)!;
        inChute.Inventory[0].Itemstack = CutterItem("game:ore-medium-hematite-granite", 1);
        inChute.Inventory[0].MarkDirty();
        await UntilChuteFed(battery, inChute);
        // in the mouth, or already crushed (the turns left from the first rock) and on its way out
        Assert.True(battery.InfeedSlot.Itemstack?.Collectible.Code.ToString() == "game:ore-medium-hematite-granite"
            || battery.OutputSlots("output").Concat(chest.Inventory).Concat(W.BlockAccessor.GetBlockEntity<BlockEntityItemFlow>(outChute)!.Inventory)
                .Any(s => s.Itemstack?.Collectible.Code.Path.Contains("hematite") == true));

        foreach (var p in new[] { rotor, outChute, chest.Pos, inChutePos })
            W.BlockAccessor.SetBlock(0, p);
        ClearBattery(origin);
    }

    /// <summary>What lies on the ground in the cell in front of the battery's output face (code, count).</summary>
    private Dictionary<string, int> InFrontOfOutput(BEStampBattery battery)
    {
        var (cell, face) = MachineChutes.World(battery.Pos, battery.Side, battery.Rig!.Anchor("output")!.Value);
        var front = cell.AddCopy(face);
        return W.GetEntitiesAround(front.ToVec3d().Add(0.5, 0.5, 0.5), 0.75f, 1.5f, e => e is EntityItem && e.Alive).OfType<EntityItem>()
            .GroupBy(e => e.Itemstack.Collectible.Code.ToString())
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Itemstack.StackSize));
    }

    // The owner's ruling: an outlet with no chute never holds its product. With nothing at its front the battery
    // drops what it crushes on the ground just in front of the face and runs on; a product held at its outlet
    // (a world saved before) is dropped too. A container attached and full holds it, and the battery stops.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Stamp_battery_with_no_chute_drops_its_crushed_ore_in_front_and_runs_on()
    {
        var origin = await CutterSite(680, 620, 4);
        KeepBatteryLoaded(origin);
        var battery = await PlaceBattery(origin);
        FillBattery(battery);
        battery.FillWater(battery.ReservoirCapacity);
        // held from before: dropped once it ticks, with nothing at the face
        var held = battery.OutputSlots("output").First();
        held.Itemstack = CutterItem(BatteryCrushed, 1);
        held.MarkDirty();
        await World.Until(() => held.Empty, 2_000);
        await World.Until(() => InFrontOfOutput(battery).GetValueOrDefault(BatteryCrushed) == 1, 200);

        var rotor = await PowerBattery(battery);
        battery.InfeedSlot.Itemstack = CutterItem(BatteryOre, 2);
        battery.InfeedSlot.MarkDirty();
        bool blocked = false;
        using (VirtualClock.Start(World))
        {
            try
            {
                await World.Until(() =>
                {
                    blocked |= battery.State == MachineState.Blocked;
                    return battery.InfeedSlot.Empty && InFrontOfOutput(battery).GetValueOrDefault(BatteryCrushed) >= 3;
                }, 12_000);
            }
            catch (Exception e)
            {
                throw new Xunit.Sdk.XunitException($"not dropped: state {battery.State}, infeed x{battery.InfeedSlot.StackSize}, in front "
                    + $"{string.Join(",", InFrontOfOutput(battery).Select(kv => $"{kv.Value}x {kv.Key}"))}, near {string.Join(",", CutterItemsNear(origin).Select(kv => $"{kv.Value}x {kv.Key}"))} ({e.Message})");
            }
        }
        Assert.False(blocked, "the battery stopped blocked with no chute at its output");
        Assert.True(battery.OutputSlots("output").All(s => s.Empty));
        Assert.Equal(3, InFrontOfOutput(battery)[BatteryCrushed]);

        // a chest at the face, every slot full: attached, so the product waits in the battery and it stops
        CutterKillItems(origin);
        var (cell, face) = MachineChutes.World(battery.Pos, battery.Side, battery.Rig!.Anchor("output")!.Value);
        var chestPos = cell.AddCopy(face);
        World.SetBlock("game:chest-east", chestPos);
        await World.Ticks(2);
        var chest = (BlockEntityContainer)W.BlockAccessor.GetBlockEntity(chestPos)!;
        var stone = W.GetItem(new AssetLocation("game:stone-granite"))!;
        foreach (var slot in chest.Inventory)
        {
            slot.Itemstack = new ItemStack(stone, stone.MaxStackSize);
            slot.MarkDirty();
        }
        battery.FillWater(battery.ReservoirCapacity);   // two rocks spent it
        battery.InfeedSlot.Itemstack = CutterItem(BatteryOre, 2);
        battery.InfeedSlot.MarkDirty();
        using (VirtualClock.Start(World))
        {
            try
            {
                await World.Until(() => battery.State == MachineState.Blocked, 12_000);
            }
            catch (Exception e)
            {
                throw new Xunit.Sdk.XunitException($"not blocked: state {battery.State}, water {battery.Water.Litres}, infeed x{battery.InfeedSlot.StackSize} ({e.Message})");
            }
        }
        Assert.Equal(BatteryCrushed, battery.OutputSlots("output").First(s => !s.Empty).Itemstack.Collectible.Code.ToString());
        Assert.Equal(1, battery.InfeedSlot.StackSize);
        Assert.Empty(CutterItemsNear(origin));

        foreach (var p in new[] { rotor, chestPos })
            W.BlockAccessor.SetBlock(0, p);
        ClearBattery(origin);
    }

    [AtlasScenario(TimeoutMs = 90_000)]
    public async Task Stamp_battery_stops_dry_and_with_its_shoes_worn_out()
    {
        var origin = await CutterSite(660, 620, 4);
        var player = await CutterPlayer();
        KeepBatteryLoaded(origin);
        var battery = await PlaceBattery(origin);
        FillBattery(battery);
        BatteryOutputChute(battery);
        var rotor = await PowerBattery(battery);
        battery.InfeedSlot.Itemstack = CutterItem(BatteryOre, 2);
        battery.InfeedSlot.MarkDirty();
        // no water: dry, and nothing worked
        await World.Until(() => battery.State == MachineState.Dry, 10_000);
        Assert.Equal(2, battery.InfeedSlot.StackSize);

        // shoes worn out: it stops, and only a set of five renews them
        battery.FillWater(battery.ReservoirCapacity);
        battery.Assembly!.Wearing.Wear("shoes", 1000);
        await World.Until(() => battery.State == MachineState.Worn, 10_000);
        Assert.Equal(4, CutterClick(player, origin, CutterItem("game:poundercap-steel", 4))?.StackSize);
        Assert.Null(CutterClick(player, origin, CutterItem("game:poundercap-steel", 5)));
        Assert.True(battery.Assembly.Ready);
        await World.Until(() => battery.State == MachineState.Working, 10_000);

        W.BlockAccessor.SetBlock(0, rotor);
        ClearBattery(origin);
    }
}
