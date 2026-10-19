using System.Globalization;
using System.Text;
using SeraphHorizons.Mod.CrucibleFurnace;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using SeraphHorizons.Mod.StallRoaster.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.StallRoaster;

/// <summary>
/// The stall roaster (#925; README "Ore processing: the stall roaster"): a roasting stall on the shared ore machine
/// code, fired (<see cref="BEFiredOreMachine"/>), batch, loaded and emptied by hand, with no power, no water and no chute
/// (<see cref="StallRoasting"/> has its rules). Built as its build file says (the iron frame, then the brickwork in
/// one fired brick type, then the lining); then, through its open front, a bed of firewood is laid and a sulfide's
/// raw or crushed rock heaped on it; lit with a firestarter or a torch, the heap burns on its own sulfur for
/// ore-processing.json's <c>machines.stallroaster.chargeHours</c> (40) at full draught: its front walled up with
/// loose brick of its own type and its row's chimney drawing, every other outlet in the row capped (the crucible
/// furnace's row rule, <see cref="FurnaceRow"/>). Each rock comes out one for one as stall-roasted rock of its
/// grade (92 %), and a third of the charge's sulfur as sulfur powder in the heap's crust, taken out first; the
/// rock is then raked out a course at a time.
/// <para>Its inventory: slot 0 (the shared infeed slot) the bed's firewood, slots 1 to <see cref="SulfurSlot"/> - 1 the
/// charge, <see cref="SulfurSlot"/> the crust's sulfur; all drop when it is broken, with the front's loose brick
/// and the cap.</para>
/// </summary>
public class BEStallRoaster : BEFiredOreMachine
{
    /// <summary>The crust's sulfur: the last slot.</summary>
    public const int SulfurSlot = MaxOutputs * SlotsPerOutput;
    private const int FirstChargeSlot = 1;
    private static readonly AssetLocation RakeSound = new("game", "sounds/block/gravel");

    private StallFront _front;
    private int _frontBricks;
    private bool _lit, _roasted;
    private double _burned;
    private double _lastHours = double.NaN;
    private double _sulfurCarry;
    private double _syncedBurned;

    public override OreMachine? Kind => OreMachine.StallRoaster;

    /// <summary>Nothing goes in by chute: the stall is charged by hand (<see cref="InteractContent"/>).</summary>
    public override bool Takes(ItemStack stack) => false;

    protected override IEnumerable<(string Anchor, ItemStack Stack)> Products(ItemStack fed) => [];

    protected override IReadOnlyList<string> OutputAnchors => [];

    private StallRoasterSystem Stall => StallRoasterSystem.Of(Api);
    private StallRoasterConfig StallConfig => Stall.Config;
    public StallRoasterRig? StallRig => Api == null ? null : Stall.Rig;
    protected override FiredRig? FiredRig => StallRig;

    public StallFront Front => _front;
    public int FrontBricks => _frontBricks;
    public override bool Lit => _lit;
    public bool Roasted => _roasted;
    /// <summary>Hours of the burn done, at full draught.</summary>
    public double BurnedHours => _burned;

    /// <summary>The burn's length at full draught (ore-processing.json <c>machines.stallroaster.chargeHours</c>).</summary>
    public double BurnHours => Spec?.ChargeHours ?? 0;

    /// <summary>The rock a charge holds (ore-processing.json <c>machines.stallroaster.charge</c>).</summary>
    public int Capacity => Spec?.ChargeItems ?? StallRig?.Charge ?? 0;

    public int Firewood => InfeedSlot.StackSize;

    public IEnumerable<ItemSlot> ChargeSlots => Enumerable.Range(FirstChargeSlot, SulfurSlot - FirstChargeSlot).Select(i => Inventory[i]);

    /// <summary>The rock in it, raw, crushed or roasted.</summary>
    public int Rock => ChargeSlots.Sum(s => s.StackSize);

    public int SulfurPowders => Inventory[SulfurSlot].StackSize;

    public StallPhase Phase => StallRoasting.PhaseOf(Firewood, Rock, StallConfig.BedFirewood, _lit, _roasted);

    /// <summary>The burn's hours done, of <see cref="BurnHours"/>, while it burns.</summary>
    public override double? WorkProgress =>
        Phase != StallPhase.Burning ? null : BurnHours > 0 ? _burned / BurnHours : 1;

    /// <summary>The share of its full draught it burns at now.</summary>
    public double Draught => StallRoasting.Draught(_front, Row.Works, StallConfig);

    public override bool HasWork => _lit && !_roasted;

    public override double JobFill => _lit ? 1 : 0;

    // ---- The burn ----

    protected override bool WorkUnpowered(float dt)
    {
        double now = Api.World.Calendar.TotalHours;
        double hours = double.IsNaN(_lastHours) ? 0 : now - _lastHours;
        _lastHours = now;
        var (burned, done) = StallRoasting.Advance(_burned, hours, Draught, BurnHours);
        bool kindled = StallRoasting.Kindling(_burned, BurnHours) != StallRoasting.Kindling(burned, BurnHours);
        _burned = burned;
        if (done)
        {
            Finish();
            return true;
        }
        // synced for the block info every hundredth of the burn, and when the bed has burnt away
        if (kindled || _burned - _syncedBurned >= BurnHours / 100)
        {
            _syncedBurned = _burned;
            return true;
        }
        return false;
    }

    /// <summary>Ages a burning stall by <paramref name="hours"/>, as if they had passed (tests).</summary>
    public void Age(double hours) => _lastHours = (double.IsNaN(_lastHours) ? Api.World.Calendar.TotalHours : _lastHours) - hours;

    /// <summary>The burn done: every rock to stall-roasted rock of its grade, one for one, and the charge's sulfur
    /// (with what the stall carried from its last burn) into the crust as whole powders.</summary>
    private void Finish()
    {
        var recovery = OreProcessingSystem.RecoveryFor(Api);
        double sulfurUnits = 0;
        foreach (var slot in ChargeSlots.Where(s => !s.Empty))
        {
            var stack = slot.Itemstack!;
            if (OreStreamAttributes.Read(stack) is { } stream
                && Api.World.GetItem(new AssetLocation(OreProducts.StallRoastedRockCode(stream.Ore, stream.Grade))) is { Id: > 0 } roasted)
            {
                sulfurUnits += stack.StackSize * recovery.SulfurUnits(recovery.Ore(stream.Ore), stream.Units, Roaster.Stall);
                slot.Itemstack = new ItemStack(roasted, stack.StackSize);
            }
            slot.MarkDirty();
        }
        var (powders, carried) = StallRoasting.Sulfur(_sulfurCarry, sulfurUnits, recovery.SulfurUnitsPerPowder);
        _sulfurCarry = carried;
        if (powders > 0 && Api.World.GetItem(new AssetLocation(StallRoasting.SulfurPowder)) is { Id: > 0 } sulfur)
        {
            Inventory[SulfurSlot].Itemstack = new ItemStack(sulfur, powders);
            Inventory[SulfurSlot].MarkDirty();
        }
        _lit = false;
        _roasted = true;
        _burned = BurnHours;
        MarkDirty(true);
    }

    // ---- Fire ----

    public override string? CannotIgnite() =>
        !Built ? Lang.Get("seraphhorizons:stallroaster-error-notbuilt")
        : StallRoasting.CannotLight(Phase, _front) is var r and not StallRefusal.None ? RefusalText(r)
        : null;

    protected override void Light(IPlayer? byPlayer)
    {
        // the bed kindles the heap and burns away
        InfeedSlot.Itemstack = null;
        InfeedSlot.MarkDirty();
        _lit = true;
        _burned = 0;
        _syncedBurned = 0;
        _lastHours = Api.World.Calendar.TotalHours;
        Api.World.PlaySoundAt(new AssetLocation("game", "sounds/torch-ignite"), Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, byPlayer, true, 16, 0.6f);
        if (byPlayer != null)
            Api.World.Logger.Audit("{0} lit the stall roaster at {1} ({2} rock).", byPlayer.PlayerName, Pos, Rock);
    }

    // ---- Interaction ----

    private static string RefusalText(StallRefusal r, params object[] args) =>
        Lang.Get("seraphhorizons:stallroaster-error-" + r.ToString().ToLowerInvariant(), args);

    private bool Refuse(IPlayer byPlayer, StallRefusal r, params object[] args)
    {
        if (Api.Side == EnumAppSide.Server && byPlayer is IServerPlayer sp)
            sp.SendIngameError("stallroaster-" + r.ToString().ToLowerInvariant(), RefusalText(r, args));
        return true;
    }

    /// <summary>A sulfide's raw or crushed rock: the stall's charge.</summary>
    public bool IsCharge(ItemStack stack) =>
        OreStreamAttributes.Read(stack) is { } stream && OreProducts.IsGrade(stream.Grade)
        && StallRoasting.Takes(OreStreamAttributes.FormOf(stack.Collectible), OreProcessingSystem.RecoveryFor(Api).Ore(stream.Ore));

    /// <summary>
    /// Once built: firewood lays the bed, sulfide rock charges it, the stall's own fired brick walls the front up a
    /// place, an iron plate caps the outlet; an empty hand takes the crust's sulfur, then rakes the roasted rock out a
    /// course at a time, or on the outlet's top takes the cap off; with sneak, an empty hand takes the front down a
    /// place. Lighting is a firestarter's or a torch's (<see cref="Ignite"/>). Before it is built, the build's.
    /// </summary>
    protected override bool? InteractContent(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        if (!Built || StallRig is not { } rig)
            return null;
        var stack = slot?.Itemstack;
        bool server = Api.Side == EnumAppSide.Server;
        if (stack == null)
        {
            if (byPlayer.Entity.Controls.ShiftKey)
            {
                var (down, why) = StallRoasting.TakeDown(_front);
                if (why != StallRefusal.None)
                    return Refuse(byPlayer, why);
                if (server)
                {
                    _front = down;
                    Api.World.PlaySoundAt(Block.Sounds.Place, Pos, -0.25, byPlayer);
                    MarkDirty(true);
                }
                return true;
            }
            if (Capped && IsOutletTop(blockSel))
            {
                Uncap(byPlayer);
                return true;
            }
            if (Phase != StallPhase.Roasted)
                return null;
            if (!Inventory[SulfurSlot].Empty)
            {
                if (server)
                {
                    Give(byPlayer, Inventory[SulfurSlot].TakeOutWhole());
                    Inventory[SulfurSlot].MarkDirty();
                    MarkDirty(true);
                }
                return true;
            }
            if (StallRoasting.CannotRake(Phase, _front) is var rake and not StallRefusal.None)
                return Refuse(byPlayer, rake);
            if (server)
                RakeCourse(byPlayer, rig.PerCourse);
            return true;
        }

        string code = stack.Collectible.Code.ToString();
        if (code == StallRoasting.Firewood)
        {
            var (take, why) = StallRoasting.LayBed(Phase, _front, Firewood, stack.StackSize, StallConfig.BedFirewood);
            if (why != StallRefusal.None)
                return Refuse(byPlayer, why);
            if (server)
            {
                MoveInto(slot!, InfeedSlot, take);
                Api.World.PlaySoundAt(new AssetLocation("game", "sounds/block/planks"), Pos, -0.25, byPlayer);
                MarkDirty(true);
            }
            return true;
        }
        if (!Capped && code == rig.CapCode)
            return Cap(byPlayer, slot!);
        if (rig.Bricks.TypeOf(code) is { } type)
        {
            if (type != BrickType)
                return Refuse(byPlayer, StallRefusal.OtherBrick, stack.GetName());
            var (next, take, why) = StallRoasting.WallUp(_front, _frontBricks, stack.StackSize, rig.FrontHalf, rig.FrontWalled);
            if (why != StallRefusal.None)
            {
                var target = _front == StallFront.Open ? StallFront.Half : StallFront.Walled;
                return Refuse(byPlayer, why, StallRoasting.BricksFor(target, rig.FrontHalf, rig.FrontWalled) - _frontBricks);
            }
            if (server)
            {
                if (take > 0)
                {
                    slot!.TakeOut(take);
                    slot.MarkDirty();
                }
                _frontBricks += take;
                _front = next;
                Api.World.PlaySoundAt(Block.Sounds.Place, Pos, -0.25, byPlayer);
                MarkDirty(true);
            }
            return true;
        }
        if (IsCharge(stack))
        {
            var (take, why) = StallRoasting.Charge(Phase, _front, Firewood, Rock, stack.StackSize, StallConfig.BedFirewood, Capacity);
            if (why != StallRefusal.None)
                return Refuse(byPlayer, why, Capacity);
            if (server)
            {
                int moved = PutCharge(slot!, take);
                if (moved <= 0)
                    return Refuse(byPlayer, StallRefusal.Full, Capacity);
                Api.World.PlaySoundAt(RakeSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, byPlayer, true, 16, 0.5f);
                MarkDirty(true);
            }
            return true;
        }
        return null;
    }

    private void MoveInto(ItemSlot from, ItemSlot into, int count)
    {
        var op = new ItemStackMoveOperation(Api.World, EnumMouseButton.Right, 0, EnumMergePriority.AutoMerge, count);
        from.TryPutInto(into, ref op);
        from.MarkDirty();
        into.MarkDirty();
    }

    /// <summary>Puts <paramref name="count"/> of the held rock on the charge, merging with its kind; the number moved.</summary>
    private int PutCharge(ItemSlot from, int count)
    {
        int moved = 0;
        foreach (var target in ChargeSlots)
        {
            if (moved >= count || from.Empty)
                break;
            if (!target.Empty && !target.Itemstack.Equals(Api.World, from.Itemstack, GlobalConstants.IgnoredStackAttributes))
                continue;
            int before = from.StackSize;
            MoveInto(from, target, count - moved);
            moved += before - from.StackSize;
        }
        return moved;
    }

    /// <summary>Rakes the top course out to the player: <paramref name="perCourse"/> rock, the last put in first.</summary>
    private void RakeCourse(IPlayer byPlayer, int perCourse)
    {
        int left = perCourse;
        foreach (var slot in ChargeSlots.Reverse())
        {
            if (left <= 0)
                break;
            if (slot.Empty)
                continue;
            var taken = slot.TakeOut(Math.Min(left, slot.StackSize));
            left -= taken.StackSize;
            slot.MarkDirty();
            Give(byPlayer, taken);
        }
        if (Rock == 0 && Inventory[SulfurSlot].Empty)
        {
            _roasted = false;
            _burned = 0;
        }
        Api.World.PlaySoundAt(RakeSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, byPlayer, true, 16, 0.7f);
        MarkDirty(true);
    }

    public override WorldInteraction[] InteractionHelp(IPlayer forPlayer)
    {
        if (!Built || StallRig is not { } rig)
            return base.InteractionHelp(forPlayer);
        var world = Api.World;
        ItemStack[] Of(params string[] codes) => codes
            .Select(c => (CollectibleObject?)world.GetItem(new AssetLocation(c)) ?? world.GetBlock(new AssetLocation(c)))
            .OfType<CollectibleObject>().Where(c => c.Id > 0).Select(c => new ItemStack(c)).ToArray();
        var help = new List<WorldInteraction>();
        string Key(string k) => "seraphhorizons:stallroaster-help-" + k;
        switch (Phase)
        {
            case StallPhase.Empty or StallPhase.Bed:
                help.Add(new WorldInteraction { ActionLangCode = Key("bed"), MouseButton = EnumMouseButton.Right, Itemstacks = Of(StallRoasting.Firewood) });
                if (Phase == StallPhase.Bed && Firewood >= StallConfig.BedFirewood)
                    goto case StallPhase.Charged;
                break;
            case StallPhase.Charged:
                help.Add(new WorldInteraction
                {
                    ActionLangCode = Key("charge"), MouseButton = EnumMouseButton.Right,
                    Itemstacks = world.SearchItems(new AssetLocation(OreProducts.Domain, "crushedore-galena-*")).Take(4).Select(i => new ItemStack(i)).ToArray(),
                });
                help.Add(new WorldInteraction
                {
                    ActionLangCode = Key("light"), MouseButton = EnumMouseButton.Right,
                    Itemstacks = FurnaceFiring.IgniteStacks(world.Api),
                });
                break;
            case StallPhase.Roasted:
                help.Add(new WorldInteraction { ActionLangCode = Key(SulfurPowders > 0 ? "sulfur" : "rake"), MouseButton = EnumMouseButton.Right });
                break;
        }
        if (_front != StallFront.Walled && BrickType is { } type)
            help.Add(new WorldInteraction { ActionLangCode = Key("wall"), MouseButton = EnumMouseButton.Right, Itemstacks = Of(rig.Bricks.ItemOf(type)) });
        if (_front != StallFront.Open)
            help.Add(new WorldInteraction { ActionLangCode = Key("unwall"), MouseButton = EnumMouseButton.Right, HotKeyCode = "shift" });
        help.Add(Capped
            ? new WorldInteraction { ActionLangCode = Key("uncap"), MouseButton = EnumMouseButton.Right }
            : new WorldInteraction { ActionLangCode = Key("cap"), MouseButton = EnumMouseButton.Right, Itemstacks = Of(rig.CapCode) });
        return help.ToArray();
    }

    // ---- Drawing ----

    /// <summary>
    /// The stages as built; then the bay's contents by the stall's state: the bed (<c>wood</c>) laid and not lit, its
    /// burning (<c>kindling</c>) at the start of the burn, the charge's courses (<c>ore</c>, <c>fire</c> while lit,
    /// <c>roasted</c> once done, <c>ceil(rock / per course)</c> of them), the sulfur in the crust, the front's place
    /// and the cap.
    /// </summary>
    public override bool ShowsPart(string? requires)
    {
        if (requires is null or StallRequires.Brickwork or StallRequires.Lining)
            return base.ShowsPart(requires);
        if (!Built || StallRig is not { } rig)
            return false;
        switch (requires)
        {
            case StallRequires.Wood: return Firewood > 0 && !_lit && !_roasted;
            case StallRequires.Kindling: return _lit && StallRoasting.Kindling(_burned, BurnHours);
            case StallRequires.Sulfur: return _roasted && SulfurPowders > 0;
            case StallRequires.FrontHalf: return _front == StallFront.Half;
            case StallRequires.FrontWalled: return _front == StallFront.Walled;
            case StallRequires.OutletCap: return Capped;
        }
        int courses = StallRoasting.Courses(Rock, rig.PerCourse, rig.Courses);
        if (requires.StartsWith("roasted", StringComparison.Ordinal))
            return _roasted && Course(requires, "roasted") <= courses;
        if (requires.StartsWith("fire", StringComparison.Ordinal))
            return _lit && Course(requires, "fire") <= courses;
        if (requires.StartsWith("ore", StringComparison.Ordinal))
            return !_lit && !_roasted && Course(requires, "ore") <= courses;
        return false;
    }

    private static int Course(string requires, string prefix) =>
        int.TryParse(requires.AsSpan(prefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : int.MaxValue;

    // ---- Breaking ----

    /// <summary>The frame and every stage's items, the cap, and the front's loose brick. The bed, the charge and
    /// the sulfur drop with the inventory.</summary>
    public override IEnumerable<ItemStack> AssemblyDrops()
    {
        foreach (var drop in base.AssemblyDrops())
            yield return drop;
        if (StallRig is not { } rig)
            yield break;
        if (_frontBricks > 0 && BrickType is { } type && Api.World.GetItem(new AssetLocation(rig.Bricks.ItemOf(type))) is { Id: > 0 } brick)
            yield return new ItemStack(brick, _frontBricks);
    }

    // ---- Saving and syncing ----

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt("front", (int)_front);
        tree.SetInt("frontBricks", _frontBricks);
        tree.SetBool("lit", _lit);
        tree.SetBool("roasted", _roasted);
        tree.SetDouble("burned", _burned);
        tree.SetDouble("lastHours", _lastHours);
        tree.SetDouble("sulfurCarry", _sulfurCarry);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        _front = (StallFront)Math.Clamp(tree.GetInt("front"), 0, 2);
        _frontBricks = Math.Max(0, tree.GetInt("frontBricks"));
        _lit = tree.GetBool("lit");
        _roasted = tree.GetBool("roasted");
        _burned = Math.Max(0, tree.GetDouble("burned"));
        _lastHours = tree.GetDouble("lastHours", double.NaN);
        _sulfurCarry = Math.Max(0, tree.GetDouble("sulfurCarry"));
    }

    // ---- Info ----

    protected override void AppendContents(StringBuilder dsc)
    {
        if (!Built || StallRig is not { } rig)
            return;
        string L(string key, params object[] args) => Lang.Get("seraphhorizons:stallroaster-info-" + key, args);
        dsc.AppendLine(L("front-" + _front.ToString().ToLowerInvariant()));
        switch (Phase)
        {
            case StallPhase.Empty:
                dsc.AppendLine(L("empty", StallConfig.BedFirewood));
                break;
            case StallPhase.Bed:
                dsc.AppendLine(L("bed", Firewood, StallConfig.BedFirewood));
                break;
            case StallPhase.Charged:
                dsc.AppendLine(L("charged", Rock, Capacity));
                break;
            case StallPhase.Burning:
                dsc.AppendLine(L("burning", Rock, (int)Math.Floor(100 * (BurnHours > 0 ? _burned / BurnHours : 1)),
                    Math.Max(0, (BurnHours - _burned) / Math.Max(1e-6, Draught)).ToString("0", CultureInfo.InvariantCulture)));
                if (Draught <= 0)
                    dsc.AppendLine(L(Row.Works ? "nodraught-front" : "nodraught-chimney"));
                else if (Draught < 1)
                    dsc.AppendLine(L("halfdraught", (int)Math.Round(100 * Draught)));
                break;
            case StallPhase.Roasted:
                dsc.AppendLine(L("roasted", Rock, SulfurPowders));
                break;
        }
        dsc.AppendLine(RowLine());
    }
}
