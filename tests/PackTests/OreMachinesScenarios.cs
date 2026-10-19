using Atlas.XUnit;
using SeraphHorizons.Mod.Jig;
using SeraphHorizons.Mod.MachineOil;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.PackTests;

// seraphhorizons, Machines/ (#711, #694): the ore machines' shared code in the pack's world: every build
// file loads and names items the game has, tailings heap up as vanilla layered gravel and sand in the
// ore's rock, and products leave by vanilla chute; the machines on a shaft take oil. Built 40 above spawn at
// x 540 to 560, z 540, and the oiled machines at x 380 to 540, z 580.
public partial class MillWorldScenarios
{
    [AtlasScenario, ReadsBootLog]
    public void Ore_machine_build_files_load_cleanly_and_name_items_the_game_has()
    {
        var logged = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error)
            .Where(e => e.Message.Contains("config/builds", StringComparison.Ordinal) || e.Message.Contains("OreMachinesSettings", StringComparison.Ordinal))
            .Select(e => $"[{e.Level}] {e.Message}").ToList();
        Assert.True(logged.Count == 0, "Logged:\n" + string.Join("\n", logged));
        var system = OreMachinesSystem.Of(World.Api);
        var builds = MachinesSystem.Of(World.Api).Builds;
        Assert.Contains("stampbattery", builds.Keys);
        Assert.Equal(0.05f, system.Config.MinSpeed);
        Assert.NotNull(system.Config.TailingsFor("sluice"));
        var missing = new List<string>();
        foreach (var (machine, build) in builds)
            foreach (var codes in build.Stages.SelectMany(s => s.Materials).Select(m => m.Codes).Concat(build.Wearing.Select(w => w.Codes)))
                foreach (var code in codes)
                {
                    var loc = new AssetLocation(code);
                    bool found = code.Contains('*')
                        ? W.SearchItems(loc).Length + W.SearchBlocks(loc).Length > 0
                        : W.GetItem(loc) is { Id: > 0 } || W.GetBlock(loc) is { Id: > 0 };
                    if (!found)
                        missing.Add($"{machine}: {code}");
                }
        Assert.True(missing.Count == 0, "Not in the game:\n" + string.Join("\n", missing));
        // the shared mechanical power consumer is registered for every machine's power cell
        Assert.Equal(typeof(BEBehaviorMachineMP), World.Api.ClassRegistry.GetBlockEntityBehaviorClass(BEBehaviorMachineMP.ClassName));
    }

    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task Tailings_heap_up_in_layers_of_the_ores_rock()
    {
        var origin = await CutterSite(540, 540, 3);
        // gravel: a full block in the tailings cell, then layers beside it
        int placed = Tailings.Discard(W, origin, TailingsForm.Gravel, "andesite", 11);
        Assert.Equal(11, placed);
        Assert.Equal("game:gravel-andesite", W.BlockAccessor.GetBlock(origin).Code.ToString());
        Assert.Equal("game:gravel-andesite-land-3", W.BlockAccessor.GetBlock(origin.NorthCopy()).Code.ToString());
        // more layers on the partial one
        Tailings.Discard(W, origin, TailingsForm.Gravel, "andesite", 2);
        Assert.Equal("game:gravel-andesite-land-5", W.BlockAccessor.GetBlock(origin.NorthCopy()).Code.ToString());
        // sand from another machine beside it is its own heap
        var sand = origin.AddCopy(0, 0, 3);
        Tailings.Discard(W, sand, TailingsForm.Sand, "chalk", 3);
        Assert.Equal("game:sand-chalk-3", W.BlockAccessor.GetBlock(sand).Code.ToString());
        // the rock of a raw ore stack is its own; a stack with none falls back
        var ore = W.SearchItems(new AssetLocation("game:ore-*-*-granite")).First();
        Assert.Equal("granite", Tailings.RockOf(new ItemStack(ore)));
        var tagged = new ItemStack(W.GetItem(new AssetLocation("game:stick")));
        tagged.Attributes.SetString("rock", "basalt");
        Assert.Equal("basalt", Tailings.RockOf(tagged));
        Assert.Equal("granite", Tailings.RockOf(new ItemStack(W.GetItem(new AssetLocation("game:stick")))));
        // a full heap takes no more: seven cells, 56 layers
        int more = Tailings.Discard(W, origin, TailingsForm.Gravel, "andesite", 100);
        Assert.True(more < 100, $"placed {more}");
        Assert.Equal(56 - 13, more);
        foreach (var o in TailingsHeap.Offsets)
            W.BlockAccessor.SetBlock(0, origin.AddCopy(o.X, o.Y, o.Z));
        W.BlockAccessor.SetBlock(0, sand);
    }

    [AtlasScenario(TimeoutMs = 60_000)]
    public async Task Products_leave_by_a_vanilla_chute_or_a_container_attached_to_the_face()
    {
        var origin = await CutterSite(560, 540, 3);
        var stone = W.GetItem(new AssetLocation("game:stone-granite"))!;
        // nothing beyond the face: nothing attached, nothing moves (the machine drops it in front)
        var slot = new DummySlot(new ItemStack(stone, 4));
        Assert.False(MachineChutes.Attached(W, origin, BlockFacing.SOUTH, slot));
        Assert.Equal(0, MachineChutes.TryPush(W, origin, BlockFacing.SOUTH, slot));

        // a straight chute laid north-south beyond the south face takes from the machine
        var chutePos = origin.SouthCopy();
        World.SetBlock("game:chute-straight-ns", chutePos);
        Assert.True(MachineChutes.Attached(W, origin, BlockFacing.SOUTH, slot));
        Assert.True(MachineChutes.TryPush(W, origin, BlockFacing.SOUTH, slot) > 0);
        var chute = (BlockEntityItemFlow)W.BlockAccessor.GetBlockEntity(chutePos)!;
        Assert.False(chute.Inventory.Empty);
        // now full (a chute takes only into an empty slot): still attached, so the machine holds what it makes
        Assert.Equal(0, MachineChutes.TryPush(W, origin, BlockFacing.SOUTH, new DummySlot(new ItemStack(stone, 1))));
        Assert.True(MachineChutes.Attached(W, origin, BlockFacing.SOUTH, slot));
        // a chute laid the other way (east-west) does not take from the south: not attached
        var across = origin.NorthCopy();
        World.SetBlock("game:chute-straight-we", across);
        Assert.False(MachineChutes.Attached(W, origin, BlockFacing.NORTH, slot));

        // a chest beyond the face takes the lot
        var chestPos = origin.EastCopy();
        World.SetBlock("game:chest-east", chestPos);
        var made = new ItemStack(stone, 3);
        Assert.True(MachineChutes.Attached(W, origin, BlockFacing.EAST, new DummySlot(made)));
        Assert.Equal(3, MachineChutes.TryPush(W, origin, BlockFacing.EAST, made));
        foreach (var p in new[] { chutePos, across, chestPos })
            W.BlockAccessor.SetBlock(0, p);
    }

    /// <summary>The ore machines that turn on a shaft, by frame, and their MachineOilSettings entries.</summary>
    private static readonly (string Frame, OilMachine Oil)[] OiledOreMachines =
    [
        ("jawbreaker", OilMachine.JawBreaker), ("stampbattery", OilMachine.StampBattery), ("arrastra", OilMachine.Arrastra),
        ("ballmill", OilMachine.BallMill), ("jig", OilMachine.Jig), ("shakingtable", OilMachine.ShakingTable),
        ("vanner", OilMachine.Vanner), ("reverberatory", OilMachine.ReverberatoryRoaster), ("parkeskettle", OilMachine.ParkesKettle),
    ];

    // Every ore machine on a shaft is built dry, its load on the shaft three times its own until oiled (the
    // shared MachineOiling); oil poured from a bucket fills its tank (scaled by its load: the jig's half a
    // bucket) and brings the load back; a job, a rock jigged, drains its DrainPerJob. Each machine's own
    // scenarios run it oiled (OilUp).
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Ore_machines_on_a_shaft_are_built_dry_take_oil_and_drain_it_by_the_job()
    {
        var hand = await CutterHand();
        var oil = MachineOilSystem.Of(World.Api).Config;
        BEOreMachine? jig = null;
        for (int i = 0; i < OiledOreMachines.Length; i++)
        {
            var (frame, kind) = OiledOreMachines[i];
            var pos = await CutterSite(380 + 20 * i, 580, 8);
            World.SetBlock($"seraphhorizons:{frame}-south", pos);
            await World.Ticks(5);
            var m = W.BlockAccessor.GetBlockEntity(pos) as BEOreMachine ?? throw new Xunit.Sdk.XunitException($"no {frame} at {pos}");
            m.PlaceCells();
            // no tank work before it is built: the token load, dry or not
            Assert.Equal(MachineLoad.Unassembled, m.Resistance);
            while (m.Assembly!.FillNext()) { }
            m.MarkDirty(true);
            Assert.True(m.Built, frame);
            var tank = m.Oiling ?? throw new Xunit.Sdk.XunitException($"the {frame} has no oil tank");
            Assert.Equal(kind, tank.Machine);
            Assert.True(tank.Dry, frame);
            Assert.Equal(oil.For(kind).Tank, tank.Tank.Capacity);
            float dry = m.Resistance;
            MachineTest.FillOil(m);
            Assert.True(dry > 0, frame);
            Assert.Equal(oil.DryResistanceMultiplier * m.Resistance, dry, 4);
            if (frame == "jig")
            {
                jig = m;
                continue;
            }
            W.BlockAccessor.BreakBlock(pos, null);
        }

        // the jig, oiled by hand from a bucket: as much as fits (500 points, five litres), the rest stays in it
        var j = (BEJig)jig!;
        var tankOf = j.Oiling!;
        tankOf.Tank = tankOf.Tank with { Points = 0 };
        Assert.Equal(0.3f, j.Resistance, 4);
        var bucket = CutterItem("game:woodbucket");
        ((BlockLiquidContainerBase)bucket.Block).SetContent(bucket, CutterItem("game:oilportion-flax", 1000));
        var left = CutterClick(hand.Player, j.Pos, bucket)!;
        Assert.Equal(500, tankOf.Tank.Points, 3);
        Assert.Equal(5f, ((BlockLiquidContainerBase)left.Block).GetCurrentLitres(left), 3);
        Assert.Equal(0.1f, j.Resistance, 4);
        var info = new System.Text.StringBuilder();
        j.GetBlockInfo(hand.Player, info);
        Assert.Contains("Oil: 500 of 500", info.ToString());

        // one rock jigged on a turning shaft: one job's oil (its columns kept loaded, so it runs on the virtual
        // clock with no player beside it)
        int size = GlobalConstants.ChunkSize;
        var sapi = (ICoreServerAPI)World.Api;
        for (int cx = (j.Pos.X - 5) / size; cx <= (j.Pos.X + 5) / size; cx++)
            for (int cz = (j.Pos.Z - 5) / size; cz <= (j.Pos.Z + 5) / size; cz++)
                sapi.WorldManager.LoadChunkColumnPriority(cx, cz, new ChunkLoadOptions { KeepLoaded = true });
        var power = j.CellPos(j.Rig!.Anchor("power")!.Value.Cell);
        var rotorPos = power.AddCopy(j.PowerFace);
        World.SetBlock($"game:creativerotor-{j.PowerFace.Code}", rotorPos);
        await World.Ticks(2);
        var rotor = W.BlockAccessor.GetBlockEntity(rotorPos)!.GetBehavior<BEBehaviorMPCreativeRotor>()!;
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "speedSetting").SetValue(rotor, 10);
        rotor.Blockentity.MarkDirty(true);
        await World.Until(() => Math.Abs(j.ShaftSpeed) >= 0.3f, 15000);
        Assert.Null(CutterClick(hand.Player, j.Pos, JigCrushed("cassiterite", "rich", 1)));
        j.FillWater(j.ReservoirCapacity);
        await JigUntil(j, () => j.InfeedSlot.Empty);
        Assert.Equal(500 - oil.Jig.DrainPerJob, tankOf.Tank.Points, 3);
        // turning idle costs nothing
        await World.Ticks(20);
        Assert.Equal(500 - oil.Jig.DrainPerJob, tankOf.Tank.Points, 3);
        W.BlockAccessor.SetBlock(0, rotorPos);
        W.BlockAccessor.BreakBlock(j.Pos, null);
    }

    /// <summary>
    /// Waits until a chute feeding <paramref name="machine"/> has emptied and the machine has drawn what it
    /// pushed. A chute does not reach the infeed: when the infeed anchor is not on the controller it empties
    /// into that cell's buffer (<see cref="BEMachineCell.Buffer"/>), and the machine pulls it into its
    /// infeed on its own tick (every 100 ms). Waiting for the chute alone ends with the item still in the
    /// buffer, before the infeed or anything worked has changed (#1176, #1259).
    /// </summary>
    private async Task UntilChuteFed(BEOreMachine machine, BlockEntityItemFlow chute, int ticks = 20000)
    {
        var cell = machine.Rig!.Anchor(OreMachineRig.Infeed)!.Value.Cell;
        var buffer = cell == Int3.Zero ? null : (BEMachineCell)W.BlockAccessor.GetBlockEntity(machine.CellPos(cell))!;
        await World.Until(() => chute.Inventory.Empty && (buffer == null || buffer.Buffer.Empty), ticks);
    }
}
