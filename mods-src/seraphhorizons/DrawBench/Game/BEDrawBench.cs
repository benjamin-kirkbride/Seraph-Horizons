using System.Text;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.DrawBench.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.DrawBench;

/// <summary>
/// The draw bench's controller. Holds the fitted parts (<see cref="DrawBenchParts"/>: every item's
/// code and the die's durability), the hollow section on the bench as its item stack and the draw
/// (<see cref="DrawJob"/>: W, the pipe sections drawn), and its MachineOil tank. The server draws
/// from the power ghost's shaft angle, drops a pipe section at the output face (or puts it in a
/// container there) each time W crosses a whole section, wears the die when the hollow is done, takes
/// the next hollow from a chest or hopper at the infeed face, and keeps the ghost cells stamped; the client draws it
/// (<see cref="DrawBenchRenderer"/>, through <see cref="IDrawBenchView"/>). The rules are
/// DrawBench/Core's.
/// </summary>
public class BEDrawBench : BlockEntity, IDrawBenchView
{
    private static readonly AssetLocation BreakSound = new("game", "sounds/effect/toolbreak");
    private static readonly AssetLocation LatchSound = new("game", "sounds/effect/latch");
    private static readonly AssetLocation SectionSound = new("game", "sounds/block/ingot");
    private static readonly AssetLocation DrawSound = new("game", "sounds/effect/gears");
    private const string PartKeyPrefix = "part-";
    // The server syncs W at least this often (sections); the renderer follows the shaft between.
    private const double SyncStep = 0.05;
    // After a hollow is done the infeed waits this long, so the drawn sections clear off the bench
    // (the model eases them out) before the next hollow goes on.
    private const long ClearMs = 600;

    private DrawBenchParts _parts = new();
    private ItemStack? _hollow;
    private DrawJob _job = DrawJob.None;
    private OilState? _oil;
    private float _lastAngle;
    private bool _angleSeeded;
    private long _finishedAt = long.MinValue / 2;
    private float _smokeSeconds;
    private float _soundSeconds;
    private float? _serverMinSpeed;
    private float? _serverTurnsLead;
    private float? _serverTurnsCopper;
    private string[]? _serverDieDraws;
    private Dictionary<Int3, Cuboidf[]>? _cells;
    private Dictionary<Int3, Cuboidf[]>? _collision;
    private DrawBenchRenderer? _renderer;

    public Side Side { get; private set; } = Side.North;
    public DrawBenchParts Parts => _parts;
    public bool Complete => _parts.Complete;
    public bool JobOn => _job.On && _hollow != null;
    public ItemStack? Hollow => _hollow;
    public DrawJob Job => _job;
    /// <summary>The bench's oil, or null without the <c>MachineOil</c> switch.</summary>
    public OilState? Oiling => _oil;
    /// <summary>The tank's fill, 0..1: 1 without a tank (MachineOil off).</summary>
    public double OilFill => _oil == null ? 1 : OilDrain.Fill(_oil.Tank);
    public float MinSpeed => _serverMinSpeed ?? Config.MinSpeed;
    public double TurnsPerSection(int k) => k switch
    {
        1 => _serverTurnsLead ?? Config.TurnsPerSectionLead,
        2 => _serverTurnsCopper ?? Config.TurnsPerSectionCopper,
        _ => 0,
    };
    /// <summary>The metals the fitted die draws (none without a die), by the server's settings.</summary>
    public IReadOnlyList<string> DieDraws =>
        _parts.DieMetal == null ? [] : _serverDieDraws ?? Config.MetalsFor(_parts.DieMetal);
    public float ShaftAngle => Power?.AngleRad ?? 0;
    public float ShaftSpeed => Math.Abs(Power?.TrueSpeed ?? 0);
    /// <summary>Complete, a hollow on and the shaft fast enough: the draw runs.</summary>
    public bool Running => Drawing.Running(_parts.Complete, JobOn, ShaftSpeed, MinSpeed);

    // IDrawBenchView
    public bool PartFitted(string? requires) => _parts.Fitted(requires);
    public string? DieMetal => _parts.DieMetal;
    public int JobClass => JobOn ? _job.Class : 0;
    public double JobWork => _job.Work;

    private DrawBenchSystem System => DrawBenchSystem.Of(Api);
    private DrawBenchConfig Config => System.Config;
    private DrawBenchRig? Rig => System.Rig;

    /// <summary>The power ghost's mechanical power behavior, looked up afresh each time.</summary>
    public BEBehaviorDrawBenchMP? Power =>
        Api != null && Rig is { } rig
            ? Api.World.BlockAccessor.GetBlockEntity(CellPos(rig.PowerCell))?.GetBehavior<BEBehaviorDrawBenchMP>()
            : null;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        Side = Sides.TryParse(Block.Variant["side"], out var side) ? side : Side.North;
        BuildBoxes();
        if (api.Side == EnumAppSide.Server)
        {
            _oil ??= Oil.NewOwn(api, OilMachine.DrawBench);
            RegisterDelayedCallback(_ =>
            {
                if (Api?.World.BlockAccessor.GetBlockEntity(Pos) == this)
                    EnsureGhosts();
            }, 100);
            RegisterGameTickListener(OnTick, 50);
            RegisterGameTickListener(OnSlowTick, 1000);
        }
        else
        {
            RegisterGameTickListener(OnClientTick, 200);
            if (api is ICoreClientAPI capi && Rig is { } rig && Block is BlockDrawBench)
                _renderer = new DrawBenchRenderer(capi, this, this, rig);
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

    /// <summary>Every ghost cell's world position, and whether it is the power ghost.</summary>
    public IEnumerable<(BlockPos Pos, bool Power)> GhostCells() =>
        Rig is { } rig ? rig.GhostCells.Select(c => (CellPos(c.Pos), c.Pos == rig.PowerCell)) : [];

    public Cuboidf[]? CellBoxes(BlockPos cellPos) => Lookup(_cells, cellPos);

    /// <summary>A cell's selection boxes and, on a column's top cell, the rig's collision-only lid.</summary>
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

    private Block? GhostBlock(bool power) =>
        Api.World.GetBlock(new AssetLocation(DrawBenchSystem.Domain, power ? "drawbench-ghostpower-" + Side.Code() : "drawbench-ghost"));

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
        if (Api.World.BlockAccessor.GetBlockEntity(pos) is BEDrawBenchGhost be)
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
                if (ba.GetBlockEntity(pos) is BEDrawBenchGhost be && !Pos.Equals(be.Principal))
                {
                    be.Principal = Pos.Copy();
                    be.MarkDirty(true);
                }
            }
            else if (current.Id == 0 || current is BlockDrawBenchGhost)
                StampGhost(expected, pos);
        }
    }

    public void RemoveGhosts()
    {
        if (Api.Side != EnumAppSide.Server)
            return;
        var ba = Api.World.BlockAccessor;
        foreach (var (pos, _) in GhostCells())
            if (ba.GetBlock(pos) is BlockDrawBenchGhost && ba.GetBlockEntity(pos) is BEDrawBenchGhost be && Pos.Equals(be.Principal))
                ba.SetBlock(0, pos);
    }

    // ---- Interaction ----

    /// <summary>
    /// Right-click on the bench or any ghost. Holding oil, it pours (MachineOil). In creative mode,
    /// Ctrl on an incomplete bench fits its next stage with nothing taken. Ctrl takes the die back
    /// once no hollow is on the bench. A part in hand is fitted if it is the next stage's; a lead or
    /// copper hollow section (the game's chute section) goes on (an ingot, an angle or a pipe section,
    /// what comes off, never does). Anything else held is the item's own business. Decided and done on the
    /// server; the client only says whether the click is the bench's.
    /// </summary>
    public bool OnInteract(IPlayer byPlayer)
    {
        if (Oil.Interact(this, _oil, byPlayer) is { } oiled)
            return oiled;
        var slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        var controls = byPlayer.Entity.Controls;
        bool take = controls.CtrlKey && !controls.ShiftKey;
        string? code = slot?.Itemstack?.Collectible?.Code?.ToString();
        bool part = DrawBenchParts.IsPart(code);
        bool hollow = Drawing.ClassOfHollow(code) != 0;
        if (!take && !part && !hollow)
            return false;
        if (Api.Side != EnumAppSide.Server)
            return true;
        if (CreativeShortcutApplies(byPlayer))
            FitNextPart(byPlayer);
        else if (take)
            TakeBack(byPlayer);
        else if (part)
            TryFitPart(slot!, byPlayer);
        else
            TryLoadHollow(slot!, byPlayer);
        return true;
    }

    /// <summary>Whether a click is the creative shortcut's: a player in creative mode, Ctrl without
    /// Shift, on a bench with a stage still to fit.</summary>
    public bool CreativeShortcutApplies(IPlayer byPlayer)
    {
        var controls = byPlayer.Entity.Controls;
        return !_parts.Complete
               && CreativeUpgrades.Applies(byPlayer.WorldData?.CurrentGameMode == EnumGameMode.Creative, controls.CtrlKey, controls.ShiftKey);
    }

    /// <summary>The creative shortcut (server side): fits the next stage's first item (a new steel
    /// die last) with nothing taken from the player.</summary>
    public bool FitNextPart(IPlayer? byPlayer)
    {
        if (_parts.NextPart is not { } next)
            return false;
        if (Api.World.GetItem(new AssetLocation(next)) is not { } item)
        {
            Api.Logger.Warning("[seraphhorizons] Draw bench: no {0} for the creative shortcut", next);
            return false;
        }
        return TryFitPart(new DummySlot(new ItemStack(item)), byPlayer, free: true);
    }

    /// <summary>Fits the part in <paramref name="slot"/> if it is the next stage's (server side);
    /// false, with an error to the player, when the rules say no. One item is taken from the slot
    /// unless the player is in creative mode or it is <paramref name="free"/>.</summary>
    public bool TryFitPart(ItemSlot slot, IPlayer? byPlayer, bool free = false)
    {
        var stack = slot.Itemstack;
        string? code = stack?.Collectible?.Code?.ToString();
        if (stack == null)
            return false;
        int left = 0, capacity = 0;
        if (DrawBenchParts.StagesOf(code).Contains(DrawBenchStage.Die))
        {
            capacity = Math.Max(1, stack.Collectible.GetMaxDurability(stack));
            left = stack.Collectible.GetRemainingDurability(stack);
        }
        var verdict = _parts.CanFit(code, out _, capacity > 0 ? left : 1);
        if (verdict != DrawBenchFitVerdict.Fits)
        {
            switch (verdict)
            {
                case DrawBenchFitVerdict.OutOfOrder:
                    Error(byPlayer, "error-order", StageName(_parts.Next));
                    break;
                case DrawBenchFitVerdict.AlreadyFitted:
                    Error(byPlayer, "error-part-fitted");
                    break;
                case DrawBenchFitVerdict.DieSpent:
                    Error(byPlayer, "error-die-spent");
                    break;
                default:
                    Error(byPlayer, "error-not-a-part");
                    break;
            }
            return false;
        }
        _parts.Fit(code, left, capacity);
        bool consumes = !free && byPlayer?.WorldData.CurrentGameMode != EnumGameMode.Creative;
        if (consumes)
        {
            slot.TakeOut(1);
            slot.MarkDirty();
        }
        Api.World.PlaySoundAt(Block.Sounds.Place, Pos, -0.25, byPlayer);
        MarkDirty(true);
        PullFromInfeed();
        return true;
    }

    /// <summary>A stage's name as the block info and the errors show it.</summary>
    public static string StageName(DrawBenchStage? stage) =>
        stage is { } s ? Lang.Get(DrawBenchSystem.Domain + ":drawbench-info-stage-" + DrawBenchRequires.Name(s)) : "";

    /// <summary>Ctrl + right-click (server side): the die, with what durability it has left, once
    /// no hollow is on the bench.</summary>
    public bool TakeBack(IPlayer byPlayer)
    {
        if (!_parts.Has(DrawBenchStage.Die))
            return false;
        if (!_parts.CanTakeDie(JobOn))
            return Error(byPlayer, "error-busy");
        if (_parts.RemoveDie() is not { } die || DieStack(die.Code, die.Left) is not { } stack)
            return false;
        Give(byPlayer, stack);
        Api.World.PlaySoundAt(LatchSound, Pos, 0, byPlayer);
        MarkDirty(true);
        return true;
    }

    /// <summary>A die of <paramref name="code"/> with <paramref name="left"/> durability.</summary>
    private ItemStack? DieStack(string code, int left)
    {
        if (Api.World.GetItem(new AssetLocation(code)) is not { } item)
            return null;
        var stack = new ItemStack(item);
        if (left < item.GetMaxDurability(stack))
            stack.Attributes.SetInt("durability", Math.Max(1, left));
        return stack;
    }

    /// <summary>The pipe section a hollow of class <paramref name="k"/> is drawn into, or null when
    /// it does not exist in this game.</summary>
    private Item? SectionItem(int k) =>
        Drawing.SectionFor(k) is { } code && Api.World.GetItem(new AssetLocation(code)) is { Id: > 0, IsMissing: false } item ? item : null;

    /// <summary>Puts a hollow section from <paramref name="slot"/> on the bench, if the bench takes it now.</summary>
    public bool TryLoadHollow(ItemSlot slot, IPlayer? byPlayer)
    {
        string? code = slot.Itemstack?.Collectible?.Code?.ToString();
        switch (Drawing.CanLoad(code, _parts.Complete, JobOn, DieDraws.ToList()))
        {
            case DrawLoadVerdict.Loads:
                break;
            case DrawLoadVerdict.Incomplete:
                return Error(byPlayer, _parts.Next == DrawBenchStage.Die && _parts.Has(DrawBenchStage.Mandrel) ? "error-no-die" : "error-incomplete");
            case DrawLoadVerdict.Occupied:
                return Error(byPlayer, "error-occupied");
            case DrawLoadVerdict.DieRefuses:
                return Error(byPlayer, "error-die-refuses-" + Drawing.MetalOf(Drawing.ClassOfHollow(code)),
                    Lang.Get(DrawBenchSystem.Domain + ":drawbench-die-" + _parts.DieMetal));
            default:
                return false;
        }
        if (SectionItem(Drawing.ClassOfHollow(code)) == null)
            return Error(byPlayer, "error-no-section");
        Load(slot.TakeOut(1));
        slot.MarkDirty();
        return true;
    }

    private void Load(ItemStack hollow)
    {
        _hollow = hollow;
        _job = new DrawJob(Drawing.ClassOfHollow(hollow.Collectible.Code.ToString()), 0);
        Api.World.PlaySoundAt(LatchSound, Pos, 0);
        MarkDirty(true);
    }

    private void ClearJob()
    {
        _hollow = null;
        _job = DrawJob.None;
    }

    private void Give(IPlayer byPlayer, ItemStack stack)
    {
        if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 1.2, 0.5));
    }

    private bool Error(IPlayer? byPlayer, string key, params object[] args)
    {
        if (byPlayer is IServerPlayer sp)
            sp.SendIngameError("drawbench-" + key, Lang.GetL(sp.LanguageCode, DrawBenchSystem.Domain + ":drawbench-" + key, args));
        return false;
    }

    // ---- The draw ----

    private void OnTick(float dt)
    {
        var power = Power;
        float angle = power?.AngleRad ?? 0, speed = Math.Abs(power?.TrueSpeed ?? 0);
        if (!Drawing.Running(_parts.Complete, JobOn, speed, Config.MinSpeed) || !_angleSeeded)
        {
            _lastAngle = angle;
            _angleSeeded = true;
            return;
        }
        float advance = ShaftClock.AngleAdvance(_lastAngle, angle, speed, dt);
        _lastAngle = angle;
        Draw(advance);
    }

    /// <summary>Draws by <paramref name="radians"/> of shaft rotation (server side; call only while
    /// running). Each time W crosses a whole section, a pipe section comes off and the tank drains
    /// a section's oil; at the fourth the die wears and the hollow is done. Returns the sections that
    /// came off.</summary>
    public int Draw(double radians)
    {
        if (!JobOn || !_parts.Complete)
            return 0;
        var before = _job;
        (_job, int sections, bool finished) = _job.Advance(radians, Config.TurnsPerSection(_job.Class));
        if (sections > 0 && SectionItem(_job.Class) is { } section)
        {
            Oil.Drain(_oil, Oil.DrainPerJob(Api, OilMachine.DrawBench) * sections);
            for (int i = 0; i < sections; i++)
                Deliver(new ItemStack(section));
        }
        if (finished)
            Finish();
        else if (sections > 0 || Math.Floor(before.Work / SyncStep) != Math.Floor(_job.Work / SyncStep))
            MarkDirty(sections > 0);
        return sections;
    }

    /// <summary>The hollow is drawn: the die wears its fixed points (a spent die breaks with the
    /// tool-break sound and the bench stops until a new one goes in), and the bench clears; the
    /// infeed waits a moment before the next hollow.</summary>
    private void Finish()
    {
        if (_parts.WearDie(Config.DieWearPerHollow))
            Api.World.PlaySoundAt(BreakSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5);
        ClearJob();
        _finishedAt = Api.World.ElapsedMilliseconds;
        MarkDirty(true);
    }

    /// <summary>Puts <paramref name="stack"/> into a container just beyond the output face, else
    /// drops it there.</summary>
    public void Deliver(ItemStack stack)
    {
        if (Rig is not { } rig)
            return;
        var dummy = new DummySlot(stack);
        if (Api.World.BlockAccessor.GetBlockEntity(CellPos(rig.OutputNeighbour())) is BlockEntityContainer container)
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

    /// <summary>A complete bench with nothing on it and its shaft turning takes one hollow section its
    /// die draws from a container at the infeed face, a moment after the last hollow was done. Returns
    /// whether one went on.</summary>
    public bool PullFromInfeed()
    {
        if (Api.Side != EnumAppSide.Server || JobOn || !_parts.Complete || ShaftSpeed < MinSpeed || Rig is not { } rig)
            return false;
        if (Api.World.ElapsedMilliseconds - _finishedAt < ClearMs)
            return false;
        var draws = DieDraws.ToList();
        foreach (var local in rig.InfeedNeighbours())
        {
            if (Api.World.BlockAccessor.GetBlockEntity(CellPos(local)) is not BlockEntityContainer container)
                continue;
            foreach (var slot in container.Inventory)
            {
                string? code = slot.Itemstack?.Collectible?.Code?.ToString();
                if (Drawing.CanLoad(code, true, false, draws) != DrawLoadVerdict.Loads || SectionItem(Drawing.ClassOfHollow(code)) == null)
                    continue;
                Load(slot.TakeOut(1));
                slot.MarkDirty();
                container.MarkDirty(true);
                return true;
            }
        }
        return false;
    }

    // Once a second: the ghosts, and the infeed.
    private void OnSlowTick(float dt)
    {
        // A ghost can vanish without being broken (an explosion, another mod), the power ghost included.
        EnsureGhosts();
        PullFromInfeed();
    }

    // The client's own: the client removes a broken bench at once, and an update the server sent
    // before it heard of the break then brings the block entity back over air: drop it. A dry bench
    // smokes while it draws, and a drawing bench is heard.
    private void OnClientTick(float dt)
    {
        if (Api.World.BlockAccessor.GetBlock(Pos) is not BlockDrawBench)
        {
            DisposeRenderer();
            Api.World.BlockAccessor.RemoveBlockEntity(Pos);
            return;
        }
        if (Api is not ICoreClientAPI capi || Rig is not { } rig)
            return;
        bool running = Running;
        if ((_smokeSeconds += dt) >= 0.4f)
        {
            _smokeSeconds = 0;
            if (_oil is { Dry: true } && running)
                Oil.Smoke(capi, WorldPoint(rig.Die));
        }
        if (!running)
        {
            _soundSeconds = 0;
            return;
        }
        if ((_soundSeconds -= dt) <= 0)
        {
            _soundSeconds = 1.8f;
            var at = WorldPoint(rig.Die);
            capi.World.PlaySoundAt(DrawSound, at.X, at.Y, at.Z, null, true, 16, 0.5f);
        }
    }

    // ---- Breaking ----

    /// <summary>What breaking the frame gives besides the frame: every fitted part (the die with its
    /// durability), and the hollow if no section has been drawn from it yet (once one has, the rest of
    /// it is lost with the bench).</summary>
    public IEnumerable<ItemStack> PartDrops()
    {
        foreach (var drop in _parts.Returns())
        {
            if (drop.Durability is { } left)
            {
                if (DieStack(drop.Code, left) is { } die)
                    yield return die;
            }
            else if (Api.World.GetItem(new AssetLocation(drop.Code)) is { } item)
                yield return new ItemStack(item);
        }
        if (JobOn && _job.SectionsDone == 0)
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
        foreach (var stage in DrawBenchRequires.Stages)
            tree.RemoveAttribute(PartKeyPrefix + DrawBenchRequires.Name(stage));
        foreach (var (requires, code) in _parts.Snapshot())
            tree.SetString(PartKeyPrefix + requires, code);
        tree.SetInt("dieLeft", _parts.DieLeft);
        tree.SetInt("dieCapacity", _parts.DieCapacity);
        if (JobOn)
            tree.SetItemstack("hollow", _hollow);
        else
            tree.RemoveAttribute("hollow");
        tree.SetInt("class", _job.Class);
        tree.SetDouble("work", _job.Work);
        if (Api?.Side == EnumAppSide.Server)
        {
            tree.SetFloat("minSpeed", Config.MinSpeed);
            tree.SetFloat("turnsPerSectionLead", Config.TurnsPerSectionLead);
            tree.SetFloat("turnsPerSectionCopper", Config.TurnsPerSectionCopper);
            tree.SetString("dieDraws", string.Join(",", Config.MetalsFor(_parts.DieMetal)));
        }
        if (_oil != null)
            Oil.Write(tree, _oil);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        var fitted = new Dictionary<string, string>();
        foreach (var stage in DrawBenchRequires.Stages)
            if (tree.GetString(PartKeyPrefix + DrawBenchRequires.Name(stage)) is { Length: > 0 } code)
                fitted[DrawBenchRequires.Name(stage)] = code;
        _parts = DrawBenchParts.Restore(fitted, tree.GetInt("dieLeft"), tree.GetInt("dieCapacity"));
        _hollow = tree.GetItemstack("hollow");
        if (_hollow != null && !_hollow.ResolveBlockOrItem(worldForResolving))
            _hollow = null;
        _job = _hollow == null ? DrawJob.None : DrawJob.Restore(tree.GetInt("class"), tree.GetDouble("work"));
        if (!_job.On)
            _hollow = null;
        _oil = Oil.LoadOwn(tree, worldForResolving, OilMachine.DrawBench);
        if (worldForResolving.Side == EnumAppSide.Client)
        {
            _serverMinSpeed = tree.TryGetFloat("minSpeed");
            _serverTurnsLead = tree.TryGetFloat("turnsPerSectionLead");
            _serverTurnsCopper = tree.TryGetFloat("turnsPerSectionCopper");
            _serverDieDraws = tree.GetString("dieDraws") is { } draws
                ? draws.Split(',', StringSplitOptions.RemoveEmptyEntries)
                : null;
        }
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
        string L(string key, params object[] args) => Lang.Get(DrawBenchSystem.Domain + ":drawbench-" + key, args);
        if (_parts.Next is { } next)
        {
            dsc.AppendLine(L("info-next", StageName(next)));
            var after = DrawBenchRequires.Stages.Where(s => s > next && !_parts.Has(s)).ToList();
            if (after.Count > 0)
                dsc.AppendLine(L("info-then", string.Join(", ", after.Select(s => StageName(s)))));
        }
        if (_parts.DieMetal is { } die)
        {
            var draws = DieDraws.Select(m => L("metal-" + m)).ToList();
            dsc.AppendLine(L("info-die", L("die-" + die), draws.Count == 0 ? L("info-draws-nothing") : string.Join(", ", draws),
                _parts.DieLeft, _parts.DieCapacity));
        }
        if (JobOn)
        {
            string metal = L("metal-" + Drawing.MetalOf(_job.Class));
            dsc.AppendLine(Running
                ? L("info-drawing", metal, _job.SectionsDone, Drawing.SectionsPerHollow)
                : L("info-stopped", metal, _job.SectionsDone, Drawing.SectionsPerHollow));
        }
        else if (_parts.Complete)
            dsc.AppendLine(L("info-empty"));
        Oil.Info(_oil, dsc);
        float speed = ShaftSpeed;
        dsc.AppendLine(speed < MinSpeed ? L("info-nopower") : L("info-speed", speed.ToString("0.00")));
    }
}
