using System.Globalization;
using System.Text;
using SeraphHorizons.Mod.CylinderBorer.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.CylinderBorer;

/// <summary>
/// The cylinder borer's controller (#639), on the shared machine base (<see cref="BEMachine"/>: its build from
/// <c>config/builds/cylinderborer.json</c> with the cutter head its durable wearing part, its cells and power
/// cell, its oil (<see cref="OilMachine.CylinderBorer"/>, whose dry tank wears the head, never the load:
/// <see cref="OilDryEffect.Wear"/>), its block info, its renderer). It keeps the casting in the cradle as its item
/// stack and the bore (<see cref="BoreJob"/>: W, the head's travel through it). The server bores from the power
/// cell's shaft angle, wears the head by the tank's fill when a cylinder is done, drains a cylinder's oil, drops
/// the bored cylinder at the output face or puts it in a container there, and takes the next casting from a chest
/// or hopper at the infeed face; the client poses the rig from the shaft and its own W
/// (<see cref="CylinderBorerClock"/>). The rules are CylinderBorer/Core's.
/// </summary>
public class BECylinderBorer : BEMachine
{
    private static readonly AssetLocation BreakSound = new("game", "sounds/effect/toolbreak");
    private static readonly AssetLocation LatchSound = new("game", "sounds/effect/latch");
    private static readonly AssetLocation CylinderSound = new("game", "sounds/block/ingot");
    private static readonly AssetLocation BoreSound = new("game", "sounds/effect/gears");
    // The server syncs W at least this often (blocks of travel); the renderer follows the shaft between.
    private const double SyncStep = 1.0 / 32;
    // After a cylinder is done the infeed waits this long, so the head eases back out of the empty
    // cradle (the model's presence) before the next casting goes in.
    private const long ClearMs = 600;
    private const float ChipInterval = 0.2f;
    private const float DripInterval = 0.45f;

    private ItemStack? _blank;
    private BoreJob _job = BoreJob.None;
    private long _finishedAt = long.MinValue / 2;
    private float _soundSeconds;
    private float? _serverTurnsIron;
    private float? _serverTurnsSteel;
    private int? _serverWearPerCylinder;
    private Dictionary<Int3, Cuboidf[]>? _cellsLoaded;
    private Dictionary<Int3, Cuboidf[]>? _collisionLoaded;
    private readonly CylinderBorerClock _clock = new();
    private double _lastPsi;
    private float _chipTimer;
    private float _dripTimer;

    public bool BlankOn => _job.On && _blank != null;
    public ItemStack? Blank => _blank;
    public BoreJob Job => _job;

    /// <summary>The fitted cutter head: its code and durability left; null without one (taken out, or spent).</summary>
    public (string Code, double Left)? Head => Assembly?.Wearing.Fitted(CylinderBorerBuild.HeadPart);

    /// <summary>The head's durability left; 0 without a head.</summary>
    public int HeadLeft => Head is { } h ? (int)Math.Round(h.Left) : 0;

    public double TurnsPerBlock(int k) => k switch
    {
        1 => _serverTurnsIron ?? Config.TurnsPerBlockIron,
        2 => _serverTurnsSteel ?? Config.TurnsPerBlockSteel,
        _ => 0,
    };
    private int WearPerCylinder => _serverWearPerCylinder ?? Config.HeadWearPerCylinder;

    private CylinderBorerConfig Config => CylinderBorerSystem.Of(Api).Config;
    private new CylinderBorerRig? Rig => base.Rig as CylinderBorerRig;

    /// <summary>The head's uses are the head item's durability.</summary>
    protected override MachineBuild? BuildSpec(IWorldAccessor world) =>
        base.BuildSpec(world) is { } spec && world.GetItem(new AssetLocation(CylinderBorerBuild.HeadCode)) is { Durability: > 0 } head
            ? spec.WithUses(CylinderBorerBuild.HeadPart, head.Durability)
            : base.BuildSpec(world);

    protected override OilMachine? OilKind => OilMachine.CylinderBorer;
    protected override OilDryEffect DryEffect => OilDryEffect.Wear;
    protected override float ConfigMinSpeed => Config.MinSpeed;
    protected override int TickMs => 50;
    protected override int ClientTickMs => 200;
    public override int RenderRange => 48;

    /// <summary>Whole with its cutter head in: until then it puts the token load on its shaft.</summary>
    public override bool Complete => Ready;
    protected override bool LoadsShaft => Ready;
    protected override bool CreativeFills => !Ready;

    /// <summary>The configured resistance, oiled or dry: its oil spares the cutter head, never its load.</summary>
    protected override double ShaftLoad => Config.Resistance;

    public override bool HasWork => BlankOn;
    public override double? WorkProgress => BlankOn ? _job.Work / Boring.Travel : null;
    protected override Float3? SmokePoint => Rig?.Chips;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        BuildCastingBoxes();
        // once a second, the infeed
        if (api.Side == EnumAppSide.Server)
            RegisterGameTickListener(_ => PullFromInfeed(), 1000);
    }

    // ---- The cells' boxes: the casting's among them while one is in the cradle ----

    public override Cuboidf[]? CellBoxes(BlockPos cellPos) =>
        BlankOn && _cellsLoaded?.GetValueOrDefault(LocalOf(cellPos)) is { } loaded ? loaded : base.CellBoxes(cellPos);

    public override Cuboidf[]? CollisionBoxes(BlockPos cellPos) =>
        BlankOn && _collisionLoaded?.GetValueOrDefault(LocalOf(cellPos)) is { } loaded ? loaded : base.CollisionBoxes(cellPos);

    // The boxes never change: built once, then only read (collision lookups can run off the main thread). The
    // loaded tables add the casting's box, cut into the cells it crosses, to the cells' own.
    private void BuildCastingBoxes()
    {
        if (Rig is not { } rig)
            return;
        var side = Side;
        var cells = new Dictionary<Int3, Cuboidf[]>();
        var collision = new Dictionary<Int3, Cuboidf[]>();
        foreach (var (cell, box) in rig.CastingByCell())
        {
            var w = Footprint.ToWorld(box, side);
            var cast = new Cuboidf(w.X1, w.Y1, w.Z1, w.X2, w.Y2, w.Z2);
            var at = CellPos(cell);
            cells[cell] = [.. base.CellBoxes(at) ?? [], cast];
            collision[cell] = [.. base.CollisionBoxes(at) ?? [], cast];
        }
        _cellsLoaded = cells;
        _collisionLoaded = collision;
    }

    // ---- Interaction ----

    /// <summary>Ctrl + right-click takes back the cutter head, unworn by it, else the casting in the cradle (the
    /// creative shortcut comes first while a stage or the head is missing).</summary>
    protected override bool? InteractContent(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        var controls = byPlayer.Entity.Controls;
        if (!controls.CtrlKey || controls.ShiftKey || CreativeShortcut(byPlayer))
            return null;
        if (Api.Side == EnumAppSide.Server)
            TakeBack(byPlayer);
        return true;
    }

    /// <summary>An iron or steel cylinder casting goes in the cradle (a bored cylinder, what comes off, never does).</summary>
    protected override bool InteractWork(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        if (Boring.ClassOfBlank(slot.Itemstack?.Collectible?.Code?.ToString()) == 0)
            return false;
        if (Api.Side == EnumAppSide.Server)
            TryLoadBlank(slot, byPlayer);
        return true;
    }

    /// <summary>A stage fitted or the head renewed: the infeed may have a casting for it.</summary>
    protected override void OnAssemblyChanged() => PullFromInfeed();

    /// <summary>Ctrl + right-click (server side): the cutter head, with the durability it has left
    /// (taking it back wears it no further), else the casting in the cradle, its bore lost.</summary>
    public bool TakeBack(IPlayer byPlayer)
    {
        if (Assembly is not { } assembly)
            return false;
        switch (CylinderBorerBuild.TakeBack(assembly, BlankOn))
        {
            case CylinderBorerTakeBack.Head:
                if (assembly.Take(CylinderBorerBuild.HeadPart) is not { } head || StackOf(new StageDrop(head.Code, 1, head.Left)) is not { } stack)
                    return false;
                Give(byPlayer, stack);
                break;
            case CylinderBorerTakeBack.Blank:
                var blank = _blank!;
                ClearJob();
                Give(byPlayer, blank);
                break;
            default:
                return false;
        }
        Api.World.PlaySoundAt(LatchSound, Pos, 0, byPlayer);
        MarkDirty(true);
        return true;
    }

    /// <summary>The bored cylinder a casting of class <paramref name="k"/> becomes, or null when it
    /// does not exist in this game.</summary>
    private Item? CylinderItem(int k) =>
        Boring.CylinderFor(k) is { } code && Api.World.GetItem(new AssetLocation(code)) is { Id: > 0, IsMissing: false } item ? item : null;

    /// <summary>Puts a casting from <paramref name="slot"/> in the cradle, if the borer takes it now.</summary>
    public bool TryLoadBlank(ItemSlot slot, IPlayer? byPlayer)
    {
        string? code = slot.Itemstack?.Collectible?.Code?.ToString();
        switch (Boring.CanLoad(code, Ready, BlankOn))
        {
            case BoreLoadVerdict.Loads:
                break;
            case BoreLoadVerdict.Incomplete:
                return Say(byPlayer, Built ? "error-no-head" : "error-incomplete");
            case BoreLoadVerdict.Occupied:
                return Say(byPlayer, "error-occupied");
            default:
                return false;
        }
        if (CylinderItem(Boring.ClassOfBlank(code)) == null)
            return Say(byPlayer, "error-no-cylinder");
        Load(slot.TakeOut(1));
        slot.MarkDirty();
        return true;
    }

    private void Load(ItemStack blank)
    {
        _blank = blank;
        _job = new BoreJob(Boring.ClassOfBlank(blank.Collectible.Code.ToString()), 0);
        Api.World.PlaySoundAt(LatchSound, Pos, 0);
        MarkDirty(true);
    }

    private void ClearJob()
    {
        _blank = null;
        _job = BoreJob.None;
    }

    private void Give(IPlayer byPlayer, ItemStack stack)
    {
        if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 1.2, 0.5));
    }

    /// <summary>The borer's own message (<c>seraphhorizons:cylinderborer-{key}</c>) to the player; false.</summary>
    private bool Say(IPlayer? byPlayer, string key, params object[] args)
    {
        if (byPlayer is IServerPlayer sp)
            sp.SendIngameError("cylinderborer-" + key, Lang.GetL(sp.LanguageCode, CylinderBorerSystem.Domain + ":cylinderborer-" + key, args));
        return false;
    }

    /// <summary>The help once built: a casting in an empty cradle, and what Ctrl takes back.</summary>
    protected override void WorkHelp(IPlayer? forPlayer, List<WorldInteraction> help)
    {
        string Key(string action) => CylinderBorerSystem.Domain + ":blockhelp-cylinderborer-" + action;
        if (Ready && !BlankOn)
        {
            var blanks = Stacks(Api.World, [Boring.BlankIron, Boring.BlankSteel], 1);
            if (blanks.Length > 0)
                help.Add(new WorldInteraction { ActionLangCode = Key("loadblank"), MouseButton = EnumMouseButton.Right, Itemstacks = blanks });
        }
        string? take = Assembly is { } a ? CylinderBorerBuild.TakeBack(a, BlankOn) switch
        {
            CylinderBorerTakeBack.Head => "takehead",
            CylinderBorerTakeBack.Blank => "takeblank",
            _ => null,
        } : null;
        bool creative = forPlayer?.WorldData?.CurrentGameMode == EnumGameMode.Creative;
        if (take != null && !(creative && CreativeFills))
            help.Add(new WorldInteraction { ActionLangCode = Key(take), MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" });
    }

    // ---- The bore ----

    // The bore syncs itself as it goes (SyncStep), so the base's full sync is not asked for.
    protected override bool Work(double radians)
    {
        Bore(radians);
        return false;
    }

    /// <summary>Bores by <paramref name="radians"/> of shaft rotation (server side; call only while
    /// running). At the end of the traverse the head wears by the oil's fill, the tank drains, the
    /// cylinder goes out and, a moment later, the next casting comes in. Returns whether a cylinder
    /// was finished.</summary>
    public bool Bore(double radians)
    {
        if (!BlankOn || !Ready)
            return false;
        var before = _job;
        (_job, bool finished) = _job.Advance(radians, Config.TurnsPerBlock(_job.Class));
        if (finished)
        {
            Finish();
            return true;
        }
        if (Math.Floor(before.Work / SyncStep) != Math.Floor(_job.Work / SyncStep))
            MarkDirty();
        return false;
    }

    /// <summary>The cylinder is bored: the head wears by the fill the tank has now, then the tank
    /// drains a cylinder's oil; a spent head breaks with the tool-break sound and the borer stops
    /// until a new one goes in. The bored cylinder goes into a container at the output face, else
    /// drops there; the infeed waits a moment before the next casting.</summary>
    private void Finish()
    {
        int k = _job.Class;
        int wear = CutterHeadWear.WearFor(Config.HeadWearPerCylinder, OilFill, HeadLeft);
        DrainOil(1);
        if (Assembly!.Wearing.Wear(CylinderBorerBuild.HeadPart, wear))
            Api.World.PlaySoundAt(BreakSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5);
        ClearJob();
        if (CylinderItem(k) is { } cylinder)
            Deliver(new ItemStack(cylinder));
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
        Api.World.PlaySoundAt(CylinderSound, at.X, at.Y, at.Z);
    }

    /// <summary>A complete borer with its cradle empty and its shaft turning takes one casting from a
    /// container at the infeed face, a moment after the last cylinder was done. Returns whether one
    /// went in.</summary>
    public bool PullFromInfeed()
    {
        if (Api.Side != EnumAppSide.Server || BlankOn || !Ready || Math.Abs(ShaftSpeed) < MinSpeed || Rig is not { } rig)
            return false;
        if (Api.World.ElapsedMilliseconds - _finishedAt < ClearMs)
            return false;
        foreach (var local in rig.InfeedNeighbours())
        {
            if (Api.World.BlockAccessor.GetBlockEntity(CellPos(local)) is not BlockEntityContainer container)
                continue;
            foreach (var slot in container.Inventory)
            {
                string? code = slot.Itemstack?.Collectible?.Code?.ToString();
                if (Boring.CanLoad(code, true, false) != BoreLoadVerdict.Loads || CylinderItem(Boring.ClassOfBlank(code)) == null)
                    continue;
                Load(slot.TakeOut(1));
                slot.MarkDirty();
                container.MarkDirty(true);
                RefreshState();
                return true;
            }
        }
        return false;
    }

    // A boring borer is heard.
    protected override void OnClientTick(float dt)
    {
        if (Api is not ICoreClientAPI capi || Rig is not { } rig || !Running)
        {
            _soundSeconds = 0;
            return;
        }
        if ((_soundSeconds -= dt) <= 0)
        {
            _soundSeconds = 2.4f;
            var at = WorldPoint(rig.Chips);
            capi.World.PlaySoundAt(BoreSound, at.X, at.Y, at.Z, null, true, 16, 0.4f);
        }
    }

    // ---- The renderer's ----

    /// <summary>The castings (<c>blankiron</c>, <c>blanksteel</c>) as the clock shows them; every other part as
    /// its stage (and the cutter head, its part) is fitted.</summary>
    public override bool ShowsPart(string? requires) =>
        requires is CylinderBorerRequires.BlankIron or CylinderBorerRequires.BlankSteel ? _clock.ShowsBlank(requires) : base.ShowsPart(requires);

    /// <summary>θ and ψ the shaft's (about the entry shaft's native z axis), W, k and p from the clock, and the
    /// oil's fill for the oil cups' level; while the borer cuts, metal chips at the bore's drive end and, with oil
    /// in the tank, drips from the far bushing's oil cup.</summary>
    public override RigInput FrameInput(float dt, bool far)
    {
        var (theta, psi) = AdvanceShaft();
        double turned = Math.Max(0, psi - _lastPsi);
        _lastPsi = psi;
        int k = BlankOn ? _job.Class : 0;
        bool running = Running;
        _clock.Advance(dt, turned, k, _job.Work, running, TurnsPerBlock(k));
        if (!far && running && Rig is { } rig)
        {
            if ((_chipTimer += dt) >= ChipInterval)
            {
                _chipTimer = 0;
                SpawnChips(rig, k);
            }
            if (OilFill > 0 && (_dripTimer += dt) >= DripInterval)
            {
                _dripTimer = 0;
                SpawnDrip(rig);
            }
        }
        return _clock.Input(theta, psi, OilFill);
    }

    private void SpawnChips(CylinderBorerRig rig, int k)
    {
        var at = WorldPoint(rig.Chips);
        // turnings curling out of the bore's mouth: iron dark grey, steel bluish
        int colour = k == 2 ? ColorUtil.ToRgba(230, 150, 145, 130) : ColorUtil.ToRgba(230, 95, 100, 105);
        Api.World.SpawnParticles(2, colour,
            at.AddCopy(-0.08, -0.08, -0.03), at.AddCopy(0.08, 0.08, 0.03),
            new Vec3f(-0.4f, 0f, -0.4f), new Vec3f(0.4f, 0.4f, 0.4f), 0.8f, 1f, 0.2f, EnumParticleModel.Cube, null);
    }

    private void SpawnDrip(CylinderBorerRig rig)
    {
        var at = WorldPoint(rig.Drip);
        Api.World.SpawnParticles(1, ColorUtil.ToRgba(190, 40, 140, 190),
            at.AddCopy(-0.01, -0.02, -0.01), at.AddCopy(0.01, 0, 0.01),
            new Vec3f(0, -0.4f, 0), new Vec3f(0, -0.2f, 0), 0.6f, 1f, 0.12f, EnumParticleModel.Quad, null);
    }

    // ---- Breaking ----

    /// <summary>What breaking the frame gives besides the frame: every fitted part (the cutter head
    /// with its durability) and the casting in the cradle, its bore lost.</summary>
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
            tree.SetFloat("turnsPerBlockIron", Config.TurnsPerBlockIron);
            tree.SetFloat("turnsPerBlockSteel", Config.TurnsPerBlockSteel);
            tree.SetInt("wearPerCylinder", Config.HeadWearPerCylinder);
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        _blank = tree.GetItemstack("blank");
        if (_blank != null && !_blank.ResolveBlockOrItem(worldForResolving))
            _blank = null;
        _job = _blank == null ? BoreJob.None : BoreJob.Restore(tree.GetInt("class"), tree.GetDouble("work"));
        if (!_job.On)
            _blank = null;
        if (worldForResolving.Side == EnumAppSide.Client)
        {
            _serverTurnsIron = tree.TryGetFloat("turnsPerBlockIron");
            _serverTurnsSteel = tree.TryGetFloat("turnsPerBlockSteel");
            _serverWearPerCylinder = tree.TryGetInt("wearPerCylinder");
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

    /// <summary>The cylinders the head has left at this oil level (and, dry, that the next breaks it), and the
    /// casting in the cradle.</summary>
    protected override void AppendContents(StringBuilder dsc)
    {
        string L(string key, params object[] args) => Lang.Get(CylinderBorerSystem.Domain + ":cylinderborer-" + key, args);
        if (Head != null)
            dsc.AppendLine(Oiling != null && OilFill <= 0
                ? L("info-wear-dry")
                : L("info-head", CutterHeadWear.CylindersLeft(WearPerCylinder, OilFill, HeadLeft)));
        if (BlankOn)
            dsc.AppendLine(L("info-blank", L("metal-" + Boring.MetalOf(_job.Class))));
    }

    /// <summary>For a wrench: the head's wear a cylinder over the oil's fill, the bore's W exactly, and the shaft
    /// turns a block of travel takes.</summary>
    protected override void AppendDetail(StringBuilder dsc)
    {
        string L(string key, params object[] args) => Lang.Get(CylinderBorerSystem.Domain + ":cylinderborer-" + key, args);
        if (Oiling != null && Head != null && OilFill > 0)
            dsc.AppendLine(L("info-wear", CutterHeadWear.Multiplier(OilFill).ToString("0.#", CultureInfo.InvariantCulture)));
        if (BlankOn)
            dsc.AppendLine(L("info-bore", _job.Work.ToString("0.000", CultureInfo.InvariantCulture),
                Boring.Travel.ToString("0.###", CultureInfo.InvariantCulture),
                TurnsPerBlock(_job.Class).ToString("0.###", CultureInfo.InvariantCulture)));
    }
}
