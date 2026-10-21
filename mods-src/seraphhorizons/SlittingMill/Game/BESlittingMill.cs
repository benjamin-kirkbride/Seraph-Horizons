using System.Globalization;
using System.Text;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.SlittingMill.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.SlittingMill;

/// <summary>
/// The slitting mill's controller, on the shared machine base (<see cref="BEMachine"/>: its build from
/// <c>config/builds/slittingmill.json</c> with the knife, a cleaver, its durable wearing part, its cells and power
/// cell, its oil (<see cref="OilMachine.SlittingMill"/>), its block info, its renderer). It keeps the plate on the
/// feed table as its item stack and the slitting (<see cref="MachineJob"/>: W, knife strokes since the plate went
/// in, 0..5). The server slits from the power cell's shaft angle; at the hand-out (W = 5) the knife wears by the
/// plate's metal, the tank drains a plate's oil, and the plate's nails and strips go into a container at the
/// output face or drop there, and the next plate comes from a chest or hopper at the infeed face. The client poses
/// the rig from the shaft and its own W (<see cref="SlittingMillClock"/>): the push bar, the operator's, is drawn
/// by W like the rest of the job and needs no hand. The rules are SlittingMill/Core's.
/// </summary>
public class BESlittingMill : BEMachine
{
    private static readonly AssetLocation BreakSound = new("game", "sounds/effect/toolbreak");
    private static readonly AssetLocation LatchSound = new("game", "sounds/effect/latch");
    private static readonly AssetLocation PlateSound = new("game", "sounds/block/plate");
    private static readonly AssetLocation NailsSound = new("game", "sounds/block/ingot");
    private static readonly AssetLocation SlitSound = new("game", "sounds/effect/gears");
    // The server syncs W at least this often (strokes); the renderer follows the shaft between.
    private const double SyncStep = 0.125;
    // After a plate is handed out the infeed waits this long, so the clutch eases out (the model's presence)
    // before the next plate goes on.
    private const long ClearMs = 600;
    private const float DustInterval = 0.2f;

    private ItemStack? _plate;
    private MachineJob _job = MachineJob.None;
    private long _finishedAt = long.MinValue / 2;
    private float _soundSeconds;
    private float? _serverTurnsPerStroke;
    private int? _serverNailsPerPlate;
    private int? _serverKnifeWear;
    private readonly SlittingMillClock _clock = new();
    private double _lastPsi;
    private float _dustTimer;

    public bool PlateOn => _job.On && _plate != null;
    public ItemStack? Plate => _plate;
    public MachineJob Job => _job;

    /// <summary>The plate's metal, or null with none on.</summary>
    public string? Metal => PlateOn ? Slitting.MetalOfPlate(_plate!.Collectible.Code.ToString()) : null;

    /// <summary>The fitted knife: its code and durability left; null without one (taken out, or spent).</summary>
    public (string Code, double Left)? Knife => Assembly?.Wearing.Fitted(SlittingMillBuild.KnifePart);

    /// <summary>The knife's durability left; 0 without one.</summary>
    public int KnifeLeft => Knife is { } k ? (int)Math.Round(k.Left) : 0;

    public double TurnsPerStroke => _serverTurnsPerStroke ?? Config.TurnsPerStroke;
    public int NailsPerPlate => _serverNailsPerPlate ?? Config.NailsPerPlate;

    private SlittingMillConfig Config => SlittingMillSystem.Of(Api).Config;
    private new SlittingMillRig? Rig => base.Rig as SlittingMillRig;

    /// <summary>The knife's uses are its cleaver's durability, each metal its own.</summary>
    protected override MachineBuild? BuildSpec(IWorldAccessor world) =>
        base.BuildSpec(world)?.WithUses(SlittingMillBuild.KnifePart,
            code => world.GetItem(new AssetLocation(code)) is { Durability: > 0 } knife ? knife.Durability : 0);

    protected override OilMachine? OilKind => OilMachine.SlittingMill;
    protected override float ConfigMinSpeed => Config.MinSpeed;
    protected override int TickMs => 50;
    protected override int ClientTickMs => 200;
    public override int RenderRange => 48;

    /// <summary>Whole with its knife in: until then it puts the token load on its shaft and takes the creative
    /// shortcut (a new steel cleaver last).</summary>
    public override bool Complete => Ready;
    protected override bool LoadsShaft => Ready;
    protected override bool CreativeFills => !Ready;

    /// <summary>The configured resistance of the metal on the table (the lightest metal's when empty).</summary>
    protected override double ShaftLoad => Config.Resistance(Metal);

    public override bool HasWork => PlateOn;
    public override double? WorkProgress => PlateOn ? _job.Progress : null;
    protected override Float3? SmokePoint => Rig?.Nip;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        // once a second, the infeed
        if (api.Side == EnumAppSide.Server)
            RegisterGameTickListener(_ => PullFromInfeed(), 1000);
    }

    // ---- Interaction ----

    /// <summary>Ctrl + right-click takes back the knife, unworn by it, else the plate on the table (the creative
    /// shortcut comes first while a stage or the knife is missing). Once built, a plate is the work's, though the
    /// shear's bed knife is an iron, meteoric iron or steel plate: the build would refuse it as already fitted
    /// before <see cref="InteractWork"/> saw it (docs/modeling/machine-base.md, "A part held on a built machine").</summary>
    protected override bool? InteractContent(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        var controls = byPlayer.Entity.Controls;
        if (Built && !controls.CtrlKey && Slitting.MetalOfPlate(slot.Itemstack?.Collectible?.Code?.ToString()) != null)
            return InteractWork(byPlayer, slot, blockSel);
        if (!controls.CtrlKey || controls.ShiftKey || CreativeShortcut(byPlayer))
            return null;
        if (Api.Side == EnumAppSide.Server)
            TakeBack(byPlayer);
        return true;
    }

    /// <summary>A plate of a metal the mill slits goes on the table (the nails and strips that come off, an
    /// ingot or a lead plate never do).</summary>
    protected override bool InteractWork(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        if (Slitting.MetalOfPlate(slot.Itemstack?.Collectible?.Code?.ToString()) == null)
            return false;
        if (Api.Side == EnumAppSide.Server)
            TryLoadPlate(slot, byPlayer);
        return true;
    }

    /// <summary>A stage fitted or the knife renewed: the infeed may have a plate for it.</summary>
    protected override void OnAssemblyChanged() => PullFromInfeed();

    /// <summary>Ctrl + right-click (server side): the knife, with the durability it has left (taking it back
    /// wears it no further), else the plate on the table, its slitting lost.</summary>
    public bool TakeBack(IPlayer byPlayer)
    {
        if (Assembly is not { } assembly)
            return false;
        switch (SlittingMillBuild.TakeBack(assembly, PlateOn))
        {
            case SlittingMillTakeBack.Knife:
                if (assembly.Take(SlittingMillBuild.KnifePart) is not { } knife || StackOf(new StageDrop(knife.Code, 1, knife.Left)) is not { } stack)
                    return false;
                Give(byPlayer, stack);
                break;
            case SlittingMillTakeBack.Plate:
                var plate = _plate!;
                ClearJob();
                Give(byPlayer, plate);
                break;
            default:
                return false;
        }
        Api.World.PlaySoundAt(LatchSound, Pos, 0, byPlayer);
        MarkDirty(true);
        return true;
    }

    /// <summary>The nails and strips a plate of <paramref name="metal"/> becomes, or null when they do not exist
    /// in this game.</summary>
    private Item? OutputItem(string? metal) =>
        metal != null && Api.World.GetItem(new AssetLocation(Slitting.OutputFor(metal))) is { Id: > 0, IsMissing: false } item ? item : null;

    /// <summary>Puts a plate from <paramref name="slot"/> on the table, if the mill takes it now.</summary>
    public bool TryLoadPlate(ItemSlot slot, IPlayer? byPlayer)
    {
        string? code = slot.Itemstack?.Collectible?.Code?.ToString();
        switch (Slitting.CanLoad(code, Ready, PlateOn))
        {
            case SlitLoadVerdict.Loads:
                break;
            case SlitLoadVerdict.Incomplete:
                return Say(byPlayer, Built ? "error-no-knife" : "error-incomplete");
            case SlitLoadVerdict.Occupied:
                return Say(byPlayer, "error-occupied");
            default:
                return false;
        }
        if (OutputItem(Slitting.MetalOfPlate(code)) == null)
            return Say(byPlayer, "error-no-nails");
        Load(slot.TakeOut(1));
        slot.MarkDirty();
        return true;
    }

    private void Load(ItemStack plate)
    {
        _plate = plate;
        _job = Slitting.Start();
        if (Rig is { } rig)
        {
            var at = WorldPoint(rig.Plate);
            Api.World.PlaySoundAt(PlateSound, at.X, at.Y, at.Z);
        }
        MarkDirty(true);
    }

    private void ClearJob()
    {
        _plate = null;
        _job = MachineJob.None;
    }

    private void Give(IPlayer byPlayer, ItemStack stack)
    {
        if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 1.2, 0.5));
    }

    /// <summary>The mill's own message (<c>seraphhorizons:slittingmill-{key}</c>) to the player; false.</summary>
    private bool Say(IPlayer? byPlayer, string key, params object[] args)
    {
        if (byPlayer is IServerPlayer sp)
            sp.SendIngameError("slittingmill-" + key, Lang.GetL(sp.LanguageCode, SlittingMillSystem.Domain + ":slittingmill-" + key, args));
        return false;
    }

    /// <summary>The help once built: a plate on an empty table, and what Ctrl takes back.</summary>
    protected override void WorkHelp(IPlayer? forPlayer, List<WorldInteraction> help)
    {
        string Key(string action) => SlittingMillSystem.Domain + ":blockhelp-slittingmill-" + action;
        if (Ready && !PlateOn)
        {
            var plates = Stacks(Api.World, Slitting.Metals.Select(Slitting.PlateFor), 1);
            if (plates.Length > 0)
                help.Add(new WorldInteraction { ActionLangCode = Key("loadplate"), MouseButton = EnumMouseButton.Right, Itemstacks = plates });
        }
        string? take = Assembly is { } a ? SlittingMillBuild.TakeBack(a, PlateOn) switch
        {
            SlittingMillTakeBack.Knife => "takeknife",
            SlittingMillTakeBack.Plate => "takeplate",
            _ => null,
        } : null;
        bool creative = forPlayer?.WorldData?.CurrentGameMode == EnumGameMode.Creative;
        if (take != null && !(creative && CreativeFills))
            help.Add(new WorldInteraction { ActionLangCode = Key(take), MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" });
    }

    // ---- The slitting ----

    // The slitting syncs itself as it goes (SyncStep), so the base's full sync is not asked for.
    protected override bool Work(double radians)
    {
        Slit(radians);
        return false;
    }

    /// <summary>Slits by <paramref name="radians"/> of shaft rotation (server side; call only while running).
    /// At the hand-out the plate's nails and strips are taken off. Returns whether a plate was finished.</summary>
    public bool Slit(double radians)
    {
        if (!PlateOn || !Ready)
            return false;
        var before = _job;
        (_job, bool finished) = Slitting.Advance(_job, radians, Config.TurnsPerStroke);
        if (finished)
        {
            Finish();
            return true;
        }
        if (Math.Floor(before.Work / SyncStep) != Math.Floor(_job.Work / SyncStep))
            MarkDirty();
        return false;
    }

    /// <summary>The plate is slit: the knife wears by the plate's metal (a spent knife breaks with the tool-break
    /// sound and the mill stops until another goes in), the tank drains a plate's oil, and the nails and strips go
    /// into a container at the output face, else drop there; the infeed waits a moment before the next plate.</summary>
    private void Finish()
    {
        string? metal = Metal;
        DrainOil(1);
        if (Assembly!.Wearing.Wear(SlittingMillBuild.KnifePart, Config.KnifeWear(metal)))
            Api.World.PlaySoundAt(BreakSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5);
        ClearJob();
        if (OutputItem(metal) is { } nails)
            Deliver(new ItemStack(nails, Config.NailsPerPlate));
        _finishedAt = Api.World.ElapsedMilliseconds;
        MarkDirty(true);
    }

    /// <summary>Puts <paramref name="stack"/> into a container just beyond the output face, else drops what is
    /// left of it there.</summary>
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
        Api.World.PlaySoundAt(NailsSound, at.X, at.Y, at.Z);
    }

    /// <summary>A ready mill with its table empty and its shaft turning takes one plate from a container at the
    /// infeed face, a moment after the last plate was handed out. Returns whether one went on.</summary>
    public bool PullFromInfeed()
    {
        if (Api.Side != EnumAppSide.Server || PlateOn || !Ready || Math.Abs(ShaftSpeed) < MinSpeed || Rig is not { } rig)
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
                if (Slitting.CanLoad(code, true, false) != SlitLoadVerdict.Loads || OutputItem(Slitting.MetalOfPlate(code)) == null)
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

    // A slitting mill is heard.
    protected override void OnClientTick(float dt)
    {
        if (Api is not ICoreClientAPI capi || Rig is not { } rig || !Running)
        {
            _soundSeconds = 0;
            return;
        }
        if ((_soundSeconds -= dt) <= 0)
        {
            _soundSeconds = 2f;
            var at = WorldPoint(rig.Knife);
            capi.World.PlaySoundAt(SlitSound, at.X, at.Y, at.Z, null, true, 16, 0.45f);
        }
    }

    // ---- The renderer's ----

    /// <summary>The work (<c>plate</c>: the plate, its rods, nails and strips) as the clock shows it; every other
    /// part as its stage (and the knife, its part) is fitted.</summary>
    public override bool ShowsPart(string? requires) =>
        requires == SlittingMillRequires.Plate ? _clock.ShowsWork : base.ShowsPart(requires);

    /// <summary>The axle turns the entry and the rectifier whether or not the mill is complete.</summary>
    public override bool Animates => true;

    /// <summary>The work's texture follows the plate's metal.</summary>
    public override string TextureKey => Metal ?? "";

    /// <summary>The block's textures with the plate's metal in place of <c>metal</c>.</summary>
    public override ITexPositionSource PartTextures(ICoreClientAPI capi) =>
        new MachineMeshes.MetalTextureSource(base.PartTextures(capi), Metal is { } metal ? MachineMeshes.MetalTexture(capi, metal) : null);

    /// <summary>θ and ψ the shaft's (about the entry shaft's native x axis), W, k and p from the clock, and the
    /// oil's fill; while the mill slits, metal dust at the nip.</summary>
    public override RigInput FrameInput(float dt, bool far)
    {
        var (theta, psi) = AdvanceShaft();
        double turned = Math.Max(0, psi - _lastPsi);
        _lastPsi = psi;
        bool running = Running;
        _clock.Advance(dt, turned, PlateOn, _job.Work, running, TurnsPerStroke);
        if (!far && running && Rig is { } rig && (_dustTimer += dt) >= DustInterval)
        {
            _dustTimer = 0;
            var at = WorldPoint(rig.Nip);
            Api.World.SpawnParticles(2, ColorUtil.ToRgba(220, 120, 115, 110),
                at.AddCopy(-0.1, -0.03, -0.03), at.AddCopy(0.1, 0.03, 0.03),
                new Vec3f(-0.2f, -0.1f, -0.2f), new Vec3f(0.2f, 0.25f, 0.2f), 0.6f, 1f, 0.15f, EnumParticleModel.Cube, null);
        }
        return _clock.Input(theta, psi, OilFill);
    }

    // ---- Breaking ----

    /// <summary>What breaking the frame gives besides the frame: every fitted part (the knife with its
    /// durability) and the plate on the table, its slitting lost.</summary>
    public override IEnumerable<ItemStack> AssemblyDrops()
    {
        foreach (var stack in base.AssemblyDrops())
            yield return stack;
        if (PlateOn)
            yield return _plate!.Clone();
    }

    // ---- Saving and syncing ----

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        if (PlateOn)
            tree.SetItemstack("plate", _plate);
        else
            tree.RemoveAttribute("plate");
        tree.SetInt("class", _job.Class);
        tree.SetDouble("work", _job.Work);
        if (Api?.Side == EnumAppSide.Server)
        {
            tree.SetFloat("turnsPerStroke", Config.TurnsPerStroke);
            tree.SetInt("nailsPerPlate", Config.NailsPerPlate);
            tree.SetInt("knifeWear", Config.KnifeWear(Metal));
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        _plate = tree.GetItemstack("plate");
        if (_plate != null && !_plate.ResolveBlockOrItem(worldForResolving))
            _plate = null;
        _job = _plate == null ? MachineJob.None : Slitting.Restore(tree.GetInt("class"), tree.GetDouble("work"));
        if (!_job.On || Slitting.MetalOfPlate(_plate?.Collectible.Code.ToString()) == null)
        {
            _plate = null;
            _job = MachineJob.None;
        }
        if (worldForResolving.Side == EnumAppSide.Client)
        {
            _serverTurnsPerStroke = tree.TryGetFloat("turnsPerStroke");
            _serverNailsPerPlate = tree.TryGetInt("nailsPerPlate");
            _serverKnifeWear = tree.TryGetInt("knifeWear");
        }
    }

    public override void OnStoreCollectibleMappings(Dictionary<int, AssetLocation> blockIdMapping, Dictionary<int, AssetLocation> itemIdMapping)
    {
        base.OnStoreCollectibleMappings(blockIdMapping, itemIdMapping);
        _plate?.Collectible.OnStoreCollectibleMappings(Api.World, new DummySlot(_plate), blockIdMapping, itemIdMapping);
    }

    public override void OnLoadCollectibleMappings(IWorldAccessor worldForResolve, Dictionary<int, AssetLocation> oldBlockIdMapping,
                                                   Dictionary<int, AssetLocation> oldItemIdMapping, int schematicSeed, bool resolveImports)
    {
        base.OnLoadCollectibleMappings(worldForResolve, oldBlockIdMapping, oldItemIdMapping, schematicSeed, resolveImports);
        _plate?.FixMapping(oldBlockIdMapping, oldItemIdMapping, worldForResolve);
    }

    // ---- Info ----

    /// <summary>The plate on the table, with the nails cut from it so far, and the plates of its metal the knife
    /// has left.</summary>
    protected override void AppendContents(StringBuilder dsc)
    {
        string L(string key, params object[] args) => Lang.Get(SlittingMillSystem.Domain + ":slittingmill-" + key, args);
        if (Metal is not { } metal)
            return;
        string name = Lang.Get("material-" + metal).ToLowerInvariant();
        dsc.AppendLine(L("info-plate", name, Slitting.CutsDone(_job), Slitting.Cuts.Count));
        int wear = _serverKnifeWear ?? Config.KnifeWear(metal);
        if (Knife != null && wear > 0)
            dsc.AppendLine(L("info-knife", (KnifeLeft + wear - 1) / wear, name));
    }

    /// <summary>For a wrench: the slitting's W exactly, the shaft turns a stroke, the nails a plate makes and the
    /// knife's wear a plate of this metal.</summary>
    protected override void AppendDetail(StringBuilder dsc)
    {
        if (!PlateOn)
            return;
        dsc.AppendLine(Lang.Get(SlittingMillSystem.Domain + ":slittingmill-info-slit",
            _job.Work.ToString("0.000", CultureInfo.InvariantCulture), Slitting.HandOut,
            TurnsPerStroke.ToString("0.###", CultureInfo.InvariantCulture)));
        dsc.AppendLine(Lang.Get(SlittingMillSystem.Domain + ":slittingmill-info-yield", NailsPerPlate,
            _serverKnifeWear ?? Config.KnifeWear(Metal)));
    }
}
