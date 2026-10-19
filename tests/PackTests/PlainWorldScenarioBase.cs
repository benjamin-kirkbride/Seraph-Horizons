using Vintagestory.GameContent.Mechanics;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Machines;
using Atlas.Api;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.PackTests;

/// <summary>
/// What the two plain-world classes, <see cref="SharedWorldScenarios"/> and
/// <see cref="MillWorldScenarios"/>, both build their machines with: a site 40 above spawn on a granite
/// floor, its columns loaded (<see cref="CutterSite"/>), a survival player with empty hands
/// (<see cref="CutterHand"/>), real items (<see cref="CutterItem"/>), a right-click
/// (<see cref="CutterClick"/>) or one held (<see cref="Hold"/>), and the items lying around a site
/// or in the player's inventory. They began as the gear cutter's, then the draw bench's and the
/// squaring shear's, and every machine after took them up; the names stayed. A file of no test class
/// (ci/atlas-shards.md): a change here runs both.
/// </summary>
public abstract class PlainWorldScenarioBase : AtlasScenarioBase
{
    protected IWorldAccessor W => World.Api.World;

    private static ITestPlayer? _cutterHand;
    private static object? _cutterWorld;

    /// <summary>The machine scenarios' player, gearcutterhand, in survival with empty hands and inventory
    /// and its keys up, one per server. The trunk tool scenarios work with it too: the server takes 16
    /// players at most, and the other scenarios use the rest.</summary>
    protected async Task<ITestPlayer> CutterHand()
    {
        if (_cutterHand == null || !ReferenceEquals(_cutterWorld, World.Api))
        {
            _cutterHand = await World.JoinRetrying("gearcutterhand");
            _cutterWorld = World.Api;
        }
        var player = _cutterHand.Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        foreach (var inv in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
            foreach (var slot in player.InventoryManager.GetOwnInventory(inv) ?? Enumerable.Empty<ItemSlot>())
            {
                slot.Itemstack = null;
                slot.MarkDirty();
            }
        player.Entity.LeftHandItemSlot.Itemstack = null;
        player.Entity.LeftHandItemSlot.MarkDirty();
        player.Entity.Controls.CtrlKey = player.Entity.Controls.ShiftKey = false;
        return _cutterHand;
    }

    protected async Task<IPlayer> CutterPlayer() => (await CutterHand()).Player;

    protected ItemStack CutterItem(string code, int size = 1) =>
        W.GetItem(new AssetLocation(code)) is { } item ? new ItemStack(item, size)
        : W.GetBlock(new AssetLocation(code)) is { Id: > 0 } block ? new ItemStack(block, size)   // metal parts are a block
        : throw new Xunit.Sdk.XunitException($"no item {code}");

    /// <summary>The longest <see cref="CutterSite"/> waits for its columns: 30 s of real time, against the
    /// half a second they take (often none: a neighbouring site loaded them) and a 60 s watchdog.</summary>
    protected const int CutterSiteLoadTicks = 900;

    /// <summary>A spot 40 above spawn, its chunk columns loaded, cleared around on a granite floor.</summary>
    protected async Task<BlockPos> CutterSite(int dx, int dz, int reach = 5)
    {
        var origin = World.Spawn.AddCopy(dx, 40, dz);
        int size = GlobalConstants.ChunkSize;
        var columns = new List<BlockPos>();
        for (int cx = (origin.X - reach) / size; cx <= (origin.X + reach) / size; cx++)
            for (int cz = (origin.Z - reach) / size; cz <= (origin.Z + reach) / size; cz++)
                columns.Add(new BlockPos(cx * size, origin.Y, cz * size));
        // The sites lie hundreds of blocks from every player, where the server unloads a column it does not
        // keep loaded about 12 s after it was last fresh, and nothing asks for one again: a request taken
        // while the column was still loaded, and the column unloaded after, would leave the wait to run on.
        // So a column still missing is asked for again every second, and the wait is bounded well inside a
        // scenario's watchdog: a hang here abandons the whole shared world and fails every scenario after it
        // (#1169), where a failure fails one. This runs before any virtual clock, in real time.
        var sapi = World.Api as ICoreServerAPI;
        void Request()
        {
            if (sapi != null)
                foreach (var c in columns.Where(c => W.BlockAccessor.GetChunkAtBlockPos(c) == null))
                    sapi.WorldManager.LoadChunkColumnPriority(c.X / size, c.Z / size);
        }
        Request();
        int polls = 0;
        try
        {
            await World.Until(() =>
            {
                if (columns.All(c => W.BlockAccessor.GetChunkAtBlockPos(c) != null))
                    return true;
                if (++polls % 30 == 0)
                    Request();
                return false;
            }, CutterSiteLoadTicks);
        }
        catch (Atlas.Api.ScenarioTimeoutException)
        {
            var missing = columns.Where(c => W.BlockAccessor.GetChunkAtBlockPos(c) == null).Select(c => $"{c.X / size},{c.Z / size}");
            throw new Xunit.Sdk.XunitException($"the site at {origin}: chunk columns {string.Join("; ", missing)} did not load in {CutterSiteLoadTicks} ticks");
        }
        int floor = W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;
        for (int x = -reach; x <= reach; x++)
        for (int z = -reach; z <= reach; z++)
        {
            W.BlockAccessor.SetBlock(floor, origin.AddCopy(x, -1, z));
            for (int y = 0; y <= 4; y++)
                W.BlockAccessor.SetBlock(0, origin.AddCopy(x, y, z));
        }
        CutterKillItems(origin);
        return origin;
    }

    // Whether the last CutterClick was taken by the block.
    protected bool _cutterHandled;

    /// <summary>Right-clicks <paramref name="at"/> holding <paramref name="held"/>; returns what is left in the hand.</summary>
    protected ItemStack? CutterClick(IPlayer player, BlockPos at, ItemStack? held, bool ctrl = false, bool creative = false)
    {
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = held;
        slot.MarkDirty();
        player.Entity.Controls.CtrlKey = ctrl;
        if (creative)
            player.WorldData.CurrentGameMode = EnumGameMode.Creative;
        try
        {
            var sel = new BlockSelection { Position = at.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.5, 0.5) };
            _cutterHandled = W.BlockAccessor.GetBlock(at).OnBlockInteractStart(W, player, sel);
        }
        finally
        {
            player.Entity.Controls.CtrlKey = false;
            player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        }
        return slot.Itemstack;
    }

    protected Dictionary<string, int> CutterItemsNear(BlockPos around, int radius = 6)
    {
        var box = new Cuboidi(around.X - radius, around.Y - 3, around.Z - radius, around.X + radius, around.Y + 6, around.Z + radius);
        return World.EntitiesIn(box).OfType<EntityItem>().Where(e => e.Alive)
            .GroupBy(e => e.Itemstack.Collectible.Code.ToString())
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Itemstack.StackSize));
    }

    protected void CutterKillItems(BlockPos around, int radius = 6)
    {
        var box = new Cuboidi(around.X - radius, around.Y - 3, around.Z - radius, around.X + radius, around.Y + 6, around.Z + radius);
        foreach (var e in World.EntitiesIn(box).OfType<EntityItem>())
            e.Die(EnumDespawnReason.Removed);
    }

    /// <summary>A creative rotor at full speed against <paramref name="machine"/>'s power face, on its power cell;
    /// waits until the shaft turns fast. Returns where the rotor stands.</summary>
    protected async Task<BlockPos> PowerMachine(BEMachine machine)
    {
        var cell = machine.CellPos(machine.Rig!.PowerAnchor()!.Value.Cell);
        if (!cell.Equals(machine.Pos))
            Assert.IsType<BlockMachineCellPower>(W.BlockAccessor.GetBlock(cell));
        var face = machine.PowerFace;
        var rotorPos = cell.AddCopy(face);
        World.SetBlock($"game:creativerotor-{face.Code}", rotorPos);
        await World.Ticks(2);
        var rotor = W.BlockAccessor.GetBlockEntity(rotorPos)!.GetBehavior<BEBehaviorMPCreativeRotor>()!;
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "speedSetting").SetValue(rotor, 10);
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "powerSetting").SetValue(rotor, 10);
        rotor.Blockentity.MarkDirty(true);
        await World.Until(() => Math.Abs(machine.ShaftSpeed) >= 0.3f, 15000);
        return rotorPos;
    }

    /// <summary>
    /// Right-click held on <paramref name="at"/> with <paramref name="held"/> in hand for
    /// <paramref name="ticks"/> server ticks, as the game plays it: the interaction's start, a step
    /// each tick while the block says go on, then the stop: any hand machine (the press brake, the
    /// squaring shear, the amalgam pan, the rocker). Returns whether the start was the machine's and how
    /// many steps it went on for.
    /// </summary>
    protected async Task<(bool Started, int Steps)> Hold(IPlayer player, BlockPos at, int ticks, ItemStack? held = null)
    {
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = held;
        slot.MarkDirty();
        var sel = new BlockSelection { Position = at.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.5, 0.5) };
        var block = W.BlockAccessor.GetBlock(at);
        if (!block.OnBlockInteractStart(W, player, sel))
            return (false, 0);
        int steps = 0;
        float seconds = 0;
        for (int i = 0; i < ticks; i++)
        {
            await World.Ticks(1);
            seconds += 0.05f;
            if (!W.BlockAccessor.GetBlock(at).OnBlockInteractStep(seconds, W, player, sel))
                break;
            steps++;
        }
        W.BlockAccessor.GetBlock(at).OnBlockInteractStop(seconds, W, player, sel);
        return (true, steps);
    }

}
