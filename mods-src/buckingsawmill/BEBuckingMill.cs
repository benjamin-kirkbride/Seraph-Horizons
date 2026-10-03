using System.Text;
using BuckingSawmill.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace BuckingSawmill;

/// <summary>
/// The mill's controller. Holds the fitted parts (Immersive Woodworking's sawmill items), the
/// blade kits as their own stacks (they wear), the loaded trunk as its whole Logging Expanded item
/// stack, and the cut's progress. The server cuts from the power ghost's shaft angle, pulls trunks
/// from a rack at the infeed side, and keeps the ghost cells stamped; the client shows it.
/// </summary>
public class BEBuckingMill : BlockEntity, IMillVisualState
{
    public const string IwDomain = "immersivewoodworking";
    private static readonly AssetLocation BreakSound = new("game", "sounds/effect/toolbreak");

    private Parts _parts = new();
    // One stack per fitted kit, in fitting order; _parts.BladeMetals mirrors their metals.
    private readonly List<ItemStack> _blades = [];
    private ItemStack? _trunk;
    private float _progress;
    private float _clientProgress;
    // The loaded trunk's stored logs, read when it was loaded (synced, so the client's progress
    // advances by the server's arithmetic).
    private int _storedLogs;
    private float _lastAngle;
    private bool _angleSeeded;
    private float _clientLastAngle;
    private bool _clientAngleSeeded;
    private float? _serverMinSpeed;
    private float? _serverRevolutions;
    private readonly Dictionary<Int3, Cuboidf[]> _boxes = [];
    private MillRenderer? _renderer;

    public Side Side { get; private set; } = Side.North;
    public BlockFacing Facing => BlockFacing.FromCode(Side.Code());
    public bool Complete => _parts.Complete;
    public float Progress => _progress;
    public IReadOnlyList<ItemStack> Blades => _blades;
    /// <summary>The configured slowest shaft speed that cuts. A client uses the server's value,
    /// which comes with the block entity's data, not its own config file's.</summary>
    public float MinSpeed => _serverMinSpeed ?? Config.MinSpeed;
    private float RevolutionsPerStoredLog => _serverRevolutions ?? Config.RevolutionsPerStoredLog;

    private BuckingSawmillSystem System => BuckingSawmillSystem.Of(Api);
    private MillConfig Config => System.Config;
    private Rig? Rig => System.Rig;

    // IMillVisualState
    public int SashCount => _parts.Sashes;
    public bool HasCrankshaft => _parts.Crankshaft;
    public int BladeCount => _blades.Count;
    public string? BladeMetal => _parts.BladeMetal;
    public ItemStack? Trunk => _trunk;
    public float ClientProgress => _trunk == null ? 0 : Math.Clamp(_clientProgress, 0, 1);
    public float ShaftAngle => Power?.AngleRad ?? 0;
    public float ShaftSpeed => Power?.TrueSpeed ?? 0;

    /// <summary>The power ghost's mechanical power behavior, looked up afresh each time.</summary>
    public BEBehaviorMillMP? Power =>
        Api != null && Rig is { } rig
            ? Api.World.BlockAccessor.GetBlockEntity(CellPos(rig.PowerCell))?.GetBehavior<BEBehaviorMillMP>()
            : null;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        Side = Sides.TryParse(Block.Variant["side"], out var side) ? side : Side.North;
        if (api.Side == EnumAppSide.Server)
        {
            RegisterDelayedCallback(_ =>
            {
                if (Api?.World.BlockAccessor.GetBlockEntity(Pos) == this)
                    EnsureGhosts();
            }, 100);
            RegisterGameTickListener(OnCutTick, 50);
            RegisterGameTickListener(OnRackTick, 1000);
        }
        else
        {
            _clientProgress = _progress;
            RegisterGameTickListener(OnClientTick, 50);
            if (api is ICoreClientAPI capi && Rig is { } rig)
                _renderer = new MillRenderer(capi, this, rig);
        }
    }

    // ---- Footprint ----

    public BlockPos CellPos(Int3 local)
    {
        var w = Footprint.ToWorld(local, Side);
        return Pos.AddCopy(w.X, w.Y, w.Z);
    }

    /// <summary>Every ghost cell's world position, with whether it is the power cell.</summary>
    public IEnumerable<(BlockPos Pos, bool Power)> GhostCells() =>
        Rig is { } rig ? rig.GhostCells.Select(c => (CellPos(c.Pos), c.Pos == rig.PowerCell)) : [];

    /// <summary>A cell's collision and selection boxes, turned to the mill's facing; null when the
    /// cell is not the mill's.</summary>
    public Cuboidf[]? CellBoxes(BlockPos cellPos)
    {
        var local = Footprint.ToLocal(new Int3(cellPos.X - Pos.X, cellPos.Y - Pos.Y, cellPos.Z - Pos.Z), Side);
        lock (_boxes)
        {
            if (_boxes.TryGetValue(local, out var cached))
                return cached;
            if (Rig?.CellAt(local) is not { } cell)
                return null;
            var boxes = cell.Boxes.Count == 0
                ? [Cuboidf.Default()]
                : cell.Boxes.Select(b => Footprint.ToWorld(b, Side)).Select(b => new Cuboidf(b.X1, b.Y1, b.Z1, b.X2, b.Y2, b.Z2)).ToArray();
            return _boxes[local] = boxes;
        }
    }

    private Block? GhostBlock(bool power) =>
        Api.World.GetBlock(new AssetLocation(BuckingSawmillSystem.Domain, power ? "buckingmill-ghostpower-" + Side.Code() : "buckingmill-ghost"));

    public void PlaceGhosts()
    {
        if (Api.Side != EnumAppSide.Server)
            return;
        foreach (var (pos, power) in GhostCells())
            if (GhostBlock(power) is { } ghost)
                StampGhost(ghost, pos);
    }

    private void StampGhost(Block ghost, BlockPos pos)
    {
        Api.World.BlockAccessor.SetBlock(ghost.BlockId, pos);
        if (Api.World.BlockAccessor.GetBlockEntity(pos) is BEMillGhost be)
        {
            be.Principal = Pos.Copy();
            be.MarkDirty(true);
        }
    }

    /// <summary>Restores ghost cells that went missing, and points every ghost back here.</summary>
    public void EnsureGhosts()
    {
        if (Api.Side != EnumAppSide.Server)
            return;
        var ba = Api.World.BlockAccessor;
        foreach (var (pos, power) in GhostCells())
        {
            if (ba.GetChunkAtBlockPos(pos) == null || GhostBlock(power) is not { } expected)
                continue;
            var current = ba.GetBlock(pos);
            if (current.Id == expected.Id)
            {
                if (ba.GetBlockEntity(pos) is BEMillGhost be && !Pos.Equals(be.Principal))
                {
                    be.Principal = Pos.Copy();
                    be.MarkDirty(true);
                }
            }
            else if (current.Id == 0 || current is BlockMillGhost)
                StampGhost(expected, pos);
        }
    }

    public void RemoveGhosts()
    {
        if (Api.Side != EnumAppSide.Server)
            return;
        var ba = Api.World.BlockAccessor;
        foreach (var (pos, _) in GhostCells())
            if (ba.GetBlock(pos) is BlockMillGhost && ba.GetBlockEntity(pos) is BEMillGhost be && Pos.Equals(be.Principal))
                ba.SetBlock(0, pos);
    }

    // ---- Interaction ----

    /// <summary>Right-click on the mill or any ghost. Ctrl takes the trunk back, else a blade kit;
    /// a part in hand is fitted; a trunk in hand, or with an empty hand one from the hotbar or
    /// backpack, is loaded. Decided and done on the server; the client only says whether the click
    /// is the mill's.</summary>
    public bool OnInteract(IPlayer byPlayer)
    {
        var slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        var controls = byPlayer.Entity.Controls;
        bool take = controls.CtrlKey && !controls.ShiftKey;
        var path = slot?.Itemstack?.Collectible?.Code?.Path;
        bool part = slot?.Itemstack?.Collectible?.Code?.Domain == IwDomain && Parts.KindOf(path, out _) != PartKind.None;
        bool trunk = Trunks.IsTrunk(slot?.Itemstack);
        if (!take && !part && !trunk && slot?.Itemstack != null)
            return false;
        if (Api.Side != EnumAppSide.Server)
            return true;

        if (take)
        {
            if (!TryTakeTrunk(byPlayer))
                TryTakeBladeKit(byPlayer);
        }
        else if (part)
            TryFitPart(slot!, byPlayer);
        else if (trunk)
            TryLoadFromSlot(slot!, byPlayer);
        else
            TryLoadFromInventory(byPlayer);
        return true;
    }

    /// <summary>Fits the part in <paramref name="slot"/> (server side); false, with an error to
    /// the player, when the rules say no.</summary>
    public bool TryFitPart(ItemSlot slot, IPlayer byPlayer)
    {
        var stack = slot.Itemstack;
        if (stack?.Collectible?.Code is not { Domain: IwDomain } code)
            return false;
        var verdict = _parts.Fit(code.Path);
        if (verdict != FitVerdict.Fits)
        {
            Error(byPlayer, verdict switch
            {
                FitVerdict.AlreadyFitted => "error-part-fitted",
                FitVerdict.NeedsFreeSash => "error-need-sash",
                FitVerdict.BladesFitted => "error-blades-fitted",
                FitVerdict.WrongMetal => "error-blade-metal",
                _ => "error-not-a-part",
            }, verdict == FitVerdict.WrongMetal ? [MetalName(_parts.BladeMetal)] : []);
            return false;
        }
        var fitted = stack.Clone();
        fitted.StackSize = 1;
        if (Parts.KindOf(code.Path, out _) == PartKind.BladeKit)
            _blades.Add(fitted);
        if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
        {
            slot.TakeOut(1);
            slot.MarkDirty();
        }
        Api.World.PlaySoundAt(Block.Sounds.Place, Pos, -0.25, byPlayer);
        MarkDirty(true);
        return true;
    }

    /// <summary>Gives the trunk back if it is still recoverable (server side).</summary>
    public bool TryTakeTrunk(IPlayer byPlayer)
    {
        if (_trunk == null)
            return false;
        if (!Cutting.Recoverable(_progress))
        {
            Error(byPlayer, "error-trunk-cut");
            return true;
        }
        var trunk = _trunk;
        ClearTrunk();
        Give(byPlayer, trunk);
        Api.World.PlaySoundAt(new AssetLocation("game", "sounds/block/wood"), Pos, 0, byPlayer);
        MarkDirty(true);
        return true;
    }

    /// <summary>Gives back the last fitted blade kit (server side).</summary>
    public bool TryTakeBladeKit(IPlayer byPlayer)
    {
        if (_blades.Count == 0)
            return false;
        var blade = _blades[^1];
        _blades.RemoveAt(_blades.Count - 1);
        _parts.RemoveBladeKit(_parts.BladeMetals.Count - 1);
        Give(byPlayer, blade);
        MarkDirty(true);
        return true;
    }

    public bool TryLoadFromSlot(ItemSlot slot, IPlayer? byPlayer)
    {
        if (slot.Itemstack is not { } stack || !CanLoad(stack, byPlayer))
            return false;
        Load(slot.TakeOutWhole());
        slot.MarkDirty();
        Api.World.PlaySoundAt(new AssetLocation("game", "sounds/block/wood"), Pos, 0, byPlayer);
        return true;
    }

    /// <summary>Loads the first trunk in the player's hotbar, then backpack, as Logging Expanded's
    /// workstations find one (branched trunks are skipped while Logging Expanded requires them
    /// debranched).</summary>
    public bool TryLoadFromInventory(IPlayer byPlayer)
    {
        ItemSlot? branched = null;
        foreach (var name in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
        {
            if (byPlayer.InventoryManager.GetOwnInventory(name) is not { } inventory)
                continue;
            foreach (var slot in inventory)
            {
                if (slot.Itemstack is not { } stack || !Trunks.IsTrunk(stack))
                    continue;
                if (BranchedAndRefused(stack))
                {
                    branched ??= slot;
                    continue;
                }
                return TryLoadFromSlot(slot, byPlayer);
            }
        }
        if (branched != null)
            return TryLoadFromSlot(branched, byPlayer);
        return false;
    }

    private bool BranchedAndRefused(ItemStack trunk) =>
        Trunks.IsBranched(trunk) && (System.Logging?.RequireBranchRemoval ?? true);

    /// <summary>Whether <paramref name="trunk"/> can go in now; tells the player why not.</summary>
    public bool CanLoad(ItemStack trunk, IPlayer? byPlayer)
    {
        if (!Trunks.IsTrunk(trunk))
            return false;
        if (System.Logging == null)
            return Error(byPlayer, "error-no-logging");
        if (!_parts.Complete)
            return Error(byPlayer, "error-incomplete");
        if (_trunk != null)
            return Error(byPlayer, "error-bed-full");
        if (BranchedAndRefused(trunk))
        {
            // Logging Expanded's own message, as its sawhorses give it.
            if (byPlayer is IServerPlayer sp)
                sp.SendIngameError("", Lang.GetL(sp.LanguageCode, "loggingmod:treetrunk-branches-first"));
            return false;
        }
        if (Trunks.StoredLogs(trunk, Api.World) <= 0)
            return Error(byPlayer, "error-empty-trunk");
        return true;
    }

    private void Load(ItemStack trunk)
    {
        _trunk = trunk;
        _storedLogs = Trunks.StoredLogs(trunk, Api.World);
        _progress = 0;
        _angleSeeded = false;
        MarkDirty(true);
    }

    private void ClearTrunk()
    {
        _trunk = null;
        _storedLogs = 0;
        _progress = 0;
    }

    private void Give(IPlayer byPlayer, ItemStack stack)
    {
        if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.6, 0.5));
    }

    private bool Error(IPlayer? byPlayer, string key, params object[] args)
    {
        if (byPlayer is IServerPlayer sp)
            sp.SendIngameError("buckingsawmill-" + key, Lang.GetL(sp.LanguageCode, BuckingSawmillSystem.Domain + ":" + key, args));
        return false;
    }

    private static string MetalName(string? metal) =>
        metal == null ? "" : Lang.Get("game:material-" + metal);

    // ---- Rack ----

    /// <summary>Once a second: an assembled, empty mill turning fast enough takes the next trunk
    /// from a Trunk Storage Rack touching its infeed side at ground level, unless that trunk is
    /// branched (it waits for the player to debranch it). The rack is looked up every time.</summary>
    private void OnRackTick(float dt)
    {
        // A ghost can vanish without being broken (an explosion, another mod), the power ghost included.
        EnsureGhosts();
        PullFromRack();
    }

    public bool PullFromRack()
    {
        if (!Config.AutoPullFromRack || _trunk != null || !_parts.Complete || ShaftSpeed < Config.MinSpeed
            || System.Logging is not { } logging || Rig is not { } rig)
            return false;
        foreach (var local in rig.InfeedNeighbours())
        {
            if (FindRack(logging, CellPos(local)) is not { } rack)
                continue;
            if (logging.PeekTrunk(rack) is not { } top || !Trunks.IsTrunk(top))
                continue;
            if (BranchedAndRefused(top) || Trunks.StoredLogs(top, Api.World) <= 0)
                continue;
            if (logging.PopTrunk(rack) is not { } trunk)
                continue;
            rack.MarkDirty(true);
            Load(trunk);
            return true;
        }
        return false;
    }

    /// <summary>The rack whose controller or filler cell is at <paramref name="pos"/>.</summary>
    private BlockEntity? FindRack(LoggingBridge logging, BlockPos pos)
    {
        var ba = Api.World.BlockAccessor;
        if (ba.GetBlock(pos) is BlockMultiblock filler)
            pos = pos.AddCopy(filler.OffsetInv);
        var be = ba.GetBlockEntity(pos);
        return logging.IsRack(be) ? be : null;
    }

    // ---- Cutting ----

    private void OnCutTick(float dt)
    {
        var power = Power;
        float angle = power?.AngleRad ?? 0, speed = power?.TrueSpeed ?? 0;
        if (_trunk == null || !_parts.Complete || speed < Config.MinSpeed || System.Logging == null)
        {
            _lastAngle = angle;
            _angleSeeded = true;
            return;
        }
        if (!_angleSeeded)
        {
            _lastAngle = angle;
            _angleSeeded = true;
            return;
        }
        float advance = Cutting.AngleAdvance(_lastAngle, angle, speed, dt);
        _lastAngle = angle;
        AdvanceCut(advance);
    }

    /// <summary>Turns the saw by <paramref name="radians"/> of shaft rotation (server side).</summary>
    public void AdvanceCut(float radians)
    {
        if (_trunk == null || radians <= 0)
            return;
        float before = _progress;
        _progress += Cutting.ProgressFor(radians, _storedLogs, Config.RevolutionsPerStoredLog);
        if (_progress >= 1)
            FinishCut();
        else if ((int)(before * 20) != (int)(_progress * 20) || Cutting.Recoverable(before) != Cutting.Recoverable(_progress))
            MarkDirty();
    }

    private void FinishCut()
    {
        var trunk = _trunk!;
        int stored = _storedLogs;
        var log = Trunks.Wood(trunk, Api.World) is { } wood && System.Logging?.PlacedLogCode(wood) is { } code
            ? Api.World.GetBlock(code)
            : null;
        ClearTrunk();
        var (spawnAt, velocity) = OutputPoint();
        if (log == null || log.Id == 0)
        {
            Api.Logger.Warning("[buckingsawmill] No log for {0} at {1}; the trunk is given back", trunk.Collectible.Code, Pos);
            Api.World.SpawnItemEntity(trunk, spawnAt, velocity);
            MarkDirty(true);
            return;
        }
        foreach (int size in Cutting.SplitStacks(Cutting.LogYield(stored, Config.LogsPerStoredLog), log.MaxStackSize))
            Api.World.SpawnItemEntity(new ItemStack(log, size), spawnAt, velocity);
        WearBlades(Cutting.BladeWear(stored, Config.BladeWearPerStoredLog));
        MarkDirty(true);
    }

    private (Vec3d At, Vec3d Velocity) OutputPoint()
    {
        var rig = Rig!;
        var p = Footprint.ToWorld(rig.OutputPos, Side);
        var n = Footprint.ToWorld(rig.OutputSide, Side).Normal();
        return (new Vec3d(Pos.X + p.X, Pos.Y + p.Y, Pos.Z + p.Z), new Vec3d(n.X * 0.05, 0, n.Z * 0.05));
    }

    private void WearBlades(int wear)
    {
        if (wear <= 0)
            return;
        bool broke = false;
        for (int i = _blades.Count - 1; i >= 0; i--)
        {
            var blade = _blades[i];
            if (blade.Collectible.GetMaxDurability(blade) <= 1)
                continue;
            int left = blade.Collectible.GetRemainingDurability(blade) - wear;
            if (left > 0)
            {
                blade.Collectible.SetDurability(blade, left);
                continue;
            }
            _blades.RemoveAt(i);
            _parts.RemoveBladeKit(i);
            broke = true;
        }
        if (broke)
            Api.World.PlaySoundAt(BreakSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5);
    }

    // The client turns its own copy of the progress with the shaft between syncs.
    private void OnClientTick(float dt)
    {
        var power = Power;
        float angle = power?.AngleRad ?? 0, speed = power?.TrueSpeed ?? 0;
        if (_trunk == null || !_parts.Complete || speed < MinSpeed || !_clientAngleSeeded)
        {
            _clientLastAngle = angle;
            _clientAngleSeeded = true;
            return;
        }
        float advance = Cutting.AngleAdvance(_clientLastAngle, angle, speed, dt);
        _clientLastAngle = angle;
        _clientProgress = Math.Min(1, _clientProgress + Cutting.ProgressFor(advance, _storedLogs, RevolutionsPerStoredLog));
    }

    // ---- Breaking ----

    /// <summary>What breaking the frame gives besides the frame: every fitted part, the blade kits,
    /// and the trunk while it is recoverable.</summary>
    public IEnumerable<ItemStack> PartDrops()
    {
        Item? Part(string path) => Api.World.GetItem(new AssetLocation(IwDomain, path));
        for (int i = 0; i < _parts.Sashes; i++)
            if (Part(Parts.SashPath) is { } sash)
                yield return new ItemStack(sash);
        if (_parts.Crankshaft && Part(Parts.CrankshaftPath) is { } crankshaft)
            yield return new ItemStack(crankshaft);
        foreach (var blade in _blades)
            yield return blade.Clone();
        if (_trunk != null && Cutting.Recoverable(_progress))
            yield return _trunk.Clone();
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
        tree.SetInt("sashes", _parts.Sashes);
        tree.SetBool("crankshaft", _parts.Crankshaft);
        tree.SetInt("bladeCount", _blades.Count);
        for (int i = 0; i < _blades.Count; i++)
            tree.SetItemstack("blade" + i, _blades[i]);
        if (_trunk != null)
            tree.SetItemstack("trunk", _trunk);
        else
            tree.RemoveAttribute("trunk");
        tree.SetInt("storedLogs", _storedLogs);
        tree.SetFloat("progress", _progress);
        if (Api?.Side == EnumAppSide.Server)
        {
            tree.SetFloat("minSpeed", Config.MinSpeed);
            tree.SetFloat("revolutionsPerStoredLog", Config.RevolutionsPerStoredLog);
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        _blades.Clear();
        int count = tree.GetInt("bladeCount");
        for (int i = 0; i < count; i++)
            if (tree.GetItemstack("blade" + i) is { } blade && blade.ResolveBlockOrItem(worldForResolving))
                _blades.Add(blade);
        _parts = new Parts(tree.GetInt("sashes"), tree.GetBool("crankshaft"),
            _blades.Select(b => Parts.KindOf(b.Collectible.Code.Path, out var metal) == PartKind.BladeKit ? metal! : ""));
        _trunk = tree.GetItemstack("trunk");
        if (_trunk != null && !_trunk.ResolveBlockOrItem(worldForResolving))
            _trunk = null;
        _storedLogs = tree.GetInt("storedLogs");
        _progress = tree.GetFloat("progress");
        if (worldForResolving.Side == EnumAppSide.Client)
        {
            _serverMinSpeed = tree.TryGetFloat("minSpeed");
            _serverRevolutions = tree.TryGetFloat("revolutionsPerStoredLog");
        }
        // A sync behind the client's own estimate (or a new trunk) resets it to the server's.
        if (_trunk == null || Math.Abs(_clientProgress - _progress) > 0.05f)
            _clientProgress = _progress;
    }

    public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping, Dictionary<int, AssetLocation> itemIdMapping)
    {
        base.OnStoreCollectibleMappings(blockIdMapping, itemIdMapping);
        foreach (var stack in _blades.Append(_trunk))
            stack?.Collectible.OnStoreCollectibleMappings(Api.World, new DummySlot(stack), blockIdMapping, itemIdMapping);
    }

    public override void OnLoadCollectibleMappings(IWorldAccessor worldForResolve, Dictionary<int, AssetLocation> oldBlockIdMapping,
                                                   Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
    {
        base.OnLoadCollectibleMappings(worldForResolve, oldBlockIdMapping, oldItemIdMapping, schematicSeed, resolveImports);
        foreach (var stack in _blades.Append(_trunk))
            stack?.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForResolve);
    }

    // ---- Info ----

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        string L(string key, params object[] args) => Lang.Get(BuckingSawmillSystem.Domain + ":" + key, args);
        if (!_parts.Complete)
        {
            dsc.AppendLine(L("info-incomplete"));
            foreach (var group in _parts.Missing().GroupBy(p => p))
            {
                string name = group.Key.EndsWith('*')
                    ? L("info-bladekit")
                    : Lang.Get(IwDomain + ":item-" + group.Key);
                dsc.AppendLine("• " + (group.Count() > 1 ? L("info-count", group.Count(), name) : name));
            }
        }
        foreach (var blade in _blades)
        {
            int max = blade.Collectible.GetMaxDurability(blade);
            dsc.AppendLine(max > 1
                ? L("info-blade-durability", blade.GetName(), blade.Collectible.GetRemainingDurability(blade), max)
                : blade.GetName());
        }
        if (!_parts.Complete)
            return;
        if (_trunk != null)
        {
            dsc.AppendLine(L("info-trunk", _trunk.GetName(), _storedLogs));
            dsc.AppendLine(L("info-progress", (int)(ClientProgress * 100)));
            if (!Cutting.Recoverable(_progress))
                dsc.AppendLine(L("info-committed"));
        }
        else
            dsc.AppendLine(L("info-empty"));
        float speed = ShaftSpeed;
        dsc.AppendLine(speed < MinSpeed ? L("info-nopower") : L("info-speed", speed.ToString("0.00")));
    }
}
