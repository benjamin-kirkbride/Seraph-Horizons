using System.Runtime.CompilerServices;
using Atlas.XUnit;
using SeraphHorizons.Mod;
using SeraphHorizons.Mod.BuckingSawmill;
using SeraphHorizons.Mod.BuckingSawmill.Core;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Rosser;
using SeraphHorizons.Mod.Rosser.Core;
using SeraphHorizons.Mod.TrunkEntities;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// What the two woodworking scenario classes share: <see cref="WoodworkingScenarios"/> (the mill,
/// the stations and the rest of the chain) and <see cref="WoodworkingRosserScenarios"/> (the rosser
/// and debarked trunks). Each class boots a server of its own (they are two CI shards), with the
/// same world and ModConfig; this base carries no attributes of its own, so each class repeats
/// <c>[AtlasWorld]</c>, <c>[AtlasDataFiles]</c> and <c>[TestCaseOrderer]</c>. The helpers here are
/// the bucking mill's and the shared player's, which the rosser's scenarios use too (a rosser in
/// line feeds a mill); a helper only one class uses stays in that class's partial files.
/// </summary>
public abstract class WoodworkingScenarioBase : AtlasScenarioBase
{
    protected readonly ITestOutputHelper output;

    protected WoodworkingScenarioBase(ITestOutputHelper output) => this.output = output;

    protected IWorldAccessor W => World.Api.World;

    protected BuckingSawmillSystem Mod => BuckingSawmillSystem.Of(World.Api);
    protected Rig Rig => Mod.Rig ?? throw new Xunit.Sdk.XunitException("the rig did not load");

    protected const string Iw = "immersivewoodworking";

    protected Block BlockOf(string code) =>
        W.GetBlock(new AssetLocation(code)) is { Id: > 0 } block ? block : throw new Xunit.Sdk.XunitException($"no block {code}");

    protected ItemStack ItemOf(string code, int size = 1) =>
        W.GetItem(new AssetLocation(code)) is { } item ? new ItemStack(item, size) : throw new Xunit.Sdk.XunitException($"no item {code}");

    /// <summary>A spot high above the ground, cleared well beyond the mill's footprint.</summary>
    protected BlockPos Sky(int dx, int dz)
    {
        var origin = World.Spawn.AddCopy(dx, 30, dz);
        for (int x = -9; x <= 9; x++)
        for (int y = -1; y <= 6; y++)
        for (int z = -9; z <= 9; z++)
            W.BlockAccessor.SetBlock(0, origin.AddCopy(x, y, z));
        return origin;
    }

    protected async Task<BEBuckingMill> PlaceMill(BlockPos pos, string side)
    {
        World.SetBlock($"seraphhorizons:buckingmill-frame-{side}", pos);
        await World.Ticks(5);
        return W.BlockAccessor.GetBlockEntity(pos) as BEBuckingMill
               ?? throw new Xunit.Sdk.XunitException($"no mill block entity at {pos}");
    }

    // One player for every mill scenario (millhand) on a server: the world takes at most 16
    // clients, more than the scenarios would join each with its own. Keyed by the server's API, as
    // each scenario class boots a server of its own in the one test process.
    private static readonly ConditionalWeakTable<ICoreAPI, IPlayer> SharedPlayers = new();

    /// <summary>The mill scenarios' player, in survival with an empty inventory, nothing carried and its keys up. The name is
    /// only for reading the scenarios.</summary>
    protected async Task<IPlayer> Player(string name)
    {
        if (!SharedPlayers.TryGetValue(World.Api, out var player))
        {
            player = (await World.JoinPlayer("millhand")).Player;
            SharedPlayers.AddOrUpdate(World.Api, player);
        }
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        foreach (var inv in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
            foreach (var slot in player.InventoryManager.GetOwnInventory(inv) ?? Enumerable.Empty<ItemSlot>())
            {
                slot.Itemstack = null;
                slot.MarkDirty();
            }
        TrunkCarry.Take((IServerPlayer)player);
        player.Entity.Controls.CtrlKey = player.Entity.Controls.ShiftKey = false;
        return player;
    }

    // Whether the last Click was taken by the block.
    protected bool _handled;

    /// <summary>Right-clicks <paramref name="at"/> (at <paramref name="hit"/> in that cell, its
    /// middle by default) holding <paramref name="held"/>; returns what is left in the hand.
    /// A trunk is never held (trunk entities run): it is carried in Carry On's hands instead, in
    /// place of whatever was carried, and the click is empty-handed; what is left is then what is
    /// still carried. Any other item in hand is held with empty Carry On hands (it once replaced a
    /// refused trunk in the hand slot). With nothing in hand, what is carried stays carried, except
    /// that a Ctrl click (the one that takes a trunk back) starts with empty hands unless
    /// <paramref name="keepCarried"/>.
    /// <paramref name="creative"/> clicks as a player in creative mode, back in survival after.</summary>
    protected ItemStack? Click(IPlayer player, BlockPos at, ItemStack? held, bool ctrl = false, bool shift = false,
        bool creative = false, Vec3d? hit = null, bool keepCarried = false)
    {
        bool trunk = Trunks.IsTrunk(held);
        if ((ctrl || held != null) && !keepCarried)
            TrunkCarry.Take((IServerPlayer)player);
        if (trunk)
        {
            TrunkCarry.Take((IServerPlayer)player);
            player.InventoryManager.ActiveHotbarSlot.Itemstack = null;   // Carry On takes a trunk only into empty hands
            Assert.True(TrunkCarry.TryGive((IServerPlayer)player, held!), "the trunk could not be carried");
            held = null;
        }
        // fetched after: carrying puts Carry On's locked slot in the hand
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = held;
        slot.MarkDirty();
        player.Entity.Controls.CtrlKey = ctrl;
        player.Entity.Controls.ShiftKey = shift;
        if (creative)
            player.WorldData.CurrentGameMode = EnumGameMode.Creative;
        try
        {
            var sel = new BlockSelection { Position = at.Copy(), Face = BlockFacing.UP, HitPosition = hit ?? new Vec3d(0.5, 0.5, 0.5) };
            _handled = W.BlockAccessor.GetBlock(at).OnBlockInteractStart(W, player, sel);
        }
        finally
        {
            player.Entity.Controls.CtrlKey = false;
            player.Entity.Controls.ShiftKey = false;
            player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        }
        return trunk ? TrunkCarry.Carried(player) : slot.Itemstack;
    }

    protected static string Info(BEBuckingMill mill, IPlayer player)
    {
        var sb = new System.Text.StringBuilder();
        mill.GetBlockInfo(player, sb);
        return sb.ToString();
    }

    protected void Assemble(BEBuckingMill mill, IPlayer player, string metal = "copper")
    {
        foreach (var path in new[] { "sawmillsash", "sawmillsash", "sawmillcrankshaft", "sawmilllevers", "sawmillblade-" + metal })
            Assert.True(mill.TryFitPart(new DummySlot(ItemOf($"{Iw}:{path}")), player), $"could not fit {path}");
        Assert.True(mill.Complete);
        Oil(mill.Oiling);
    }

    /// <summary>Fills a machine's oil tank (MachineOil): a creative rotor cannot turn a dry mill or
    /// rosser, whose load is three times as high. <c>MachineOilMillScenarios.cs</c> tests the oil
    /// itself.</summary>
    protected static void Oil(OilState? oil)
    {
        Assert.NotNull(oil);
        oil.Tank = oil.Tank.Fill(oil.Tank.Capacity);
    }

    /// <summary>A creative rotor against the power face; waits until the mill's shaft turns. With
    /// <paramref name="full"/>, the rotor is set to its top speed and torque (10 and 10, as a player
    /// gets by right-clicking it), and this waits until the shaft is up to speed. With
    /// <paramref name="fast"/>, the rotor is set to its top settings too, but this returns as soon as
    /// the shaft turns, as without: the scenario sees the mill from its first turn, only sooner
    /// through its cycle (an empty cycle is about 50 ticks at top speed with the fixture's raise).</summary>
    protected async Task Power(BEBuckingMill mill, bool full = false, bool fast = false)
    {
        var rotorPos = RotorPos(mill);
        var face = Assert.IsType<BlockMillGhostPower>(W.BlockAccessor.GetBlock(mill.CellPos(Rig.PowerCell))).PowerFace;
        World.SetBlock($"game:creativerotor-{face.Code}", rotorPos);
        if (full || fast)
        {
            await World.Ticks(2);
            var rotor = W.BlockAccessor.GetBlockEntity(rotorPos)!.GetBehavior<BEBehaviorMPCreativeRotor>()!;
            HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "speedSetting").SetValue(rotor, 10);
            HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "powerSetting").SetValue(rotor, 10);
            rotor.Blockentity.MarkDirty(true);
        }
        if (full)
            await World.Until(() => mill.ShaftSpeed >= 0.8f, 10000);
        await World.Until(() => mill.ShaftSpeed >= Mod.Config.MinSpeed, 2000);
    }

    /// <summary>Waits until the running mill's saws have come over the top <paramref name="times"/>
    /// times (each time it would take a trunk on offer), plus a couple of ticks.</summary>
    protected async Task PastTheTop(BEBuckingMill mill, int times = 2)
    {
        for (int i = 0; i < times; i++)
        {
            await World.Until(() => mill.Rising, 2000);
            await World.Until(() => !mill.Rising, 2000);
        }
        await World.Ticks(2);
    }

    protected BlockPos RotorPos(BEBuckingMill mill)
    {
        var ghost = mill.CellPos(Rig.PowerCell);
        return ghost.AddCopy(Assert.IsType<BlockMillGhostPower>(W.BlockAccessor.GetBlock(ghost)).PowerFace);
    }

    /// <summary>Takes the rotor away; waits until the shaft has run down below the mill's speed.</summary>
    protected async Task Unpower(BEBuckingMill mill)
    {
        W.BlockAccessor.SetBlock(0, RotorPos(mill));
        await World.Until(() => mill.ShaftSpeed < Mod.Config.MinSpeed, 20000);
    }

    protected ItemStack Trunk(string wood, int logs, bool branched = false, string size = "sm")
    {
        var stack = new ItemStack(BlockOf($"loggingmod:treetrunk-{wood}-{size}-{(branched ? "yes" : "no")}-north"));
        var slots = new TreeAttribute();
        slots["0"] = new ItemstackAttribute(new ItemStack(BlockOf($"game:log-placed-{wood}-ud"), logs));
        stack.Attributes["slots"] = slots;
        if (branched)
            stack.Attributes.SetInt("branchCount", 3);
        return stack;
    }

    /// <summary>The stacks of item entities in a tall box around <paramref name="around"/>, by code.</summary>
    protected Dictionary<string, int> ItemsNear(BlockPos around, int radius = 8)
    {
        var box = new Cuboidi(around.X - radius, around.Y - 40, around.Z - radius, around.X + radius, around.Y + 8, around.Z + radius);
        return World.EntitiesIn(box).OfType<EntityItem>().Where(e => e.Alive)
            .GroupBy(e => e.Itemstack.Collectible.Code.ToString())
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Itemstack.StackSize));
    }

    /// <summary>Waits until the item entities around <paramref name="around"/> satisfy
    /// <paramref name="done"/> (what a machine drops spawns over a tick or two), for at most
    /// <paramref name="ticks"/> ticks; it never fails, the scenario's own asserts do.</summary>
    protected async Task ItemsSettle(BlockPos around, int radius, System.Func<Dictionary<string, int>, bool> done, int ticks = 40)
    {
        for (int i = 0; i < ticks && !done(ItemsNear(around, radius)); i++)
            await World.Ticks(1);
    }

    protected void KillItemsNear(BlockPos around, int radius = 8)
    {
        var box = new Cuboidi(around.X - radius, around.Y - 40, around.Z - radius, around.X + radius, around.Y + 8, around.Z + radius);
        foreach (var e in World.EntitiesIn(box).Where(e => e is EntityItem or EntityTrunk))
            e.Die(EnumDespawnReason.Removed);
    }


    /// <summary>Turns the (unpowered) mill's cycle by hand: <paramref name="turns"/> shaft turns.</summary>
    protected static void Turn(BEBuckingMill mill, float turns) => mill.Advance(turns * 2 * MathF.PI);

    /// <summary>Turns an empty mill by hand from wherever it is to the top, just as it starts down again.</summary>
    protected void TurnToTop(BEBuckingMill mill)
    {
        float rr = Mod.Config.RaiseRevolutions;
        Turn(mill, ((mill.Rising ? 0 : 1 - mill.Depth) + (mill.Rising ? mill.Depth : 1)) * rr);
        Assert.True(SawDepth.AtTop(mill.Depth), $"depth {mill.Depth} after turning to the top");
    }

    public static TheoryData<string, int> Facings() => new() { { "north", 0 }, { "east", 1 }, { "south", 2 }, { "west", 3 } };

    // ---- Boxes (the mill's and the rosser's scenarios) ----

    protected static string Key(Cuboidd b) => $"{b.X1:F4},{b.Y1:F4},{b.Z1:F4},{b.X2:F4},{b.Y2:F4},{b.Z2:F4}";

    /// <summary>The boxes in <paramref name="with"/> that are not in <paramref name="without"/>.</summary>
    protected static List<Cuboidd> Added(List<Cuboidd> with, List<Cuboidd> without)
    {
        var left = without.GroupBy(Key).ToDictionary(g => g.Key, g => g.Count());
        var added = new List<Cuboidd>();
        foreach (var b in with)
            if (left.TryGetValue(Key(b), out int n) && n > 0)
                left[Key(b)] = n - 1;
            else
                added.Add(b);
        return added;
    }

    /// <summary>Whether <paramref name="boxes"/>, cell-local and projected on XZ, cover the whole
    /// cell: tried at the centres of a 32 × 32 grid.</summary>
    protected static bool CoversCell(IEnumerable<Cuboidf> boxes)
    {
        var list = boxes.ToList();
        for (int i = 0; i < 32; i++)
        for (int j = 0; j < 32; j++)
        {
            double x = (i + 0.5) / 32, z = (j + 0.5) / 32;
            if (!list.Any(b => x >= b.X1 && x <= b.X2 && z >= b.Z1 && z <= b.Z2))
                return false;
        }
        return true;
    }

    /// <summary>
    /// The top of a machine is solid to walk on: every column of <paramref name="cells"/> (its whole
    /// footprint, or the lidded part of it: the rosser's station), placed by <paramref name="cellPos"/>,
    /// has a rig lid on its top cell, and that cell's collision
    /// boxes, those reaching the lid's height, cover the whole cell, while its selection boxes do
    /// not hold the lid (it is collision only).
    /// </summary>
    protected void AssertTopIsADeck(IReadOnlyList<RigCell> cells, System.Func<Int3, BlockPos> cellPos, string where)
    {
        static bool IsLid(Cuboidf b, float lid) => Math.Abs(b.Y2 - lid) < 1e-4 && Math.Abs(b.Y2 - b.Y1 - RigCell.LidThickness) < 1e-4 && CoversCell([b]);
        foreach (var top in cells.GroupBy(c => (c.Pos.X, c.Pos.Z)).Select(g => g.MaxBy(c => c.Pos.Y)!))
        {
            string cell = $"{where}: column {top.Pos.X},{top.Pos.Z} (top cell {top.Pos})";
            Assert.True(top.Lid is not null, $"{cell} has no lid");
            float lid = top.Lid!.Value;
            var at = cellPos(top.Pos);
            var block = W.BlockAccessor.GetBlock(at);
            var collision = block.GetCollisionBoxes(W.BlockAccessor, at) ?? [];
            Assert.True(CoversCell(collision.Where(b => b.Y2 >= lid - 1e-4f)), $"{cell}: its top can be fallen through");
            Assert.Contains(collision, b => IsLid(b, lid));
            Assert.DoesNotContain(block.GetSelectionBoxes(W.BlockAccessor, at) ?? [], b => IsLid(b, lid));
        }
    }

    // ---- The rosser's (also used by TrunkStation's scenarios) ----

    protected RosserSystem RosserMod => RosserSystem.Of(World.Api);
    protected RosserRig RosserRig => RosserMod.Rig ?? throw new Xunit.Sdk.XunitException("the rosser's rig did not load");

    /// <summary>A spot 50 above spawn, its chunk columns loaded, cleared well beyond the rosser's
    /// footprint in every facing, on a granite floor one below it.</summary>
    protected async Task<BlockPos> RosserSky(int dx, int dz, int reach = 18)
    {
        var origin = World.Spawn.AddCopy(dx, 50, dz);
        int size = Vintagestory.API.Config.GlobalConstants.ChunkSize;
        // one block in every chunk column the area touches (37 or more wide, it can span three)
        var columns = new List<BlockPos>();
        for (int cx = (origin.X - reach) / size; cx <= (origin.X + reach) / size; cx++)
            for (int cz = (origin.Z - reach) / size; cz <= (origin.Z + reach) / size; cz++)
                columns.Add(new BlockPos(cx * size, origin.Y, cz * size));
        if (World.Api is ICoreServerAPI sapi)
            foreach (var c in columns)
                sapi.WorldManager.LoadChunkColumnPriority(c.X / size, c.Z / size);
        await World.Until(() => columns.All(c => W.BlockAccessor.GetChunkAtBlockPos(c) != null), 30000);
        int floor = BlockOf("game:rock-granite").Id;
        for (int x = -reach; x <= reach; x++)
        for (int z = -reach; z <= reach; z++)
        {
            W.BlockAccessor.SetBlock(floor, origin.AddCopy(x, -1, z));
            for (int y = 0; y <= 6; y++)
                W.BlockAccessor.SetBlock(0, origin.AddCopy(x, y, z));
        }
        return origin;
    }

    protected async Task<BERosser> PlaceRosser(BlockPos pos, string side)
    {
        World.SetBlock($"seraphhorizons:rosser-frame-{side}", pos);
        await World.Ticks(5);
        return W.BlockAccessor.GetBlockEntity(pos) as BERosser
               ?? throw new Xunit.Sdk.XunitException($"no rosser block entity at {pos}");
    }

    /// <summary>Every stage by right-clicks with real stacks, through the frame and a ghost.</summary>
    protected void AssembleRosser(BERosser rosser, IPlayer player, string heads = "steel")
    {
        var ghost = rosser.GhostCells().First().Pos;
        foreach (var (code, count) in new[] { (RosserParts.ShaftCode, 1), (RosserParts.RingCode, 4), ("game:hoop-iron", 2), ("game:rod-iron", 4),
                                              ("game:metalplate-iron", 2), (RosserParts.LeversCode, 1), ($"{Iw}:barkspudhead-{heads}", 4) })
            Assert.True(Click(player, code.Contains("rod") ? ghost : rosser.Pos, ItemOf(code, count)) == null, $"{code} ×{count} not all fitted");
        Assert.True(rosser.Complete);
        Oil(rosser.Oiling);
    }

    /// <summary>A creative rotor at full speed against the power face; waits until the shaft turns fast.</summary>
    protected async Task PowerRosser(BERosser rosser)
    {
        var ghost = rosser.CellPos(RosserRig.PowerCell);
        var face = Assert.IsType<BlockRosserGhostPower>(W.BlockAccessor.GetBlock(ghost)).PowerFace;
        var rotorPos = ghost.AddCopy(face);
        World.SetBlock($"game:creativerotor-{face.Code}", rotorPos);
        await World.Ticks(2);
        var rotor = W.BlockAccessor.GetBlockEntity(rotorPos)!.GetBehavior<BEBehaviorMPCreativeRotor>()!;
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "speedSetting").SetValue(rotor, 10);
        HarmonyLib.AccessTools.Field(typeof(BEBehaviorMPCreativeRotor), "powerSetting").SetValue(rotor, 10);
        rotor.Blockentity.MarkDirty(true);
        await World.Until(() => rosser.ShaftSpeed >= 0.5f, 15000);
    }

    /// <summary>A trunk of <paramref name="wood"/> holding <paramref name="logs"/> logs, with
    /// <paramref name="branches"/> branches counted (and the <c>yes</c> state) when above 0.</summary>
    protected ItemStack RosserTrunk(string wood, int logs, int branches, string size = "sm")
    {
        var stack = new ItemStack(BlockOf($"loggingmod:treetrunk-{wood}-{size}-{(branches > 0 ? "yes" : "no")}-north"));
        var slots = new TreeAttribute();
        slots["0"] = new ItemstackAttribute(new ItemStack(BlockOf($"game:log-placed-{wood}-ud"), logs));
        stack.Attributes["slots"] = slots;
        if (branches > 0)
            stack.Attributes.SetInt(Trunks.BranchCountKey, branches);
        return stack;
    }

    // A second player who only stands by a machine: the server unloads chunk columns no player is
    // near, and a scenario longer than a minute would lose its machine. Not the shared player, who
    // stays where the other woodworking scenarios expect (their floors near spawn load no chunks).
    // Keyed by the server's API, as the shared player is.
    private static readonly ConditionalWeakTable<ICoreAPI, IPlayer> Keepers = new();

    /// <summary>This server's keeper, once a scenario has stood it by a machine.</summary>
    protected IPlayer? Keeper => Keepers.TryGetValue(World.Api, out var keeper) ? keeper : null;

    /// <summary>Keeps the chunk columns around <paramref name="pos"/> loaded (the keeper player
    /// stands above it) until the next scenario moves the keeper.</summary>
    protected async Task StandBy(IPlayer _, BlockPos pos)
    {
        if (!Keepers.TryGetValue(World.Api, out var keeper))
        {
            keeper = (await World.JoinPlayer("rosserkeeper")).Player;
            Keepers.AddOrUpdate(World.Api, keeper);
        }
        keeper.WorldData.CurrentGameMode = EnumGameMode.Creative;
        keeper.Entity.TeleportTo(pos.ToVec3d().Add(0.5, 5, 0.5));
    }

    protected BlockFacing RosserPowerFace(BERosser rosser) =>
        Assert.IsType<BlockRosserGhostPower>(W.BlockAccessor.GetBlock(rosser.CellPos(RosserRig.PowerCell))).PowerFace;

    protected BlockPos RosserRotorPos(BERosser rosser) => rosser.CellPos(RosserRig.PowerCell).AddCopy(RosserPowerFace(rosser));

    protected ItemStack RosserReady(BERosser rosser, IPlayer player, string heads = "steel")
    {
        AssembleRosser(rosser, player, heads);
        return ItemOf($"{Iw}:barkspudhead-{heads}");
    }

    /// <summary>A debarked trunk, as the rosser leaves it (<c>DebarkedTrunkScenarios.cs</c>).</summary>
    protected ItemStack DebarkedTrunk(string wood, int logs, bool branched = false, string size = "sm")
    {
        var debarking = Trunks.Debark(Trunk(wood, logs, branched, size), W);
        Assert.NotNull(debarking);
        return debarking.Trunk;
    }
}
