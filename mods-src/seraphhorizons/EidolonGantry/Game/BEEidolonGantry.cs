using System.Text;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.EidolonGantry.Core;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.EidolonGantry;

/// <summary>
/// The eidolon gantry's controller (the foot of its front right post). Holds the winch's fitted
/// parts (<see cref="GantryParts"/>: every item's code, the drum's and spine's of the gantry's
/// wood) and the winch's depth (0 hung, 1 let down; nothing drives it yet), keeps its ghost cells
/// stamped, and hands every click the winch does not take to its extensions
/// (<see cref="IEidolonGantryExtension"/>: the body stages, #672). The client draws the winch and
/// the hung parts (<see cref="EidolonGantryRenderer"/>). The rules are EidolonGantry/Core's.
/// </summary>
public class BEEidolonGantry : BlockEntity
{
    private const string PartKeyPrefix = "part-";
    public static readonly AssetLocation GhostCode = new(EidolonGantrySystem.Domain, "eidolongantryghost");

    private GantryParts _parts = new("oak");
    private double _depth;
    private Dictionary<Int3, Cuboidf[]>? _cells;
    private EidolonGantryRenderer? _renderer;

    public Side Side { get; private set; } = Side.North;
    /// <summary>The gantry's wood, its <c>wood</c> variant.</summary>
    public string Wood { get; private set; } = "oak";
    public GantryParts Parts => _parts;
    /// <summary>The winch and the spine are fitted: the body can be built on the spine.</summary>
    public bool WinchComplete => _parts.Complete;

    /// <summary>How far the winch has let the spine (and whatever hangs on it) down, 0..1: 0 hung, 1
    /// the body's feet on the floor (the rig's <c>depth</c>). Set on the server; synced.</summary>
    public double WinchDepth
    {
        get => _depth;
        set
        {
            double d = Math.Clamp(double.IsFinite(value) ? value : 0, 0, 1);
            if (d == _depth)
                return;
            _depth = d;
            MarkDirty(true);
        }
    }

    /// <summary>The gantry's extensions, in their blocktype order.</summary>
    public IEnumerable<IEidolonGantryExtension> Extensions => Behaviors.OfType<IEidolonGantryExtension>();

    /// <summary>Every winch stage, the spine and every extension fitted.</summary>
    public bool Complete => _parts.Complete && Extensions.All(e => e.Complete);

    private GantryRig? Rig => EidolonGantrySystem.Of(Api).Rig;

    public override void Initialize(ICoreAPI api)
    {
        // Before the behaviors initialise, so an extension finds the wood and side.
        Side = Sides.TryParse(Block.Variant["side"], out var side) ? side : Side.North;
        Wood = Block.Variant["wood"] ?? "oak";
        if (_parts.Wood != Wood)
            _parts = GantryParts.Restore(Wood, _parts.Snapshot());
        base.Initialize(api);
        BuildBoxes();
        if (api.Side == EnumAppSide.Server)
        {
            RegisterDelayedCallback(_ =>
            {
                if (Api?.World.BlockAccessor.GetBlockEntity(Pos) == this)
                    EnsureGhosts();
            }, 100);
            // A ghost can vanish without being broken (an explosion, another mod).
            RegisterGameTickListener(_ => EnsureGhosts(), 2000);
        }
        else
        {
            RegisterGameTickListener(OnClientTick, 500);
            if (api is ICoreClientAPI capi && Rig is { } rig && Block is BlockEidolonGantry)
                _renderer = new EidolonGantryRenderer(capi, this, rig);
        }
    }

    // ---- Footprint ----

    public BlockPos CellPos(Int3 local)
    {
        var w = Footprint.ToWorld(local, Side);
        return Pos.AddCopy(w.X, w.Y, w.Z);
    }

    /// <summary>A native-frame point (the rig's anchors: <c>body</c>, <c>hang</c>, <c>fit</c>,
    /// <c>exit</c>) as a world position.</summary>
    public Vec3d WorldPoint(Float3 native)
    {
        var w = Footprint.ToWorld(native, Side);
        return new Vec3d(Pos.X + w.X, Pos.Y + w.Y, Pos.Z + w.Z);
    }

    /// <summary>The world side a native side is turned to (the rig's <c>exitSide</c>, west).</summary>
    public Side WorldSide(Side native) => Footprint.ToWorld(native, Side);

    public IEnumerable<BlockPos> GhostCells() => Rig is { } rig ? rig.GhostCells.Select(c => CellPos(c.Pos)) : [];

    /// <summary>A cell's boxes, for selection and collision alike: none in a hollow cell (the
    /// crank's, and the open space the body hangs in and walks out of).</summary>
    public Cuboidf[]? CellBoxes(BlockPos cellPos) =>
        _cells?.GetValueOrDefault(Footprint.ToLocal(new Int3(cellPos.X - Pos.X, cellPos.Y - Pos.Y, cellPos.Z - Pos.Z), Side));

    // The boxes never change: built once, then only read (collision lookups can run off the main thread).
    private void BuildBoxes()
    {
        if (Rig is not { } rig)
            return;
        var side = Side;
        Cuboidf World(Box b)
        {
            var w = Footprint.ToWorld(b, side);
            return new Cuboidf(w.X1, w.Y1, w.Z1, w.X2, w.Y2, w.Z2);
        }
        _cells = rig.Cells.ToDictionary(c => c.Pos, c =>
            c.Hollow ? [] : c.Boxes.Count == 0 ? [Cuboidf.Default()] : c.Boxes.Select(World).ToArray());
    }

    private Block? GhostBlock() => Api.World.GetBlock(GhostCode);

    public void PlaceGhosts()
    {
        if (Api.Side != EnumAppSide.Server || GhostBlock() is not { } ghost)
            return;
        foreach (var pos in GhostCells())
            StampGhost(ghost, pos);
    }

    private void StampGhost(Block ghost, BlockPos pos)
    {
        Api.World.BlockAccessor.SetBlock(ghost.BlockId, pos);
        if (Api.World.BlockAccessor.GetBlockEntity(pos) is BEEidolonGantryGhost be)
        {
            be.Principal = Pos.Copy();
            be.MarkDirty(true);
        }
    }

    /// <summary>Restores ghost cells that went missing, and points every ghost back here.</summary>
    public void EnsureGhosts()
    {
        if (Api.Side != EnumAppSide.Server || GhostBlock() is not { } expected)
            return;
        var ba = Api.World.BlockAccessor;
        foreach (var pos in GhostCells())
        {
            if (ba.GetChunkAtBlockPos(pos) == null)
                continue;
            var current = ba.GetBlock(pos);
            if (current.Id == expected.Id)
            {
                if (ba.GetBlockEntity(pos) is BEEidolonGantryGhost be && !Pos.Equals(be.Principal))
                {
                    be.Principal = Pos.Copy();
                    be.MarkDirty(true);
                }
            }
            else if (current.Id == 0)
                StampGhost(expected, pos);
        }
    }

    public void RemoveGhosts()
    {
        if (Api.Side != EnumAppSide.Server)
            return;
        var ba = Api.World.BlockAccessor;
        foreach (var pos in GhostCells())
            if (ba.GetBlock(pos) is BlockEidolonGantryGhost && ba.GetBlockEntity(pos) is BEEidolonGantryGhost be && Pos.Equals(be.Principal))
                ba.SetBlock(0, pos);
    }

    // ---- Interaction ----

    /// <summary>
    /// Right-click on the gantry or any of its cells. In creative mode, Ctrl fits the next stage
    /// (the winch's, then each extension's) with nothing taken. A winch part in hand while the winch
    /// is not complete goes to the winch (fitted if it is the next stage's). Every other click is
    /// offered to the extensions in turn; a winch part nobody takes is refused as already fitted.
    /// Decided and done on the server; the client only says whether the click is the gantry's.
    /// </summary>
    public bool OnInteract(IPlayer byPlayer)
    {
        var slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        string? code = slot?.Itemstack?.Collectible?.Code?.ToString();
        if (CreativeShortcutApplies(byPlayer))
        {
            if (Api.Side == EnumAppSide.Server)
                FitNext(byPlayer);
            return true;
        }
        bool part = _parts.IsPart(code);
        if (part && !_parts.Complete)
        {
            if (Api.Side == EnumAppSide.Server)
                TryFitPart(slot!, byPlayer);
            return true;
        }
        foreach (var extension in Extensions)
            if (extension.OnGantryInteract(this, byPlayer, slot))
                return true;
        if (!part)
            return false;
        if (Api.Side == EnumAppSide.Server)
            Error(byPlayer, "error-part-fitted");
        return true;
    }

    /// <summary>Whether a click is the creative shortcut's: a player in creative mode, Ctrl without
    /// Shift, on a gantry with a stage still to fit.</summary>
    public bool CreativeShortcutApplies(IPlayer byPlayer)
    {
        var controls = byPlayer.Entity.Controls;
        return !Complete
               && CreativeUpgrades.Applies(byPlayer.WorldData?.CurrentGameMode == EnumGameMode.Creative, controls.CtrlKey, controls.ShiftKey);
    }

    /// <summary>The creative shortcut (server side): fits the winch's next stage with its first
    /// item, or once the winch is complete, the first extension's that has one, nothing taken.</summary>
    public bool FitNext(IPlayer? byPlayer)
    {
        if (_parts.NextPart is { } next)
        {
            if (Collectible(next) is not { } collectible)
            {
                Api.Logger.Warning("[seraphhorizons] Eidolon gantry: no {0} for the creative shortcut", next);
                return false;
            }
            return TryFitPart(new DummySlot(new ItemStack(collectible, GantryParts.Needed(_parts.Next!.Value))), byPlayer, free: true);
        }
        foreach (var extension in Extensions)
            if (!extension.Complete && extension.CreativeFitNext(this, byPlayer))
                return true;
        return false;
    }

    /// <summary>Fits every winch stage still open (the assembled creative stack; server side).</summary>
    public void FitAll()
    {
        _parts.FitAll();
        MarkDirty(true);
    }

    /// <summary>Fits the part in <paramref name="slot"/> if it is the winch's next stage's (server
    /// side); false, with an error to the player, when the rules say no. As many as the stage takes
    /// (<see cref="GantryParts.Needed"/>) are taken from the slot unless the player is in creative
    /// mode or it is <paramref name="free"/>.</summary>
    public bool TryFitPart(ItemSlot slot, IPlayer? byPlayer, bool free = false)
    {
        var stack = slot.Itemstack;
        string? code = stack?.Collectible?.Code?.ToString();
        if (stack == null)
            return false;
        bool consumes = !free && byPlayer?.WorldData.CurrentGameMode != EnumGameMode.Creative;
        var verdict = _parts.CanFit(code, out var stage, consumes ? stack.StackSize : int.MaxValue);
        if (verdict != GantryFitVerdict.Fits)
        {
            switch (verdict)
            {
                case GantryFitVerdict.OutOfOrder:
                    Error(byPlayer, "error-order", StageName(_parts.Next));
                    break;
                case GantryFitVerdict.AlreadyFitted:
                    Error(byPlayer, "error-part-fitted");
                    break;
                case GantryFitVerdict.TooFew:
                    Error(byPlayer, "error-too-few", GantryParts.Needed(_parts.Next!.Value), StageName(_parts.Next));
                    break;
                default:
                    Error(byPlayer, "error-not-a-part");
                    break;
            }
            return false;
        }
        _parts.Fit(code);
        if (consumes)
        {
            slot.TakeOut(GantryParts.Needed(stage));
            slot.MarkDirty();
        }
        Api.World.PlaySoundAt(Block.Sounds.Place, Pos, -0.25, byPlayer);
        MarkDirty(true);
        return true;
    }

    /// <summary>A winch stage's name as the block info and the errors show it.</summary>
    public static string StageName(GantryStage? stage) =>
        stage is { } s ? Lang.Get(EidolonGantrySystem.Domain + ":eidolongantry-stage-" + GantryRequires.Name(s)) : "";

    /// <summary>Sends <paramref name="byPlayer"/> the gantry's error <paramref name="key"/>
    /// (<c>seraphhorizons:eidolongantry-{key}</c>); false, for returning.</summary>
    public bool Error(IPlayer? byPlayer, string key, params object[] args)
    {
        if (byPlayer is IServerPlayer sp)
            sp.SendIngameError("eidolongantry-" + key, Lang.GetL(sp.LanguageCode, EidolonGantrySystem.Domain + ":eidolongantry-" + key, args));
        return false;
    }

    /// <summary>The item or block of <paramref name="code"/> in this game, or null.</summary>
    public CollectibleObject? Collectible(string code)
    {
        var loc = new AssetLocation(code);
        if (Api.World.GetItem(loc) is { Id: > 0 } item)
            return item;
        return Api.World.GetBlock(loc) is { Id: > 0 } block ? block : null;
    }

    // The client removes a broken gantry at once, and an update the server sent before it heard of
    // the break then brings the block entity back over air: drop it.
    private void OnClientTick(float dt)
    {
        if (Api.World.BlockAccessor.GetBlock(Pos) is BlockEidolonGantry)
            return;
        DisposeRenderer();
        Api.World.BlockAccessor.RemoveBlockEntity(Pos);
    }

    // ---- Breaking ----

    /// <summary>What breaking the gantry gives besides the frame: every winch part fitted, as many
    /// as each stage took, and everything the extensions hold.</summary>
    public IEnumerable<ItemStack> PartDrops()
    {
        foreach (var drop in _parts.Returns())
            if (Collectible(drop.Code) is { } collectible)
                yield return new ItemStack(collectible, drop.Count);
        foreach (var extension in Extensions)
            foreach (var stack in extension.Drops(Api.World))
                yield return stack;
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        RemoveGhosts();
        DisposeRenderer();
    }

    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        DisposeRenderer();
    }

    private void DisposeRenderer()
    {
        _renderer?.Dispose();
        _renderer = null;
    }

    // ---- Saving and syncing ----

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        foreach (var stage in GantryRequires.Stages)
            tree.RemoveAttribute(PartKeyPrefix + GantryRequires.Name(stage));
        foreach (var (requires, code) in _parts.Snapshot())
            tree.SetString(PartKeyPrefix + requires, code);
        tree.SetDouble("depth", _depth);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        var fitted = new Dictionary<string, string>();
        foreach (var stage in GantryRequires.Stages)
            if (tree.GetString(PartKeyPrefix + GantryRequires.Name(stage)) is { Length: > 0 } code)
                fitted[GantryRequires.Name(stage)] = code;
        // The wood is the block's; before Initialize (a chunk loading) it is not known yet, and
        // Initialize restores the parts again on the right wood.
        string wood = Block?.Variant?["wood"] ?? Wood;
        _parts = GantryParts.Restore(wood, fitted);
        _depth = Math.Clamp(tree.GetDouble("depth"), 0, 1);
    }

    // ---- Info ----

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        string L(string key, params object[] args) => Lang.Get(EidolonGantrySystem.Domain + ":eidolongantry-" + key, args);
        if (_parts.Next is { } next)
        {
            dsc.AppendLine(L("info-next", StageName(next), StageNeeds(next)));
            var after = GantryRequires.Stages.Where(s => s > next && !_parts.Has(s)).ToList();
            if (after.Count > 0)
                dsc.AppendLine(L("info-then", string.Join(", ", after.Select(s => StageName(s)))));
        }
        else
            dsc.AppendLine(L("info-winch-complete"));
    }

    /// <summary>What a stage takes, as the info line shows it: its count and item (the drum's
    /// planks and the spine's beams named in the gantry's wood).</summary>
    public string StageNeeds(GantryStage stage) =>
        Lang.Get(EidolonGantrySystem.Domain + ":eidolongantry-needs-" + GantryRequires.Name(stage), GantryParts.Needed(stage),
            Lang.Get("game:material-" + Wood));
}
