using System.Text;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
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
/// An ore machine's controller (#711, #694): the shared game half of every ore machine, each machine a
/// subclass naming its figures (<see cref="Kind"/>) and its job (<see cref="Takes"/>,
/// <see cref="Products"/>, or its own <see cref="Work"/>). It keeps:
/// <list type="bullet">
/// <item>its build and wearing parts (<see cref="MachineAssembly"/> from <c>config/builds/{machine}.json</c>):
/// a right-click fits the next stage or renews a worn-out set; the creative shortcut (Ctrl) fits the next
/// stage; breaking gives back everything that went in;</item>
/// <item>its cells (<see cref="BlockOreMachineCell"/>), stamped from the rig and kept;</item>
/// <item>the running rules (<see cref="MachineRun"/>): built, parts fitted, the shaft at
/// <see cref="OreMachinesConfig.MinSpeed"/> or faster, an item's water, room for the output; work in
/// shaft turns (<see cref="WorkTurns"/>) at the machine's turns an item, so its rate follows its speed;</item>
/// <item>its load (<see cref="Resistance"/>): <see cref="MachineLoad.Resistance"/> of the job's
/// figure (<see cref="OreRecovery.LoadAt"/> at <see cref="JobFill"/>);</item>
/// <item>its reservoir (<see cref="MachineReservoir"/>), filled from a ppex pipe at the rig's <c>water</c>
/// anchor (<see cref="PpexWater"/>) or by <see cref="FillWater"/>;</item>
/// <item>its infeed (slot 0) and one held product a output anchor: a chute into the infeed cell's face
/// fills its buffer (<see cref="BEOreMachineCell"/>), a hand puts in what the machine takes, and each
/// product leaves through its anchor's face into a chute or container (<see cref="MachineChutes"/>);
/// with nothing there it is held (blocked), or for <c>tailings</c> discarded on a heap
/// (<see cref="Tailings"/>).</item>
/// </list>
/// The client draws it (<see cref="OreMachineRenderer"/>).
/// </summary>
public abstract class BEOreMachine : BlockEntityContainer, IMachinePower
{
    public const string Infeed = OreMachineRig.Infeed, Tailings = "tailings";
    private static readonly AssetLocation WorkSound = new("game", "sounds/block/rock-hit-pickaxe");

    private InventoryGeneric _inventory = null!;
    private MachineAssembly? _assembly;
    private WorkTurns _turns = WorkTurns.None;
    private MachineReservoir _water = MachineReservoir.Empty;
    private TailingsOwed _owed = TailingsOwed.None;
    private MachineState _state = MachineState.NotBuilt;
    private float _lastAngle;
    private bool _angleSeeded;
    private float? _serverMinSpeed;
    private Dictionary<Int3, Cuboidf[]>? _cells;
    private Dictionary<Int3, Cuboidf[]>? _collision;
    private OreMachineRenderer? _renderer;
    private string[]? _outputs;

    /// <summary>The machine's figures in ore-core (#765), or null for one that has none.</summary>
    public abstract OreMachine? Kind { get; }

    /// <summary>Whether the machine takes <paramref name="stack"/> into its infeed (by chute or by hand).</summary>
    public abstract bool Takes(ItemStack stack);

    /// <summary>What one item of <paramref name="fed"/> (one of the infeed's, already taken out) gives, by
    /// output anchor name. Called once an item by the default <see cref="Work"/>.</summary>
    protected abstract IEnumerable<(string Anchor, ItemStack Stack)> Products(ItemStack fed);

    public string Machine => (Block as BlockOreMachine)?.Machine ?? Block.Code.FirstCodePart();
    public Side Side { get; private set; } = Side.North;
    public OreMachineRig? Rig => (Block as BlockOreMachine)?.Rig;
    public MachineAssembly? Assembly => _assembly;
    public bool Built => _assembly?.Built == true;
    public MachineState State => _state;
    public MachineReservoir Water => _water;
    public WorkTurns Turns => _turns;

    public override InventoryBase Inventory => _inventory;
    public override string InventoryClassName => "oremachine";

    /// <summary>The infeed slot.</summary>
    public ItemSlot InfeedSlot => _inventory[0];

    /// <summary>Slots an output anchor holds products in (one item can give several kinds).</summary>
    public const int SlotsPerOutput = 4;

    /// <summary>Output anchors a machine can have: the inventory's size is fixed (1 + this ×
    /// <see cref="SlotsPerOutput"/>) so a save loads before the rig is known.</summary>
    public const int MaxOutputs = 4;

    /// <summary>The held products of output anchor <paramref name="anchor"/> (none for one it does not have).</summary>
    public IEnumerable<ItemSlot> OutputSlots(string anchor)
    {
        int i = Array.IndexOf(Outputs, anchor);
        if (i < 0)
            yield break;
        for (int k = 0; k < SlotsPerOutput; k++)
            yield return _inventory[1 + i * SlotsPerOutput + k];
    }

    protected OreMachinesSystem System => OreMachinesSystem.Of(Api);
    protected OreMachinesConfig Config => System.Config;
    public float MinSpeed => _serverMinSpeed ?? Config.MinSpeed;

    /// <summary>The machine's figures (load, water an item, rate), or null.</summary>
    public OreMachineSpec? Spec
    {
        get
        {
            if (Kind is not { } kind || Api == null)
                return null;
            try
            {
                return OreProcessingSystem.RecoveryFor(Api).Machine(kind);
            }
            catch (KeyNotFoundException)
            {
                return null;
            }
        }
    }

    /// <summary>The output anchors a product can leave by, in slot order: by default those of
    /// <c>output</c>, <c>concentrate</c>, <c>middlings</c> and <c>tailings</c> the rig has. A batch
    /// machine emptied by hand returns none.</summary>
    protected virtual IReadOnlyList<string> OutputAnchors =>
        Rig is { } rig ? new[] { "output", "concentrate", "middlings", Tailings }.Where(a => rig.Anchors.ContainsKey(a)).ToArray() : [];

    private string[] Outputs => _outputs ??= OutputAnchors.Take(MaxOutputs).ToArray();

    /// <summary>How full the job is, 0 to 1, for the three machines whose load follows it
    /// (<see cref="OreRecovery.LoadAt"/>); the others' load is constant. By default 1 with anything in.</summary>
    public virtual double JobFill => InfeedSlot.Empty ? 0 : 1;

    /// <summary>Whether there is something to work.</summary>
    public virtual bool HasWork => !InfeedSlot.Empty;

    /// <summary>Whether a product made now has somewhere to go: every held product gone.</summary>
    public virtual bool OutputRoom => Outputs.All(a => OutputSlots(a).All(s => s.Empty));

    /// <summary>The water an item takes, 0 for a dry machine.</summary>
    public double WaterPerItem => Spec?.WaterPerItem ?? 0;

    public double ReservoirCapacity => MachineReservoir.Capacity(Config, WaterPerItem);

    /// <summary>The load on the shaft now.</summary>
    public virtual float Resistance =>
        MachineLoad.Resistance(Built, Kind is { } k && Api != null ? OreProcessingSystem.RecoveryFor(Api).LoadAt(k, JobFill) : 0);

    public BlockFacing PowerFace => Rig?.Anchor(OreMachineRig.Power) is { } a
        ? BlockFacing.FromCode(CellFaces.ToWorld(a.Face, Side).Code())
        : BlockFacing.UP;

    /// <summary>The power cell's mechanical power behavior.</summary>
    public BEBehaviorMachineMP? Power =>
        Api != null && Rig?.Anchor(OreMachineRig.Power) is { } a
            ? Api.World.BlockAccessor.GetBlockEntity(CellPos(a.Cell))?.GetBehavior<BEBehaviorMachineMP>()
            : null;

    /// <summary>Whether the power cell is the controller's own: its block type then carries
    /// <c>seraphhorizons.MachineMP</c> itself.</summary>
    public bool PowerAtController => Rig?.Anchor(OreMachineRig.Power) is { Cell: var c } && c == Int3.Zero;

    /// <summary>Whether the machine takes power: its rig has a power anchor.</summary>
    public bool Powered => Rig?.Anchor(OreMachineRig.Power) != null;

    public float ShaftAngle => Power?.AngleRad ?? 0;
    public float ShaftSpeed => Power?.TrueSpeed ?? 0;
    public bool Running => _state == MachineState.Working;

    /// <summary>Whether a rig part needing <paramref name="requires"/> is drawn.</summary>
    public virtual bool ShowsPart(string? requires) => _assembly?.Shows(requires) ?? requires == null;

    /// <summary>Whether the moving parts move: the shaft is held still until the machine is built.</summary>
    public virtual bool Animates => Built;

    // ---- Textures (the renderer's) ----

    /// <summary>What the parts' textures depend on beyond the block's own (the stall roaster's brick type):
    /// the renderer builds its meshes again when it changes. Empty by default.</summary>
    public virtual string TextureKey => "";

    /// <summary>The textures the renderer draws the parts with: the frame block's, by default.</summary>
    public virtual ITexPositionSource PartTextures(ICoreClientAPI capi) => capi.Tesselator.GetTextureSource(Block);

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

    public override void Initialize(ICoreAPI api)
    {
        _inventory ??= NewInventory();
        // before the behaviors start: the power behavior reads the power face, which turns with the side
        Side = Sides.TryParse(Block.Variant["side"], out var side) ? side : Side.North;
        base.Initialize(api);
        if (_assembly == null && System.Build(Machine) is { } spec)
            _assembly = new MachineAssembly(spec);
        BuildBoxes();
        if (api.Side == EnumAppSide.Server)
        {
            RegisterDelayedCallback(_ =>
            {
                if (Api?.World.BlockAccessor.GetBlockEntity(Pos) == this)
                    EnsureCells();
            }, 100);
            RegisterGameTickListener(OnMachineTick, 100);
            RegisterGameTickListener(_ => EnsureCells(), 1000);
        }
        else
        {
            RegisterGameTickListener(OnClientTick, 250);
            if (api is ICoreClientAPI capi && Rig is { } rig && Block is BlockOreMachine)
                _renderer = new OreMachineRenderer(capi, this, rig);
        }
    }

    private InventoryGeneric NewInventory()
    {
        var inv = new InventoryGeneric(1 + MaxOutputs * SlotsPerOutput, null, null);
        // chutes take from the outputs only through their own push (MachineChutes.TryPush), never pull
        inv.OnGetAutoPullFromSlot = _ => null;
        inv.OnGetAutoPushIntoSlot = (face, from) => InfeedBuffer(Pos, face) == null && Rig?.Anchor(Infeed) is { Cell: var c } && c == Int3.Zero
            && from?.Itemstack is { } stack && Takes(stack) && CanMerge(InfeedSlot, stack) && IsInfeedFace(face) ? InfeedSlot : null;
        return inv;
    }

    /// <summary>How many of <paramref name="stack"/>'s kind the infeed holds: a stack, by default.</summary>
    public virtual int InfeedCapacity(ItemStack stack) => stack.Collectible.MaxStackSize;

    /// <summary>Room in the infeed for <paramref name="stack"/>: 0 when it holds something else.</summary>
    public int InfeedRoom(ItemStack stack) =>
        InfeedSlot.Empty ? InfeedCapacity(stack)
        : InfeedSlot.Itemstack.Equals(Api.World, stack, GlobalConstants.IgnoredStackAttributes) ? Math.Max(0, InfeedCapacity(stack) - InfeedSlot.StackSize) : 0;

    private bool CanMerge(ItemSlot slot, ItemStack stack) => slot == InfeedSlot
        ? InfeedRoom(stack) > 0
        : slot.Empty || slot.Itemstack.Equals(Api.World, stack, GlobalConstants.IgnoredStackAttributes) && slot.StackSize < slot.Itemstack.Collectible.MaxStackSize;

    private bool IsInfeedFace(BlockFacing face) =>
        Rig?.Anchor(Infeed) is { } a && face.Code == CellFaces.ToWorld(a.Face, Side).Code();

    // ---- Cells ----

    public BlockPos CellPos(Int3 local)
    {
        var w = Footprint.ToWorld(local, Side);
        return Pos.AddCopy(w.X, w.Y, w.Z);
    }

    public Vec3d WorldPoint(Float3 native) => MachinePoints.WorldPoint(Pos, native, Side);

    private Int3 LocalOf(BlockPos cell) => Footprint.ToLocal(new Int3(cell.X - Pos.X, cell.Y - Pos.Y, cell.Z - Pos.Z), Side);

    /// <summary>The infeed cell's block entity, when <paramref name="cell"/> is the infeed cell and
    /// <paramref name="face"/> its infeed face; else null.</summary>
    public BEOreMachineCell? InfeedBuffer(BlockPos cell, BlockFacing face)
    {
        if (Rig?.Anchor(Infeed) is not { } a || !IsInfeedFace(face) || !CellPos(a.Cell).Equals(cell))
            return null;
        return Api.World.BlockAccessor.GetBlockEntity(cell) as BEOreMachineCell;
    }

    /// <summary>Whether <paramref name="face"/> of the cell at <paramref name="cell"/> is one of the
    /// machine's anchors (a pipe or chute meets it there).</summary>
    public bool AnchorFaceAt(BlockPos cell, BlockFacing face)
    {
        if (Rig is not { } rig)
            return false;
        var local = LocalOf(cell);
        return rig.AnchorsAt(local).Any(a => CellFaces.ToWorld(a.Face, Side).Code() == face.Code);
    }

    public Cuboidf[]? CellBoxes(BlockPos cellPos) => _cells?.GetValueOrDefault(LocalOf(cellPos));

    public Cuboidf[]? CollisionBoxes(BlockPos cellPos) => _collision?.GetValueOrDefault(LocalOf(cellPos));

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

    private IEnumerable<(BlockPos Pos, Block? Block)> CellBlocks()
    {
        if (Rig is not { } rig)
            yield break;
        var power = rig.Anchor(OreMachineRig.Power);
        foreach (var cell in rig.OtherCells)
        {
            var code = power is { } p && p.Cell == cell.Pos
                ? BlockOreMachineCellPower.CodeFor(PowerFace)
                : BlockOreMachineCell.CellCode;
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
        if (Api.World.BlockAccessor.GetBlockEntity(pos) is BEOreMachineCell be)
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
                if (ba.GetBlockEntity(pos) is BEOreMachineCell be && !Pos.Equals(be.Principal))
                {
                    be.Principal = Pos.Copy();
                    be.MarkDirty(true);
                }
            }
            else if (current.Id == 0 || current is BlockOreMachineCell)
                StampCell(expected, pos);
        }
    }

    /// <summary>Clears the cells, dropping what a chute left in the infeed's buffer.</summary>
    public void RemoveCells()
    {
        if (Api.Side != EnumAppSide.Server)
            return;
        var ba = Api.World.BlockAccessor;
        foreach (var (pos, _) in CellBlocks())
            if (ba.GetBlock(pos) is BlockOreMachineCell && ba.GetBlockEntity(pos) is BEOreMachineCell be && Pos.Equals(be.Principal))
            {
                if (!be.Buffer.Empty)
                    Api.World.SpawnItemEntity(be.Buffer.TakeOutWhole(), pos.ToVec3d().Add(0.5, 0.5, 0.5));
                ba.SetBlock(0, pos);
            }
    }

    // ---- Interaction ----

    /// <summary>A right-click on the machine's own contents before the build's (a batch machine's
    /// charge, a bucket of water, the product drawn out): true or false when handled (whether the click
    /// was the machine's), null to go on. Runs on both sides; act on the server.</summary>
    protected virtual bool? InteractContent(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel) => null;

    /// <summary>
    /// Right-click on the machine or any of its cells: the machine's own (<see cref="InteractContent"/>);
    /// in creative mode, Ctrl on a machine still being built fits its next stage; a stage's material
    /// fits it, a worn-out set's renews it; else something the machine takes goes into its infeed.
    /// Decided and done on the server; the client only says whether the click is the machine's.
    /// </summary>
    public bool OnInteract(IPlayer byPlayer, BlockSelection blockSel)
    {
        var slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        if (InteractContent(byPlayer, slot, blockSel) is { } handled)
            return handled;
        if (_assembly is not { } assembly)
            return false;
        var controls = byPlayer.Entity.Controls;
        bool creative = byPlayer.WorldData?.CurrentGameMode == EnumGameMode.Creative;
        if (!assembly.Built && CreativeUpgrades.Applies(creative, controls.CtrlKey, controls.ShiftKey))
        {
            if (Api.Side == EnumAppSide.Server && assembly.FillNext())
            {
                Api.World.PlaySoundAt(Block.Sounds.Place, Pos, -0.25, byPlayer);
                MarkDirty(true);
            }
            return true;
        }
        var stack = slot?.Itemstack;
        string? code = stack?.Collectible?.Code?.ToString();
        if (stack == null || code == null)
            return false;
        int held = creative ? int.MaxValue / 2 : stack.StackSize;
        var check = assembly.Check(code, held, stack.Collectible.MaxStackSize);
        if (check.Verdict != AssemblyVerdict.NotAPart)
        {
            if (Api.Side != EnumAppSide.Server)
                return true;
            var click = assembly.Fit(code, held, stack.Collectible.MaxStackSize);
            if (!click.Took)
                return Error(byPlayer, MachineText.Error(click, Api.World, Machine, assembly), click.ErrorLangCode);
            if (!creative)
            {
                slot!.TakeOut(click.Take);
                slot.MarkDirty();
            }
            Api.World.PlaySoundAt(Block.Sounds.Place, Pos, -0.25, byPlayer);
            Api.World.Logger.Audit("{0} fitted {1}x{2} to the {3} at {4}.", byPlayer.PlayerName, click.Take, code, Machine, Pos);
            MarkDirty(true);
            return true;
        }
        if (Outputs.Length > 0 && Takes(stack))
        {
            if (Api.Side != EnumAppSide.Server)
                return true;
            int moved = Math.Min(InfeedRoom(stack), stack.StackSize);
            if (moved <= 0)
                return Error(byPlayer, Lang.Get("seraphhorizons:machine-error-infeedfull"), "seraphhorizons:machine-error-infeedfull");
            var op = new ItemStackMoveOperation(Api.World, EnumMouseButton.Right, 0, EnumMergePriority.AutoMerge, moved);
            slot!.TryPutInto(InfeedSlot, ref op);
            slot.MarkDirty();
            MarkDirty(true);
            return true;
        }
        return false;
    }

    private bool Error(IPlayer byPlayer, string? message, string? code)
    {
        if (message != null && byPlayer is IServerPlayer sp)
            sp.SendIngameError(code ?? "oremachine", message);
        return true;
    }

    /// <summary>The block's interaction help: the next stage's materials, a worn-out set, what it takes,
    /// the creative shortcut.</summary>
    public virtual WorldInteraction[] InteractionHelp(IPlayer forPlayer)
    {
        var help = new List<WorldInteraction>();
        if (_assembly is not { } assembly)
            return [];
        var world = Api.World;
        if (!assembly.Built)
        {
            var stacks = assembly.Build.NextNeeds().SelectMany(n => Stacks(world, n.Material.Codes, n.Missing)).ToArray();
            if (stacks.Length > 0)
                help.Add(new WorldInteraction { ActionLangCode = "seraphhorizons:machine-help-fit", MouseButton = EnumMouseButton.Right, Itemstacks = stacks });
            if (forPlayer?.WorldData?.CurrentGameMode == EnumGameMode.Creative)
                help.AddRange(SplittingBlockUpgrades.CreativeUpgradeHelp);
            return help.ToArray();
        }
        foreach (var part in assembly.Wearing.Parts.Where(p => assembly.Wearing.Fitted(p.Id) is not { Left: > 0 }))
            if (Stacks(world, part.Codes, part.Count) is { Length: > 0 } stacks)
                help.Add(new WorldInteraction { ActionLangCode = "seraphhorizons:machine-help-renew", MouseButton = EnumMouseButton.Right, Itemstacks = stacks });
        if (Outputs.Length > 0 && FeedStacks(world) is { Length: > 0 } feed)
            help.Add(new WorldInteraction { ActionLangCode = "seraphhorizons:machine-help-feed", MouseButton = EnumMouseButton.Right, Itemstacks = feed });
        return help.ToArray();
    }

    /// <summary>Stacks the help shows for what the machine takes; none by default.</summary>
    protected virtual ItemStack[] FeedStacks(IWorldAccessor world) => [];

    private static ItemStack[] Stacks(IWorldAccessor world, IEnumerable<string> codes, int count)
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

    private void OnMachineTick(float dt)
    {
        if (_assembly == null)
            return;
        var power = Power;
        float angle = power?.AngleRad ?? 0, speed = power?.TrueSpeed ?? 0;
        float advance = _angleSeeded ? ShaftClock.AngleAdvance(_lastAngle, angle, speed, dt) : 0;
        _lastAngle = angle;
        _angleSeeded = true;

        bool dirty = PullInfeed();
        dirty |= PushOutputs();
        FillFromPipe(dt);

        var state = MachineRun.StateOf(new MachineCheck(Built, _assembly.Wearing.AllFitted, Powered, speed,
            Wet, HasWork, OutputRoom), Config.MinSpeed);
        if (MachineRun.Works(state))
            dirty |= Powered ? advance > 0 && Work(advance) : WorkUnpowered(dt);
        if (state != _state)
        {
            _state = state;
            dirty = true;
        }
        if (dirty)
            MarkDirty(true);
    }

    /// <summary>
    /// Works <paramref name="radians"/> of shaft rotation (server side; called while the state is
    /// <see cref="MachineState.Working"/>); whether anything changed. The default is the continuous
    /// machines': each <see cref="OreMachineSpec.TurnsPerItem"/> turns one item of the infeed is worked,
    /// spending an item's water and wearing every wearing part a use, its products held at their anchors
    /// and pushed on.
    /// </summary>
    protected virtual bool Work(double radians)
    {
        if (Spec is not { TurnsPerItem: > 0 } spec)
            return false;
        (_turns, int items) = _turns.Add(radians, spec.TurnsPerItem);
        bool changed = false;
        for (int i = 0; i < items; i++)
        {
            if (!WorkItem())
            {
                _turns = WorkTurns.None;
                break;
            }
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Works <paramref name="dt"/> real seconds of a machine with no power anchor (water and gravity,
    /// the sluice): server side, while the state is <see cref="MachineState.Working"/>. None by default:
    /// such a machine counts its own time (in-game hours, <c>WorkHours</c>) and works its items with
    /// <see cref="WorkItem"/>.
    /// </summary>
    protected virtual bool WorkUnpowered(float dt) => false;

    /// <summary>Works one item of the infeed as the default <see cref="Work"/> does (its water, a use of
    /// every wearing part, its products at their anchors); false when it cannot now.</summary>
    protected bool WorkItem()
    {
        if (_assembly is not { } assembly || !HasWork || !OutputRoom || !_water.Holds(WaterPerItem))
            return false;
        (_water, _) = _water.Spend(WaterPerItem);
        assembly.Wearing.WearAll(1);
        var fed = InfeedSlot.TakeOut(1);
        InfeedSlot.MarkDirty();
        foreach (var (anchor, product) in Products(fed))
            Deliver(anchor, product);
        OnWorked(fed);
        PushOutputs();
        return true;
    }

    /// <summary>After an item is worked (a sound, by default).</summary>
    protected virtual void OnWorked(ItemStack fed) =>
        Api.World.PlaySoundAt(WorkSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, null, true, 16, 0.6f);

    /// <summary>Puts a product at its anchor: into what is beyond the anchor's face, else held in its
    /// slot; tailings with nowhere to go are discarded on the heap.</summary>
    protected void Deliver(string anchor, ItemStack product)
    {
        if (!Outputs.Contains(anchor))
        {
            Api.World.Logger.Warning("[seraphhorizons] The {0} has no output anchor {1}; {2} is dropped", Machine, anchor, product.Collectible.Code);
            Api.World.SpawnItemEntity(product, Pos.ToVec3d().Add(0.5, 1, 0.5));
            return;
        }
        if (Rig?.Anchor(anchor) is { } a)
        {
            var (cell, face) = MachineChutes.World(Pos, Side, a);
            int moved = MachineChutes.TryPush(Api.World, cell, face, product);
            product.StackSize -= moved;
            if (product.StackSize <= 0)
                return;
            if (anchor == Tailings && !MachineChutes.ChutedOn(Api.World, cell, face, new DummySlot(product)) && Discard(cell.AddCopy(face), product))
                return;
        }
        var slots = OutputSlots(anchor).ToList();
        if (slots.FirstOrDefault(s => !s.Empty && s.Itemstack.Equals(Api.World, product, GlobalConstants.IgnoredStackAttributes)) is { } same)
        {
            same.Itemstack!.StackSize += product.StackSize;
            same.MarkDirty();
        }
        else if (slots.FirstOrDefault(s => s.Empty) is { } empty)
        {
            empty.Itemstack = product;
            empty.MarkDirty();
        }
        else
            Api.World.SpawnItemEntity(product, Pos.ToVec3d().Add(0.5, 1, 0.5));
    }

    /// <summary>Discards tailings on the heap beyond the tailings anchor (#694): the machine's
    /// <see cref="OreMachinesConfig.TailingsFor"/>'s layers a rock, in the stack's host rock. False when
    /// the machine discards none.</summary>
    protected bool Discard(BlockPos at, ItemStack stack)
    {
        if (Config.TailingsFor(Machine) is not { } rule)
            return false;
        (int whole, _owed) = _owed.Add(stack.StackSize, rule);
        Machines.Tailings.Discard(Api.World, at, rule.Form, Machines.Tailings.RockOf(stack), whole);
        return true;
    }

    /// <summary>Moves what a chute left in the infeed cell's buffer into the infeed slot.</summary>
    private bool PullInfeed()
    {
        if (Rig?.Anchor(Infeed) is not { } a || a.Cell == Int3.Zero)
            return false;
        if (Api.World.BlockAccessor.GetBlockEntity(CellPos(a.Cell)) is not BEOreMachineCell cell || cell.Buffer.Empty)
            return false;
        if (!Takes(cell.Buffer.Itemstack) || !CanMerge(InfeedSlot, cell.Buffer.Itemstack))
            return false;
        var op = new ItemStackMoveOperation(Api.World, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge,
            Math.Min(cell.Buffer.StackSize, InfeedRoom(cell.Buffer.Itemstack)));
        int moved = cell.Buffer.TryPutInto(InfeedSlot, ref op);
        if (moved > 0)
        {
            cell.Buffer.MarkDirty();
            cell.MarkDirty();
        }
        return moved > 0;
    }

    /// <summary>Pushes every held product on through its anchor.</summary>
    private bool PushOutputs()
    {
        bool moved = false;
        foreach (var anchor in Outputs)
        foreach (var slot in OutputSlots(anchor))
        {
            if (slot.Empty || Rig?.Anchor(anchor) is not { } a)
                continue;
            var (cell, face) = MachineChutes.World(Pos, Side, a);
            if (MachineChutes.TryPush(Api.World, cell, face, slot) > 0)
                moved = true;
            else if (anchor == Tailings && !MachineChutes.ChutedOn(Api.World, cell, face, slot) && Discard(cell.AddCopy(face), slot.Itemstack))
            {
                slot.Itemstack = null;
                slot.MarkDirty();
                moved = true;
            }
        }
        return moved;
    }

    private void FillFromPipe(float dt)
    {
        double per = WaterPerItem;
        if (per <= 0 || Rig?.Anchor(OreMachineRig.Water) is not { } a)
            return;
        double cap = ReservoirCapacity;
        double wanted = _water.Wanted(cap, Config.WaterIntakeLitresPerSecond, dt);
        if (wanted <= 1e-6)
            return;
        var (cell, face) = MachineChutes.World(Pos, Side, a);
        double got = PpexWater.Draw(Api, cell, face, wanted);
        if (got > 0)
            FillWater(got);
    }

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

    // The client's own: a broken machine's block entity brought back over air by a late update is dropped.
    private void OnClientTick(float dt)
    {
        if (Api.World.BlockAccessor.GetBlock(Pos) is not BlockOreMachine)
        {
            DisposeRenderer();
            Api.World.BlockAccessor.RemoveBlockEntity(Pos);
        }
    }

    // ---- Breaking ----

    /// <summary>What breaking the frame gives besides the frame: everything fitted (a worn-out wearing
    /// set is gone). The inventory drops with the container.</summary>
    public virtual IEnumerable<ItemStack> AssemblyDrops()
    {
        if (_assembly == null)
            yield break;
        foreach (var drop in _assembly.Returns())
            if (StackOf(drop) is { } stack)
                yield return stack;
    }

    private ItemStack? StackOf(StageDrop drop)
    {
        var loc = new AssetLocation(drop.Code);
        CollectibleObject? c = (CollectibleObject?)Api.World.GetItem(loc) ?? Api.World.GetBlock(loc);
        return c is { Id: > 0, IsMissing: false } && drop.Count > 0 ? new ItemStack(c, drop.Count) : null;
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
        tree.SetDouble("turns", _turns.Turns);
        tree.SetDouble("water", _water.Litres);
        tree.SetDouble("tailingsOwed", _owed.Layers);
        tree.SetInt("state", (int)_state);
        if (Api?.Side == EnumAppSide.Server)
            tree.SetFloat("minSpeed", Config.MinSpeed);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        _inventory ??= NewInventory();
        base.FromTreeAttributes(tree, worldForResolving);
        var spec = worldForResolving.Api.ModLoader.GetModSystem<OreMachinesSystem>()?.Build(MachineOf(worldForResolving, tree));
        if (spec != null)
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
        double perTurns = Spec?.TurnsPerItem ?? 0;
        _turns = perTurns > 0 ? WorkTurns.Restore(tree.GetDouble("turns"), perTurns) : new WorkTurns(Math.Max(0, tree.GetDouble("turns")));
        _water = new MachineReservoir(Math.Max(0, tree.GetDouble("water")));
        _owed = TailingsOwed.Restore(tree.GetDouble("tailingsOwed"));
        _state = (MachineState)tree.GetInt("state", (int)MachineState.NotBuilt);
        if (worldForResolving.Side == EnumAppSide.Client)
            _serverMinSpeed = tree.TryGetFloat("minSpeed");
    }

    // The block is set (CreateBehaviors) before a save is read.
    private string MachineOf(IWorldAccessor world, ITreeAttribute tree)
    {
        if (Block is BlockOreMachine b)
            return b.Machine;
        return Machine;
    }

    // ---- Info ----

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        if (_assembly == null)
            return;
        MachineText.AppendAssembly(dsc, Api.World, Machine, _assembly);
        if (_assembly.Built)
        {
            MachineText.AppendState(dsc, _state, MinSpeed, Machine);
            if (WaterPerItem > 0)
                MachineText.AppendWater(dsc, _water, ReservoirCapacity, WaterPerItem);
        }
        AppendContents(dsc);
    }

    /// <summary>What the machine holds, for its block info.</summary>
    protected virtual void AppendContents(StringBuilder dsc)
    {
        if (!InfeedSlot.Empty)
            dsc.AppendLine(Lang.Get("seraphhorizons:machine-info-infeed", InfeedSlot.StackSize, InfeedSlot.Itemstack.GetName()));
        foreach (var anchor in Outputs)
            foreach (var slot in OutputSlots(anchor).Where(s => !s.Empty))
                dsc.AppendLine(Lang.Get("seraphhorizons:machine-info-held", slot.StackSize, slot.Itemstack!.GetName()));
    }
}
