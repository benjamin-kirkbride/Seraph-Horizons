using System.Text;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.MandrelStation.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.MandrelStation;

/// <summary>
/// The mandrel station's controller. Holds the fitted mandrel (its code), the hollow section on it as
/// its item stack, and the forging (<see cref="ForgeJob"/>: the blows struck and W, 0..1). The player
/// forges as on the anvil: each right-click with a hammer is a blow (the anvil's sound and sparks at
/// <c>strike.pos</c>, the hammer's durability paid; a hammer of a higher tool tier forges more a blow),
/// and at the blow that finishes the hollow two pipe sections of the
/// hollow's metal go into a container beyond the tip, or drop there. A blow on a bare mandrel takes a
/// hollow from a chest or hopper beside the stump. The server keeps the ghost cell stamped; the client
/// draws the station (<see cref="MandrelStationRenderer"/>, through <see cref="IMandrelStationView"/>).
/// The rules are MandrelStation/Core's.
/// </summary>
public class BEMandrelStation : BlockEntity, IMandrelStationView
{
    private static readonly AssetLocation LatchSound = new("game", "sounds/effect/latch");
    private static readonly AssetLocation HollowSound = new("game", "sounds/block/chute");
    private static readonly AssetLocation BlowSound = new("game", "sounds/effect/anvilhit");
    private static readonly AssetLocation SectionSound = new("game", "sounds/block/chute");

    private string? _mandrel;
    private ItemStack? _hollow;
    private ForgeJob _job = ForgeJob.None;
    private long _lastBlowAt = long.MinValue / 2;
    private int? _serverBlowsNeeded;
    private Dictionary<Int3, Cuboidf[]>? _cells;
    private Dictionary<Int3, Cuboidf[]>? _collision;
    private MandrelStationRenderer? _renderer;

    public Side Side { get; private set; } = Side.North;
    /// <summary>The fitted mandrel's code, or null.</summary>
    public string? Mandrel => _mandrel;
    /// <summary>The mandrel is fitted: the station works.</summary>
    public bool Complete => _mandrel != null;
    /// <summary>A hollow is on the mandrel.</summary>
    public bool HollowOn => _job.On && _hollow != null;
    public ItemStack? Hollow => _hollow;
    public ForgeJob Job => _job;

    /// <summary>Blows the hollow on takes with the base hammer (the copper one), as the server runs (0
    /// with none); a better hammer takes fewer.</summary>
    public int BlowsNeeded => _job.On ? _serverBlowsNeeded ?? Config.BlowsPerHollow(_job.Class) : 0;

    // IMandrelStationView
    public bool PartFitted(string? requires) => MandrelPart.Fitted(requires, _mandrel);
    public string? MandrelMetal => MandrelPart.MetalOf(_mandrel);
    public int HollowClass => HollowOn ? _job.Class : 0;
    public double ForgeWork => _job.Work;
    public int Blows => _job.Blows;

    private MandrelStationSystem System => MandrelStationSystem.Of(Api);
    private MandrelStationConfig Config => System.Config;
    private MandrelStationRig? Rig => System.Rig;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        Side = Sides.TryParse(Block.Variant["side"], out var side) ? side : Side.North;
        BuildBoxes();
        if (api.Side == EnumAppSide.Server)
        {
            RegisterDelayedCallback(_ =>
            {
                if (Api?.World.BlockAccessor.GetBlockEntity(Pos) == this)
                    EnsureGhosts();
            }, 100);
            RegisterGameTickListener(_ => EnsureGhosts(), 1000);
        }
        else
        {
            RegisterGameTickListener(OnClientTick, 250);
            if (api is ICoreClientAPI capi && Rig is { } rig && Block is BlockMandrelStation)
                _renderer = new MandrelStationRenderer(capi, this, this, rig);
        }
    }

    // ---- Footprint ----

    public BlockPos CellPos(Int3 local)
    {
        var w = Footprint.ToWorld(local, Side);
        return Pos.AddCopy(w.X, w.Y, w.Z);
    }

    /// <summary>A native-frame point as a world position.</summary>
    public Vec3d WorldPoint(Float3 native)
    {
        var w = Footprint.ToWorld(native, Side);
        return new Vec3d(Pos.X + w.X, Pos.Y + w.Y, Pos.Z + w.Z);
    }

    /// <summary>Every ghost cell's world position.</summary>
    public IEnumerable<BlockPos> GhostCells() =>
        Rig is { } rig ? rig.GhostCells.Select(c => CellPos(c.Pos)) : [];

    public Cuboidf[]? CellBoxes(BlockPos cellPos) => Lookup(_cells, cellPos);

    /// <summary>A cell's selection boxes and the rig's collision-only lid.</summary>
    public Cuboidf[]? CollisionBoxes(BlockPos cellPos) => Lookup(_collision, cellPos);

    private Cuboidf[]? Lookup(Dictionary<Int3, Cuboidf[]>? table, BlockPos cellPos) =>
        table?.GetValueOrDefault(Footprint.ToLocal(new Int3(cellPos.X - Pos.X, cellPos.Y - Pos.Y, cellPos.Z - Pos.Z), Side));

    // The boxes never change: built once, then only read (collision lookups can run off the main thread).
    private void BuildBoxes()
    {
        if (Rig is not { } rig || !Sides.TryParse(Block?.Variant["side"], out var side))
            return;
        Cuboidf World(Box b)
        {
            var w = Footprint.ToWorld(b, side);
            return new Cuboidf(w.X1, w.Y1, w.Z1, w.X2, w.Y2, w.Z2);
        }
        var cells = rig.Cells.ToDictionary(c => c.Pos, c => c.Boxes.Count == 0 ? [Cuboidf.Default()] : c.Boxes.Select(World).ToArray());
        var collision = new Dictionary<Int3, Cuboidf[]>(cells);
        foreach (var c in rig.Cells)
            if (c.LidBox is { } lid)
                collision[c.Pos] = [.. cells[c.Pos], World(lid)];
        _cells = cells;
        _collision = collision;
    }

    private Block? GhostBlock() => Api.World.GetBlock(new AssetLocation(MandrelStationSystem.Domain, "mandrelstation-ghost"));

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
        if (Api.World.BlockAccessor.GetBlockEntity(pos) is BEMandrelStationGhost be)
        {
            be.Principal = Pos.Copy();
            be.MarkDirty(true);
        }
    }

    /// <summary>Restores the ghost cell if it went missing, and points it back here.</summary>
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
                if (ba.GetBlockEntity(pos) is BEMandrelStationGhost be && !Pos.Equals(be.Principal))
                {
                    be.Principal = Pos.Copy();
                    be.MarkDirty(true);
                }
            }
            else if (current.Id == 0 || current is BlockMandrelStationGhost)
                StampGhost(expected, pos);
        }
    }

    public void RemoveGhosts()
    {
        if (Api.Side != EnumAppSide.Server)
            return;
        var ba = Api.World.BlockAccessor;
        foreach (var pos in GhostCells())
            if (ba.GetBlock(pos) is BlockMandrelStationGhost && ba.GetBlockEntity(pos) is BEMandrelStationGhost be && Pos.Equals(be.Principal))
                ba.SetBlock(0, pos);
    }

    // ---- Interaction ----

    /// <summary>
    /// Right-click on the station or its ghost. Ctrl takes back: in creative mode with no mandrel it
    /// fits an iron one with nothing taken; else a hollow not yet struck comes off, or with no hollow
    /// on the mandrel comes out. A rod of iron, meteoric iron or steel is fitted as the mandrel; a lead
    /// or copper hollow section goes on a bare mandrel; a hammer strikes a blow (on a bare mandrel it
    /// takes a hollow from the infeed instead). Anything else is the item's own business. Decided and
    /// done on the server; the client says whether the click is the station's.
    /// </summary>
    public bool OnInteract(IPlayer byPlayer)
    {
        var slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        var controls = byPlayer.Entity.Controls;
        bool take = controls.CtrlKey && !controls.ShiftKey;
        string? code = slot?.Itemstack?.Collectible?.Code?.ToString();
        bool server = Api.Side == EnumAppSide.Server;
        if (take)
        {
            if (!server)
                return true;
            if (CreativeShortcutApplies(byPlayer))
                FitCreativeMandrel(byPlayer);
            else
                TakeBack(byPlayer);
            return true;
        }
        if (MandrelPart.IsMandrel(code))
        {
            if (server)
                TryFitMandrel(slot!, byPlayer);
            return true;
        }
        if (Forging.ClassOfHollow(code) != 0 && !HollowOn)
            return !server || TryLoadHollow(slot!, byPlayer);
        if (Forging.IsHammer(code) && !controls.ShiftKey)
        {
            if (server)
                StrikeBy(byPlayer, slot!);
            return true;
        }
        return false;
    }

    /// <summary>Whether a click is the creative shortcut's: a player in creative mode, Ctrl without
    /// Shift, on a station with no mandrel.</summary>
    public bool CreativeShortcutApplies(IPlayer byPlayer)
    {
        var controls = byPlayer.Entity.Controls;
        return !Complete
               && CreativeUpgrades.Applies(byPlayer.WorldData?.CurrentGameMode == EnumGameMode.Creative, controls.CtrlKey, controls.ShiftKey);
    }

    /// <summary>The creative shortcut (server side): fits an iron mandrel with nothing taken.</summary>
    public bool FitCreativeMandrel(IPlayer? byPlayer)
    {
        if (Complete)
            return false;
        if (Api.World.GetItem(new AssetLocation(MandrelPart.Codes[0])) is not { } item)
        {
            Api.Logger.Warning("[seraphhorizons] Mandrel station: no {0} for the creative shortcut", MandrelPart.Codes[0]);
            return false;
        }
        return TryFitMandrel(new DummySlot(new ItemStack(item)), byPlayer, free: true);
    }

    /// <summary>Fits the rod in <paramref name="slot"/> as the mandrel (server side); false, with an
    /// error to the player, when one is already in. One item is taken from the slot unless the player
    /// is in creative mode or it is <paramref name="free"/>.</summary>
    public bool TryFitMandrel(ItemSlot slot, IPlayer? byPlayer, bool free = false)
    {
        string? code = slot.Itemstack?.Collectible?.Code?.ToString();
        switch (MandrelPart.CanFit(code, _mandrel))
        {
            case MandrelFitVerdict.Fits:
                break;
            case MandrelFitVerdict.AlreadyFitted:
                return Error(byPlayer, "error-mandrel-fitted");
            default:
                return false;
        }
        _mandrel = MandrelPart.Normalise(code);
        if (!free && byPlayer?.WorldData.CurrentGameMode != EnumGameMode.Creative)
        {
            slot.TakeOut(1);
            slot.MarkDirty();
        }
        Api.World.PlaySoundAt(LatchSound, Pos, -0.25, byPlayer);
        MarkDirty(true);
        return true;
    }

    /// <summary>Ctrl + right-click (server side): a hollow not yet struck comes off; with no hollow
    /// on, the mandrel comes out. A hollow being forged stays, and so does the mandrel under it.</summary>
    public bool TakeBack(IPlayer byPlayer)
    {
        if (HollowOn)
        {
            if (!_job.Untouched)
                return Error(byPlayer, "error-busy");
            var hollow = _hollow!;
            ClearJob();
            Give(byPlayer, hollow);
            Api.World.PlaySoundAt(HollowSound, Pos, 0, byPlayer);
            MarkDirty(true);
            return true;
        }
        if (!MandrelPart.CanTakeBack(_mandrel, HollowOn) || Api.World.GetItem(new AssetLocation(_mandrel!)) is not { } item)
            return false;
        _mandrel = null;
        Give(byPlayer, new ItemStack(item));
        Api.World.PlaySoundAt(LatchSound, Pos, 0, byPlayer);
        MarkDirty(true);
        return true;
    }

    /// <summary>The pipe section a hollow of class <paramref name="k"/> is forged into, or null when
    /// it does not exist in this game (it is UnifiedPipes' item).</summary>
    private Item? SectionItem(int k) =>
        Forging.SectionFor(k) is { } code && Api.World.GetItem(new AssetLocation(code)) is { Id: > 0, IsMissing: false } item ? item : null;

    /// <summary>Puts a hollow from <paramref name="slot"/> on the mandrel, if the station takes it now.</summary>
    public bool TryLoadHollow(ItemSlot slot, IPlayer? byPlayer)
    {
        string? code = slot.Itemstack?.Collectible?.Code?.ToString();
        switch (Forging.CanLoad(code, Complete, HollowOn))
        {
            case HollowLoadVerdict.Loads:
                break;
            case HollowLoadVerdict.NoMandrel:
                return Error(byPlayer, "error-no-mandrel");
            case HollowLoadVerdict.Occupied:
                return Error(byPlayer, "error-occupied");
            default:
                return false;
        }
        if (SectionItem(Forging.ClassOfHollow(code)) == null)
            return Error(byPlayer, "error-no-section");
        Load(slot.TakeOut(1));
        slot.MarkDirty();
        return true;
    }

    private void Load(ItemStack hollow)
    {
        _hollow = hollow;
        _job = new ForgeJob(Forging.ClassOfHollow(hollow.Collectible.Code.ToString()), 0, 0);
        var at = WorldPoint(Rig?.Strike ?? new Float3(0.5f, 0.75f, 1f));
        Api.World.PlaySoundAt(HollowSound, at.X, at.Y, at.Z);
        MarkDirty(true);
    }

    private void ClearJob()
    {
        _hollow = null;
        _job = ForgeJob.None;
    }

    private void Give(IPlayer byPlayer, ItemStack stack)
    {
        if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 1.2, 0.5));
    }

    private bool Error(IPlayer? byPlayer, string key, params object[] args)
    {
        if (byPlayer is IServerPlayer sp)
            sp.SendIngameError("mandrelstation-" + key, Lang.GetL(sp.LanguageCode, MandrelStationSystem.Domain + ":mandrelstation-" + key, args));
        return false;
    }

    // ---- Forging ----

    /// <summary>A right-click with a hammer (server side): on a bare mandrel, takes a hollow from the
    /// infeed; on a hollow, strikes a blow, no faster than <see cref="Forging.BlowIntervalMs"/>.</summary>
    public bool StrikeBy(IPlayer byPlayer, ItemSlot hammer)
    {
        if (!Complete)
            return Error(byPlayer, "error-no-mandrel");
        if (!HollowOn)
        {
            if (PullFromInfeed())
                return true;
            return Error(byPlayer, "error-no-hollow");
        }
        long now = Api.World.ElapsedMilliseconds;
        if (!Forging.Ready(now - _lastBlowAt))
            return false;
        _lastBlowAt = now;
        Blow(byPlayer, hammer);
        return true;
    }

    /// <summary>
    /// One blow of the hammer on the hollow (server side): W advances by the hammer's tool tier over
    /// the base tier, over the hollow's blows (<see cref="Forging.WorkPerBlow"/>; no hammer, or one with
    /// no tier, is the base), the anvil's sound and sparks at <c>strike.pos</c>, the hammer in
    /// <paramref name="hammer"/> (if any) loses its wear a blow, and at the blow that brings W to 1 the
    /// hollow is used up and its two pipe sections come off. Returns the sections delivered.
    /// </summary>
    public int Blow(IPlayer? byPlayer = null, ItemSlot? hammer = null)
    {
        if (!HollowOn || !Complete)
            return 0;
        int tier = hammer?.Itemstack?.Collectible?.ToolTier ?? 0;
        (_job, bool finished) = _job.Strike(Config.BlowsPerHollow(_job.Class), tier, Config.BaseHammerTier);
        var at = WorldPoint(Rig?.Strike ?? new Float3(0.5f, 0.75f, 1f));
        Api.World.PlaySoundAt(BlowSound, at.X, at.Y, at.Z, null, true, 16, 0.8f);
        Sparks(at);
        if (hammer?.Itemstack is { } stack && Config.HammerWearPerBlow > 0
            && byPlayer?.WorldData.CurrentGameMode != EnumGameMode.Creative)
        {
            stack.Collectible.DamageItem(Api.World, byPlayer?.Entity, hammer, Config.HammerWearPerBlow);
            hammer.MarkDirty();
        }
        if (finished)
        {
            int k = _job.Class;
            ClearJob();
            if (SectionItem(k) is { } section)
                Deliver(new ItemStack(section, Forging.SectionsPerHollow));
            MarkDirty(true);
            return Forging.SectionsPerHollow;
        }
        MarkDirty(false);
        return 0;
    }

    // Small sparks off the blow, the anvil's colour.
    private void Sparks(Vec3d at)
    {
        var props = new SimpleParticleProperties(
            3, 6, ColorUtil.ToRgba(255, 255, 233, 83),
            at.AddCopy(-0.1, 0, -0.1), at.AddCopy(0.1, 0.05, 0.1),
            new Vec3f(-1.2f, 0.6f, -1.2f), new Vec3f(1.2f, 2.2f, 1.2f),
            0.12f, 1f, 0.1f, 0.2f, EnumParticleModel.Cube)
        {
            VertexFlags = 128,
            WithTerrainCollision = true,
        };
        Api.World.SpawnParticles(props);
    }

    /// <summary>Puts <paramref name="stack"/> into a container just beyond the output face, else
    /// drops it there.</summary>
    public void Deliver(ItemStack stack)
    {
        if (Rig is not { } rig)
            return;
        var dummy = new DummySlot(stack);
        if (Api.World.BlockAccessor.GetBlockEntity(CellPos(rig.OutputNeighbour())) is Vintagestory.GameContent.BlockEntityContainer container)
        {
            foreach (var slot in container.Inventory)
            {
                if (dummy.Empty)
                    break;
                if (slot.CanHold(dummy))
                    dummy.TryPutInto(Api.World, slot, dummy.StackSize);
            }
            container.MarkDirty(true);
        }
        if (dummy.Empty)
            return;
        var at = WorldPoint(rig.OutputDrop());
        var n = Footprint.ToWorld(rig.OutputSide, Side).Normal();
        Api.World.SpawnItemEntity(dummy.Itemstack, at, new Vec3d(n.X * 0.05, 0.02, n.Z * 0.05));
        Api.World.PlaySoundAt(SectionSound, at.X, at.Y, at.Z);
    }

    /// <summary>A hollow a container at the infeed face holds that the station would take.</summary>
    private IEnumerable<(Vintagestory.GameContent.BlockEntityContainer Container, ItemSlot Slot)> InfeedHollows()
    {
        if (Rig is not { } rig)
            yield break;
        foreach (var local in rig.InfeedNeighbours())
        {
            if (Api.World.BlockAccessor.GetBlockEntity(CellPos(local)) is not Vintagestory.GameContent.BlockEntityContainer container)
                continue;
            foreach (var slot in container.Inventory)
            {
                int k = Forging.ClassOfHollow(slot.Itemstack?.Collectible?.Code?.ToString());
                if (k != 0 && SectionItem(k) != null)
                    yield return (container, slot);
            }
        }
    }

    /// <summary>A station with a mandrel and nothing on it takes one hollow from a container at the
    /// infeed face (when struck). Returns whether one went on.</summary>
    public bool PullFromInfeed()
    {
        if (Api.Side != EnumAppSide.Server || HollowOn || !Complete)
            return false;
        if (InfeedHollows().FirstOrDefault() is not ({ } container, { } slot))
            return false;
        Load(slot.TakeOut(1));
        slot.MarkDirty();
        container.MarkDirty(true);
        return true;
    }

    // The client's own: the client removes a broken station at once, and an update the server sent
    // before it heard of the break then brings the block entity back over air: drop it.
    private void OnClientTick(float dt)
    {
        if (Api.World.BlockAccessor.GetBlock(Pos) is not BlockMandrelStation)
        {
            DisposeRenderer();
            Api.World.BlockAccessor.RemoveBlockEntity(Pos);
        }
    }

    // ---- Breaking ----

    /// <summary>What breaking the frame gives besides the frame: the mandrel, and the hollow if it has
    /// not been struck yet (once forging has begun, it is lost with the station).</summary>
    public IEnumerable<ItemStack> PartDrops()
    {
        if (_mandrel != null && Api.World.GetItem(new AssetLocation(_mandrel)) is { } item)
            yield return new ItemStack(item);
        if (HollowOn && _job.Untouched)
            yield return _hollow!.Clone();
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
        if (_mandrel != null)
            tree.SetString("mandrel", _mandrel);
        else
            tree.RemoveAttribute("mandrel");
        if (HollowOn)
            tree.SetItemstack("hollow", _hollow);
        else
            tree.RemoveAttribute("hollow");
        tree.SetInt("class", _job.Class);
        tree.SetInt("blows", _job.Blows);
        tree.SetDouble("work", _job.Work);
        if (Api?.Side == EnumAppSide.Server)
            tree.SetInt("needed", Config.BlowsPerHollow(_job.Class));
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        _mandrel = MandrelPart.Restore(tree.GetString("mandrel"));
        _hollow = tree.GetItemstack("hollow");
        if (_hollow != null && !_hollow.ResolveBlockOrItem(worldForResolving))
            _hollow = null;
        _job = _hollow == null || _mandrel == null ? ForgeJob.None : ForgeJob.Restore(tree.GetInt("class"), tree.GetInt("blows"), tree.GetDouble("work"));
        if (!_job.On)
            _hollow = null;
        if (worldForResolving.Side == EnumAppSide.Client)
            _serverBlowsNeeded = tree.TryGetInt("needed") is > 0 and var n ? n : null;
    }

    public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping, Dictionary<int, AssetLocation> itemIdMapping)
    {
        base.OnStoreCollectibleMappings(blockIdMapping, itemIdMapping);
        _hollow?.Collectible.OnStoreCollectibleMappings(Api.World, new DummySlot(_hollow), blockIdMapping, itemIdMapping);
    }

    public override void OnLoadCollectibleMappings(IWorldAccessor worldForResolve, Dictionary<int, AssetLocation> oldBlockIdMapping,
                                                   Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
    {
        base.OnLoadCollectibleMappings(worldForResolve, oldBlockIdMapping, oldItemIdMapping, schematicSeed, resolveImports);
        _hollow?.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForResolve);
    }

    // ---- Info ----

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        string L(string key, params object[] args) => Lang.Get(MandrelStationSystem.Domain + ":mandrelstation-" + key, args);
        if (!Complete)
            dsc.AppendLine(L("info-next"));
        else if (HollowOn)
            dsc.AppendLine(L("info-hollow", L("metal-" + Forging.MetalOf(_job.Class)), _job.Blows, (int)Math.Floor(_job.Work * 100 + 1e-6)));
        else
            dsc.AppendLine(L("info-empty"));
    }
}
