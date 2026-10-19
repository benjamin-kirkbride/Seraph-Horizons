using Atlas.XUnit;
using SeraphHorizons.Mod.Arrastra;
using SeraphHorizons.Mod.Arrastra.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.PackTests;

// seraphhorizons, Arrastra (mods-src/seraphhorizons/Arrastra/, #732): the arrastra in the plain world on
// the shared ore machine, built by right-clicks with real items, loaded with crushed ore and watered from a
// bucket by hand, driven by a real mechanical power network (a vanilla creative rotor against its power
// face) on the virtual clock (tests/PackTests/README.md) until its charge is ground, then drawn out. Built
// on a granite floor of its own 40 above spawn at x 600 to 640, z 700, with the gear cutter's player
// (gearcutterhand).
public partial class MillWorldScenarios
{
    private const string ArrastraFrame = "seraphhorizons:arrastra-north";
    private const string PoorHematite = "seraphhorizons:crushedore-hematite-poor";

    /// <summary>One concrete item for each material of each stage, in order.</summary>
    private static readonly (string Code, int Count)[] ArrastraOrder =
    [
        ("game:cobblestone-granite", 1), ("game:drystone-granite", 1), ("game:plank-oak", 2), ("game:metalnailsandstrips-iron", 1),
        ("game:woodenaxle-ud", 1), ("game:plank-oak", 4), ("game:metalplate-iron", 1), ("game:metalnailsandstrips-iron", 2), ("game:metalnailsandstrips-steel", 1),
        ("game:supportbeam-oak", 2), ("game:metalnailsandstrips-iron", 1),
        ("game:stone-granite", 4), ("game:metalchain-iron", 1),
    ];

    private async Task<BEArrastra> PlaceArrastra(BlockPos pos)
    {
        World.SetBlock(ArrastraFrame, pos);
        await World.Ticks(5);
        var be = W.BlockAccessor.GetBlockEntity(pos) as BEArrastra ?? throw new Xunit.Sdk.XunitException($"no arrastra at {pos}");
        be.PlaceCells();
        await World.Ticks(2);
        return be;
    }

    /// <summary>Every stage by right-clicks with real items, alternately on the frame and its other cells.</summary>
    private void BuildArrastra(BEArrastra arrastra, IPlayer player)
    {
        var cells = arrastra.Rig!.OtherCells.Select(c => arrastra.CellPos(c.Pos)).ToList();
        int i = 0;
        foreach (var (code, count) in ArrastraOrder)
        {
            var at = i % 2 == 0 ? arrastra.Pos : cells[i % cells.Count];
            i++;
            Assert.True(CutterClick(player, at, CutterItem(code, count)) == null, $"{code} x{count} was not fitted");
        }
        Assert.True(arrastra.Built);
        OilUp(arrastra);
    }

    private static string ArrastraInfo(BEArrastra arrastra, IPlayer player)
    {
        var sb = new System.Text.StringBuilder();
        arrastra.GetBlockInfo(player, sb);
        return sb.ToString();
    }

    [AtlasScenario]
    public void Arrastra_loads_with_its_block_build_and_recipe()
    {
        Assert.True(ArrastraSystem.Applies(World.Api));
        foreach (var side in new[] { "north", "east", "south", "west" })
            Assert.IsType<BlockMachine>(W.GetBlock(new AssetLocation($"seraphhorizons:arrastra-{side}")));
        var build = MachinesSystem.Of(World.Api).Build("arrastra");
        Assert.NotNull(build);
        Assert.Equal(FeedMode.Batch, build!.Feed);
        Assert.Equal(new[] { "pit", "post", "arms", "stones" }, build.Stages.Select(s => s.Id));
        Assert.Empty(build.Wearing);
        // the frame's grid recipe
        Assert.Contains(World.Api.World.GridRecipes, r => r.Output?.Code?.ToString() == ArrastraFrame);
        // what it grinds and what comes out exist
        Assert.True(W.GetItem(new AssetLocation(PoorHematite)) is { Id: > 0 });
        Assert.True(W.GetItem(new AssetLocation(ArrastraCharge.GroundCode(PoorHematite)!)) is { Id: > 0 });
    }

    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task Arrastra_is_built_charged_watered_by_bucket_ground_by_its_axle_and_drawn_by_hand()
    {
        var pos = await CutterSite(600, 700, 4);
        var hand = await CutterHand();
        var player = hand.Player;
        // nearby, so the site's chunks stay loaded through the grind's thousands of ticks
        await hand.TeleportTo(pos.AddCopy(-3, 0, -3));
        var arrastra = await PlaceArrastra(pos);
        Assert.False(arrastra.Built);

        // not built: crushed ore is not a part, and nothing goes in
        CutterClick(player, pos, CutterItem(PoorHematite));
        Assert.True(arrastra.InfeedSlot.Empty);
        // out of order: the drag stones before the pit
        Assert.NotNull(CutterClick(player, pos, CutterItem("game:metalchain-iron")));

        BuildArrastra(arrastra, player);
        Assert.True(arrastra.ShowsPart("stones"));
        Assert.False(arrastra.ShowsPart("charge"));

        // a rock of poor hematite tipped in; another ore is refused (a charge is one ore and grade)
        Assert.Equal(0, CutterClick(player, pos, CutterItem(PoorHematite))?.StackSize ?? 0);
        Assert.Equal(1, arrastra.InfeedSlot.StackSize);
        Assert.Equal(1, CutterClick(player, pos, CutterItem("seraphhorizons:crushedore-hematite-medium"))!.StackSize);
        Assert.Equal(1, arrastra.InfeedSlot.StackSize);
        Assert.True(arrastra.ShowsPart("charge"));

        // powered but dry: it does not grind
        var cell = arrastra.CellPos(arrastra.Rig!.Anchor(OreMachineRig.Power)!.Value.Cell);
        var face = arrastra.PowerFace;
        var rotorPos = cell.AddCopy(face);
        World.SetBlock($"game:creativerotor-{face.Code}", rotorPos);
        await World.Ticks(2);
        var rotor = W.BlockAccessor.GetBlockEntity(rotorPos)!.GetBehavior<BEBehaviorMPCreativeRotor>()!;
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "speedSetting").SetValue(rotor, 10);
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "powerSetting").SetValue(rotor, 10);
        rotor.Blockentity.MarkDirty(true);
        await World.Until(() => arrastra.ShaftSpeed >= 0.3f, 15000);
        await World.Until(() => arrastra.State == MachineState.Dry, 5000);
        Assert.Equal(0, arrastra.Charge.Turns);
        Assert.Contains(Lang.Get("seraphhorizons:arrastra-state-dry"), ArrastraInfo(arrastra, player));
        // its load is the arrastra's, constant once built
        Assert.Equal(0.1f, arrastra.Resistance, 3);

        // a bucket of 10 L: a rock's water
        var bucket = CutterClick(player, pos, WaterBucket(10))!;
        Assert.Equal(10, arrastra.Water.Litres, 3);
        Assert.Equal(0, SeraphHorizons.Mod.Rocker.RockerWater.Litres(bucket), 3);

        await World.Until(() => arrastra.Charge.Turns > 0, 5000);
        Assert.Equal(MachineState.Working, arrastra.State);
        // a rock is half a charge: about 215 shaft turns, some 14,000 ticks at this speed
        using (var clock = VirtualClock.Start(World))
            for (int k = 0; k < 60 && !arrastra.IsGround; k++)
                await World.Ticks(600);
        Assert.True(arrastra.IsGround, $"not ground: {arrastra.Charge.Turns:F1} turns, {arrastra.State}, {arrastra.Water.Litres} L");
        Assert.Equal(PitState.Ground, arrastra.Charge.State);
        var ground = arrastra.InfeedSlot.Itemstack!;
        Assert.Equal("seraphhorizons:groundore-hematite-poor", ground.Collectible.Code.ToString());
        Assert.Equal(1, ground.StackSize);
        // nothing lost, and poor ore's lock lifted
        var stream = OreStreamAttributes.Read(ground)!;
        Assert.True(stream.State.HasFlag(OreState.Ground));
        Assert.Equal(OreProducts.RawUnits("hematite", "poor")!.Value, stream.Units, 6);
        Assert.Equal(0, arrastra.Water.Litres, 3);
        Assert.True(arrastra.ShowsPart("paste"));
        Assert.False(arrastra.ShowsPart("charge"));
        // no rock goes in on the paste
        Assert.Equal(1, CutterClick(player, pos, CutterItem(PoorHematite))!.StackSize);

        // drawn by hand
        var cells = arrastra.Rig.OtherCells.Select(c => arrastra.CellPos(c.Pos)).ToList();
        CutterClick(player, cells[0], null);
        Assert.True(arrastra.InfeedSlot.Empty);
        Assert.True(BenchHeld(player, "seraphhorizons:groundore-hematite-poor") >= 1);
        Assert.False(arrastra.ShowsPart("paste"));

        // breaking gives back the frame, every stage and what is in the pit
        CutterClick(player, pos, CutterItem(PoorHematite));
        CutterKillItems(pos);
        W.BlockAccessor.SetBlock(0, rotorPos);
        W.GetBlock(new AssetLocation(ArrastraFrame))!.OnBlockBroken(W, pos, player);
        await World.Ticks(5);
        var drops = CutterItemsNear(pos);
        Assert.Equal(1, drops.GetValueOrDefault(ArrastraFrame));
        Assert.Equal(4, drops.GetValueOrDefault("game:stone-granite"));
        Assert.Equal(1, drops.GetValueOrDefault("game:woodenaxle-ud"));
        Assert.Equal(1, drops.GetValueOrDefault(PoorHematite));
        foreach (var c in cells)
            Assert.Equal(0, W.BlockAccessor.GetBlock(c).Id);
        CutterKillItems(pos);
    }
}
