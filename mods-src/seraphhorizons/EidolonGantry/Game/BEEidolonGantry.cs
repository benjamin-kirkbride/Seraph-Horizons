using System.Globalization;
using System.Text;
using SeraphHorizons.Mod.EidolonGantry.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.EidolonGantry;

/// <summary>
/// The eidolon gantry's controller (the foot of its front right post), on the shared machine base
/// (<see cref="BEMachine"/>: its winch the staged build from <c>config/builds/eidolongantry.json</c>, in
/// the gantry's wood (<see cref="GantryBuild.ForWood"/>), its cells, its block info, its renderer). It
/// keeps the winch's depth (0 hung, 1 let down; nothing drives it yet) and the eidolon's body on the spine
/// (<see cref="Body"/>, <see cref="GantryBody"/>: the body stages, waking it, the dock), which takes every
/// click the winch does not and the crank's turns. Hand-built and not driven: no power, no oil. Its hollow
/// cells, the open middle the body hangs in and walks out of and the crank's, have no boxes. The rules are
/// EidolonGantry/Core's.
/// </summary>
public class BEEidolonGantry : BEMachine
{
    /// <summary>How fast the drawn depth follows the gantry's, per second (a full let-down in 8 s).</summary>
    public const double DepthRate = 0.125;

    private double _depth;
    private double? _shownDepth;
    private readonly GantryBody _body;

    public BEEidolonGantry()
    {
        _body = new GantryBody(this);
    }

    /// <summary>The gantry's wood, its <c>wood</c> variant: the drum's planks and the spine's beams are of it.</summary>
    public string Wood => Block?.Variant?["wood"] ?? "oak";

    /// <summary>The eidolon's body on the spine, and the dock.</summary>
    public GantryBody Body => _body;

    /// <summary>The winch and the spine are fitted: the body can be built on the spine.</summary>
    public bool WinchComplete => Built;

    public new GantryRig? Rig => base.Rig as GantryRig;

    /// <summary>How far the winch has let the spine (and whatever hangs on it) down, 0..1: 0 hung, 1
    /// the body's feet on the floor (the rig's <c>depth</c>). Set on the server; synced.</summary>
    public double WinchDepth
    {
        get => _depth;
        set
        {
            double d = Math.Clamp(double.IsFinite(value) ? value : 0, 0, 1);
            if (d == _depth)
                return;
            _depth = d;
            MarkDirty(true);
        }
    }

    protected override MachineBuild? BuildSpec(IWorldAccessor world) =>
        base.BuildSpec(world) is { } build ? GantryBuild.ForWood(build, Wood) : null;

    protected override bool HollowCellsOpen => true;

    // the creative shortcut fits the body's stages too, once the winch is built
    protected override bool CreativeFills => !Built || !_body.Complete;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        _body.Initialize(api);
    }

    // ---- Footprint ----

    /// <summary>How far from where the body hangs (the rig's <c>body</c> point) an eidolon counts as
    /// standing in the gantry, its dock (<see cref="Docks"/>), blocks.</summary>
    public const double DockRadius = 1.25;

    /// <summary>Whether an entity at <paramref name="pos"/> stands in this gantry's dock: within
    /// <see cref="DockRadius"/> of where the body hangs, level with the gantry's floor (within a block).</summary>
    public bool Docks(Vec3d pos)
    {
        if (Rig is not { } rig)
            return false;
        var body = WorldPoint(rig.Body);
        return pos.HorizontalSquareDistanceTo(body) <= DockRadius * DockRadius && Math.Abs(pos.Y - body.Y) <= 1;
    }

    /// <summary>The gantry whose dock <paramref name="pos"/> (an entity's feet) is in, or null: the
    /// gantry cell there (a cell, or the controller) and <see cref="Docks"/>. For repair in the
    /// gantry (#674, <c>EntityBehaviorEidolonRepair.GantryAround</c>). Server or client, wherever the cell's block entity is loaded.</summary>
    public static BEEidolonGantry? DockAt(IBlockAccessor blockAccessor, Vec3d pos) =>
        At(blockAccessor, pos.AsBlockPos) is { } gantry && gantry.Docks(pos) ? gantry : null;

    /// <summary>The gantry <paramref name="pos"/> is a cell of (its controller, or a cell pointing at
    /// one), or null.</summary>
    public static BEEidolonGantry? At(IBlockAccessor blockAccessor, BlockPos pos) =>
        MachineFire.MachineAt(blockAccessor, pos) as BEEidolonGantry;

    /// <summary>Where the body hangs (the rig's <c>body</c> point) in the world: where a woken eidolon
    /// stands as it wakes and a docking one backs onto the spine from. Null without the rig.</summary>
    public Vec3d? BodyPoint => Rig is { } rig ? WorldPoint(rig.Body) : null;

    /// <summary>The yaw of an eidolon facing out of the open front (the rig's <c>exitSide</c>).</summary>
    public float OutwardYaw
    {
        get
        {
            var outward = Outward;
            return (float)Math.Atan2(outward.X, outward.Z);
        }
    }

    /// <summary>A unit step out of the open front, in the world.</summary>
    public Vec3d Outward => (Rig is { } rig ? WorldSide(rig.ExitSide) : Side.West) switch
    {
        Side.North => new Vec3d(0, 0, -1),
        Side.South => new Vec3d(0, 0, 1),
        Side.East => new Vec3d(1, 0, 0),
        _ => new Vec3d(-1, 0, 0),
    };

    /// <summary>Where a woken eidolon walks to: <paramref name="blocks"/> past the open front's middle
    /// (the rig's <c>exit</c>), on a block's corner. Null without the rig.</summary>
    public Vec3d? WalkOutTarget(double blocks)
    {
        if (Rig is not { } rig)
            return null;
        var exit = WorldPoint(rig.Exit);
        var outward = Outward;
        return new Vec3d(Math.Round(exit.X + outward.X * blocks), exit.Y, Math.Round(exit.Z + outward.Z * blocks));
    }

    /// <summary>The world side a native side is turned to (the rig's <c>exitSide</c>, west).</summary>
    public Side WorldSide(Side native) => Footprint.ToWorld(native, Side);

    /// <summary>The crank's selection box in its cell (cell-local, native frame): the crank's web and
    /// handle and the ratchet, from the post's outer face out. Selection only: the cell stays hollow,
    /// nothing to collide with.</summary>
    public static readonly Box CrankBox = new(0.05f, 0.05f, 0f, 1f, 1f, 0.55f);

    /// <summary>The winch's crank is fitted.</summary>
    public bool CrankFitted => Assembly?.Build.Fitted("crank") == true;

    /// <summary>Whether <paramref name="cellPos"/> is the winch crank's cell.</summary>
    public bool IsCrankCell(BlockPos? cellPos) => cellPos != null && Rig is { } rig && LocalOf(cellPos) == rig.CrankCell;

    /// <summary>A cell's selection boxes: its own, and in the crank's cell, once the crank is fitted, the
    /// crank (<see cref="CrankBox"/>), so it can be turned.</summary>
    public override Cuboidf[]? SelectionBoxes(BlockPos cellPos)
    {
        if (CrankFitted && IsCrankCell(cellPos))
        {
            var w = Footprint.ToWorld(CrankBox, Side);
            return [new Cuboidf(w.X1, w.Y1, w.Z1, w.X2, w.Y2, w.Z2)];
        }
        return base.SelectionBoxes(cellPos);
    }

    // ---- Interaction ----

    /// <summary>
    /// Before the winch's build: a click on the crank, once fitted, is a turn of it (the body's: it wakes a
    /// complete eidolon); once the winch is built, the creative shortcut fits the body's next stage, and
    /// every other click is offered to the body first (its layers take rods, plates, nails and chain, which
    /// the winch did too). A winch part the body does not take then goes on to the build, which refuses it.
    /// </summary>
    protected override bool? InteractContent(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel)
    {
        bool server = Api.Side == EnumAppSide.Server;
        if (CrankFitted && IsCrankCell(blockSel?.Position))
        {
            if (!_body.OnCrank(byPlayer) && server)
                Refuse(byPlayer, "error-crank-nothing");
            return true;
        }
        if (!Built)
            return null;
        if (CreativeShortcut(byPlayer))
        {
            if (server)
                _body.CreativeFitNext(byPlayer);
            return true;
        }
        return _body.OnInteract(byPlayer, slot) ? true : null;
    }

    /// <summary>After the winch's build: a body item before the spine is hung (the body says it needs it).</summary>
    protected override bool InteractWork(IPlayer byPlayer, ItemSlot slot, BlockSelection blockSel) =>
        !Built && _body.OnInteract(byPlayer, slot);

    /// <summary>Fits every winch stage still open, nothing taken (server side; the scenarios').</summary>
    public void FitAll()
    {
        if (Assembly is not { } assembly)
            return;
        while (!assembly.Built && assembly.FillNext())
        {
        }
        MarkDirty(true);
    }

    /// <summary>Sends <paramref name="byPlayer"/> the gantry's error <paramref name="key"/>
    /// (<c>seraphhorizons:eidolongantry-{key}</c>); false, for returning.</summary>
    public bool Refuse(IPlayer? byPlayer, string key, params object[] args)
    {
        if (byPlayer is IServerPlayer sp)
            sp.SendIngameError("eidolongantry-" + key, Lang.GetL(sp.LanguageCode, EidolonGantrySystem.Domain + ":eidolongantry-" + key, args));
        return false;
    }

    /// <summary>The item or block of <paramref name="code"/> in this game, or null.</summary>
    public CollectibleObject? Collectible(string code)
    {
        var loc = new AssetLocation(code);
        if (Api.World.GetItem(loc) is { Id: > 0 } item)
            return item;
        return Api.World.GetBlock(loc) is { Id: > 0 } block ? block : null;
    }

    protected override void WorkHelp(IPlayer? forPlayer, List<WorldInteraction> help) => help.AddRange(_body.Help(forPlayer));

    // ---- Drawing ----

    /// <summary>A body stage's parts are drawn by the body's rules (<see cref="GantryBody.Shows"/>), the
    /// winch's by its build.</summary>
    public override bool ShowsPart(string? requires) =>
        GantryRequires.IsBodyStage(requires) ? _body.Shows(requires!) : base.ShowsPart(requires);

    /// <summary>The rig posed by the winch's depth, eased at <see cref="DepthRate"/>: letting down turns the
    /// crank, layshaft, drum and sheave, throws the pawl off, pays out the chain and brings the hook, the
    /// spine and the body down. The crank's clock θ moves nothing.</summary>
    public override RigInput FrameInput(float dt, bool far)
    {
        double target = _depth, shown = _shownDepth ?? target, step = DepthRate * dt;
        _shownDepth = Math.Abs(target - shown) <= step ? target : shown + Math.Sign(target - shown) * step;
        return new RigInput(0, Depth: _shownDepth.Value);
    }

    /// <summary>The hung body's layers, each in its material: the parts are drawn again when they change.</summary>
    public override string TextureKey =>
        string.Join(";", _body.Textures.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value));

    /// <summary>The block's textures (the frame's wood), with the body's layer codes in the layers it wears.</summary>
    public override ITexPositionSource PartTextures(ICoreClientAPI capi) =>
        new LayerTextureSource(capi, base.PartTextures(capi), _body.Textures);

    /// <summary>A block's textures with the body's layer codes drawn in the textures it wears (asset
    /// paths under <c>textures/</c>), each put in the block atlas the first time it is asked for.</summary>
    private sealed class LayerTextureSource(ICoreClientAPI capi, ITexPositionSource inner, IReadOnlyDictionary<string, string> layers) : ITexPositionSource
    {
        public Size2i AtlasSize => inner.AtlasSize!;

        public TextureAtlasPosition this[string textureCode]
        {
            get
            {
                if (!layers.TryGetValue(textureCode, out var path))
                    return inner[textureCode]!;
                var loc = new AssetLocation(path);
                var pos = capi.BlockTextureAtlas[loc];
                if (pos == null && capi.Assets.Exists(loc.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png")))
                    capi.BlockTextureAtlas.GetOrInsertTexture(loc, out _, out pos);
                return pos ?? inner[textureCode]!;
            }
        }
    }

    // ---- Breaking ----

    /// <summary>Every winch part fitted, as many as each stage took, and everything the body holds.</summary>
    public override IEnumerable<ItemStack> AssemblyDrops() => base.AssemblyDrops().Concat(_body.Drops());

    public override void OnBlockRemoved()
    {
        _body.OnBlockRemoved();
        base.OnBlockRemoved();
    }

    // ---- Saving and syncing ----

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetDouble("depth", _depth);
        _body.ToTreeAttributes(tree);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        _depth = Math.Clamp(tree.GetDouble("depth"), 0, 1);
        _body.FromTreeAttributes(tree);
    }

    // ---- Info ----

    protected override void AppendContents(StringBuilder dsc) => _body.AppendInfo(dsc);

    protected override void AppendDetail(StringBuilder dsc)
    {
        string L(string key, params object[] args) => Lang.Get(EidolonGantrySystem.Domain + ":eidolongantry-" + key, args);
        if (Built)
            dsc.AppendLine(L("info-depth", Math.Round(100 * _depth).ToString(CultureInfo.InvariantCulture)));
        if (_body.HoldsEidolon)
            dsc.AppendLine(L("info-docked-id", _body.DockedId, L(_body.DockedEidolon != null ? "info-docked-loaded" : "info-docked-unloaded")));
    }
}
