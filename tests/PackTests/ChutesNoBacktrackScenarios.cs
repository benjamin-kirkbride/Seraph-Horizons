using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod;
using SeraphHorizons.Mod.Machines;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons, ChutesNoBacktrack (#1348): a chute or Archimedes screw never pushes or
/// throws an item out of the face it came in by; a blocked item waits, and goes on once its way is
/// free. Each case holds a blocked item for 10 s of virtual time (a chute moves every 200 ms, a
/// screw every 500 ms) and fails on the first tick the item is back behind it or loose. The game
/// sends it back within a few moves: an elbow in front of a full chest pushes into its intake, a
/// straight chute fed by a machine (no direction) picks its back half the time, or throws out of
/// its back into air, and a ported screw with its top blocked pushes back out of its port.
/// <para>A plain world of its own, kept out of the <c>rest</c> shard (at its time limit): CI runs it
/// in <c>fixes</c> (ci/atlas-shards.md).</para>
/// </summary>
[AtlasWorld]
public class ChutesNoBacktrackScenarios : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;

    private const long ChuteHoldMs = 10_000;

    private static bool ChutesNoBacktrackPatched() =>
        Harmony.GetPatchInfo(ChutesNoBacktrack.TryPushInto)?.Prefixes.Any(p => p.owner == SeraphHorizonsSystem.HarmonyId) == true
        && Harmony.GetPatchInfo(ChutesNoBacktrack.TrySpitOut)?.Prefixes.Any(p => p.owner == SeraphHorizonsSystem.HarmonyId) == true;

    /// <summary>A spot 40 above spawn, the chunk columns around it kept loaded, cleared on a granite floor.</summary>
    private async Task<BlockPos> ChuteSite(int site)
    {
        const int reach = 5;
        var b = World.Spawn.AddCopy(12 * site, 40, 0);
        var sapi = (ICoreServerAPI)World.Api;
        int size = GlobalConstants.ChunkSize;
        var columns = new List<BlockPos>();
        for (int cx = (b.X - reach) / size; cx <= (b.X + reach) / size; cx++)
            for (int cz = (b.Z - reach) / size; cz <= (b.Z + reach) / size; cz++)
            {
                sapi.WorldManager.LoadChunkColumnPriority(cx, cz, new ChunkLoadOptions { KeepLoaded = true });
                columns.Add(new BlockPos(cx * size, b.Y, cz * size));
            }
        await World.Until(() => columns.All(c => W.BlockAccessor.GetChunkAtBlockPos(c) != null), 900);
        int floor = W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;
        for (int x = -3; x <= 3; x++)
            for (int z = -3; z <= 3; z++)
            {
                W.BlockAccessor.SetBlock(floor, b.AddCopy(x, -1, z));
                for (int y = 0; y <= 5; y++)
                    W.BlockAccessor.SetBlock(0, b.AddCopy(x, y, z));
            }
        await World.Ticks(5);
        return b;
    }

    private ItemStack ChuteFibre(BlockFacing? dir = null)
    {
        var stack = new ItemStack(W.GetItem(new AssetLocation("game:flaxfibers"))!, 1);
        if (dir != null)
            stack.Attributes.SetInt("chuteDir", dir.Index);
        return stack;
    }

    /// <summary>A chest at <paramref name="pos"/> with every slot full of fibre, so it takes none.</summary>
    private BlockEntityContainer ChuteFullChest(BlockPos pos)
    {
        World.SetBlock("game:chest-south", pos);
        var chest = (BlockEntityContainer)W.BlockAccessor.GetBlockEntity(pos)!;
        var item = W.GetItem(new AssetLocation("game:flaxfibers"))!;
        foreach (var slot in chest.Inventory)
        {
            slot.Itemstack = new ItemStack(item, item.MaxStackSize);
            slot.MarkDirty();
        }
        return chest;
    }

    private BlockEntityItemFlow ChuteFlow(BlockPos pos) => (BlockEntityItemFlow)W.BlockAccessor.GetBlockEntity(pos)!;

    private int ChuteLoose(BlockPos b) =>
        W.GetEntitiesInsideCuboid(b.AddCopy(-4, -3, -4), b.AddCopy(5, 6, 5), e => e is EntityItem)
            .Sum(e => ((EntityItem)e).Itemstack.StackSize);

    /// <summary>Runs <paramref name="ms"/> of virtual time, failing on the first tick
    /// <paramref name="back"/> holds or <paramref name="b"/>'s area has a loose item.</summary>
    private async Task ChuteHolds(BlockPos b, long ms, Func<bool> back, string what)
    {
        using var clock = VirtualClock.Start(World);
        long start = W.ElapsedMilliseconds;
        while (W.ElapsedMilliseconds - start < ms)
        {
            await World.Ticks(1);
            Assert.False(back(), $"{what} after {W.ElapsedMilliseconds - start} ms");
            Assert.Equal(0, ChuteLoose(b));
        }
    }

    /// <summary>Clears the site: the item flows and chests emptied first, so nothing drops.</summary>
    private async Task ClearChuteSite(BlockPos b, params BlockPos[] cells)
    {
        foreach (var p in cells)
        {
            if (W.BlockAccessor.GetBlockEntity(p) is BlockEntityContainer c)
                foreach (var slot in c.Inventory)
                {
                    slot.Itemstack = null;
                    slot.MarkDirty();
                }
            World.SetBlock("game:air", p);
        }
        foreach (var e in W.GetEntitiesInsideCuboid(b.AddCopy(-4, -3, -4), b.AddCopy(5, 6, 5), e => e is EntityItem).ToList())
            e.Die(EnumDespawnReason.Removed);
        await World.Ticks(2);
    }

    // The case seen in play: straight chute -> elbow-up-north -> a machine's full infeed (here a chest).
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Chute_elbow_in_front_of_a_full_container_keeps_its_item()
    {
        Assert.True(ChutesNoBacktrackPatched());
        var b = await ChuteSite(0);
        var elbowPos = b.UpCopy(2);
        var chestPos = elbowPos.DownCopy();
        var straightPos = elbowPos.SouthCopy();
        var endPos = straightPos.SouthCopy();
        var chest = ChuteFullChest(chestPos);
        World.SetBlock("game:chute-elbow-up-north", elbowPos);
        World.SetBlock("game:chute-straight-ns", straightPos);
        World.SetBlock("game:rock-granite", endPos);
        await World.Ticks(3);
        var elbow = ChuteFlow(elbowPos);
        var straight = ChuteFlow(straightPos);
        straight.Inventory[0].Itemstack = ChuteFibre(BlockFacing.NORTH);
        straight.Inventory[0].MarkDirty();

        using (VirtualClock.Start(World))
            await World.Until(() => !elbow.Inventory.Empty, 5000);
        Assert.Equal(BlockFacing.SOUTH, elbow.LastReceivedFromDir);
        // The game pushes it back into the straight chute within a move or two.
        await ChuteHolds(b, ChuteHoldMs, () => !straight.Inventory.Empty || elbow.Inventory.Empty, "the elbow pushed its item back");

        // Room in the chest: it goes on down.
        chest.Inventory[0].Itemstack = null;
        chest.Inventory[0].MarkDirty();
        using (VirtualClock.Start(World))
            await World.Until(() => elbow.Inventory.Empty && !chest.Inventory[0].Empty, 5000);
        Assert.Null(elbow.LastReceivedFromDir);
        await ClearChuteSite(b, elbowPos, chestPos, straightPos, endPos);
    }

    // A machine's product has no chute history: tagged with the machine's direction, it never goes
    // back into the empty chute behind, nor out of the back into air once that chute is gone.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Chute_fed_by_a_machine_never_sends_its_item_back()
    {
        Assert.True(ChutesNoBacktrackPatched());
        var b = await ChuteSite(1);
        var midPos = b.UpCopy(2);
        var behindPos = midPos.SouthCopy();
        var chestPos = midPos.NorthCopy();
        ChuteFullChest(chestPos);
        World.SetBlock("game:chute-straight-ns", midPos);
        World.SetBlock("game:chute-straight-ns", behindPos);
        World.SetBlock("game:rock-granite", behindPos.SouthCopy());
        await World.Ticks(3);
        var mid = ChuteFlow(midPos);
        var behind = ChuteFlow(behindPos);

        // As a machine's outlet south of it does, from the cell behind.
        Assert.Equal(1, MachineChutes.TryPush(W, behindPos, BlockFacing.NORTH, ChuteFibre()));
        Assert.Equal(BlockFacing.NORTH.Index, mid.Inventory[0].Itemstack.Attributes.GetInt("chuteDir", -1));
        Assert.Equal(BlockFacing.SOUTH, mid.LastReceivedFromDir);
        // The game strips the direction and picks the back half the time.
        await ChuteHolds(b, ChuteHoldMs, () => !behind.Inventory.Empty || mid.Inventory.Empty, "the chute sent its item back");

        // The chute behind gone: the game throws out of the back into air (TrySpitOut before its guard).
        World.SetBlock("game:air", behindPos);
        await ChuteHolds(b, ChuteHoldMs, () => mid.Inventory.Empty, "the chute threw its item out of its back");
        await ClearChuteSite(b, midPos, chestPos, behindPos.SouthCopy());
    }

    // A bottom ported screw fed through its port, its top blocked: the game pushes the item back out
    // of the port into the chute that fed it. It waits instead, and goes up once the top is free.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Chute_ported_screw_with_its_top_blocked_never_pushes_out_of_its_port()
    {
        Assert.True(ChutesNoBacktrackPatched());
        var b = await ChuteSite(2);
        World.SetBlock("game:angledgears-eu", b);
        var screwPos = b.UpCopy();
        World.SetBlock("game:archimedesscrew-ported-north", screwPos);
        var capPos = screwPos.UpCopy();
        World.SetBlock("game:rock-granite", capPos);
        var chutePos = screwPos.NorthCopy();
        World.SetBlock("game:chute-straight-ns", chutePos);
        World.SetBlock("game:rock-granite", chutePos.NorthCopy());
        var rotorPos = b.EastCopy();
        World.SetBlock("game:creativerotor-east", rotorPos);
        await World.Ticks(3);
        var rotor = W.BlockAccessor.GetBlockEntity(rotorPos)!.GetBehavior<BEBehaviorMPCreativeRotor>()!;
        AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "speedSetting").SetValue(rotor, 10);
        AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "powerSetting").SetValue(rotor, 10);
        rotor.Blockentity.MarkDirty(true);
        var screw = (BlockEntityArchimedesScrew)W.BlockAccessor.GetBlockEntity(screwPos)!;
        var mp = screw.GetBehavior<BEBehaviorMPArchimedesScrew>();
        await World.Until(() => mp?.Network != null && Math.Abs(mp.Network.Speed) > 0.01f, 15000);
        await World.Ticks(30);
        var chute = ChuteFlow(chutePos);
        chute.Inventory[0].Itemstack = ChuteFibre(BlockFacing.SOUTH);
        chute.Inventory[0].MarkDirty();

        using (VirtualClock.Start(World))
            await World.Until(() => !screw.Inventory.Empty, 5000);
        Assert.Equal(BlockFacing.NORTH, screw.LastReceivedFromDir);
        await ChuteHolds(b, ChuteHoldMs, () => !chute.Inventory.Empty || screw.Inventory.Empty, "the screw pushed its item out of its port");

        // A chest on top in place of the granite: up it goes.
        World.SetBlock("game:chest-south", capPos);
        var chest = (BlockEntityContainer)W.BlockAccessor.GetBlockEntity(capPos)!;
        using (VirtualClock.Start(World))
            await World.Until(() => screw.Inventory.Empty && !chest.Inventory.Empty, 10000);
        Assert.True(chute.Inventory.Empty);
        await ClearChuteSite(b, capPos, screwPos, chutePos, chutePos.NorthCopy(), rotorPos, b);
    }
}
