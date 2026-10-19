using System.Globalization;
using System.Text;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.SquaringShear.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.SquaringShear;

/// <summary>
/// The squaring shear's controller, on the shared machine base (<see cref="BEMachine"/>: its build from
/// <c>config/builds/squaringshear.json</c>, its cell, its block info, its renderer). A hand machine
/// (<see cref="BEMachine.HandWorked"/>): the player works the treadle by holding right-click on it, as on
/// the quern, and the server advances W while anyone holds, sounds the cut, and at W = 1 drops two half
/// plates at the output face. It keeps the plate on the table as its item stack and the cut
/// (<see cref="MachineJob"/>: W, 0..1). The client poses the rig from its own treadle clock
/// (<see cref="SquaringShearClock"/>). The rules are SquaringShear/Core's.
/// </summary>
public class BESquaringShear : BEMachine
{
    private static readonly AssetLocation PlateSound = new("game", "sounds/block/plate");
    private static readonly AssetLocation CutSound = new("game", "sounds/block/heavymetal-hit2");
    private static readonly AssetLocation HalvesSound = new("game", "sounds/block/plate");
    private static readonly AssetLocation[] CreakSounds =
        [.. Enumerable.Range(1, 4).Select(i => new AssetLocation("game", $"sounds/block/woodcreak_{i}"))];
    // The server syncs W at least this often (plates); the renderer follows the treadle between.
    private const double SyncStep = 0.02;
    private const float DustInterval = 0.12f;

    private ItemStack? _plate;
    private MachineJob _job = MachineJob.None;
    private float _creakSeconds;
    private float? _serverStrokesLead;
    private float? _serverStrokesCopper;
    private readonly SquaringShearClock _clock = new();
    private float _dustTimer;

    /// <summary>A plate is on the table.</summary>
    public bool PlateOn => _job.On && _plate != null;
    public ItemStack? Plate => _plate;
    public MachineJob Job => _job;

    /// <summary>The fitted blades' metal (<c>iron</c>, <c>steel</c>), or null.</summary>
    public string? BladeMetal => FittedMetal(0);

    /// <summary>The fitted gauge's and hold-down's metal (<c>iron</c>, <c>meteoriciron</c>, <c>steel</c>), or null.</summary>
    public string? GaugeMetal => FittedMetal(1);

    private string? FittedMetal(int stage) =>
        Assembly is { } a && a.Build.Stages.Count > stage ? SquaringShearRequires.MetalOf(a.Build.CodeIn(stage, 0)) : null;

    public double StrokesPerPlate(int k) => k switch
    {
        1 => _serverStrokesLead ?? Config.StrokesPerPlateLead,
        2 => _serverStrokesCopper ?? Config.StrokesPerPlateCopper,
        _ => 0,
    };

    private SquaringShearSystem System => SquaringShearSystem.Of(Api);
    private SquaringShearConfig Config => System.Config;
    private new SquaringShearRig? Rig => base.Rig as SquaringShearRig;

    protected override bool HandWorked => true;
    protected override int TickMs => 50;
    protected override int ClientTickMs => 100;
    public override int RenderRange => 48;

    public override bool HasWork => PlateOn;
    public override double? WorkProgress => PlateOn ? _job.Progress : null;

    // ---- Interaction ----

    /// <summary>Ctrl on a shear with a plate on takes it back off while it is still whole; one being cut
    /// stays. Fitted parts never come out: only breaking the shear returns them.</summary>
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
    /// The shear's own right-click, after its build's: a lead or copper plate goes on an empty table.
    /// Anything else (an empty hand, a tool, a plate when one is already on) on a complete shear with a
    /// plate on starts working the treadle, held as on the quern (<see cref="BEMachine.OnInteractStep"/>);
    /// Shift lets a held block be placed against it instead.
    /// </summary>
    protected override bool InteractWork(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        var controls = byPlayer.Entity.Controls;
        string? code = slot.Itemstack?.Collectible?.Code?.ToString();
        bool server = Api.Side == EnumAppSide.Server;
        if (Cutting.ClassOfPlate(code) != 0 && !PlateOn)
            // a click loads it; held on, the steps that follow work the treadle
            return !server || TryLoadPlate(slot, byPlayer);
        if (controls.ShiftKey || !Built)
            return false;
        if (!server)
            return true;
        if (!PlateOn)
            return Error(byPlayer, "error-no-plate");
        return StartHold(byPlayer);
    }

    /// <summary>Ctrl + right-click (server side): a plate still whole comes off the table; one being
    /// cut stays.</summary>
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

    /// <summary>The half plate a plate of class <paramref name="k"/> is cut into, or null when it does
    /// not exist in this game (the shear's own item, so only when its type file is missing or broken).</summary>
    private Item? HalfPlateItem(int k) =>
        Cutting.HalfPlateFor(k) is { } code && Api.World.GetItem(new AssetLocation(code)) is { Id: > 0, IsMissing: false } item ? item : null;

    /// <summary>Puts a plate from <paramref name="slot"/> on the table, if the shear takes it now.</summary>
    public bool TryLoadPlate(ItemSlot slot, IPlayer? byPlayer)
    {
        string? code = slot.Itemstack?.Collectible?.Code?.ToString();
        switch (Cutting.CanLoad(code, Built, PlateOn))
        {
            case CutLoadVerdict.Loads:
                break;
            case CutLoadVerdict.Incomplete:
                return Error(byPlayer, "error-incomplete");
            case CutLoadVerdict.Occupied:
                return Error(byPlayer, "error-occupied");
            default:
                return false;
        }
        if (HalfPlateItem(Cutting.ClassOfPlate(code)) == null)
            return Error(byPlayer, "error-no-halfplate");
        Load(slot.TakeOut(1));
        slot.MarkDirty();
        return true;
    }

    private void Load(ItemStack plate)
    {
        _plate = plate;
        _job = Cutting.Start(Cutting.ClassOfPlate(plate.Collectible.Code.ToString()));
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

    /// <summary>The shear's own message (<c>seraphhorizons:squaringshear-{key}</c>) to the player; false.</summary>
    private bool Error(IPlayer? byPlayer, string key, params object[] args)
    {
        if (byPlayer is IServerPlayer sp)
            sp.SendIngameError("squaringshear-" + key, Lang.GetL(sp.LanguageCode, SquaringShearSystem.Domain + ":squaringshear-" + key, args));
        return false;
    }

    /// <summary>The help once built: a plate goes on an empty table; the treadle is worked with one on, and
    /// Ctrl takes it back while it is still whole.</summary>
    protected override void WorkHelp(IPlayer? forPlayer, List<WorldInteraction> help)
    {
        string Key(string action) => SquaringShearSystem.Domain + ":blockhelp-squaringshear-" + action;
        if (!PlateOn && Stacks(Api.World, [Cutting.LeadPlate, Cutting.CopperPlate], 1) is { Length: > 0 } plates)
            help.Add(new WorldInteraction { ActionLangCode = Key("loadplate"), MouseButton = EnumMouseButton.Right, Itemstacks = plates });
        if (PlateOn)
            help.Add(new WorldInteraction { ActionLangCode = Key("work"), MouseButton = EnumMouseButton.Right });
        if (PlateOn && _job.Untouched)
            help.Add(new WorldInteraction { ActionLangCode = Key("takeplate"), MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" });
    }

    // ---- The cut ----

    // The cut syncs itself as it goes (SyncStep), so the base's full sync is not asked for.
    protected override bool WorkUnpowered(float dt)
    {
        Cut(Cutting.StrokeRadiansPerSecond * dt);
        return false;
    }

    /// <summary>Cuts by <paramref name="radians"/> of the treadle clock (server side): W advances by
    /// their strokes over the plate's strokes, the cut is heard at the bottom of the stroke, and at
    /// W = 1 the plate is used up and its two half plates come off. Returns the half plates delivered.</summary>
    public int Cut(double radians)
    {
        if (!PlateOn || !Built)
            return 0;
        var before = _job;
        (_job, bool finished) = Cutting.Advance(_job, radians, Config.StrokesPerPlate(_job.Class));
        if (Rig is { } rig && MachineJob.Crossed(rig.CutMoments, before.Work, _job.Work) > 0)
        {
            var at = WorldPoint(rig.Edge);
            Api.World.PlaySoundAt(CutSound, at.X, at.Y, at.Z, null, true, 16, 0.6f);
        }
        if (finished)
        {
            int k = _job.Class;
            ClearJob();
            if (HalfPlateItem(k) is { } half)
                Deliver(new ItemStack(half, Cutting.HalfPlatesPerPlate));
            MarkDirty(true);
            return Cutting.HalfPlatesPerPlate;
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
        Api.World.PlaySoundAt(HalvesSound, at.X, at.Y, at.Z);
    }

    // A worked shear creaks.
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

    /// <summary>The plates (<c>platelead</c>, <c>platecopper</c>) while that metal's plate is on the table, as
    /// the clock shows it; every other part as its stage is fitted.</summary>
    public override bool ShowsPart(string? requires) =>
        requires is SquaringShearRequires.PlateLead or SquaringShearRequires.PlateCopper ? _clock.ShowsPlate(requires) : base.ShowsPart(requires);

    /// <summary>The blades' and gauge's textures follow their metals.</summary>
    public override string TextureKey => $"{BladeMetal}/{GaugeMetal}";

    /// <summary>The block's textures with the blades' and gauge's metals in place of <c>blade</c> and <c>gauge</c>.</summary>
    public override ITexPositionSource PartTextures(ICoreClientAPI capi) =>
        new FittedTextureSource(base.PartTextures(capi), MachineMeshes.MetalTexture(capi, BladeMetal), MachineMeshes.MetalTexture(capi, GaugeMetal));

    /// <summary>The treadle clock θ, turning while the treadle is worked, and W, k and p
    /// (<see cref="SquaringShearClock"/>); while the blade moves, metal dust at the cut.</summary>
    public override RigInput FrameInput(float dt, bool far)
    {
        int k = PlateOn ? _job.Class : 0;
        bool held = Held && k != 0;
        _clock.Advance(dt, k, _job.Work, held, StrokesPerPlate(k));
        if (!far && held && Rig is { } rig && rig.IsCutting(_clock.Work) && (_dustTimer += dt) >= DustInterval)
        {
            _dustTimer = 0;
            SpawnDust(rig, k);
        }
        return _clock.Input();
    }

    private void SpawnDust(SquaringShearRig rig, int k)
    {
        var at = WorldPoint(rig.Edge);
        // fine metal off the blades: lead grey, copper red
        int colour = k == 2 ? ColorUtil.ToRgba(200, 70, 120, 190) : ColorUtil.ToRgba(200, 120, 110, 105);
        Api.World.SpawnParticles(2, colour,
            at.AddCopy(-0.35, -0.02, -0.03), at.AddCopy(0.35, 0.02, 0.03),
            new Vec3f(-0.15f, 0f, -0.15f), new Vec3f(0.15f, 0.25f, 0.15f), 0.6f, 1f, 0.12f, EnumParticleModel.Cube, null);
    }

    /// <summary>The block's textures with the blades' and gauge's metals in place of <c>blade</c> and <c>gauge</c>.</summary>
    private sealed class FittedTextureSource(ITexPositionSource inner, TextureAtlasPosition? blade, TextureAtlasPosition? gauge) : ITexPositionSource
    {
        public Size2i AtlasSize => inner.AtlasSize!;
        public TextureAtlasPosition this[string textureCode] => textureCode switch
        {
            "blade" when blade != null => blade,
            "gauge" when gauge != null => gauge,
            _ => inner[textureCode]!,
        };
    }

    // ---- Breaking ----

    /// <summary>What breaking the frame gives besides the frame: every fitted part, and the plate if it
    /// is still whole (once cutting has begun, it is lost with the shear).</summary>
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
            tree.SetFloat("strokesLead", Config.StrokesPerPlateLead);
            tree.SetFloat("strokesCopper", Config.StrokesPerPlateCopper);
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        _plate = tree.GetItemstack("plate");
        if (_plate != null && !_plate.ResolveBlockOrItem(worldForResolving))
            _plate = null;
        _job = _plate == null ? MachineJob.None : Cutting.Restore(tree.GetInt("class"), tree.GetDouble("work"));
        if (!_job.On)
            _plate = null;
        if (worldForResolving.Side == EnumAppSide.Client)
        {
            _serverStrokesLead = tree.TryGetFloat("strokesLead");
            _serverStrokesCopper = tree.TryGetFloat("strokesCopper");
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

    /// <summary>The plate on the table and how far it is cut.</summary>
    protected override void AppendContents(StringBuilder dsc)
    {
        if (PlateOn)
            dsc.AppendLine(Lang.Get(SquaringShearSystem.Domain + ":squaringshear-info-plate",
                Lang.Get(SquaringShearSystem.Domain + ":squaringshear-metal-" + Cutting.MetalOf(_job.Class)), (int)Math.Floor(_job.Work * 100 + 1e-9)));
    }

    /// <summary>For a wrench: the cut cycle W exactly, and the treadle strokes a plate of its metal takes.</summary>
    protected override void AppendDetail(StringBuilder dsc)
    {
        if (PlateOn)
            dsc.AppendLine(Lang.Get(SquaringShearSystem.Domain + ":squaringshear-info-cut",
                _job.Work.ToString("0.000", CultureInfo.InvariantCulture),
                StrokesPerPlate(_job.Class).ToString("0.##", CultureInfo.InvariantCulture)));
    }
}
