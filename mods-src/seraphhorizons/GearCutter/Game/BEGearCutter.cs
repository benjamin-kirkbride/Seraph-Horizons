using System.Globalization;
using System.Text;
using SeraphHorizons.Mod.GearCutter.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.GearCutter;

/// <summary>
/// The gear cutter's controller (#480, #481), on the shared machine base (<see cref="BEMachine"/>: its build
/// from <c>config/builds/gearcutter.json</c> with the cutter kit its durable wearing part and the master its last
/// stage, its cells and power cell, its oil (<see cref="OilMachine.GearCutter"/>, whose dry tank wears the kit,
/// never the load: <see cref="OilDryEffect.Wear"/>), its block info, its renderer). It keeps the blank on the
/// arbor as its item stack and the cut (<see cref="MachineJob"/>: W, the teeth cut). The server cuts from the power
/// cell's shaft angle, wears the kit by the tank's fill when a gear is done, drains a gear's oil and drops the
/// gear at the output face; the client poses the rig from the shaft and its own W (<see cref="GearCutterClock"/>).
/// The rules are GearCutter/Core's.
/// </summary>
public class BEGearCutter : BEMachine
{
    private static readonly AssetLocation BreakSound = new("game", "sounds/effect/toolbreak");
    private static readonly AssetLocation LatchSound = new("game", "sounds/effect/latch");
    private static readonly AssetLocation GearSound = new("game", "sounds/block/ingot");
    // The server syncs W at least this often (teeth); the renderer follows the shaft between.
    private const double SyncStep = 0.1;
    private const float ChipInterval = 0.12f;
    private const float SprayInterval = 0.2f;

    private ItemStack? _blank;
    private MachineJob _job = MachineJob.None;
    private float? _serverTurnsPerTooth;
    private int? _serverWearPerGear;
    private readonly GearCutterClock _clock = new();
    private double _lastPsi;
    private readonly ParticleTimer _chips = new(ChipInterval);
    private readonly ParticleTimer _spray = new(SprayInterval);

    public bool BlankOn => _job.On && _blank != null;
    public ItemStack? Blank => _blank;
    public MachineJob Job => _job;

    /// <summary>The master's class: 1 small, 2 large, 0 none.</summary>
    public int Master => Assembly is { } a ? GearCutterBuild.Master(a) : 0;

    /// <summary>The fitted kit: its code and durability left; null without one (taken out, or spent).</summary>
    public (string Code, double Left)? Kit => Assembly?.Wearing.Fitted(GearCutterBuild.KitPart);

    /// <summary>The kit's durability left; 0 without a kit.</summary>
    public int KitLeft => Kit is { } k ? (int)Math.Round(k.Left) : 0;

    public float TurnsPerTooth => _serverTurnsPerTooth ?? Config.TurnsPerTooth;
    private int WearPerGear => _serverWearPerGear ?? Config.CutterWearPerGear;

    private GearCutterConfig Config => GearCutterSystem.Of(Api).Config;
    private new GearCutterRig? Rig => base.Rig as GearCutterRig;

    /// <summary>The kit's uses are the kit item's durability.</summary>
    protected override MachineBuild? BuildSpec(IWorldAccessor world) =>
        base.BuildSpec(world) is { } spec && world.GetItem(new AssetLocation(GearCutterBuild.KitCode)) is { Durability: > 0 } kit
            ? spec.WithUses(GearCutterBuild.KitPart, kit.Durability)
            : base.BuildSpec(world);

    protected override OilMachine? OilKind => OilMachine.GearCutter;
    protected override OilDryEffect DryEffect => OilDryEffect.Wear;
    protected override float ConfigMinSpeed => Config.MinSpeed;
    protected override int TickMs => 50;
    protected override int ClientTickMs => 200;
    public override int RenderRange => 48;

    /// <summary>Whole with its kit in: until then it puts the token load on its shaft (the master is a stage, so
    /// <see cref="BEMachine.Built"/> already needs it).</summary>
    public override bool Complete => Ready;
    protected override bool LoadsShaft => Ready;
    protected override bool CreativeFills => !Ready;

    /// <summary>The configured resistance, oiled or dry (#481: its oil wears the kit, never the load).</summary>
    protected override double ShaftLoad => Config.Resistance;

    public override bool HasWork => BlankOn;
    public override double? WorkProgress => BlankOn ? _job.Work / _job.End : null;
    protected override Float3? SmokePoint => Rig?.Chips;

    // ---- Interaction ----

    /// <summary>Ctrl + right-click takes back the kit, else the blank on the arbor, else the master (the creative
    /// shortcut comes first while a stage or the kit is missing). A kit held while the master is off goes on
    /// after it, so the click says so.</summary>
    protected override bool? InteractContent(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        var controls = byPlayer.Entity.Controls;
        if (controls.CtrlKey && !controls.ShiftKey && !CreativeShortcut(byPlayer))
        {
            if (Api.Side == EnumAppSide.Server)
                TakeBack(byPlayer);
            return true;
        }
        if (Assembly is { Built: false } a && Kit == null && GearCutterBuild.MasterIndex(a.Build) == a.Build.Next
            && CodePattern.Normalise(slot.Itemstack?.Collectible?.Code?.ToString()) == GearCutterBuild.KitCode)
        {
            if (Api.Side == EnumAppSide.Server)
                base.Error(byPlayer, MachineText.Error(new AssemblyClick(AssemblyVerdict.OutOfOrder, 0, -1), Api.World, Machine, a), null);
            return true;
        }
        return null;
    }

    /// <summary>A stainless gear blank of the master's size goes on the arbor.</summary>
    protected override bool InteractWork(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        if (GearCut.ClassOfBlank(slot.Itemstack?.Collectible?.Code?.ToString()) == 0)
            return false;
        if (Api.Side == EnumAppSide.Server)
            TryLoadBlank(slot, byPlayer);
        return true;
    }

    /// <summary>Ctrl + right-click (server side): the kit (with what durability it has left), else
    /// the blank on the arbor (its cut lost), else the master.</summary>
    public bool TakeBack(IPlayer byPlayer)
    {
        if (Assembly is not { } assembly)
            return false;
        switch (GearCutterBuild.TakeBack(assembly, BlankOn))
        {
            case GearCutterTakeBack.Kit:
                if (assembly.Take(GearCutterBuild.KitPart) is not { } kit || StackOf(new StageDrop(kit.Code, 1, kit.Left)) is not { } stack)
                    return false;
                Give(byPlayer, stack);
                break;
            case GearCutterTakeBack.Blank:
                var blank = _blank!;
                ClearBlank();
                Give(byPlayer, blank);
                break;
            case GearCutterTakeBack.Master:
                if (GearCutterBuild.TakeMaster(assembly) is not { } code || StackOf(new StageDrop(code, 1)) is not { } master)
                    return false;
                Give(byPlayer, master);
                break;
            default:
                return false;
        }
        Api.World.PlaySoundAt(LatchSound, Pos, 0, byPlayer);
        MarkDirty(true);
        return true;
    }

    /// <summary>Puts a blank from <paramref name="slot"/> on the arbor, if the cutter takes it now.</summary>
    public bool TryLoadBlank(ItemSlot slot, IPlayer? byPlayer)
    {
        string? code = slot.Itemstack?.Collectible?.Code?.ToString();
        switch (GearCut.CanLoad(code, Ready, BlankOn, Master))
        {
            case GearCutterBlankVerdict.Loads:
                break;
            case GearCutterBlankVerdict.Incomplete:
                return Say(byPlayer, Built ? "error-no-kit" : "error-incomplete");
            case GearCutterBlankVerdict.Occupied:
                return Say(byPlayer, "error-occupied");
            case GearCutterBlankVerdict.WrongSize:
                return Say(byPlayer, Master == 1 ? "error-wrong-size-small" : "error-wrong-size-large");
            default:
                return false;
        }
        Load(slot.TakeOut(1));
        slot.MarkDirty();
        return true;
    }

    private void Load(ItemStack blank)
    {
        _blank = blank;
        _job = GearCut.Start(GearCut.ClassOfBlank(blank.Collectible.Code.ToString()));
        Api.World.PlaySoundAt(LatchSound, Pos, 0);
        MarkDirty(true);
    }

    private void ClearBlank()
    {
        _blank = null;
        _job = MachineJob.None;
    }

    private void Give(IPlayer byPlayer, ItemStack stack)
    {
        if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 1.2, 0.5));
    }

    /// <summary>The cutter's own message (<c>seraphhorizons:gearcutter-{key}</c>) to the player; false.</summary>
    private bool Say(IPlayer? byPlayer, string key, params object[] args)
    {
        if (byPlayer is IServerPlayer sp)
            sp.SendIngameError("gearcutter-" + key, Lang.GetL(sp.LanguageCode, GearCutterSystem.Domain + ":gearcutter-" + key, args));
        return false;
    }

    /// <summary>The help once built: a blank of the master's size on an empty arbor, and what Ctrl takes back.</summary>
    protected override void WorkHelp(IPlayer? forPlayer, List<WorldInteraction> help)
    {
        string Key(string action) => GearCutterSystem.Domain + ":blockhelp-gearcutter-" + action;
        if (Ready && !BlankOn && GearCut.BlankFor(Master) is { } blankCode)
        {
            var blanks = Stacks(Api.World, [blankCode], 1);
            if (blanks.Length > 0)
                help.Add(new WorldInteraction { ActionLangCode = Key("loadblank"), MouseButton = EnumMouseButton.Right, Itemstacks = blanks });
        }
        string? take = Assembly is { } a ? GearCutterBuild.TakeBack(a, BlankOn) switch
        {
            GearCutterTakeBack.Kit => "takekit",
            GearCutterTakeBack.Blank => "takeblank",
            GearCutterTakeBack.Master => "takemaster",
            _ => null,
        } : null;
        bool creative = forPlayer?.WorldData?.CurrentGameMode == EnumGameMode.Creative;
        if (take != null && !(creative && CreativeFills))
            help.Add(new WorldInteraction { ActionLangCode = Key(take), MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" });
    }

    // ---- The cut ----

    // The cut syncs itself as it goes (SyncStep), so the base's full sync is not asked for.
    protected override bool Work(double radians)
    {
        Cut(radians);
        return false;
    }

    /// <summary>Cuts by <paramref name="radians"/> of shaft rotation (server side; call only while
    /// running). At the end of the gear the kit wears by the oil's fill, the tank drains, and the gear
    /// goes out. Returns whether a gear was finished.</summary>
    public bool Cut(double radians)
    {
        if (!BlankOn || !Ready)
            return false;
        var before = _job;
        (_job, bool finished) = GearCut.Advance(_job, radians, Config.TurnsPerTooth);
        if (finished)
        {
            Finish();
            return true;
        }
        if (Math.Floor(before.Work / SyncStep) != Math.Floor(_job.Work / SyncStep))
            MarkDirty();
        return false;
    }

    /// <summary>The gear is cut: the kit wears by the fill the tank has now (#481), then the tank
    /// drains (double for a large gear); a spent kit breaks with the tool-break sound. The gear drops
    /// at the output face.</summary>
    private void Finish()
    {
        int k = _job.Class;
        int wear = GearCutterWear.WearFor(Config.CutterWearPerGear, GearCut.Teeth(k), OilFill, KitLeft);
        DrainOil(k == 2 ? 2 : 1);
        if (Assembly!.Wearing.Wear(GearCutterBuild.KitPart, wear))
            Api.World.PlaySoundAt(BreakSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5);
        ClearBlank();
        if (GearCut.GearFor(k) is { } code && Api.World.GetItem(new AssetLocation(code)) is { } gear)
            Deliver(new ItemStack(gear));
        MarkDirty(true);
    }

    /// <summary>Drops <paramref name="stack"/> just beyond the output face.</summary>
    public void Deliver(ItemStack stack)
    {
        if (Rig is not { } rig)
            return;
        var at = WorldPoint(rig.OutputDrop());
        var n = Footprint.ToWorld(rig.OutputSide, Side).Normal();
        Api.World.SpawnItemEntity(stack, at, new Vec3d(n.X * 0.05, 0.02, n.Z * 0.05));
        Api.World.PlaySoundAt(GearSound, at.X, at.Y, at.Z);
    }

    // ---- The renderer's ----

    /// <summary>Each blank while it is on the arbor; every other part as its stage (the cutter, its kit) is
    /// fitted, each master while it is the one fitted, and the cover always.</summary>
    public override bool ShowsPart(string? requires) => requires switch
    {
        GearCutterRequires.BlankSmall => BlankOn && _job.Class == 1,
        GearCutterRequires.BlankLarge => BlankOn && _job.Class == 2,
        GearCutterRequires.Cover => true,
        _ => base.ShowsPart(requires),
    };

    /// <summary>θ and ψ the shaft's (about the entry shaft's native x axis), W, k and p from the clock, and the
    /// oil's fill for the reservoir's level; while the cut runs, chips and sparks at the cutter and, with oil in
    /// the tank, a spray from the injection valve's nozzle.</summary>
    public override RigInput FrameInput(float dt, bool far)
    {
        var (theta, psi) = AdvanceShaft();
        double turned = Math.Max(0, psi - _lastPsi);
        _lastPsi = psi;
        bool running = Running;
        _clock.Advance(dt, turned, Master, BlankOn, _job.Work, (int)_job.End, running, TurnsPerTooth);
        if (!far && running && Rig is { } rig)
        {
            if (_chips.Due(dt))
                SpawnChips(rig);
            if (OilFill > 0 && _spray.Due(dt))
                SpawnSpray(rig);
        }
        return _clock.Input(theta, psi, OilFill);
    }

    private void SpawnChips(GearCutterRig rig)
    {
        var at = WorldPoint(rig.Chips);
        // steel chips, and now and then a spark
        MachineParticles.Chips(ColorUtil.ToRgba(255, 150, 150, 160)).Spawn(Api.World, at, MachineParticles.Around(0.04));
        if (Api.World.Rand.NextDouble() < 0.3)
            new Puff(1, ColorUtil.ToRgba(255, 60, 190, 255), new Vec3f(-1.5f, 0.2f, -1.5f), new Vec3f(1.5f, 1.2f, 1.5f), 0.3f, 1f, 0.15f)
                .SpawnBetween(Api.World, at, at.AddCopy(0.02, 0.02, 0.02));
    }

    private void SpawnSpray(GearCutterRig rig)
    {
        var at = WorldPoint(rig.Drip);
        (MachineParticles.Drip(ColorUtil.ToRgba(170, 40, 140, 190)) with
        {
            Count = 2, MinVelocity = new Vec3f(-0.15f, -0.6f, -0.15f), MaxVelocity = new Vec3f(0.15f, -0.3f, 0.15f), Life = 0.5f, Size = 0.2f,
        }).SpawnBetween(Api.World, at.AddCopy(-0.02, -0.05, -0.02), at.AddCopy(0.02, 0, 0.02));
    }

    // ---- Breaking ----

    /// <summary>What breaking the frame gives besides the frame: every fitted part (the kit with its
    /// durability) and the blank on the arbor.</summary>
    public override IEnumerable<ItemStack> AssemblyDrops()
    {
        foreach (var stack in base.AssemblyDrops())
            yield return stack;
        if (BlankOn)
            yield return _blank!.Clone();
    }

    // ---- Saving and syncing ----

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        if (BlankOn)
            tree.SetItemstack("blank", _blank);
        else
            tree.RemoveAttribute("blank");
        tree.SetInt("class", _job.Class);
        tree.SetDouble("work", _job.Work);
        if (Api?.Side == EnumAppSide.Server)
        {
            tree.SetFloat("turnsPerTooth", Config.TurnsPerTooth);
            tree.SetInt("wearPerGear", Config.CutterWearPerGear);
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        _blank = tree.GetItemstack("blank");
        if (_blank != null && !_blank.ResolveBlockOrItem(worldForResolving))
            _blank = null;
        _job = _blank == null ? MachineJob.None : GearCut.Restore(tree.GetInt("class"), tree.GetDouble("work"));
        if (!_job.On)
            _blank = null;
        if (worldForResolving.Side == EnumAppSide.Client)
        {
            _serverTurnsPerTooth = tree.TryGetFloat("turnsPerTooth");
            _serverWearPerGear = tree.TryGetInt("wearPerGear");
        }
    }

    public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping, Dictionary<int, AssetLocation> itemIdMapping)
    {
        base.OnStoreCollectibleMappings(blockIdMapping, itemIdMapping);
        _blank?.Collectible.OnStoreCollectibleMappings(Api.World, new DummySlot(_blank), blockIdMapping, itemIdMapping);
    }

    public override void OnLoadCollectibleMappings(IWorldAccessor worldForResolve, Dictionary<int, AssetLocation> oldBlockIdMapping,
                                                   Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
    {
        base.OnLoadCollectibleMappings(worldForResolve, oldBlockIdMapping, oldItemIdMapping, schematicSeed, resolveImports);
        _blank?.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForResolve);
    }

    // ---- Info ----

    /// <summary>The master and the size it cuts, the gears the kit has left at this oil level (and, dry, that the
    /// next breaks it), and the blank on the arbor with its teeth cut.</summary>
    protected override void AppendContents(StringBuilder dsc)
    {
        string L(string key, params object[] args) => Lang.Get(GearCutterSystem.Domain + ":gearcutter-" + key, args);
        if (Master is var k and > 0)
            dsc.AppendLine(L(k == 1 ? "info-master-small" : "info-master-large"));
        if (Kit != null)
            dsc.AppendLine(Oiling != null && OilFill <= 0
                ? L("info-wear-dry")
                : L("info-kit", GearCutterWear.GearsLeft(WearPerGear, GearCut.Teeth(Master is > 0 and var m ? m : 1), OilFill, KitLeft)));
        if (BlankOn)
            dsc.AppendLine(L("info-blank", Math.Floor(_job.Work).ToString("0", CultureInfo.InvariantCulture), _job.End));
    }

    /// <summary>For a wrench: the kit's wear a gear over the oil's fill, the cut's W exactly, and the shaft turns a
    /// tooth takes.</summary>
    protected override void AppendDetail(StringBuilder dsc)
    {
        string L(string key, params object[] args) => Lang.Get(GearCutterSystem.Domain + ":gearcutter-" + key, args);
        if (Oiling != null && Kit != null && OilFill > 0)
            dsc.AppendLine(L("info-wear", GearCutterWear.Multiplier(OilFill).ToString("0.#", CultureInfo.InvariantCulture)));
        if (BlankOn)
            dsc.AppendLine(L("info-cut", _job.Work.ToString("0.000", CultureInfo.InvariantCulture), _job.End,
                TurnsPerTooth.ToString("0.###", CultureInfo.InvariantCulture)));
    }
}
