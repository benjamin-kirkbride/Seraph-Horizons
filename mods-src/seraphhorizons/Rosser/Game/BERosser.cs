using System.Globalization;
using System.Text;
using SeraphHorizons.Mod.BuckingSawmill;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Rosser.Core;
using SeraphHorizons.Mod.TrunkEntities;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Rosser;

/// <summary>
/// The rosser's controller, on the shared machine base (<see cref="BEMachine"/>: its build from
/// <c>config/builds/rosser.json</c> with the scraper heads its wearing part; its cells, power cell and water
/// anchor; its oil (<see cref="OilMachine.Rosser"/>); the drip's reservoir, filled from a ppex pipe at the water
/// face once the drip's pipes are in; its block info). It keeps the trunk as its whole Logging Expanded item
/// stack and its trip (<see cref="RosserTrip"/>: travel, rate and what has dropped). The server feeds the trunk
/// from the power cell's shaft angle, drops sticks and bark at the chute as the trunk passes the limb breaker and
/// the ring, debarks it at the end of the trip, pulls trunks from a rack at the infeed end and pushes finished
/// ones into a rack at the outfeed end (or leaves them to the bucking mill in line, <see cref="ITrunkFeeder"/>);
/// the client follows the trunk between syncs and draws it (<see cref="RosserRenderer"/>). Every cell's collision
/// and selection boxes, with the travelling trunk's (<see cref="TrunkBox.BoundsOnPath"/>), come from here. The
/// rules are Rosser/Core's.
/// </summary>
public class BERosser : BEMachine, ITrunkFeeder
{
    private static readonly AssetLocation BreakSound = new("game", "sounds/effect/toolbreak");
    private static readonly AssetLocation LatchSound = new("game", "sounds/effect/latch");
    private static readonly AssetLocation StickSound = new("game", "sounds/block/stickbreak");
    private static readonly AssetLocation TrunkSound = new("game", "sounds/block/planks");
    // The server syncs the travel at least this often (blocks); the client follows it between.
    private const double SyncStep = 0.25;

    private ItemStack? _trunk;
    private RosserTrip _trip = RosserTrip.None;
    private RosserRackState _rackState;
    private RosserOutfeedState _outfeedState;
    private bool _warnedNoDebark;
    // The client's estimate of T, advanced with the shaft between syncs, and the shown T one client
    // tick back with when and how long that tick was: frames between ticks draw part way.
    private double _clientTravel;
    private double _clientTravelBefore;
    private long _clientTravelMs;
    private float _clientTravelSeconds;
    private float _clientLastAngle;
    private bool _clientAngleSeeded;
    private bool _serverWet;
    // Every cell's boxes for the trunk at one quantised T, turned to the facing: built whole, then
    // published; collision lookups, which can run off the main thread, only read it.
    private volatile BoxTable? _boxes;
    private Dictionary<Int3, Cuboidf[]>? _frameBoxes;
    private Dictionary<Int3, Cuboidf>? _lidBoxes;

    private sealed record BoxTable(TrunkClass Class, double Travel, (Float3 Min, Float3 Max) Trunk,
        IReadOnlyDictionary<Int3, Cuboidf[]> Cells, IReadOnlyDictionary<Int3, Cuboidf[]> Collision);

    public BlockFacing Facing => BlockFacing.FromCode(Side.Code());
    /// <summary>The trip as the server knows it (the client's copy carries the server's last T).</summary>
    public RosserTrip Trip => _trip;
    public RosserRackState RackState => _rackState;
    public RosserOutfeedState OutfeedState => _outfeedState;

    private RosserSystem System => RosserSystem.Of(Api);
    private RosserConfig Config => System.Config;
    private new RosserRig? Rig => System.Rig;
    private RosserPace? Pace => System.Pace;

    /// <summary>The fitted scraper heads: their code and wear points left; null without (spent heads are gone).</summary>
    public (string Code, double Left)? Heads => Assembly?.Wearing.Fitted(RosserBuild.HeadsPart);

    /// <summary>The scraper heads' metal (the tips' texture); null without heads.</summary>
    public string? HeadMetal => RosserBuild.HeadMetalOf(Heads?.Code);

    /// <summary>The heads' wear points left; 0 without heads.</summary>
    public int HeadsLeft => Heads is { } h ? (int)Math.Round(h.Left) : 0;

    /// <summary>The heads' wear points when fitted (their metal's); 0 without heads.</summary>
    public int HeadsCapacity => HeadMetal is { } metal && Api != null ? System.HeadCapacity(metal) : 0;

    /// <summary>Whether the heads are fitted and have never been used (they can come back out).</summary>
    public bool HeadsUnworn => Assembly is { } a && RosserBuild.HeadsUnworn(a);

    /// <summary>Whether the four ring sections are in: the tyres and the heads go on it.</summary>
    public bool RingIn => Assembly is { } a && RosserBuild.RingIn(a);

    /// <summary>The drip's pipes' metal, copper or lead, once all four are in; null before, or while the
    /// rosser needs none.</summary>
    public string? PipeMetal => Assembly is { } a ? RosserBuild.PipeMetal(a) : null;

    /// <summary>Whether the drip's water comes in: through its pipes once they are in, or straight from a pipe
    /// at the water face while the rosser needs none (Pipes and Power Expanded without UnifiedPipes' metals).</summary>
    public bool DripPiped => PipeMetal != null || !System.PipesNeeded;

    // What the renderer draws
    public ItemStack? Trunk => _trunk;
    public TrunkClass TrunkClass => (TrunkClass)_trip.Class;
    public double ClientTravel
    {
        get
        {
            // T moves at the client's ticks (20 a second); read every frame, it would step.
            float t = _clientTravelSeconds > 0 && Api != null
                ? Math.Clamp((Api.World.ElapsedMilliseconds - _clientTravelMs) / (_clientTravelSeconds * 1000), 0, 1)
                : 1;
            return RosserVisuals.Interpolate(_clientTravelBefore, _clientTravel, t);
        }
    }
    /// <summary>T as this side knows it: the client's estimate there, the server's own here.</summary>
    public double SideTravel => Api?.Side == EnumAppSide.Client ? _clientTravel : _trip.Travel;
    /// <summary>Where the trunk is, from this side's trip.</summary>
    public RosserState TripState => Pace is { } pace ? (_trip with { Travel = SideTravel }).State(pace) : _trunk == null ? RosserState.Empty : RosserState.Waiting;
    /// <summary>Assembled with its heads and turning at <see cref="BEMachine.MinSpeed"/> or faster: the feed runs.</summary>
    public bool ShaftRuns => RosserTrip.Running(Ready, ShaftSpeed, MinSpeed);
    /// <summary>Whether the next log would be scraped wet: the reservoir holds a log's water (synced: the drip's
    /// particles).</summary>
    public bool DripWet => Api?.Side == EnumAppSide.Client ? _serverWet : Water.Holds(Config.WaterPerLog);

    // ---- The machine base ----

    /// <summary>The heads' uses are their metal's capacity; the hoops, rods and plates any metal while the
    /// woodworking machines are not iron work.</summary>
    protected override MachineBuild? BuildSpec(IWorldAccessor world)
    {
        var system = RosserSystem.Of(world.Api);
        var build = base.BuildSpec(world)?.WithUses(RosserBuild.HeadsPart, code =>
            RosserBuild.HeadMetalOf(code) is { } metal ? system.HeadCapacity(metal) : 0);
        return build != null && world.Side == EnumAppSide.Server && system.PartMetals == null ? RosserBuild.AnyMetal(build) : build;
    }

    protected override OilMachine? OilKind => OilMachine.Rosser;
    protected override float ConfigMinSpeed => Config.MinSpeed;
    protected override int TickMs => 50;
    protected override int ClientTickMs => 50;

    /// <summary>Whole with its heads on: until then it is the frame, puts the token load on its shaft and takes
    /// the creative shortcut (the woodworking stations', with <c>UnifiedWoodworking</c>: steel heads last).</summary>
    public override bool Complete => Ready;
    protected override bool LoadsShaft => Ready;
    protected override bool CreativeFills => !Ready && WoodworkingShortcut;
    protected override double ShaftLoad => Config.Resistance;
    protected override Float3? SmokePoint => new Float3(0.5f, 1.2f, 0.5f);

    /// <summary>A trunk waiting or feeding, with Logging Expanded as the rosser needs it.</summary>
    public override bool HasWork =>
        Pace != null && System.Logging != null && TripState is RosserState.Waiting or RosserState.Feeding;

    public override double? WorkProgress =>
        TripState == RosserState.Feeding && Pace is { } p && _trip.End(p) > 0 ? Math.Clamp(SideTravel / _trip.End(p), 0, 1) : null;

    // The drip: a log's water, through its pipes from a ppex pipe at the water face; a dry drip still debarks.
    public override double WaterPerItem => Config.WaterPerLog;
    public override double ReservoirCapacity => Config.ReservoirLitres;
    protected override double WaterIntakePerSecond => DripPiped ? Config.WaterIntakeLitresPerSecond : 0;
    protected override bool Wet => true;

    /// <summary>The drip's pipes are drawn in the metal fitted only, the infeed rolls once two rods are in.</summary>
    public override bool ShowsPart(string? requires) => Assembly is { } a ? RosserBuild.Shows(a, requires) : requires == null;

    /// <summary>Whether the creative shortcut is on: with <c>UnifiedWoodworking</c>, whose creative
    /// shortcut on the woodworking stations this is, running (as the bucking mill's).</summary>
    public bool WoodworkingShortcut => Api?.ModLoader.GetModSystem<SeraphHorizonsSystem>()?.Woodworking is { Active: true };

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        RebuildBoxes(force: true);
        if (api.Side == EnumAppSide.Server)
            RegisterGameTickListener(OnSlowTick, 1000);
        else
            _clientTravel = _clientTravelBefore = _trip.Travel;
    }

    protected override IRenderer CreateRenderer(ICoreClientAPI capi, IMachineRig rig) => new RosserRenderer(capi, this, (RosserRig)rig);

    // ---- Footprint ----

    /// <summary>A cell's selection boxes, turned to the rosser's facing, with the trunk's part in
    /// that cell; an empty set for a hollow cell the trunk is not in; null when the cell is not the
    /// rosser's. Safe off the main thread: the table is built whole and only read here.</summary>
    public override Cuboidf[]? CellBoxes(BlockPos cellPos) => Table(cellPos, collision: false);

    /// <summary>A cell's collision boxes: its <see cref="CellBoxes"/> and, on a column's top cell
    /// (hollow or not), the rig's collision-only lid, so nothing falls into the rosser from above.</summary>
    public override Cuboidf[]? CollisionBoxes(BlockPos cellPos) => Table(cellPos, collision: true);

    private Cuboidf[]? Table(BlockPos cellPos, bool collision) =>
        _boxes is { } table ? (collision ? table.Collision : table.Cells).GetValueOrDefault(LocalOf(cellPos)) : null;

    /// <summary>Rebuilds the box table when the trunk's class or its quantised travel
    /// (<see cref="RosserTrip.BoxTravel"/>, 1/16 block) changed, on either side.</summary>
    private void RebuildBoxes(bool force = false)
    {
        if (Api == null || Rig is not { } rig || !Sides.TryParse(Block?.Variant["side"], out var side))
            return;
        var k = _trunk == null ? TrunkClass.None : (TrunkClass)_trip.Class;
        double travel = k == TrunkClass.None ? 0 : RosserTrip.BoxTravel(SideTravel);
        if (!force && _boxes is { } current && current.Class == k && current.Travel == travel)
            return;
        Cuboidf World(Box b)
        {
            var w = Footprint.ToWorld(b, side);
            return new Cuboidf(w.X1, w.Y1, w.Z1, w.X2, w.Y2, w.Z2);
        }
        var frame = _frameBoxes ??= rig.Cells.ToDictionary(c => c.Pos, c =>
            c.Hollow ? [] : c.Boxes.Count == 0 ? [Cuboidf.Default()] : c.Boxes.Select(World).ToArray());
        var lids = _lidBoxes ??= rig.Cells.Where(c => c.LidBox != null).ToDictionary(c => c.Pos, c => World(c.LidBox!.Value));
        var cells = new Dictionary<Int3, Cuboidf[]>(frame);
        var bounds = TrunkBox.BoundsOnPath(rig.Path, k, (float)travel);
        if (k != TrunkClass.None)
            foreach (var (cell, box) in TrunkBox.CellBoxes(rig.Cells, bounds))
                cells[cell] = [.. frame[cell], World(box)];
        var collision = new Dictionary<Int3, Cuboidf[]>(cells);
        foreach (var (cell, lid) in lids)
            collision[cell] = [.. cells[cell], lid];
        _boxes = new BoxTable(k, travel, bounds, cells, collision);
    }

    /// <summary>Whether a click on cell <paramref name="cellPos"/> at <paramref name="hit"/> (the
    /// selection's hit point, relative to that cell) is on the trunk.</summary>
    public bool HitsTrunk(BlockPos cellPos, Vec3d? hit)
    {
        if (hit == null || _boxes is not { Class: not TrunkClass.None } table)
            return false;
        var local = MachinePoints.NativeHit(Pos, cellPos, hit, Side);
        return TrunkBox.Contains(table.Trunk, local);
    }

    // ---- Interaction ----

    /// <summary>Before the build's: Ctrl takes the waiting or delivered trunk back (into Carry On's hands with
    /// trunk entities), else the unworn heads (the creative shortcut comes first while a part is still to fit);
    /// a straight pipe once the drip's pipes are in, or while the rosser needs none, is placed against the
    /// rosser as any pipe is, as on the water face; the tyres and the heads go on the ring once it is in; a
    /// hoop, rod or plate not of the metals it takes is refused as such.</summary>
    protected override bool? InteractContent(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        if (CreativeShortcut(byPlayer))
            return null;
        var controls = byPlayer.Entity.Controls;
        if (controls.CtrlKey && !controls.ShiftKey)
        {
            if (Api.Side == EnumAppSide.Server)
                TakeBack(byPlayer);
            return true;
        }
        string? code = slot?.Itemstack?.Collectible?.Code?.ToString();
        if (RosserBuild.PipeMetalOf(code) is { } pipe)
        {
            if (DripPiped)
                return false;
            if (!RosserBuild.PipeMetals.Contains(pipe))
                return Api.Side != EnumAppSide.Server || !Refuse(byPlayer, "error-wrong-pipe-metal");
        }
        if (RosserBuild.HeadMetalOf(code) != null)
        {
            if (Api.Side == EnumAppSide.Server)
                TryFitHeads(slot!, byPlayer);
            return true;
        }
        if (RosserBuild.IsHoop(code) && !RingIn)
            return Api.Side != EnumAppSide.Server || !Refuse(byPlayer, "error-needs-ring");
        if (RosserBuild.IsIronWork(code) && !Built && slot!.Itemstack is { } stack
            && Assembly?.Check(code, stack.StackSize, stack.Collectible.MaxStackSize).Verdict == AssemblyVerdict.NotAPart)
            return Api.Side != EnumAppSide.Server || !Refuse(byPlayer, "error-wrong-metal");
        return null;
    }

    /// <summary>After the build's: with an empty hand the trunk carried in Carry On's hands (with trunk
    /// entities), else a trunk in hand or one from the hotbar or backpack, is loaded onto the infeed bed.
    /// Anything else held is the item's own business, except on the trunk, where the click is the rosser's and
    /// does nothing (a block would be placed inside the trunk).</summary>
    protected override bool InteractWork(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        bool trunk = Trunks.IsTrunk(slot.Itemstack);
        if (!trunk && slot.Itemstack != null)
            return HitsTrunk(blockSel.Position, blockSel.HitPosition);
        if (Api.Side != EnumAppSide.Server)
            return true;
        if (trunk)
            TryLoadFromSlot(slot, byPlayer);
        else
            TryLoadFromInventory(byPlayer);
        return true;
    }

    /// <summary>Fits what <paramref name="slot"/> holds (server side), as a right-click holding it does: the
    /// heads and the tyres by the ring's rule, any other part into the build, as many as it still needs. The
    /// parts are taken from the slot unless the player is in creative mode or it is <paramref name="free"/>.
    /// False, with an error to the player, when it does not go in.</summary>
    public bool TryFitPart(ItemSlot slot, IPlayer byPlayer, bool free = false)
    {
        string? code = slot.Itemstack?.Collectible?.Code?.ToString();
        if (RosserBuild.HeadMetalOf(code) != null)
            return TryFitHeads(slot, byPlayer, free);
        if (RosserBuild.IsHoop(code) && !RingIn)
            return Refuse(byPlayer, "error-needs-ring");
        if (free)
            slot = new DummySlot(slot.Itemstack?.Clone());
        bool fitted = TryFit(slot, byPlayer) == true;
        RefreshState();
        return fitted;
    }

    /// <summary>Fits four scraper heads of one metal from <paramref name="slot"/> (server side): on the ring once
    /// it is in, any metal, lasting their metal's capacity. False, with an error to the player, when the ring is
    /// not in, heads are already on, or fewer than four are held.</summary>
    public bool TryFitHeads(ItemSlot slot, IPlayer? byPlayer, bool free = false)
    {
        if (Assembly is not { } assembly || slot.Itemstack is not { } stack || stack.Collectible?.Code?.ToString() is not { } code)
            return false;
        bool consumes = !free && byPlayer?.WorldData.CurrentGameMode != EnumGameMode.Creative;
        // a player in creative fits a whole set from a single head
        int available = consumes || free ? stack.StackSize : RosserBuild.HeadsNeeded;
        switch (RosserBuild.FitHeads(assembly, code, available))
        {
            case HeadsVerdict.Fits:
                break;
            case HeadsVerdict.NeedsRing:
                return Refuse(byPlayer, "error-needs-ring");
            case HeadsVerdict.AlreadyFitted:
                return Refuse(byPlayer, "error-part-fitted");
            case HeadsVerdict.NeedsFullSet:
                return Refuse(byPlayer, "error-needs-full-set");
            default:
                return Refuse(byPlayer, "error-not-a-part");
        }
        if (consumes)
        {
            slot.TakeOut(RosserBuild.HeadsNeeded);
            slot.MarkDirty();
        }
        Api.World.PlaySoundAt(Block.Sounds.Place, Pos, -0.25, byPlayer);
        MarkDirty(true);
        RefreshState();
        return true;
    }

    /// <summary>The creative shortcut (server side): fits the next stage (every part still missing, metal parts
    /// steel, the pipes copper), then steel heads, with nothing taken from the player.</summary>
    public bool FitNextPart(IPlayer byPlayer)
    {
        if (Assembly is not { } assembly || Ready || !assembly.FillNext())
            return false;
        Api.World.PlaySoundAt(Block.Sounds.Place, Pos, -0.25, byPlayer);
        MarkDirty(true);
        RefreshState();
        return true;
    }

    /// <summary>Ctrl + right click (server side): the waiting or delivered trunk, else the four
    /// unworn heads (<see cref="RosserTrip.TakeBack"/>).</summary>
    public bool TakeBack(IPlayer byPlayer)
    {
        switch (RosserTrip.TakeBack(TripState, Heads != null, HeadsUnworn))
        {
            case RosserTakeBack.Trunk:
                var trunk = _trunk!;
                if (!GiveTrunk(byPlayer, trunk))
                    return true;   // hands full: it stays
                ClearTrunk();
                Api.World.PlaySoundAt(TrunkSound, Pos, 0, byPlayer);
                MarkDirty(true);
                RefreshState();
                return true;
            case RosserTakeBack.InRolls:
                return Refuse(byPlayer, "error-trunk-in-rolls");
            case RosserTakeBack.Heads:
                if (Api.World.GetItem(new AssetLocation(Heads!.Value.Code)) is not { } item || Assembly!.Take(RosserBuild.HeadsPart) == null)
                    return false;
                Give(byPlayer, new ItemStack(item, RosserBuild.HeadsNeeded));
                MarkDirty(true);
                RefreshState();
                return true;
            case RosserTakeBack.HeadsWorn:
                return Refuse(byPlayer, "error-heads-worn");
            default:
                return false;
        }
    }

    public bool TryLoadFromSlot(ItemSlot slot, IPlayer? byPlayer)
    {
        if (slot.Itemstack is not { } stack || !CanLoad(stack, byPlayer))
            return false;
        Load(slot.TakeOut(1));
        slot.MarkDirty();
        Api.World.PlaySoundAt(TrunkSound, Pos, 0, byPlayer);
        return true;
    }

    /// <summary>Loads the trunk the player carries in Carry On's hands, while trunks go through
    /// hands (<see cref="TrunkStations.Hands"/>); else the first trunk in the player's hotbar, then
    /// backpack, that the rosser takes. With none, says why the first trunk found is refused, or
    /// that there is none.</summary>
    public bool TryLoadFromInventory(IPlayer byPlayer)
    {
        if (TrunkStations.Hands(Api))
            return TryLoadFromHands(byPlayer);
        ItemSlot? first = null;
        foreach (var name in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
        {
            if (byPlayer.InventoryManager.GetOwnInventory(name) is not { } inventory)
                continue;
            foreach (var slot in inventory)
            {
                if (slot.Itemstack is not { } stack || !Trunks.IsTrunk(stack))
                    continue;
                if (!Trunks.IsDebarked(stack) && Trunks.StoredLogs(stack, Api.World) > 0)
                    return TryLoadFromSlot(slot, byPlayer);
                first ??= slot;
            }
        }
        return first != null ? TryLoadFromSlot(first, byPlayer) : Refuse(byPlayer, "error-no-trunk");
    }

    /// <summary>Loads the trunk the player carries in Carry On's hands, if the rosser takes it
    /// (server side).</summary>
    public bool TryLoadFromHands(IPlayer byPlayer)
    {
        if (TrunkStations.Carried(Api, byPlayer) is not { } carried)
            return Refuse(byPlayer, "error-no-trunk");
        if (!CanLoad(carried, byPlayer) || byPlayer is not IServerPlayer sp || TrunkCarry.Take(sp) is not { } trunk)
            return false;
        Load(trunk);
        Api.World.PlaySoundAt(TrunkSound, Pos, 0, byPlayer);
        return true;
    }

    /// <summary>Whether <paramref name="trunk"/> can go on now (<see cref="RosserTrip.CanLoad"/>);
    /// tells the player why not.</summary>
    public bool CanLoad(ItemStack trunk, IPlayer? byPlayer)
    {
        if (!Trunks.IsTrunk(trunk) || Pace == null)
            return false;
        if (System.Logging == null)
            return Refuse(byPlayer, "error-no-logging");
        return RosserTrip.CanLoad(Ready, TripState, Trunks.IsDebarked(trunk), Trunks.StoredLogs(trunk, Api.World)) switch
        {
            RosserLoadVerdict.Loads => true,
            RosserLoadVerdict.Incomplete => Refuse(byPlayer, "error-incomplete"),
            RosserLoadVerdict.Occupied => Refuse(byPlayer, "error-occupied"),
            RosserLoadVerdict.AlreadyDebarked => Refuse(byPlayer, "error-already-debarked"),
            _ => Refuse(byPlayer, "error-empty-trunk"),
        };
    }

    /// <summary>Puts <paramref name="trunk"/> on the infeed bed (T = 0): its trip's rate is fixed
    /// now, by its class and the heads' metal.</summary>
    private void Load(ItemStack trunk)
    {
        _trunk = trunk;
        _trip = RosserTrip.Load(Pace!, TrunkBox.ClassOf(trunk.Block?.Variant["size"]), Trunks.StoredLogs(trunk, Api.World),
                                trunk.Attributes.GetInt(Trunks.BranchCountKey), System.HeadTier(HeadMetal));
        RebuildBoxes();
        // the trunk's weight on the treadle throws the feed dog in
        Api.World.PlaySoundAt(LatchSound, Pos, 0);
        MarkDirty(true);
        RefreshState();
    }

    private void ClearTrunk()
    {
        _trunk = null;
        _trip = RosserTrip.Taken;
        RebuildBoxes();
    }

    /// <summary>Gives a trunk taken off the rosser to the player (server side): into Carry On's
    /// hands while trunks go through hands, false with the hands-full error when they are full;
    /// with trunk entities but no Carry On, a trunk entity beyond the infeed end; else to the
    /// inventory as any item.</summary>
    private bool GiveTrunk(IPlayer byPlayer, ItemStack trunk)
    {
        if (TrunkStations.Hands(Api))
            return byPlayer is IServerPlayer sp && TrunkStations.GiveToHands(sp, trunk);
        if (TrunkEntitySystem.Of(Api).Enabled && Rig is { } rig)
        {
            var outward = Footprint.ToWorld(rig.InfeedSide, Side).Normal();
            if (TrunkStations.DropBeyond(Api.World, trunk, rig.InfeedNeighbours().Select(CellPos).ToList(), outward.X, outward.Z) != null)
                return true;
        }
        Give(byPlayer, trunk);
        return true;
    }

    private void Give(IPlayer byPlayer, ItemStack stack)
    {
        if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.6, 0.5));
    }

    /// <summary>The rosser's own message (<c>seraphhorizons:rosser-{key}</c>) to the player; false.</summary>
    private bool Refuse(IPlayer? byPlayer, string key, params object[] args)
    {
        if (byPlayer is IServerPlayer sp)
            sp.SendIngameError("rosser-" + key, Lang.GetL(sp.LanguageCode, RosserSystem.Domain + ":rosser-" + key, args));
        return false;
    }

    /// <summary>The help: the base's (the parts still to fit, heads once worn out), the drip's pipes and the
    /// heads once the ring is in while the parts are still going in, then a trunk to load and Ctrl for the
    /// trunk or the unworn heads.</summary>
    public override WorldInteraction[] InteractionHelp(IPlayer forPlayer)
    {
        var help = base.InteractionHelp(forPlayer).ToList();
        if (!Built)
        {
            if (Heads == null && RingIn && System.HeadStacks() is { Length: > 0 } heads)
                help.Add(new WorldInteraction { ActionLangCode = Key("fitheads"), MouseButton = EnumMouseButton.Right, Itemstacks = heads });
            AddTakeBackHelp(help);
        }
        return help.ToArray();
    }

    private static string Key(string action) => RosserSystem.Domain + ":blockhelp-rosser-" + action;

    protected override void WorkHelp(IPlayer? forPlayer, List<WorldInteraction> help)
    {
        if (Ready && TripState == RosserState.Empty && System.TrunkStacks() is { Length: > 0 } trunks)
            help.Add(new WorldInteraction { ActionLangCode = Key("loadtrunk"), MouseButton = EnumMouseButton.Right, Itemstacks = trunks });
        AddTakeBackHelp(help);
    }

    private void AddTakeBackHelp(List<WorldInteraction> help)
    {
        if (TripState is RosserState.Waiting or RosserState.Delivered)
            help.Add(new WorldInteraction { ActionLangCode = Key("taketrunk"), MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" });
        else if (TripState == RosserState.Empty && HeadsUnworn)
            help.Add(new WorldInteraction { ActionLangCode = Key("removeheads"), MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" });
    }

    // ---- The trip ----

    // The trip syncs itself as it goes (SyncStep), so the base's full sync is not asked for.
    protected override bool Work(double radians)
    {
        Feed((float)radians);
        return false;
    }

    /// <summary>Feeds the trunk by <paramref name="radians"/> of shaft rotation (server side; call
    /// only while running): drops the sticks and bark now due at the chute, and at the end of the
    /// trip debarks the trunk, wears the heads and hands it on. Returns what the step gave.</summary>
    public TripStep Feed(float radians)
    {
        if (Pace is not { } pace || _trunk == null)
            return TripStep.Nothing;
        var before = _trip;
        (_trip, var step) = _trip.Advance(pace, radians);
        if (step.Sticks > 0)
            DropSticks(step.Sticks);
        if (step.BarkLogs > 0)
            DropBark(step.BarkLogs);
        RebuildBoxes();
        if (step.Delivered)
            Deliver();
        else if (step.Sticks > 0 || step.BarkLogs > 0 || before.State(pace) != _trip.State(pace)
                 || Math.Floor(before.Travel / SyncStep) != Math.Floor(_trip.Travel / SyncStep))
            MarkDirty();
        return step;
    }

    /// <summary>The end of the trip: the trunk becomes the debarked trunk (its branch count gone),
    /// the heads wear by its stored logs (spent heads break and are gone), and it goes on to a rack
    /// if one with room touches the outfeed end.</summary>
    private void Deliver()
    {
        if (Trunks.Debark(_trunk, Api.World) is { } debarked)
            _trunk = debarked.Trunk;
        else if (!_warnedNoDebark)
        {
            _warnedNoDebark = true;
            Api.Logger.Warning("[seraphhorizons] Rosser: no debarked trunk for {0} at {1}; it leaves as it came", _trunk?.Collectible?.Code, Pos);
        }
        // a stored log's oil, rounded up a trunk (OilDrain.PerTrunk), as jobs of the setting's drain
        if (Oiling is { } oil && oil.DrainPerJob(Api) is > 0 and var perLog)
            DrainOil(OilDrain.PerTrunk(_trip.Logs, perLog) / perLog);
        if (Assembly!.Wearing.Wear(RosserBuild.HeadsPart, RosserBuild.WearFor(_trip.Logs, Config.HeadWearPerStoredLog)))
            Api.World.PlaySoundAt(BreakSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5);
        // the last top roll drops and its weight throws the dog out
        Api.World.PlaySoundAt(LatchSound, Pos, 0);
        MarkDirty(true);
        PushToRack();
    }

    /// <summary>Where bark and sticks come out (world) and the push they get, out of the chute's side.</summary>
    private (Vec3d At, Vec3d Velocity) ChutePoint()
    {
        var chute = Rig!.Chute;
        var n = Footprint.ToWorld(chute.Side, Side).Normal();
        return (MachinePoints.WorldPoint(Pos, chute.Pos, Side), new Vec3d(n.X * 0.08, 0.02, n.Z * 0.08));
    }

    private void Spawn(ItemStack stack)
    {
        var (at, velocity) = ChutePoint();
        int max = Math.Max(1, stack.Collectible.MaxStackSize);
        for (int left = stack.StackSize; left > 0; left -= max)
        {
            var part = stack.Clone();
            part.StackSize = Math.Min(left, max);
            Api.World.SpawnItemEntity(part, at, velocity);
        }
    }

    private void DropSticks(int count)
    {
        if (Api.World.GetItem(new AssetLocation("game", "stick")) is not { } stick)
            return;
        Spawn(new ItemStack(stick, count));
        var (at, _) = ChutePoint();
        Api.World.PlaySoundAt(StickSound, at.X, at.Y, at.Z, null, true, 16);
    }

    /// <summary>Immersive Woodworking's bark for <paramref name="logs"/> scraped logs of the
    /// trunk's wood, one roll each (<see cref="BarkDrops.Roll"/>): wet while the reservoir holds a
    /// log's water, which it spends, raising the roll's chance multiplier and the count
    /// (<see cref="RosserBark.WetCount"/>) as the settings say.</summary>
    private void DropBark(int logs)
    {
        if (!System.BarkBound || _trunk == null)
            return;
        var config = Config;
        string? species = SawhorseWorks.Species(Trunks.Wood(_trunk, Api.World));
        var drops = new List<ItemStack>();
        for (int i = 0; i < logs; i++)
        {
            bool wet = SpendWater(config.WaterPerLog);
            if (BarkDrops.Roll(Api.World, species, wet ? config.WetBarkMultiplier : config.DryBarkMultiplier) is not { } bark)
                continue;
            if (wet)
                bark.StackSize = RosserBark.WetCount(bark.StackSize, config.WetBarkCountMultiplier, Api.World.Rand.NextDouble);
            if (bark.StackSize <= 0)
                continue;
            if (drops.FirstOrDefault(d => d.Equals(Api.World, bark, GlobalConstants.IgnoredStackAttributes)) is { } same)
                same.StackSize += bark.StackSize;
            else
                drops.Add(bark);
        }
        foreach (var stack in drops)
            Spawn(stack);
    }

    // The client follows the trunk with the shaft between syncs, by the server's rate.
    protected override void OnClientTick(float dt)
    {
        float angle = ShaftAngle, speed = ShaftSpeed;
        float advance = 0;
        if (_clientAngleSeeded && Pace != null && ShaftRuns && TripState is RosserState.Waiting or RosserState.Feeding)
            advance = ShaftClock.AngleAdvance(_clientLastAngle, angle, speed, dt);
        _clientLastAngle = angle;
        _clientAngleSeeded = true;
        _clientTravelBefore = ClientTravel;
        if (advance > 0 && Pace is { } p)
            _clientTravel = RosserVisuals.EstimateTravel(_clientTravel, _trip.Rate, advance, _trip.End(p));
        _clientTravelMs = Api.World.ElapsedMilliseconds;
        _clientTravelSeconds = dt;
        RebuildBoxes();
    }

    // ---- Racks, the mill ----

    // Once a second: the racks at both ends (and the in-line mill).
    private void OnSlowTick(float dt)
    {
        if (TripState == RosserState.Empty && !PullFromRack())
            PullFromGround();
        else if (TripState == RosserState.Delivered)
            PushToRack();
        var rack = CheckRack(out _);
        var outfeed = CheckOutfeed(out _);
        bool wet = DripWet;
        if (rack != _rackState || outfeed != _outfeedState || wet != _serverWet)
        {
            (_rackState, _outfeedState, _serverWet) = (rack, outfeed, wet);
            MarkDirty();
        }
    }

    /// <summary>What the racks at the infeed end offer (server side): the first with a trunk the
    /// rosser takes is <paramref name="ready"/>; otherwise the most telling reason it has none. A
    /// rack counts when its controller or filler cell is one of the ground cells just beyond the
    /// infeed end (<see cref="RosserRig.InfeedNeighbours"/>).</summary>
    public RosserRackState CheckRack(out BlockEntity? ready)
    {
        ready = null;
        if (!Config.AutoPullFromRack)
            return RosserRackState.Off;
        if (System.Logging is not { } logging)
            return RosserRackState.NoLogging;
        if (Rig is not { } rig)
            return RosserRackState.None;
        var best = RosserRackState.None;
        foreach (var local in rig.InfeedNeighbours())
        {
            if (logging.FindRack(Api.World.BlockAccessor, CellPos(local)) is not { } rack)
                continue;
            var top = logging.PeekTrunk(rack);
            bool isTrunk = Trunks.IsTrunk(top);
            var state = RosserTrip.RackOffer(isTrunk, isTrunk && Trunks.IsDebarked(top), isTrunk ? Trunks.StoredLogs(top!, Api.World) : 0);
            if (state == RosserRackState.Ready)
            {
                ready = rack;
                return state;
            }
            if (state > best)
                best = state;
        }
        return best;
    }

    /// <summary>An empty, running rosser takes the top trunk of a rack at its infeed end, unless it
    /// is already debarked or holds no logs (it waits there). Returns whether one went on.</summary>
    public bool PullFromRack()
    {
        if (TripState != RosserState.Empty || !ShaftRuns || CheckRack(out var rack) != RosserRackState.Ready || System.Logging is not { } logging)
            return false;
        var trunk = logging.PopTrunk(rack!);
        if (trunk == null)
            return false;
        rack!.MarkDirty(true);
        if (!CanLoad(trunk, null))
        {
            logging.PushTrunk(rack, trunk);
            rack.MarkDirty(true);
            return false;
        }
        Load(trunk);
        _rackState = CheckRack(out _);
        return true;
    }

    /// <summary>An empty, running rosser takes a trunk entity lying in its infeed cells (<see
    /// cref="RosserRig.InfeedNeighbours"/>), under the rack's rules: while <c>AutoPullFromRack</c>
    /// is on, and not one already debarked or holding no logs. A rack there goes first. Returns
    /// whether one went on.</summary>
    public bool PullFromGround()
    {
        if (TripState != RosserState.Empty || !ShaftRuns || !Config.AutoPullFromRack || Pace == null || System.Logging == null
            || Rig is not { } rig || !TrunkEntitySystem.Of(Api).Enabled)
            return false;
        var cells = rig.InfeedNeighbours().Select(CellPos).ToList();
        if (TrunkStations.FindInCells(Api.World, cells, t => CanLoad(t, null)) is not { } entity
            || TrunkStations.TakeEntity(entity) is not { } trunk)
            return false;
        Load(trunk);
        Api.World.PlaySoundAt(TrunkSound, Pos, 0);
        return true;
    }

    // ---- Hauling (the eidolon's haul order, Eidolon/Game/HaulOrder.cs) ----

    /// <summary>The ground cells just beyond the infeed end, where a trunk entity lying is taken
    /// (<see cref="PullFromGround"/>), and the way out of the machine from them; null without a rig.</summary>
    public (List<BlockPos> Cells, int OutwardX, int OutwardZ)? InfeedCells()
    {
        if (Rig is not { } rig)
            return null;
        var outward = Footprint.ToWorld(rig.InfeedSide, Side).Normal();
        return (rig.InfeedNeighbours().Select(CellPos).ToList(), outward.X, outward.Z);
    }

    /// <summary>Whether it takes <paramref name="trunk"/> at all, whatever it is doing now: a trunk
    /// with logs, not already debarked.</summary>
    public bool Takes(ItemStack trunk) =>
        Trunks.IsTrunk(trunk) && !Trunks.IsDebarked(trunk) && Api != null && Trunks.StoredLogs(trunk, Api.World) > 0;

    /// <summary>Where the delivered trunk can go at the outfeed end (server side): a bucking mill in
    /// line takes it itself; else, while <c>AutoPushToRack</c> is on, a rack touching the outfeed
    /// end with room is <paramref name="rack"/>.</summary>
    public RosserOutfeedState CheckOutfeed(out BlockEntity? rack)
    {
        rack = null;
        if (Rig is not { } rig)
            return RosserOutfeedState.None;
        if (MillInLine())
            return RosserOutfeedState.Mill;
        // a rack with room is no use when nothing pushes into it
        if (!Config.AutoPushToRack)
            return RosserOutfeedState.Off;
        if (System.Logging is not { } logging)
            return RosserOutfeedState.None;
        var best = RosserOutfeedState.None;
        foreach (var local in rig.OutfeedNeighbours())
        {
            if (logging.FindRack(Api.World.BlockAccessor, CellPos(local)) is not { } found)
                continue;
            if (RosserTrip.RackHasRoom(logging.TrunkCount(found)))
            {
                rack = found;
                return RosserOutfeedState.Rack;
            }
            best = RosserOutfeedState.RackFull;
        }
        return best;
    }

    /// <summary>A delivered trunk goes onto a rack with room at the outfeed end while the shaft
    /// turns, heads or no heads (a full rack leaves it on the bed; Logging Expanded's rack drops a fifth trunk
    /// silently, so the count is checked first). Then an empty rosser takes the next trunk.</summary>
    public bool PushToRack()
    {
        // only a new trip needs heads: a trunk debarked by the heads' last points still leaves
        if (TripState != RosserState.Delivered || ShaftSpeed < MinSpeed || !Config.AutoPushToRack || _trunk == null
            || CheckOutfeed(out var rack) != RosserOutfeedState.Rack || System.Logging is not { } logging)
            return false;
        logging.PushTrunk(rack!, _trunk.Clone());
        rack!.MarkDirty(true);
        ClearTrunk();
        MarkDirty(true);
        _outfeedState = CheckOutfeed(out _);
        PullFromRack();
        return true;
    }

    /// <summary>Whether a bucking mill facing the same way has this controller's cell among the
    /// cells just beyond its infeed end: it takes the delivered trunk itself (<see cref="ITrunkFeeder"/>).
    /// Not while the mills' <c>AutoPullFromRack</c> is off: then no mill takes it.</summary>
    public bool MillInLine()
    {
        var mills = BuckingSawmillSystem.Of(Api);
        if (Rig is not { } rig || mills.Rig is not { } millRig || !mills.Config.AutoPullFromRack)
            return false;
        var ba = Api.World.BlockAccessor;
        foreach (var local in rig.OutfeedNeighbours())
            if (MachineFire.MachineAt(ba, CellPos(local)) is BEBuckingMill mill && mill.Side == Side
                && millRig.InfeedNeighbours().Any(n => mill.CellPos(n).Equals(Pos)))
                return true;
        return false;
    }

    // ---- ITrunkFeeder (the bucking mill in line) ----

    /// <summary>Only the controller's cell: the outfeed end's cell on the trunk's line.</summary>
    public bool HasOutfeedCell(BlockPos world) => LocalOf(world) == Int3.Zero;

    public bool Busy => TripState is RosserState.Waiting or RosserState.Feeding;

    public ItemStack? PeekFinished() => TripState == RosserState.Delivered ? _trunk : null;

    public ItemStack? TakeFinished()
    {
        if (TripState != RosserState.Delivered || _trunk is not { } trunk)
            return null;
        ClearTrunk();
        MarkDirty(true);
        _outfeedState = CheckOutfeed(out _);
        return trunk;
    }

    // ---- Breaking ----

    /// <summary>What breaking the frame gives besides the frame: every fitted part (the heads only
    /// while unworn), and the trunk as far as it got (<see cref="RosserTrip.Broken"/>), so breaking
    /// and running it again never gives its sticks or bark twice.</summary>
    public override IEnumerable<ItemStack> AssemblyDrops()
    {
        if (Assembly is { } assembly)
            foreach (var drop in RosserBuild.Returns(assembly))
            {
                if (StackOf(drop) is not { } stack)
                    continue;
                int max = Math.Max(1, stack.Collectible.MaxStackSize);
                for (int left = drop.Count; left > 0; left -= max)
                {
                    var part = stack.Clone();
                    part.StackSize = Math.Min(left, max);
                    yield return part;
                }
            }
        if (_trunk == null)
            yield break;
        var broken = Pace is { } pace ? _trip.Broken(pace) : RosserBrokenTrunk.AsLoaded;
        var trunk = broken switch
        {
            RosserBrokenTrunk.Debranched => Trunks.Debranch(_trunk, Api.World),
            RosserBrokenTrunk.Debarked when !Trunks.IsDebarked(_trunk) => Trunks.Debark(_trunk, Api.World)?.Trunk,
            _ => null,
        };
        yield return trunk ?? _trunk.Clone();
    }

    // ---- Saving and syncing ----

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        if (_trunk != null)
            tree.SetItemstack("trunk", _trunk);
        else
            tree.RemoveAttribute("trunk");
        tree.SetInt("logs", _trip.Logs);
        tree.SetInt("branches", _trip.Branches);
        tree.SetDouble("travel", _trip.Travel);
        tree.SetDouble("rate", _trip.Rate);
        tree.SetInt("sticksDone", _trip.SticksDone);
        tree.SetInt("barkDone", _trip.BarkDone);
        if (Api?.Side == EnumAppSide.Server)
        {
            tree.SetBool("wet", DripWet);
            tree.SetInt("rackState", (int)_rackState);
            tree.SetInt("outfeedState", (int)_outfeedState);
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        var system = RosserSystem.Of(worldForResolving.Api);
        var oldTrunk = _trunk;
        int oldClass = _trip.Class;
        _trunk = tree.GetItemstack("trunk");
        if (_trunk != null && !_trunk.ResolveBlockOrItem(worldForResolving))
            _trunk = null;
        var k = _trunk == null ? TrunkClass.None : TrunkBox.ClassOf(_trunk.Block?.Variant["size"]);
        int logs = tree.GetInt("logs"), branches = tree.GetInt("branches");
        double travel = tree.GetDouble("travel"), rate = tree.GetDouble("rate");
        int? sticksDone = tree.TryGetInt("sticksDone"), barkDone = tree.TryGetInt("barkDone");
        _trip = system.Pace is { } pace
            ? RosserTrip.Restore(pace, k, logs, branches, travel, rate, sticksDone, barkDone, system.HeadTier(HeadMetal))
            : k == TrunkClass.None ? RosserTrip.None : new RosserTrip((int)k, logs, branches, travel, rate, sticksDone ?? 0, barkDone ?? 0);

        if (worldForResolving.Side == EnumAppSide.Client)
        {
            _serverWet = tree.GetBool("wet");
            _rackState = (RosserRackState)tree.GetInt("rackState");
            _outfeedState = (RosserOutfeedState)tree.GetInt("outfeedState");
            // A new trunk (or none) starts the estimate at the server's T; otherwise a sync that
            // parts from the estimate resets it, and small differences are left to the shaft.
            bool sameTrunk = _trunk != null && oldTrunk != null && oldClass == _trip.Class;
            if (!sameTrunk)
                _clientTravel = _clientTravelBefore = _trip.Travel;
            else
                _clientTravel = RosserVisuals.Reconcile(_clientTravel, _trip.Travel);
        }
        RebuildBoxes();
    }

    public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping, Dictionary<int, AssetLocation> itemIdMapping)
    {
        base.OnStoreCollectibleMappings(blockIdMapping, itemIdMapping);
        _trunk?.Collectible.OnStoreCollectibleMappings(Api.World, new DummySlot(_trunk), blockIdMapping, itemIdMapping);
    }

    public override void OnLoadCollectibleMappings(IWorldAccessor worldForResolve, Dictionary<int, AssetLocation> oldBlockIdMapping,
                                                   Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
    {
        base.OnLoadCollectibleMappings(worldForResolve, oldBlockIdMapping, oldItemIdMapping, schematicSeed, resolveImports);
        _trunk?.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForResolve);
    }

    // ---- Info ----

    private static string L(string key, params object[] args) => Lang.Get(RosserSystem.Domain + ":rosser-" + key, args);

    /// <summary>The rosser's own lines: the heads while the parts are still going in (the wearing line says
    /// them once the parts are all in), the drip's pipes, the trunk and where it is, and why the racks' trunks
    /// are, or are not, coming and going.</summary>
    protected override void AppendContents(StringBuilder dsc)
    {
        if (!Built)
        {
            if (Heads == null)
                dsc.AppendLine(L(RingIn ? "info-needs-heads" : "info-needs-heads-ring"));
            else
                dsc.AppendLine(L("info-heads", Lang.Get("material-" + HeadMetal), HeadsLeft, HeadsCapacity));
        }
        if (PipeMetal is { } pipes)
            dsc.AppendLine(L("info-pipes", Lang.Get("material-" + pipes)));
        if (!Ready && _trunk == null)
            return;
        var state = TripState;
        if (_trunk != null)
            dsc.AppendLine(L("info-trunk", _trunk.GetName(), _trip.Logs, _trip.Branches));
        switch (state)
        {
            case RosserState.Empty:
                dsc.AppendLine(L("info-empty"));
                break;
            case RosserState.Waiting:
                dsc.AppendLine(L(ShaftRuns ? "info-waiting" : "info-waiting-stopped"));
                break;
            case RosserState.Feeding:
                double end = Pace is { } p ? _trip.End(p) : 0;
                int percent = end > 0 ? (int)(Math.Clamp(SideTravel / end, 0, 1) * 100) : 0;
                if (!ShaftRuns)
                    dsc.AppendLine(L("info-stalled", percent));
                break;
            case RosserState.Delivered:
                dsc.AppendLine(L("info-delivered"));
                if (_outfeedState != RosserOutfeedState.Unknown)
                    dsc.AppendLine(L("info-outfeed-" + _outfeedState.ToString().ToLowerInvariant()));
                break;
        }
        if (state == RosserState.Empty && _rackState != RosserRackState.Unknown)
            dsc.AppendLine(L("info-rack-" + _rackState.ToString().ToLowerInvariant()));
        if (!DripWet)
            dsc.AppendLine(L("info-dry"));
    }

    /// <summary>With a wrench: how fast the heads feed, and the trip in blocks.</summary>
    protected override void AppendDetail(StringBuilder dsc)
    {
        if (HeadMetal is { } metal && Pace is { } pace)
            dsc.AppendLine(L("info-head-speed", pace.HeadSpeed(System.HeadTier(metal)).ToString("0.##", CultureInfo.InvariantCulture)));
        if (_trunk != null && Pace is { } p)
            dsc.AppendLine(L("info-travel", SideTravel.ToString("0.0", CultureInfo.InvariantCulture),
                _trip.End(p).ToString("0.0", CultureInfo.InvariantCulture)));
    }
}
