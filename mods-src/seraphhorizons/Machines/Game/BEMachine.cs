using System.Text;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Machines;

/// <summary>
/// The controller of every machine on the shared base (docs/modeling/machine-base.md): the game half
/// each machine's block entity builds on, the machine a subclass naming what is its own. It keeps:
/// <list type="bullet">
/// <item>its build and wearing parts (<see cref="MachineAssembly"/> from <c>config/builds/{machine}.json</c>,
/// <see cref="MachinesSystem.Build"/>): a right-click fits the next stage or renews a worn-out set; the
/// creative shortcut (Ctrl) fits the next stage; breaking gives back everything that went in;</item>
/// <item>its cells (<see cref="BlockMachineCell"/>), stamped from the rig (<see cref="IMachineRig"/>) and
/// kept, their boxes the rig's;</item>
/// <item>the running rules (<see cref="MachineRun"/>): built, parts fitted, the shaft at
/// <see cref="MinSpeed"/> or faster where it takes power, a hand at it where a hand works it
/// (<see cref="HandWorked"/>), its water, room for its output, something to work; then its work
/// (<see cref="Work"/> by shaft radians, <see cref="WorkUnpowered"/> by seconds);</item>
/// <item>its load on the shaft (<see cref="Resistance"/>: <see cref="MachineLoad.Unassembled"/> until it
/// loads it, then <see cref="ShaftLoad"/>, through its oil), carried by the power cell's
/// <see cref="BEBehaviorMachineMP"/>;</item>
/// <item>its oil, for a machine that takes it (<see cref="OilKind"/>, <see cref="DryEffect"/>);</item>
/// <item>its reservoir, for a wet machine (<see cref="WaterPerItem"/>), filled from a ppex pipe at the rig's
/// <c>water</c> anchor (<see cref="PpexWater"/>) or by <see cref="FillWater"/>;</item>
/// <item>its fire, for a machine that is lit (<see cref="Ignitable"/>, <see cref="MachineFire"/>);</item>
/// <item>its block info in the shared layout (<see cref="MachineText"/>), with more for a wrench;</item>
/// <item>its renderer on the client (<see cref="MachineRenderer"/>).</item>
/// </list>
/// </summary>
public abstract class BEMachine : BlockEntityContainer, IMachinePower
{
    private InventoryBase _inventory = null!;
    private MachineAssembly? _assembly;
    private MachineReservoir _water = MachineReservoir.Empty;
    private MachineState _state = MachineState.NotBuilt;
    private ProgressSync _progressSync = ProgressSync.None;
    private MachineOiling? _oil;
    private HandHolds? _holds;
    private bool _held;
    private long _localHeldAt = long.MinValue / 2;
    private float _lastAngle;
    private bool _angleSeeded;
    private float? _serverMinSpeed;
    private float? _load;
    private float _smokeSeconds;
    private Dictionary<Int3, Cuboidf[]>? _cells;
    private Dictionary<Int3, Cuboidf[]>? _collision;
    private IRenderer? _renderer;

    // A client's own player holding right-click counts as working for this long after its last step.
    private const long LocalHoldMs = 250;

    public string Machine => (Block as BlockMachine)?.Machine ?? Block.Code.FirstCodePart();
    public Side Side { get; private set; } = Side.North;
    public IMachineRig? Rig => (Block as BlockMachine)?.Rig;
    public MachineAssembly? Assembly => _assembly;
    public bool Built => _assembly?.Built == true;

    /// <summary>Built, with every wearing part fitted and not worn out.</summary>
    public bool Ready => _assembly?.Ready == true;

    /// <summary>Whether the machine is whole, for its name (the frame's until then): built, by default; the
    /// draw bench only with its die in.</summary>
    public virtual bool Complete => Built;

    public MachineState State => _state;

    /// <summary>The machine works now: its state is <see cref="MachineState.Working"/>, and a hand machine is
    /// also held (<see cref="Held"/>: let go, it stops at once, before its next tick).</summary>
    public bool Running => _state == MachineState.Working && (!HandWorked || Held);

    public override InventoryBase Inventory => _inventory;
    public override string InventoryClassName => "machine";

    /// <summary>The machine's inventory, made once before a save is read: none by default (a machine whose
    /// work is a field of its own). An ore machine's is its infeed and outlets.</summary>
    protected virtual InventoryBase NewInventory() => new InventoryGeneric(0, null, null);

    /// <summary>The build the machine keeps: its build file's, by default. A machine with a figure of its own
    /// in it (the draw bench's die durability, from its settings) returns its build with it.</summary>
    protected virtual MachineBuild? BuildSpec(IWorldAccessor world) =>
        world.Api.ModLoader.GetModSystem<MachinesSystem>()?.Build(Machine);

    // ---- Power ----

    /// <summary>Whether the machine takes power: its rig has a power anchor.</summary>
    public bool Powered => Rig?.PowerAnchor() != null;

    public BlockFacing PowerFace => Rig?.PowerAnchor() is { } a
        ? BlockFacing.FromCode(CellFaces.ToWorld(a.Face, Side).Code())
        : BlockFacing.UP;

    /// <summary>The power cell's mechanical power behavior.</summary>
    public BEBehaviorMachineMP? Power =>
        Api != null && Rig?.PowerAnchor() is { } a
            ? Api.World.BlockAccessor.GetBlockEntity(CellPos(a.Cell))?.GetBehavior<BEBehaviorMachineMP>()
            : null;

    /// <summary>Whether the power cell is the controller's own: its block type then carries
    /// <c>seraphhorizons.MachineMP</c> itself.</summary>
    public bool PowerAtController => Rig?.PowerAnchor() is { Cell: var c } && c == Int3.Zero;

    public float ShaftAngle => Power?.AngleRad ?? 0;
    public float ShaftSpeed => Power?.TrueSpeed ?? 0;

    /// <summary>The shaft speed under which the machine stops, as the server runs (synced).</summary>
    public float MinSpeed => _serverMinSpeed ?? ConfigMinSpeed;

    /// <summary>This side's setting for <see cref="MinSpeed"/>: 0.05 by default.</summary>
    protected virtual float ConfigMinSpeed => 0.05f;

    /// <summary>The load the built machine puts on its shaft now, before its oil: none by default.</summary>
    protected virtual double ShaftLoad => 0;

    /// <summary>Whether the machine puts its own load on the shaft, not the token
    /// <see cref="MachineLoad.Unassembled"/>: once built, by default.</summary>
    protected virtual bool LoadsShaft => Built;

    /// <summary>The load on the shaft now: <see cref="MachineLoad.Unassembled"/> until <see cref="LoadsShaft"/>,
    /// then <see cref="ShaftLoad"/>, multiplied while dry for a machine whose oil carries the load
    /// (<see cref="DryEffect"/>). The server syncs a changed figure for the block info.</summary>
    public float Resistance
    {
        get
        {
            float load = MachineLoad.Resistance(LoadsShaft, ShaftLoad);
            if (_oil != null && LoadsShaft)
                return _oil.Asked(load, this);
            if (_load != load && Api?.Side == EnumAppSide.Server)
            {
                _load = load;
                MarkDirty();
            }
            return load;
        }
    }

    /// <summary>The load last put on the shaft, as the server said; null until it has.</summary>
    public float? LastLoad => _oil?.LastLoad ?? _load;

    // ---- Oil ----

    /// <summary>The machine's entry in MachineOilSettings, or null for a machine that takes no oil (the
    /// default). A machine opts in with a value of <see cref="OilMachine"/> and the settings' entry of its name.</summary>
    protected virtual OilMachine? OilKind => null;

    /// <summary>What a dry tank does: multiplies the load (the default), or wears the machine's cutter
    /// instead (<see cref="OilDryEffect.Wear"/>, which the machine does itself by <see cref="OilFill"/>).</summary>
    protected virtual OilDryEffect DryEffect => OilDryEffect.Load;

    /// <summary>The machine's oil (<see cref="MachineOiling"/>, the piece the game's and Immersive Woodworking's
    /// machines compose too), or null without a tank (none taken, or the <c>MachineOil</c> switch off).</summary>
    public MachineOiling? Oiling => _oil;

    /// <summary>The tank's fill, 0..1: 1 without a tank.</summary>
    public double OilFill => _oil?.Fill ?? 1;

    /// <summary>Drains <paramref name="jobs"/> finished jobs' oil (server side).</summary>
    protected void DrainOil(double jobs) => _oil?.Drain(this, jobs);

    /// <summary>Where a dry machine smokes while it runs, native: the footprint's middle by default.</summary>
    protected virtual Float3? SmokePoint => Rig?.Middle();

    // ---- Hands ----

    /// <summary>Whether players work the machine by holding right-click on it, as on the quern (a hand
    /// machine: the press brake). False by default.</summary>
    protected virtual bool HandWorked => false;

    /// <summary>How long a player counts as working after their last step.</summary>
    protected virtual long HoldTimeoutMs => HandHolds.DefaultTimeoutMs;

    /// <summary>Someone works the machine: the server's word, or this client's own player holding right-click on it.</summary>
    public bool Held =>
        _held || Api is { Side: EnumAppSide.Client } && Api.World.ElapsedMilliseconds - _localHeldAt <= LocalHoldMs;

    /// <summary>Counts <paramref name="byPlayer"/> as working the machine (server side), from a click that starts it.</summary>
    protected bool StartHold(IPlayer byPlayer)
    {
        (_holds ??= new HandHolds(HoldTimeoutMs)).Hold(byPlayer.PlayerUID, Api.World.ElapsedMilliseconds);
        UpdateHeld();
        return true;
    }

    /// <summary>
    /// Right-click still held on the machine (both sides, every tick, as the quern's grinding): on a hand
    /// machine the server counts the player as working it and the client marks its own player for the
    /// renderer. Whether the player goes on: while it is ready and has work (a client gives a fresh click half
    /// a second for the server's work to arrive). False on a machine no hand works.
    /// </summary>
    public virtual bool OnInteractStep(IPlayer byPlayer, float secondsUsed)
    {
        if (!HandWorked)
            return false;
        long now = Api.World.ElapsedMilliseconds;
        if (Api.Side == EnumAppSide.Client)
        {
            _localHeldAt = now;
            return Ready && (HasWork || secondsUsed < 0.5f);
        }
        if (!Ready)
            return false;
        if (!HasWork)
        {
            OnInteractStop(byPlayer);
            return false;
        }
        (_holds ??= new HandHolds(HoldTimeoutMs)).Hold(byPlayer.PlayerUID, now);
        UpdateHeld();
        return true;
    }

    /// <summary>The player let go (or the interaction was cancelled).</summary>
    public virtual void OnInteractStop(IPlayer byPlayer)
    {
        if (!HandWorked)
            return;
        if (Api.Side == EnumAppSide.Client)
        {
            _localHeldAt = long.MinValue / 2;
            return;
        }
        _holds?.Release(byPlayer.PlayerUID);
        UpdateHeld();
    }

    // The server's "someone is working it", synced when it changes.
    private void UpdateHeld()
    {
        bool held = _holds?.Any(Api.World.ElapsedMilliseconds) == true && Ready && HasWork;
        if (held == _held)
            return;
        _held = held;
        MarkDirty(false);
    }

    // ---- Water ----

    public MachineReservoir Water => _water;

    /// <summary>The water an item takes, 0 for a dry machine (the default).</summary>
    public virtual double WaterPerItem => 0;

    /// <summary>Litres the reservoir holds.</summary>
    public virtual double ReservoirCapacity => 0;

    /// <summary>Litres a second the reservoir draws from its pipe until full.</summary>
    protected virtual double WaterIntakePerSecond => 0;

    /// <summary>Whether the reservoir holds the water the next work takes: an item's, by default.</summary>
    protected virtual bool Wet => _water.Holds(WaterPerItem);

    /// <summary>Spends <paramref name="litres"/> of the reservoir if it holds them; whether it did.</summary>
    protected bool SpendWater(double litres)
    {
        (_water, bool spent) = _water.Spend(litres);
        return spent;
    }

    /// <summary>Puts <paramref name="litres"/> into the reservoir (never past full); the litres it took.</summary>
    public double FillWater(double litres)
    {
        double before = _water.Litres;
        _water = _water.Fill(litres, ReservoirCapacity);
        return _water.Litres - before;
    }

    private void FillFromPipe(float dt)
    {
        if (WaterPerItem <= 0 || Rig?.Anchor(OreMachineRig.Water) is not { } a)
            return;
        double wanted = _water.Wanted(ReservoirCapacity, WaterIntakePerSecond, dt);
        if (wanted <= 1e-6)
            return;
        var (cell, face) = MachineChutes.World(Pos, Side, a);
        double got = PpexWater.Draw(Api, cell, face, wanted);
        if (got > 0)
            FillWater(got);
    }

    // ---- Fire (a furnace's: lit as the forge is, with a firestarter or a torch on any cell) ----

    /// <summary>Why the machine cannot be lit now (a message), or null when it can. A machine that is never
    /// lit (the default) says nothing: <see cref="Ignitable"/> is false.</summary>
    public virtual string? CannotIgnite() => null;

    /// <summary>Whether the machine is lit at all: a furnace. False by default.</summary>
    public virtual bool Ignitable => false;

    /// <summary>Whether it is burning now, so a torch can be lit from it.</summary>
    public virtual bool Burning => false;

    /// <summary>Lights it (server side), or tells the player why not.</summary>
    public virtual void Ignite(IPlayer? byPlayer)
    {
    }

    // ---- The renderer's ----

    /// <summary>Whether a rig part needing <paramref name="requires"/> is drawn: its stage fitted, by default.</summary>
    public virtual bool ShowsPart(string? requires) => _assembly?.Shows(requires) ?? requires == null;

    /// <summary>The rig's depth input the renderer draws the parts at (a furnace's fire door, slid shut at 1); 0, the
    /// rest pose, by default.</summary>
    public virtual double PartDepth => 0;

    /// <summary>Whether the moving parts move: the shaft is held still until the machine is built.</summary>
    public virtual bool Animates => Built;

    /// <summary>What the parts' textures depend on beyond the block's own (the stall roaster's brick type, the
    /// draw bench's die metal): the renderer builds its meshes again when it changes. Empty by default.</summary>
    public virtual string TextureKey => "";

    /// <summary>The textures the renderer draws the parts with: the frame block's, by default.</summary>
    public virtual ITexPositionSource PartTextures(ICoreClientAPI capi) => capi.Tesselator.GetTextureSource(Block);

    /// <summary>How far away the moving parts are drawn, blocks.</summary>
    public virtual int RenderRange => 64;

    private double _theta, _psi, _lastRenderAngle;
    private bool _renderSeeded;

    /// <summary>The rig's inputs this frame (client side, the renderer asks once a frame in its opaque pass,
    /// <paramref name="far"/> when out of sight): by default θ the power cell's shaft angle about the power
    /// face's native axis, accumulated, ψ the total angle it turned either way, both held still until
    /// <see cref="Animates"/>, and depth <see cref="PartDepth"/>. A machine with a clock of its own (the work's
    /// W, a hand lever) gives its own.</summary>
    public virtual RigInput FrameInput(float dt, bool far)
    {
        (double theta, double psi) = AdvanceShaft();
        return new RigInput(theta, Depth: PartDepth, Travel: psi);
    }

    /// <summary>The default clock's θ and ψ after this frame's shaft turn (<see cref="FrameInput"/>).</summary>
    protected (double Theta, double Psi) AdvanceShaft()
    {
        double raw = ShaftAngle;
        double angle = Rig?.ShaftAxis() is { } axis ? MillMotion.NativeShaftAngle(Side, raw, axis) : raw;
        if (!_renderSeeded || !Animates || ShaftSpeed == 0)
        {
            _lastRenderAngle = angle;
            _renderSeeded = true;
            return (_theta, _psi);
        }
        double delta = MillMotion.WrappedDelta(_lastRenderAngle, angle);
        _lastRenderAngle = angle;
        _theta += delta;
        _psi += Math.Abs(delta);
        if (Math.Abs(_theta) > 1e6)
            _theta %= 2 * Math.PI;
        if (_psi > 1e6)
            _psi %= 2 * Math.PI;
        return (_theta, _psi);
    }

    /// <summary>The client's renderer for this machine: the shared one, by default (the crucible furnace's is its own:
    /// its pots' metals and the fire's glow).</summary>
    protected virtual IRenderer CreateRenderer(ICoreClientAPI capi, IMachineRig rig) => new MachineRenderer(capi, this, rig);

    // ---- Life ----

    public override void Initialize(ICoreAPI api)
    {
        _inventory ??= NewInventory();
        // before the behaviors start: the power behavior reads the power face, which turns with the side
        Side = Sides.TryParse(Block.Variant["side"], out var side) ? side : Side.North;
        base.Initialize(api);
        if (_assembly == null && BuildSpec(api.World) is { } spec)
            _assembly = new MachineAssembly(spec);
        BuildBoxes();
        if (api.Side == EnumAppSide.Server)
        {
            if (OilKind is { } kind)
                _oil ??= MachineOiling.NewIfOn(api, kind, DryEffect);
            RegisterDelayedCallback(_ =>
            {
                if (Api?.World.BlockAccessor.GetBlockEntity(Pos) == this)
                    EnsureCells();
            }, 100);
            RegisterGameTickListener(OnServerTick, TickMs);
            RegisterGameTickListener(_ => EnsureCells(), 1000);
        }
        else
        {
            RegisterGameTickListener(OnClientTickBase, ClientTickMs);
            if (api is ICoreClientAPI capi && Rig is { } rig && Block is BlockMachine)
                _renderer = CreateRenderer(capi, rig);
        }
    }

    /// <summary>The server's tick, milliseconds.</summary>
    protected virtual int TickMs => 100;

    /// <summary>The client's tick, milliseconds (<see cref="OnClientTick"/>).</summary>
    protected virtual int ClientTickMs => 250;

    // ---- Cells ----

    public BlockPos CellPos(Int3 local)
    {
        var w = Footprint.ToWorld(local, Side);
        return Pos.AddCopy(w.X, w.Y, w.Z);
    }

    public Vec3d WorldPoint(Float3 native) => MachinePoints.WorldPoint(Pos, native, Side);

    protected Int3 LocalOf(BlockPos cell) => Footprint.ToLocal(new Int3(cell.X - Pos.X, cell.Y - Pos.Y, cell.Z - Pos.Z), Side);

    /// <summary>Whether <paramref name="face"/> of the cell at <paramref name="cell"/> is one of the
    /// machine's anchors (a pipe or chute meets it there).</summary>
    public bool AnchorFaceAt(BlockPos cell, BlockFacing face)
    {
        if (Rig is not { } rig)
            return false;
        var local = LocalOf(cell);
        return rig.Anchors.Values.Any(a => a.Cell == local && CellFaces.ToWorld(a.Face, Side).Code() == face.Code);
    }

    /// <summary>Whether the cell at <paramref name="cell"/> takes <paramref name="stack"/> from a chute
    /// through <paramref name="face"/> into its buffer (<see cref="BEMachineCell"/>): never, by default.</summary>
    public virtual bool CellTakes(BlockPos cell, BlockFacing face, ItemStack stack) => false;

    /// <summary>A cell's selection boxes: the rig's, by default (the crucible furnace's change with its build and lid).</summary>
    public virtual Cuboidf[]? CellBoxes(BlockPos cellPos) => _cells?.GetValueOrDefault(LocalOf(cellPos));

    /// <summary>A cell's selection boxes and the rig's collision-only lid.</summary>
    public virtual Cuboidf[]? CollisionBoxes(BlockPos cellPos) => _collision?.GetValueOrDefault(LocalOf(cellPos));

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
        var cells = rig.Cells.ToDictionary(c => c.Pos, c => c.Boxes.Count == 0 ? [Cuboidf.Default()] : c.Boxes.Select(World).ToArray());
        var collision = new Dictionary<Int3, Cuboidf[]>(cells);
        foreach (var c in rig.Cells)
            if (c.LidBox is { } lid)
                collision[c.Pos] = [.. cells[c.Pos], World(lid)];
        _cells = cells;
        _collision = collision;
    }

    /// <summary>Every cell but the controller's, with the block it holds: the power cell's
    /// <see cref="BlockMachineCellPower"/> for the power face, the plain <see cref="BlockMachineCell"/> else.</summary>
    public IEnumerable<(BlockPos Pos, Block? Block)> CellBlocks()
    {
        if (Rig is not { } rig)
            yield break;
        var power = rig.PowerAnchor();
        foreach (var cell in rig.OtherCells())
        {
            var code = power is { } p && p.Cell == cell.Pos
                ? BlockMachineCellPower.CodeFor(PowerFace)
                : BlockMachineCell.CellCode;
            yield return (CellPos(cell.Pos), Api.World.GetBlock(code));
        }
    }

    public void PlaceCells()
    {
        if (Api.Side != EnumAppSide.Server)
            return;
        foreach (var (pos, block) in CellBlocks())
            if (block != null)
                StampCell(block, pos);
    }

    private void StampCell(Block block, BlockPos pos)
    {
        Api.World.BlockAccessor.SetBlock(block.BlockId, pos);
        if (Api.World.BlockAccessor.GetBlockEntity(pos) is BEMachineCell be)
        {
            be.Principal = Pos.Copy();
            be.MarkDirty(true);
        }
    }

    /// <summary>Restores cells that went missing, and points every cell back here.</summary>
    public void EnsureCells()
    {
        if (Api.Side != EnumAppSide.Server)
            return;
        var ba = Api.World.BlockAccessor;
        foreach (var (pos, expected) in CellBlocks())
        {
            if (expected == null || ba.GetChunkAtBlockPos(pos) == null)
                continue;
            var current = ba.GetBlock(pos);
            if (current.Id == expected.Id)
            {
                if (ba.GetBlockEntity(pos) is BEMachineCell be && !Pos.Equals(be.Principal))
                {
                    be.Principal = Pos.Copy();
                    be.MarkDirty(true);
                }
            }
            else if (current.Id == 0 || current is BlockMachineCell)
                StampCell(expected, pos);
        }
    }

    /// <summary>Clears the cells, dropping what a chute left in a cell's buffer.</summary>
    public void RemoveCells()
    {
        if (Api.Side != EnumAppSide.Server)
            return;
        var ba = Api.World.BlockAccessor;
        foreach (var (pos, _) in CellBlocks())
            if (ba.GetBlock(pos) is BlockMachineCell && ba.GetBlockEntity(pos) is BEMachineCell be && Pos.Equals(be.Principal))
            {
                if (!be.Buffer.Empty)
                    Api.World.SpawnItemEntity(be.Buffer.TakeOutWhole(), pos.ToVec3d().Add(0.5, 0.5, 0.5));
                ba.SetBlock(0, pos);
            }
    }

    // ---- Interaction ----

    /// <summary>A right-click on the machine's own contents before the build's (a batch machine's charge, a
    /// bucket of water, the work taken back off): true or false when handled (whether the click was the
    /// machine's), null to go on. Runs on both sides; act on the server.</summary>
    protected virtual bool? InteractContent(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel) => null;

    /// <summary>A right-click after the build's (the machine's work: something it takes, a hand starting to
    /// work it); whether the click was the machine's. Runs on both sides; act on the server. False by default.</summary>
    protected virtual bool InteractWork(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel) => false;

    /// <summary>Whether the creative shortcut (Ctrl) fills the next stage now: while a stage is still to fit,
    /// by default (a machine whose wearing part is fitted last, the draw bench's die, also while that is missing).</summary>
    protected virtual bool CreativeFills => !Built;

    /// <summary>Whether a click by <paramref name="byPlayer"/> is the creative shortcut's: a player in creative
    /// mode, Ctrl without Shift, while <see cref="CreativeFills"/>.</summary>
    public bool CreativeShortcut(IPlayer byPlayer)
    {
        var controls = byPlayer.Entity.Controls;
        return CreativeFills && CreativeUpgrades.Applies(byPlayer.WorldData?.CurrentGameMode == EnumGameMode.Creative, controls.CtrlKey, controls.ShiftKey);
    }

    /// <summary>
    /// Right-click on the machine or any of its cells: oil poured in, for a machine with a tank; the
    /// machine's own (<see cref="InteractContent"/>); in creative mode, Ctrl fits the next stage
    /// (<see cref="CreativeShortcut"/>); a stage's material fits it, a worn-out set's renews it; else the
    /// machine's work (<see cref="InteractWork"/>). Decided and done on the server; the client only says
    /// whether the click is the machine's.
    /// </summary>
    public bool OnInteract(IPlayer byPlayer, BlockSelection blockSel)
    {
        bool handled = Interact(byPlayer, blockSel);
        // what the click changed (a stage, the work) shows in the state at once, not a tick later
        if (handled && Api.Side == EnumAppSide.Server)
            RefreshState();
        return handled;
    }

    private bool Interact(IPlayer byPlayer, BlockSelection blockSel)
    {
        if (_oil?.Interact(this, byPlayer) is { } oiled)
            return oiled;
        var slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        if (InteractContent(byPlayer, slot, blockSel) is { } handled)
            return handled;
        if (_assembly is not { } assembly)
            return false;
        bool creative = byPlayer.WorldData?.CurrentGameMode == EnumGameMode.Creative;
        if (CreativeShortcut(byPlayer))
        {
            if (Api.Side == EnumAppSide.Server && assembly.FillNext())
            {
                Api.World.PlaySoundAt(Block.Sounds.Place, Pos, -0.25, byPlayer);
                OnAssemblyChanged();
                MarkDirty(true);
            }
            return true;
        }
        var stack = slot?.Itemstack;
        string? code = stack?.Collectible?.Code?.ToString();
        if (stack != null && code != null)
        {
            int held = creative ? int.MaxValue / 2 : stack.StackSize;
            double? uses = DurabilityOf(stack);
            var check = assembly.Check(code, held, stack.Collectible.MaxStackSize, uses);
            if (check.Verdict != AssemblyVerdict.NotAPart)
            {
                if (Api.Side != EnumAppSide.Server)
                    return true;
                var click = assembly.Fit(code, held, stack.Collectible.MaxStackSize, uses);
                if (!click.Took)
                    return Error(byPlayer, MachineText.Error(click, Api.World, Machine, assembly), click.ErrorLangCode);
                if (!creative)
                {
                    slot!.TakeOut(click.Take);
                    slot.MarkDirty();
                }
                Api.World.PlaySoundAt(Block.Sounds.Place, Pos, -0.25, byPlayer);
                Api.World.Logger.Audit("{0} fitted {1}x{2} to the {3} at {4}.", byPlayer.PlayerName, click.Take, code, Machine, Pos);
                OnAssemblyChanged();
                MarkDirty(true);
                return true;
            }
        }
        return slot != null && InteractWork(byPlayer, slot, blockSel);
    }

    /// <summary>After a stage was fitted or a set renewed (server side): nothing by default.</summary>
    protected virtual void OnAssemblyChanged()
    {
    }

    /// <summary>The durability left of a stack whose item has durability, else null.</summary>
    private static double? DurabilityOf(ItemStack stack) =>
        stack.Collectible.GetMaxDurability(stack) > 0 ? stack.Collectible.GetRemainingDurability(stack) : null;

    /// <summary>Sends <paramref name="byPlayer"/> <paramref name="message"/> as an in-game error; true (the
    /// click was the machine's).</summary>
    protected bool Error(IPlayer? byPlayer, string? message, string? code)
    {
        if (message != null && byPlayer is IServerPlayer sp)
            sp.SendIngameError(code ?? "machine", message);
        return true;
    }

    /// <summary>The block's interaction help: the next stage's materials, a worn-out set, the creative
    /// shortcut, then the machine's own (<see cref="WorkHelp"/>).</summary>
    public virtual WorldInteraction[] InteractionHelp(IPlayer forPlayer)
    {
        var help = new List<WorldInteraction>();
        if (_assembly is not { } assembly)
            return [];
        var world = Api.World;
        bool creative = forPlayer?.WorldData?.CurrentGameMode == EnumGameMode.Creative;
        if (!assembly.Built)
        {
            var stacks = assembly.Build.NextNeeds().SelectMany(n => Stacks(world, n.Material.Codes, n.Missing)).ToArray();
            if (stacks.Length > 0)
                help.Add(new WorldInteraction { ActionLangCode = "seraphhorizons:machine-help-fit", MouseButton = EnumMouseButton.Right, Itemstacks = stacks });
            if (creative)
                help.AddRange(SplittingBlockUpgrades.CreativeUpgradeHelp);
            return help.ToArray();
        }
        foreach (var part in assembly.Wearing.Parts.Where(p => assembly.Wearing.Fitted(p.Id) is not { Left: > 0 }))
            if (Stacks(world, part.Codes, part.Count) is { Length: > 0 } stacks)
                help.Add(new WorldInteraction { ActionLangCode = "seraphhorizons:machine-help-renew", MouseButton = EnumMouseButton.Right, Itemstacks = stacks });
        if (creative && CreativeFills)
            help.AddRange(SplittingBlockUpgrades.CreativeUpgradeHelp);
        WorkHelp(forPlayer, help);
        return help.ToArray();
    }

    /// <summary>The machine's own help lines once it is built (what it takes, a hand's work).</summary>
    protected virtual void WorkHelp(IPlayer? forPlayer, List<WorldInteraction> help)
    {
    }

    /// <summary>Stacks of every item in the world <paramref name="codes"/> name (patterns: the first eight), of
    /// <paramref name="count"/> each, for help lines.</summary>
    public static ItemStack[] Stacks(IWorldAccessor world, IEnumerable<string> codes, int count)
    {
        var stacks = new List<ItemStack>();
        foreach (var code in codes)
        {
            var loc = new AssetLocation(code);
            IEnumerable<CollectibleObject> found = code.Contains('*')
                ? world.SearchItems(loc).Cast<CollectibleObject>().Concat(world.SearchBlocks(loc)).Take(8)
                : new CollectibleObject?[] { world.GetItem(loc), world.GetBlock(loc) }.OfType<CollectibleObject>();
            stacks.AddRange(found.Where(c => c is { Id: > 0, IsMissing: false }).Select(c => new ItemStack(c, Math.Max(1, count))));
        }
        return stacks.ToArray();
    }

    // ---- Running ----

    private void OnServerTick(float dt)
    {
        if (_assembly == null)
            return;
        var power = Power;
        float angle = power?.AngleRad ?? 0, speed = power?.TrueSpeed ?? 0;
        float advance = _angleSeeded ? ShaftClock.AngleAdvance(_lastAngle, angle, speed, dt) : 0;
        _lastAngle = angle;
        _angleSeeded = true;

        if (HandWorked)
            UpdateHeld();
        bool dirty = Tick(dt);
        FillFromPipe(dt);

        var state = StateNow(speed);
        if (MachineRun.Works(state))
            dirty |= Powered ? advance > 0 && Work(advance) : WorkUnpowered(dt);
        if (state != _state)
        {
            _state = state;
            dirty = true;
        }
        // The percent next to "Working" moves on the client only when the machine syncs (ProgressSync).
        int? percent = MachineRun.Works(state) ? MachineRun.Percent(WorkProgress) : null;
        double now = Api.World.ElapsedMilliseconds / 1000.0;
        dirty |= _progressSync.Due(percent, now);
        if (dirty)
        {
            _progressSync = _progressSync.Sent(percent, now);
            MarkDirty(true);
        }
    }

    private MachineState StateNow(float speed) =>
        MachineRun.StateOf(new MachineCheck(Built, _assembly!.Wearing.AllFitted, Powered, speed,
            Wet, HasWork, OutputRoom, HandWorked ? _held : null), ConfigMinSpeed);

    /// <summary>Takes the state the running rules give now (server side), syncing it when it changed: after
    /// something that changes it between ticks (a click; the work taken off).</summary>
    protected void RefreshState()
    {
        if (_assembly == null || Api?.Side != EnumAppSide.Server)
            return;
        var state = StateNow(Power?.TrueSpeed ?? 0);
        if (state == _state)
            return;
        _state = state;
        MarkDirty(true);
    }

    /// <summary>The machine's own business each tick before the running rules (server side): moving what
    /// chutes brought, pushing out what it holds. Whether anything changed. Nothing by default.</summary>
    protected virtual bool Tick(float dt) => false;

    /// <summary>Whether there is something to work. False by default.</summary>
    public virtual bool HasWork => false;

    /// <summary>Whether what the work makes has somewhere to go. True by default.</summary>
    public virtual bool OutputRoom => true;

    /// <summary>Works <paramref name="radians"/> of shaft rotation (server side; called while the state is
    /// <see cref="MachineState.Working"/>); whether anything changed. Nothing by default.</summary>
    protected virtual bool Work(double radians) => false;

    /// <summary>Works <paramref name="dt"/> real seconds of a machine with no power anchor (a hand machine
    /// while worked, water and gravity): server side, while the state is <see cref="MachineState.Working"/>.
    /// Nothing by default.</summary>
    protected virtual bool WorkUnpowered(float dt) => false;

    /// <summary>The share of the work in hand done, 0 to 1, shown as a percent next to "Working"
    /// (<see cref="MachineRun.Percent"/>); null where it has none (the default).</summary>
    public virtual double? WorkProgress => null;

    // The client's own: a broken machine's block entity brought back over air by a late update is dropped;
    // a dry machine smokes while it runs.
    private void OnClientTickBase(float dt)
    {
        if (Api.World.BlockAccessor.GetBlock(Pos) is not BlockMachine)
        {
            DisposeRenderer();
            Api.World.BlockAccessor.RemoveBlockEntity(Pos);
            return;
        }
        if (_oil is { Dry: true } && Api is ICoreClientAPI capi && (_smokeSeconds += dt) >= 0.4f)
        {
            _smokeSeconds = 0;
            if (Running && SmokePoint is { } at)
                _oil.SmokeIfDry(capi, WorldPoint(at));
        }
        OnClientTick(dt);
    }

    /// <summary>The machine's own client tick (sounds while it works). Nothing by default.</summary>
    protected virtual void OnClientTick(float dt)
    {
    }

    // ---- Breaking ----

    /// <summary>What breaking the frame gives besides the frame: everything fitted (a worn-out wearing
    /// set is gone; a durable part with what is left of it). The inventory drops with the container.</summary>
    public virtual IEnumerable<ItemStack> AssemblyDrops()
    {
        if (_assembly == null)
            yield break;
        foreach (var drop in _assembly.Returns())
            if (StackOf(drop) is { } stack)
                yield return stack;
    }

    /// <summary>The stack breaking gives for <paramref name="drop"/>: a durable part's item with the
    /// durability it has left.</summary>
    protected ItemStack? StackOf(StageDrop drop)
    {
        var loc = new AssetLocation(drop.Code);
        CollectibleObject? c = (CollectibleObject?)Api.World.GetItem(loc) ?? Api.World.GetBlock(loc);
        if (c is not { Id: > 0, IsMissing: false } || drop.Count <= 0)
            return null;
        var stack = new ItemStack(c, drop.Count);
        if (drop.Durability is { } left && left < c.GetMaxDurability(stack))
            stack.Attributes.SetInt("durability", Math.Max(1, (int)Math.Round(left)));
        return stack;
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        RemoveCells();
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
        if (_assembly != null)
        {
            var (build, wearing) = _assembly.Snapshot();
            var b = new TreeAttribute();
            foreach (var (key, count) in build)
                b.SetInt(key, count);
            tree["build"] = b;
            var w = new TreeAttribute();
            foreach (var (id, (code, left)) in wearing)
            {
                var part = new TreeAttribute();
                part.SetString("code", code);
                part.SetDouble("left", left);
                w[id] = part;
            }
            tree["wearing"] = w;
        }
        tree.SetDouble("water", _water.Litres);
        tree.SetInt("state", (int)_state);
        tree.SetBool("held", _held);
        if (_load is { } load)
            tree.SetFloat("load", load);
        _oil?.Write(tree);
        if (Api?.Side == EnumAppSide.Server)
            tree.SetFloat("minSpeed", ConfigMinSpeed);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        _inventory ??= NewInventory();
        base.FromTreeAttributes(tree, worldForResolving);
        if (BuildSpec(worldForResolving) is { } spec)
        {
            var build = new Dictionary<string, int>(StringComparer.Ordinal);
            if (tree["build"] is ITreeAttribute b)
                foreach (var (key, value) in b)
                    if (value is IntAttribute i)
                        build[key] = i.value;
            var wearing = new Dictionary<string, (string, double)>(StringComparer.Ordinal);
            if (tree["wearing"] is ITreeAttribute w)
                foreach (var (id, value) in w)
                    if (value is ITreeAttribute part && part.GetString("code") is { } code)
                        wearing[id] = (code, part.GetDouble("left"));
            _assembly = MachineAssembly.Restore(spec, build, wearing, out var dropped);
            if (dropped.Count > 0 && worldForResolving.Side == EnumAppSide.Server && Pos != null)
                worldForResolving.RegisterCallback(_ =>
                {
                    foreach (var d in dropped)
                        if (StackOf(d) is { } stack)
                            worldForResolving.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 1, 0.5));
                }, 1);
        }
        _water = new MachineReservoir(Math.Max(0, tree.GetDouble("water")));
        _state = (MachineState)tree.GetInt("state", (int)MachineState.NotBuilt);
        if (OilKind is { } kind)
            _oil = MachineOiling.Restore(tree, worldForResolving, kind, DryEffect);
        if (worldForResolving.Side == EnumAppSide.Client)
        {
            // who works it is the server's to say; a loaded world starts with no one at it
            _held = tree.GetBool("held");
            _load = tree.TryGetFloat("load");
            _serverMinSpeed = tree.TryGetFloat("minSpeed");
        }
    }

    // ---- Info ----

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        if (_assembly == null)
            return;
        var info = new MachineInfo();
        MachineText.AppendAssembly(info, Api.World, Machine, _assembly);
        if (_assembly.Built)
            MachineText.AppendState(info.Lines, _state, MinSpeed, Machine, MachineRun.Works(_state) ? MachineRun.Percent(WorkProgress) : null);
        _oil?.AppendInfo(info, Api);
        if (WaterPerItem > 0 && _assembly.Built)
            MachineText.AppendWater(info.Lines, _water, ReservoirCapacity, WaterPerItem);
        AppendContents(info.Lines);
        AppendDetail(info.Detail);
        if (Powered)
            MachineText.AppendShaft(info.Detail, ShaftSpeed, LastLoad);
        MachineText.AppendFootprint(info.Detail, Side, Powered ? PowerFace : null);
        info.WriteTo(dsc, MachineText.HoldsWrench(forPlayer), Lang.Get(MachineText.WrenchHintKey));
    }

    /// <summary>The machine's own lines in its block info, after the shared ones: what it holds, its work.</summary>
    protected virtual void AppendContents(StringBuilder dsc)
    {
    }

    /// <summary>The machine's own lines for a player holding a wrench: exact work counts, its job's internals.</summary>
    protected virtual void AppendDetail(StringBuilder dsc)
    {
    }
}
