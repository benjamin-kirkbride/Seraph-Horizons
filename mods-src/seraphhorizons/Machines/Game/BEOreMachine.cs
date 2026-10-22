using System.Globalization;
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
/// An ore machine's controller (#711, #694): the ore half every ore machine shares, on the machine base
/// (<see cref="BEMachine"/>: build, cells, running rules, power, water, fire, info, renderer), each machine a
/// subclass naming its figures (<see cref="Kind"/>) and its job (<see cref="Takes"/>, <see cref="Products"/>, or
/// its own <see cref="BEMachine.Work"/>). It adds:
/// <list type="bullet">
/// <item>its figures in ore-core (<see cref="Spec"/>): its load (<see cref="OreRecovery.LoadAt"/> at
/// <see cref="JobFill"/>), the water an item takes, its turns an item, so its rate follows its speed
/// (<see cref="WorkTurns"/>); the shared settings' minimum speed and reservoir (<see cref="OreMachinesConfig"/>);</item>
/// <item>its infeed (slot 0) and its output anchors' slots: a chute into the infeed cell's face
/// fills its buffer (<see cref="BEMachineCell"/>), a hand puts in what the machine takes, and each
/// product leaves through its anchor's face into a chute or container (<see cref="MachineChutes"/>),
/// held while the one there is full (the machine then stops, blocked); with nothing there it drops on the
/// ground in front of the face, or for <c>tailings</c> (<see cref="DiscardsAt"/>) is discarded on a heap
/// (<see cref="Tailings"/>), and the machine runs on (<see cref="Outlet"/>).</item>
/// </list>
/// </summary>
public abstract class BEOreMachine : BEMachine
{
    public const string Infeed = OreMachineRig.Infeed, Tailings = "tailings";
    private static readonly AssetLocation WorkSound = new("game", "sounds/block/rock-hit-pickaxe");

    private WorkTurns _turns = WorkTurns.None;
    private TailingsOwed _owed = TailingsOwed.None;
    private string[]? _outputs;

    /// <summary>The machine's figures in ore-core (#765), or null for one that has none.</summary>
    public abstract OreMachine? Kind { get; }

    /// <summary>Whether the machine takes <paramref name="stack"/> into its infeed (by chute or by hand).</summary>
    public abstract bool Takes(ItemStack stack);

    /// <summary>What one item of <paramref name="fed"/> (one of the infeed's, already taken out) gives, by
    /// output anchor name. Called once an item by the default <see cref="Work"/>.</summary>
    protected abstract IEnumerable<(string Anchor, ItemStack Stack)> Products(ItemStack fed);

    public new OreMachineRig? Rig => base.Rig as OreMachineRig;
    public WorkTurns Turns => _turns;

    /// <summary>The share of the item or charge in hand done, 0 to 1, shown as a percent next to "Working"
    /// (<see cref="MachineRun.Percent"/>); null where it has none. The default is the continuous machines':
    /// the turns counted toward the next item (<see cref="Turns"/>). A machine that counts its work some
    /// other way (in-game hours, a charge's turns, a burn) gives its own, from what it saves and syncs.</summary>
    public override double? WorkProgress => Spec is { TurnsPerItem: > 0 } spec ? _turns.Progress(spec.TurnsPerItem) : null;

    /// <summary>Whether the default <see cref="Work"/> drives the machine, counting its shaft turns toward the next
    /// item (<see cref="Turns"/>), so a wrench shows them. A machine worked some other way (in-game hours, a fire, a
    /// charge's or its own turns) turns it off, and shows its own work, if any, in its <see cref="AppendDetail"/>.</summary>
    protected virtual bool CountsTurns => true;

    public override string InventoryClassName => "oremachine";

    /// <summary>The infeed slot.</summary>
    public ItemSlot InfeedSlot => Inventory[0];

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
            yield return Inventory[1 + i * SlotsPerOutput + k];
    }

    protected OreMachinesSystem System => OreMachinesSystem.Of(Api);
    protected OreMachinesConfig Config => System.Config;
    protected override float ConfigMinSpeed => Config.MinSpeed;

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
    public override bool HasWork => !InfeedSlot.Empty;

    /// <summary>Whether a product made now has somewhere to go: every held product gone. Only a chute or
    /// container attached and backed up holds one (<see cref="Deliver"/>).</summary>
    public override bool OutputRoom => Outputs.All(a => OutputSlots(a).All(s => s.Empty));

    /// <summary>The water an item takes, 0 for a dry machine.</summary>
    public override double WaterPerItem => Spec?.WaterPerItem ?? 0;

    public override double ReservoirCapacity => MachineReservoir.Capacity(Config, WaterPerItem);

    protected override double WaterIntakePerSecond => Config.WaterIntakeLitresPerSecond;

    /// <summary>The job's load: <see cref="OreRecovery.LoadAt"/> of its figure at <see cref="JobFill"/>.</summary>
    protected override double ShaftLoad =>
        Kind is { } k && Api != null ? OreProcessingSystem.RecoveryFor(Api).LoadAt(k, JobFill) : 0;

    protected override InventoryBase NewInventory()
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

    /// <summary>The infeed cell's block entity, when <paramref name="cell"/> is the infeed cell and
    /// <paramref name="face"/> its infeed face; else null.</summary>
    public BEMachineCell? InfeedBuffer(BlockPos cell, BlockFacing face)
    {
        if (Rig?.Anchor(Infeed) is not { } a || !IsInfeedFace(face) || !CellPos(a.Cell).Equals(cell))
            return null;
        return Api.World.BlockAccessor.GetBlockEntity(cell) as BEMachineCell;
    }

    /// <summary>A chute into the infeed cell's infeed face, or into a side feed's, fills that cell's buffer
    /// with what the machine (or the side feed) takes.</summary>
    public override bool CellTakes(BlockPos cell, BlockFacing face, ItemStack stack) =>
        InfeedBuffer(cell, face) != null && Takes(stack)
        || SideFeedAt(cell, face) is { } feed && SideFeedTakes(feed, stack);

    // ---- Side feeds (a furnace's fuel chute) ----

    /// <summary>Anchors besides the infeed that a chute feeds (the reverberatory roaster's <c>fuel</c>, #736): each
    /// fills its cell's buffer through its face with what <see cref="SideFeedTakes"/> accepts, and the controller
    /// moves it into <see cref="SideFeedSlot"/>. None by default. The anchor's cell must not be the controller's.</summary>
    protected virtual IReadOnlyList<string> SideFeeds => [];

    /// <summary>Whether side feed <paramref name="anchor"/> takes <paramref name="stack"/>.</summary>
    public virtual bool SideFeedTakes(string anchor, ItemStack stack) => false;

    /// <summary>The slot side feed <paramref name="anchor"/> fills, and how many of the stack's kind it holds.</summary>
    protected virtual (ItemSlot Slot, int Capacity)? SideFeedSlot(string anchor) => null;

    /// <summary>The side feed whose cell is <paramref name="cell"/> and whose face is <paramref name="face"/>, or null.</summary>
    public string? SideFeedAt(BlockPos cell, BlockFacing face)
    {
        if (Rig is not { } rig)
            return null;
        foreach (var name in SideFeeds)
            if (rig.Anchor(name) is { } a && a.Cell != Int3.Zero && CellFaces.ToWorld(a.Face, Side).Code() == face.Code && CellPos(a.Cell).Equals(cell))
                return name;
        return null;
    }

    /// <summary>Moves what chutes left in the side feeds' buffers into their slots.</summary>
    private bool PullSideFeeds()
    {
        bool moved = false;
        foreach (var name in SideFeeds)
        {
            if (Rig?.Anchor(name) is not { } a || a.Cell == Int3.Zero || SideFeedSlot(name) is not { } target)
                continue;
            if (Api.World.BlockAccessor.GetBlockEntity(CellPos(a.Cell)) is not BEMachineCell cell || cell.Buffer.Empty)
                continue;
            var stack = cell.Buffer.Itemstack;
            if (!SideFeedTakes(name, stack))
                continue;
            int room = target.Slot.Empty ? target.Capacity
                : target.Slot.Itemstack.Equals(Api.World, stack, GlobalConstants.IgnoredStackAttributes) ? target.Capacity - target.Slot.StackSize : 0;
            if (room <= 0)
                continue;
            var op = new ItemStackMoveOperation(Api.World, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, Math.Min(cell.Buffer.StackSize, room));
            if (cell.Buffer.TryPutInto(target.Slot, ref op) > 0)
            {
                cell.Buffer.MarkDirty();
                cell.MarkDirty();
                target.Slot.MarkDirty();
                moved = true;
            }
        }
        return moved;
    }

    // ---- Interaction ----

    /// <summary>Something the machine takes goes into its infeed, as much as fits.</summary>
    protected override bool InteractWork(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        if (slot.Itemstack is not { } stack || Outputs.Length == 0 || !Takes(stack))
            return false;
        if (Api.Side != EnumAppSide.Server)
            return true;
        int moved = Math.Min(InfeedRoom(stack), stack.StackSize);
        if (moved <= 0)
            return Error(byPlayer, Lang.Get("seraphhorizons:machine-error-infeedfull"), "seraphhorizons:machine-error-infeedfull");
        var op = new ItemStackMoveOperation(Api.World, EnumMouseButton.Right, 0, EnumMergePriority.AutoMerge, moved);
        slot.TryPutInto(InfeedSlot, ref op);
        slot.MarkDirty();
        MarkDirty(true);
        return true;
    }

    /// <summary>The feed line: what the machine takes (<see cref="FeedStacks"/>).</summary>
    protected override void WorkHelp(IPlayer? forPlayer, List<WorldInteraction> help)
    {
        if (Outputs.Length > 0 && FeedStacks(Api.World) is { Length: > 0 } feed)
            help.Add(new WorldInteraction { ActionLangCode = "seraphhorizons:machine-help-feed", MouseButton = EnumMouseButton.Right, Itemstacks = feed });
    }

    /// <summary>Stacks the help shows for what the machine takes; none by default.</summary>
    protected virtual ItemStack[] FeedStacks(IWorldAccessor world) => [];

    // ---- Running ----

    /// <summary>Moves what chutes brought in and pushes held products on.</summary>
    protected override bool Tick(float dt)
    {
        bool dirty = PullInfeed();
        dirty |= PullSideFeeds();
        dirty |= PushOutputs();
        return dirty;
    }

    /// <summary>
    /// Works <paramref name="radians"/> of shaft rotation (server side; called while the state is
    /// <see cref="MachineState.Working"/>); whether anything changed. The default is the continuous
    /// machines': each <see cref="OreMachineSpec.TurnsPerItem"/> turns one item of the infeed is worked,
    /// spending an item's water and wearing every wearing part a use, its products delivered at their
    /// anchors (<see cref="Deliver"/>).
    /// </summary>
    protected override bool Work(double radians)
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

    /// <summary>Works one item of the infeed as the default <see cref="Work"/> does (its water, a use of
    /// every wearing part, a job's oil, its products at their anchors); false when it cannot now.</summary>
    protected bool WorkItem()
    {
        if (Assembly is not { } assembly || !HasWork || !OutputRoom || !Water.Holds(WaterPerItem))
            return false;
        SpendWater(WaterPerItem);
        assembly.Wearing.WearAll(1);
        DrainOil(1);
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

    /// <summary>Puts a product at its anchor: into what is beyond the anchor's face; what that cannot take
    /// yet is held in its slot while a chute or container is attached there, and otherwise discarded on the
    /// heap (tailings) or dropped on the ground in front of the face (<see cref="Outlet.Fate"/>).</summary>
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
            if (LeaveUnheld(anchor, cell, face, new DummySlot(product), product))
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

    /// <summary>A product left over at an outlet with nothing attached: discarded on the heap (tailings, when the
    /// machine makes one) or dropped in front of the face; false (it is held) while a chute or container is
    /// attached.</summary>
    private bool LeaveUnheld(string anchor, BlockPos cell, BlockFacing face, ItemSlot probe, ItemStack product)
    {
        bool attached = MachineChutes.Attached(Api.World, cell, face, probe);
        switch (Outlet.Fate(attached, DiscardsAt(anchor)))
        {
            case OutletFate.Held:
                return false;
            case OutletFate.Discarded when Discard(cell.AddCopy(face), product):
                return true;
            default:
                MachineChutes.Drop(Api.World, cell, face, product);
                return true;
        }
    }

    /// <summary>Whether what leaves by output anchor <paramref name="anchor"/> is tailings, discarded on the heap
    /// (<see cref="Discard"/>) when nothing takes it: <c>tailings</c>, by default. A machine whose only outlet
    /// carries its tailings on (the amalgamation plates' <c>output</c>) says so.</summary>
    protected virtual bool DiscardsAt(string anchor) => anchor == Tailings;

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
        if (Api.World.BlockAccessor.GetBlockEntity(CellPos(a.Cell)) is not BEMachineCell cell || cell.Buffer.Empty)
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

    /// <summary>Pushes every held product on through its anchor; with nothing attached there any more (or a
    /// world saved while products were held), it goes as <see cref="Deliver"/> leaves it.</summary>
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
            else if (LeaveUnheld(anchor, cell, face, slot, slot.Itemstack))
            {
                slot.Itemstack = null;
                slot.MarkDirty();
                moved = true;
            }
        }
        return moved;
    }

    // ---- Saving and syncing ----

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetDouble("turns", _turns.Turns);
        tree.SetDouble("tailingsOwed", _owed.Layers);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        double perTurns = Spec?.TurnsPerItem ?? 0;
        _turns = perTurns > 0 ? WorkTurns.Restore(tree.GetDouble("turns"), perTurns) : new WorkTurns(Math.Max(0, tree.GetDouble("turns")));
        _owed = TailingsOwed.Restore(tree.GetDouble("tailingsOwed"));
    }

    // ---- Info ----

    /// <summary>What the machine holds, for its block info.</summary>
    protected override void AppendContents(StringBuilder dsc)
    {
        if (!InfeedSlot.Empty)
            dsc.AppendLine(Lang.Get("seraphhorizons:machine-info-infeed", InfeedSlot.StackSize, InfeedSlot.Itemstack.GetName()));
        foreach (var anchor in Outputs)
            foreach (var slot in OutputSlots(anchor).Where(s => !s.Empty))
                dsc.AppendLine(Lang.Get("seraphhorizons:machine-info-held", slot.StackSize, slot.Itemstack!.GetName()));
    }

    /// <summary>The turns toward the item in hand, of its turns an item, for a wrench (where they drive it:
    /// <see cref="CountsTurns"/>).</summary>
    protected override void AppendDetail(StringBuilder dsc)
    {
        if (Built && CountsTurns && Spec is { TurnsPerItem: > 0 } spec)
            dsc.AppendLine(Lang.Get("seraphhorizons:machine-info-turns",
                _turns.Turns.ToString("0.##", CultureInfo.InvariantCulture), spec.TurnsPerItem.ToString("0.##", CultureInfo.InvariantCulture)));
    }

    /// <summary>For a wrench on a machine worked by the hour (<see cref="WorkHours"/>): the in-game hours toward the
    /// item in hand, of the hours an item takes.</summary>
    protected void AppendHours(StringBuilder dsc, WorkHours hours)
    {
        if (Built && Spec is { ItemsPerHour: > 0 } spec)
            dsc.AppendLine(Lang.Get("seraphhorizons:machine-info-hours",
                (hours.Items / spec.ItemsPerHour).ToString("0.###", CultureInfo.InvariantCulture), (1 / spec.ItemsPerHour).ToString("0.###", CultureInfo.InvariantCulture)));
    }
}
