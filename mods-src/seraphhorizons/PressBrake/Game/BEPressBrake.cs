using System.Globalization;
using System.Text;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.PressBrake.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.PressBrake;

/// <summary>
/// The press brake's controller, on the shared machine base (<see cref="BEMachine"/>: its build from
/// <c>config/builds/pressbrake.json</c>, its cell, its block info, its renderer). A hand machine
/// (<see cref="BEMachine.HandWorked"/>): the player works it by holding right-click on it, as on the
/// quern, and the server advances W while anyone holds, sounds the fold, and at W = 1 drops one angle at
/// the output face. It keeps the half plate on the bed as its item stack and the fold
/// (<see cref="MachineJob"/>: W, 0..1). Its work, the half plate, is the squaring shear's item: with that
/// switch off there is none, and the brake refuses work. The client poses the rig from its own lever clock
/// (<see cref="PressBrakeClock"/>). The rules are PressBrake/Core's.
/// </summary>
public class BEPressBrake : BEMachine
{
    private static readonly AssetLocation PlateSound = new("game", "sounds/block/plate");
    private static readonly AssetLocation BendSound = new("game", "sounds/block/heavymetal-hit");
    private static readonly AssetLocation AngleSound = new("game", "sounds/block/chute");
    private static readonly AssetLocation[] CreakSounds =
        [.. Enumerable.Range(1, 4).Select(i => new AssetLocation("game", $"sounds/block/woodcreak_{i}"))];
    // The server syncs W at least this often (plates); the renderer follows the lever between.
    private const double SyncStep = 0.02;
    private const float DustInterval = 0.12f;

    private ItemStack? _plate;
    private MachineJob _job = MachineJob.None;
    private float _creakSeconds;
    private float? _serverTurnsLead;
    private float? _serverTurnsCopper;
    private readonly PressBrakeClock _clock = new();
    private readonly ParticleTimer _dust = new(DustInterval);

    /// <summary>A plate is on the bed.</summary>
    public bool PlateOn => _job.On && _plate != null;
    public ItemStack? Plate => _plate;
    public MachineJob Job => _job;

    /// <summary>The fitted edges' metal (<c>iron</c>, <c>steel</c>), or null.</summary>
    public string? EdgeMetal => Assembly is { } a && a.Build.Stages.Count > 1 ? PressBrakeRequires.EdgeMetal(a.Build.CodeIn(1, 0)) : null;

    public double LeverTurnsPerPlate(int k) => k switch
    {
        1 => _serverTurnsLead ?? Config.LeverTurnsPerPlateLead,
        2 => _serverTurnsCopper ?? Config.LeverTurnsPerPlateCopper,
        _ => 0,
    };

    private PressBrakeSystem System => PressBrakeSystem.Of(Api);
    private PressBrakeConfig Config => System.Config;
    private new PressBrakeRig? Rig => base.Rig as PressBrakeRig;

    protected override bool HandWorked => true;
    protected override int TickMs => 50;
    protected override int ClientTickMs => 100;
    public override int RenderRange => 48;

    public override bool HasWork => PlateOn;
    public override double? WorkProgress => PlateOn ? _job.Progress : null;

    // ---- Interaction ----

    /// <summary>Ctrl on a brake with a half plate on takes it back off while it is still flat; one being
    /// folded stays. Fitted parts never come out: only breaking the brake returns them.</summary>
    protected override bool? InteractContent(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        var controls = byPlayer.Entity.Controls;
        if (!controls.CtrlKey || controls.ShiftKey || !PlateOn || CreativeShortcut(byPlayer))
            return null;
        if (Api.Side == EnumAppSide.Server)
            TakePlate(byPlayer);
        return true;
    }

    /// <summary>
    /// The brake's own right-click, after its build's: a lead or copper half plate goes on an empty bed.
    /// Anything else (an empty hand, a tool, a whole plate, a half plate when one is already on) on a
    /// complete brake with a half plate on starts working the lever, held as on the quern
    /// (<see cref="BEMachine.OnInteractStep"/>); Shift lets a held block be placed against it instead. With no
    /// half plates in the world (the squaring shear's switch off) a complete brake refuses work with a message.
    /// </summary>
    protected override bool InteractWork(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        var controls = byPlayer.Entity.Controls;
        string? code = slot.Itemstack?.Collectible?.Code?.ToString();
        bool server = Api.Side == EnumAppSide.Server;
        if (Built && !controls.ShiftKey && !PlateOn && !HalfPlatesExist())
            return !server || Error(byPlayer, "error-no-halfplates");
        if (Folding.ClassOfPlate(code) != 0 && !PlateOn)
            // a click loads it; held on, the steps that follow work the lever
            return !server || TryLoadPlate(slot, byPlayer);
        if (controls.ShiftKey || !Built)
            return false;
        if (!server)
            return true;
        if (!PlateOn)
            return Error(byPlayer, "error-no-plate");
        return StartHold(byPlayer);
    }

    /// <summary>Ctrl + right-click (server side): a half plate still flat comes off the bed; one being
    /// folded stays.</summary>
    public bool TakePlate(IPlayer byPlayer)
    {
        if (!PlateOn)
            return false;
        if (!_job.Untouched)
            return Error(byPlayer, "error-busy");
        var plate = _plate!;
        ClearJob();
        Give(byPlayer, plate);
        Api.World.PlaySoundAt(PlateSound, Pos, 0, byPlayer);
        MarkDirty(true);
        return true;
    }

    /// <summary>Whether the brake has work in this world: the half plates, the squaring shear's item,
    /// exist (with its <c>SquaringShear</c> switch off they do not, and nothing can go on the bed).</summary>
    public bool HalfPlatesExist() =>
        new[] { 1, 2 }.Any(k => Folding.PlateFor(k) is { } code && Api.World.GetItem(new AssetLocation(code)) is { Id: > 0, IsMissing: false });

    /// <summary>The angle a half plate of class <paramref name="k"/> is folded into, or null when it
    /// does not exist in this game (it is UnifiedPipes' item).</summary>
    private Item? AngleItem(int k) =>
        Folding.AngleFor(k) is { } code && Api.World.GetItem(new AssetLocation(code)) is { Id: > 0, IsMissing: false } item ? item : null;

    /// <summary>Puts a half plate from <paramref name="slot"/> on the bed, if the brake takes it now.</summary>
    public bool TryLoadPlate(ItemSlot slot, IPlayer? byPlayer)
    {
        string? code = slot.Itemstack?.Collectible?.Code?.ToString();
        switch (Folding.CanLoad(code, Built, PlateOn))
        {
            case FoldLoadVerdict.Loads:
                break;
            case FoldLoadVerdict.Incomplete:
                return Error(byPlayer, "error-incomplete");
            case FoldLoadVerdict.Occupied:
                return Error(byPlayer, "error-occupied");
            default:
                return false;
        }
        if (AngleItem(Folding.ClassOfPlate(code)) == null)
            return Error(byPlayer, "error-no-angle");
        Load(slot.TakeOut(1));
        slot.MarkDirty();
        return true;
    }

    private void Load(ItemStack plate)
    {
        _plate = plate;
        _job = Folding.Start(Folding.ClassOfPlate(plate.Collectible.Code.ToString()));
        var at = WorldPoint(Rig?.Plate ?? new Float3(0.5f, 0.5f, 0.5f));
        Api.World.PlaySoundAt(PlateSound, at.X, at.Y, at.Z);
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

    /// <summary>The brake's own message (<c>seraphhorizons:pressbrake-{key}</c>) to the player; false.</summary>
    private bool Error(IPlayer? byPlayer, string key, params object[] args)
    {
        if (byPlayer is IServerPlayer sp)
            sp.SendIngameError("pressbrake-" + key, Lang.GetL(sp.LanguageCode, PressBrakeSystem.Domain + ":pressbrake-" + key, args));
        return false;
    }

    /// <summary>The help once built: a half plate goes on an empty bed; the lever is worked with one on, and
    /// Ctrl takes it back while it is still flat.</summary>
    protected override void WorkHelp(IPlayer? forPlayer, List<WorldInteraction> help)
    {
        string Key(string action) => PressBrakeSystem.Domain + ":blockhelp-pressbrake-" + action;
        if (!PlateOn && Stacks(Api.World, [Folding.LeadHalfPlate, Folding.CopperHalfPlate], 1) is { Length: > 0 } plates)
            help.Add(new WorldInteraction { ActionLangCode = Key("loadplate"), MouseButton = EnumMouseButton.Right, Itemstacks = plates });
        if (PlateOn)
            help.Add(new WorldInteraction { ActionLangCode = Key("work"), MouseButton = EnumMouseButton.Right });
        if (PlateOn && _job.Untouched)
            help.Add(new WorldInteraction { ActionLangCode = Key("takeplate"), MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" });
    }

    // ---- The fold ----

    // The fold syncs itself as it goes (SyncStep), so the base's full sync is not asked for.
    protected override bool WorkUnpowered(float dt)
    {
        Fold(Folding.LeverRadiansPerSecond * dt);
        return false;
    }

    /// <summary>Folds by <paramref name="radians"/> of the lever clock (server side): W advances by
    /// their turns over the half plate's lever turns, the bend is heard at the middle of the fold, and
    /// at W = 1 the half plate is used up and its angle comes off. Returns the angles delivered.</summary>
    public int Fold(double radians)
    {
        if (!PlateOn || !Built)
            return 0;
        var before = _job;
        (_job, bool finished) = Folding.Advance(_job, radians, Config.LeverTurnsPerPlate(_job.Class));
        if (Rig is { } rig && MachineJob.Crossed(rig.FoldMoments, before.Work, _job.Work) > 0)
        {
            var at = WorldPoint(rig.Edge);
            Api.World.PlaySoundAt(BendSound, at.X, at.Y, at.Z, null, true, 16, 0.6f);
        }
        if (finished)
        {
            int k = _job.Class;
            ClearJob();
            if (AngleItem(k) is { } angle)
                Deliver(new ItemStack(angle, Folding.AnglesPerPlate));
            MarkDirty(true);
            return Folding.AnglesPerPlate;
        }
        if (Math.Floor(before.Work / SyncStep) != Math.Floor(_job.Work / SyncStep))
            MarkDirty(false);
        return 0;
    }

    /// <summary>Drops <paramref name="stack"/> just beyond the output face.</summary>
    public void Deliver(ItemStack stack)
    {
        if (Rig is not { } rig)
            return;
        var at = WorldPoint(rig.OutputDrop());
        var n = Footprint.ToWorld(rig.OutputSide, Side).Normal();
        Api.World.SpawnItemEntity(stack, at, new Vec3d(n.X * 0.05, 0.02, n.Z * 0.05));
        Api.World.PlaySoundAt(AngleSound, at.X, at.Y, at.Z);
    }

    // A worked brake creaks.
    protected override void OnClientTick(float dt)
    {
        if (Api is not ICoreClientAPI capi || Rig is not { } rig || !Running)
        {
            _creakSeconds = 0.3f;
            return;
        }
        if ((_creakSeconds -= dt) <= 0)
        {
            _creakSeconds = 1.4f + (float)capi.World.Rand.NextDouble() * 0.6f;
            var at = WorldPoint(rig.Plate);
            capi.World.PlaySoundAt(CreakSounds[capi.World.Rand.Next(CreakSounds.Length)], at.X, at.Y, at.Z, null, true, 12, 0.5f);
        }
    }

    // ---- The renderer's ----

    /// <summary>The plates (<c>platelead</c>, <c>platecopper</c>) while that metal's half plate is on the bed, as
    /// the clock shows it; every other part as its stage is fitted.</summary>
    public override bool ShowsPart(string? requires) =>
        requires is PressBrakeRequires.PlateLead or PressBrakeRequires.PlateCopper ? _clock.ShowsPlate(requires) : base.ShowsPart(requires);

    /// <summary>The edges' texture follows their metal.</summary>
    public override string TextureKey => EdgeMetal ?? "";

    /// <summary>The block's textures with the edges' metal in place of <c>edge</c>; the screws, made from metal
    /// parts, are always the block's own <c>cupronickel</c>.</summary>
    public override ITexPositionSource PartTextures(ICoreClientAPI capi) =>
        new FittedTextureSource(base.PartTextures(capi), MachineMeshes.MetalTexture(capi, EdgeMetal));

    /// <summary>The lever clock θ, turning while the lever is worked, and W, k and p (<see cref="PressBrakeClock"/>);
    /// while the leaf swings, metal dust at the folding edge.</summary>
    public override RigInput FrameInput(float dt, bool far)
    {
        int k = PlateOn ? _job.Class : 0;
        bool held = Held && k != 0;
        _clock.Advance(dt, k, _job.Work, held, LeverTurnsPerPlate(k));
        if (!far && held && Rig is { } rig && rig.IsFolding(_clock.Work) && _dust.Due(dt))
        {
            SpawnDust(rig, k);
        }
        return _clock.Input();
    }

    private void SpawnDust(PressBrakeRig rig, int k)
    {
        var at = WorldPoint(rig.Edge);
        // fine metal off the folding edge: lead grey, copper red
        int colour = MachineParticles.MetalColour(k == 2 ? "copper" : "lead", 200, 0);
        (MachineParticles.Dust(colour) with { MinVelocity = new Vec3f(-0.15f, 0f, -0.15f), MaxVelocity = new Vec3f(0.15f, 0.25f, 0.15f), Size = 0.12f })
            .Spawn(Api.World, at, MachineParticles.Around(0.35, 0.02, 0.03));
    }

    /// <summary>The block's textures with the edges' metal in place of <c>edge</c>.</summary>
    private sealed class FittedTextureSource(ITexPositionSource inner, TextureAtlasPosition? edge) : ITexPositionSource
    {
        public Size2i AtlasSize => inner.AtlasSize!;
        public TextureAtlasPosition this[string textureCode] =>
            textureCode == "edge" && edge != null ? edge : inner[textureCode]!;
    }

    // ---- Breaking ----

    /// <summary>What breaking the frame gives besides the frame: every fitted part, and the half plate
    /// if it is still flat (once folding has begun, it is lost with the brake).</summary>
    public override IEnumerable<ItemStack> AssemblyDrops()
    {
        foreach (var stack in base.AssemblyDrops())
            yield return stack;
        if (PlateOn && _job.Untouched)
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
            tree.SetFloat("leverTurnsLead", Config.LeverTurnsPerPlateLead);
            tree.SetFloat("leverTurnsCopper", Config.LeverTurnsPerPlateCopper);
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        _plate = tree.GetItemstack("plate");
        if (_plate != null && !_plate.ResolveBlockOrItem(worldForResolving))
            _plate = null;
        _job = _plate == null ? MachineJob.None : Folding.Restore(tree.GetInt("class"), tree.GetDouble("work"));
        if (!_job.On)
            _plate = null;
        if (worldForResolving.Side == EnumAppSide.Client)
        {
            _serverTurnsLead = tree.TryGetFloat("leverTurnsLead");
            _serverTurnsCopper = tree.TryGetFloat("leverTurnsCopper");
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

    /// <summary>The half plate on the bed and how far it is folded.</summary>
    protected override void AppendContents(StringBuilder dsc)
    {
        if (PlateOn)
            dsc.AppendLine(Lang.Get(PressBrakeSystem.Domain + ":pressbrake-info-plate",
                Lang.Get(PressBrakeSystem.Domain + ":pressbrake-metal-" + Folding.MetalOf(_job.Class)), (int)Math.Floor(_job.Work * 100 + 1e-9)));
    }

    /// <summary>For a wrench: the fold cycle W exactly, and the lever turns a half plate of its metal takes.</summary>
    protected override void AppendDetail(StringBuilder dsc)
    {
        if (PlateOn)
            dsc.AppendLine(Lang.Get(PressBrakeSystem.Domain + ":pressbrake-info-fold",
                _job.Work.ToString("0.000", CultureInfo.InvariantCulture),
                LeverTurnsPerPlate(_job.Class).ToString("0.##", CultureInfo.InvariantCulture)));
    }
}
