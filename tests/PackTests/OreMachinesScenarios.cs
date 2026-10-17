using Atlas.XUnit;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

// seraphhorizons, Machines/ (#711, #694): the ore machines' shared code in the pack's world: every build
// file loads and names items the game has, tailings heap up as vanilla layered gravel and sand in the
// ore's rock, and products leave by vanilla chute. Built 40 above spawn at x 540 to 560, z 540.
public partial class SharedWorldScenarios
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
        Assert.Contains("stampbattery", system.Builds.Keys);
        Assert.Equal(0.05f, system.Config.MinSpeed);
        Assert.NotNull(system.Config.TailingsFor("sluice"));
        var missing = new List<string>();
        foreach (var (machine, build) in system.Builds)
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
    public async Task Products_leave_by_a_vanilla_chute_or_a_container_and_stay_without_one()
    {
        var origin = await CutterSite(560, 540, 3);
        var stone = W.GetItem(new AssetLocation("game:stone-granite"))!;
        // nothing beyond the face: not chuted on, nothing moves
        var slot = new DummySlot(new ItemStack(stone, 4));
        Assert.False(MachineChutes.ChutedOn(W, origin, BlockFacing.SOUTH, slot));
        Assert.Equal(0, MachineChutes.TryPush(W, origin, BlockFacing.SOUTH, slot));

        // a straight chute laid north-south beyond the south face takes from the machine
        var chutePos = origin.SouthCopy();
        World.SetBlock("game:chute-straight-ns", chutePos);
        Assert.True(MachineChutes.ChutedOn(W, origin, BlockFacing.SOUTH, slot));
        Assert.True(MachineChutes.TryPush(W, origin, BlockFacing.SOUTH, slot) > 0);
        var chute = (BlockEntityItemFlow)W.BlockAccessor.GetBlockEntity(chutePos)!;
        Assert.False(chute.Inventory.Empty);
        // a chute laid the other way (east-west) does not take from the south
        var across = origin.NorthCopy();
        World.SetBlock("game:chute-straight-we", across);
        Assert.False(MachineChutes.ChutedOn(W, origin, BlockFacing.NORTH, slot));

        // a chest beyond the face takes the lot
        var chestPos = origin.EastCopy();
        World.SetBlock("game:chest-east", chestPos);
        var made = new ItemStack(stone, 3);
        Assert.Equal(3, MachineChutes.TryPush(W, origin, BlockFacing.EAST, made));
        foreach (var p in new[] { chutePos, across, chestPos })
            W.BlockAccessor.SetBlock(0, p);
    }
}
