using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.CrucibleFurnace;

/// <summary>
/// Draws the crucible furnace's parts by its state (client only; created and disposed by
/// <see cref="BECrucibleFurnace"/> in place of the shared <see cref="MachineRenderer"/>, which draws one texture
/// set and no glow, and only while its block is the furnace). One mesh per rig part
/// from <c>shapes/block/cruciblefurnace.json</c>; the iron frame is the block's own shape
/// (cruciblefurnace_frame.json) and is not drawn here. Each part is drawn turned to the furnace's
/// facing, and only when gameplay shows it:
/// <list type="bullet">
/// <item>the stages fitted: the brickwork and the hinge's iron, the lining, the lid (at its eased
/// depth, 0 open, 1 shut, turning through its swing in <see cref="BECrucibleFurnace.SwingMs"/>);</item>
/// <item>the brickwork in the furnace's brick type: the shape's <c>brick</c> code set to the rig's
/// texture for that type, a composite of its base and any overlay (six of the nine types are the
/// cream brick with a colour drawn over it), one mesh set per type;</item>
/// <item>the damper in its place, the outlet's cap, each pot, its charge (unmelted) or its melt (in the
/// melt's metal), the coke's layers <c>coke1..ceil(coke / cokePerLayer)</c>, burning (<c>fire…</c>)
/// while lit;</item>
/// <item>the glow: the burning coke and the melts glow by the fire's temperature, not the model's fixed
/// glow, which is taken off their meshes.</item>
/// </list>
/// </summary>
public sealed class CrucibleFurnaceRenderer : IRenderer
{
    public static readonly AssetLocation ShapeLoc = CrucibleFurnaceSystem.ShapeAsset;
    private const int DrawRange = 48;
    private const string Name = "cruciblefurnace";

    private readonly ICoreClientAPI _capi;
    private readonly BECrucibleFurnace _be;
    private readonly CrucibleFurnaceRig _rig;
    private readonly RigParts _parts;
    private readonly bool[] _drawn;
    private readonly bool[] _brick;
    private readonly bool[] _metal;
    private readonly bool[] _glows;
    private readonly int _frame;
    private PartMeshSet? _base;
    private readonly Dictionary<string, PartMeshSet?> _variants = [];
    private bool _built;
    private double _depth = -1;
    private readonly Matrixf _model = new();

    public double RenderOrder => 0.5;
    public int RenderRange => DrawRange;

    public CrucibleFurnaceRenderer(ICoreClientAPI capi, BECrucibleFurnace be, CrucibleFurnaceRig rig)
    {
        _capi = capi;
        _be = be;
        _rig = rig;
        _parts = rig.MovingParts;
        _frame = _parts.Parts.Select((p, i) => (p, i)).FirstOrDefault(x => x.p.Requires == null && x.p.Drivers.Count == 0, (null!, -1)).Item2;
        _drawn = _parts.Parts.Select((p, i) => i != _frame).ToArray();
        _brick = _parts.Parts.Select(p => p.Requires == FurnaceRequires.Brickwork && p.Id == FurnaceRequires.Brickwork).ToArray();
        _metal = _parts.Parts.Select(p => p.Requires is { } r && (r.StartsWith("melt", StringComparison.Ordinal) || r.StartsWith("charge", StringComparison.Ordinal))).ToArray();
        _glows = _parts.Parts.Select(p => p.Requires is { } r && (r.StartsWith("melt", StringComparison.Ordinal) || r.StartsWith("fire", StringComparison.Ordinal))).ToArray();
        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, Name);
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowFar, Name);
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowNear, Name);
    }

    // the glow the model bakes into the burning coke and the melts is drawn by temperature instead
    private void TakeGlowOff(int part, MeshData mesh)
    {
        if (!_glows[part] || mesh.Flags == null)
            return;
        for (int v = 0; v < mesh.VerticesCount; v++)
            mesh.Flags[v] &= ~VertexFlags.GlowLevelBitMask;
    }

    private void BuildMeshes()
    {
        _built = true;
        _base = MachinePartMeshes.Get(_capi, "parts", ShapeLoc, _parts, i => _drawn[i] && !_brick[i] && !_metal[i],
            () => _capi.Tesselator.GetTextureSource(_be.Block), Name, TakeGlowOff);
        if (_base == null)
            _capi.Logger.Error("[seraphhorizons] Crucible furnace: {0} is missing; it is drawn as its iron frame alone", ShapeLoc);
    }

    /// <summary>The brickwork in brick type <paramref name="type"/>, built (or found in the cache) the
    /// first time it is asked for.</summary>
    private PartMeshSet? BrickMeshes(string type)
    {
        string key = "brick|" + type;
        if (_variants.TryGetValue(key, out var set))
            return set;
        var brick = _rig.Bricks.Get(type) ?? _rig.Bricks.Get(_rig.Bricks.Default);
        set = MachinePartMeshes.Get(_capi, key, ShapeLoc, _parts, i => _brick[i],
            () => new CodeTextureSource(_be.Block, _capi, _rig.Bricks.TextureCode, BrickTexture(brick)), Name);
        _variants[key] = set;
        return set;
    }

    private TextureAtlasPosition? BrickTexture(BrickType? brick)
    {
        if (brick == null)
            return null;
        var composite = new CompositeTexture(new AssetLocation(brick.Base))
        {
            BlendedOverlays = brick.Overlays.Select(o => new BlendedOverlayTexture { Base = new AssetLocation(o) }).ToArray(),
        };
        return _capi.BlockTextureAtlas.GetOrInsertTexture(composite, out _, out var pos) ? pos : null;
    }

    /// <summary>The charges and melts in <paramref name="metal"/>'s ingot texture.</summary>
    private PartMeshSet? MetalMeshes(string? metal)
    {
        string key = "metal|" + (metal ?? "");
        if (_variants.TryGetValue(key, out var set))
            return set;
        var tex = MachineMeshes.MetalTexture(_capi, metal);
        set = MachinePartMeshes.Get(_capi, key, ShapeLoc, _parts, i => _metal[i],
            () => new CodeTextureSource(_be.Block, _capi, null, tex, "melt", "charge"), Name, TakeGlowOff);
        _variants[key] = set;
        return set;
    }

    /// <summary>The metal a pot's charge or melt is drawn in: the melt's output metal; a charge's iron
    /// (or stainless, remelted); null for the shape's own.</summary>
    private string? MetalOf(FurnacePot pot, bool melt)
    {
        var recipes = CrucibleFurnaceSystem.Of(_capi).Recipes;
        if (melt)
            return recipes.Get(pot.Melted) is { Pourable: true } r && CrucibleFurnaceSystem.Resolve(_capi.World, r.Output)?.Variant["metal"] is { } m ? m : null;
        var first = pot.Charge.FirstOrDefault().Code;
        return first == Stainless.Bit || first == Stainless.Ingot ? "stainlesssteel" : "iron";
    }

    /// <summary>Whether part <paramref name="i"/> is drawn now, and the mesh set it is drawn from.</summary>
    private PartMeshSet? Shown(int i)
    {
        if (_be.Assembly is not { } assembly || !_drawn[i])
            return null;
        string? requires = _parts.Parts[i].Requires;
        if (!assembly.Shows(requires))
            return null;
        if (_brick[i])
            return BrickMeshes(_be.BrickType ?? _rig.Bricks.Default);
        switch (requires)
        {
            case null or FurnaceRequires.Brickwork or FurnaceRequires.Lining or FurnaceRequires.Lid:
                return _base;
            case FurnaceRequires.OutletCap:
                return _be.Capped && _be.LiningIn ? _base : null;
        }
        if (!assembly.Built)
            return null;
        for (int p = 0; p < _rig.Figures.Pots; p++)
        {
            var pot = _be.Pot(p);
            if (requires == FurnaceRequires.Pot(p))
                return pot != null ? _base : null;
            if (requires == FurnaceRequires.Charge(p))
                return pot is { Melted: null, Charge.Count: > 0 } ? MetalMeshes(MetalOf(pot, false)) : null;
            if (requires == FurnaceRequires.Melt(p))
                return pot is { Melted: not null } ? MetalMeshes(MetalOf(pot, true)) : null;
        }
        int layers = Math.Min(_rig.Figures.CokeLayers, (int)Math.Ceiling(_be.Coke / _rig.Figures.CokePerLayer - 1e-9));
        for (int l = 1; l <= _rig.Figures.CokeLayers; l++)
        {
            if (requires == FurnaceRequires.Coke(l))
                return l <= layers && !_be.Lit ? _base : null;
            if (requires == FurnaceRequires.Fire(l))
                return l <= layers && _be.Lit ? _base : null;
        }
        return requires == FurnaceRequires.DamperPart(_be.Damper) ? _base : null;
    }

    /// <summary>The glow by the fire's temperature: none below 500 °C, full at 1300 °C and over.</summary>
    private int Glow(int part)
    {
        if (!_glows[part])
            return 0;
        double t = _be.Temperature;
        double f = Math.Clamp((t - 500) / 800, 0, 1);
        bool fire = _parts.Parts[part].Requires?.StartsWith("fire", StringComparison.Ordinal) == true;
        return (int)Math.Round(f * (fire ? 200 : 255));
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        var camPos = _capi.World.Player?.Entity?.CameraPos;
        if (camPos == null)
            return;
        if (stage == EnumRenderStage.Opaque)
        {
            if (!_built)
                BuildMeshes();
            double target = _be.LidClosed ? 1 : 0;
            double step = deltaTime * 1000.0 / BECrucibleFurnace.SwingMs;
            _depth = _depth < 0 ? target : Math.Abs(target - _depth) <= step ? target : _depth + Math.Sign(target - _depth) * step;
        }
        if (_base == null)
            return;
        var middle = _be.WorldPoint(new Float3(0.5f, 1, 1.5f));
        if (camPos.SquareDistanceTo(middle.X, middle.Y, middle.Z) > DrawRange * DrawRange)
            return;

        // eased in and out of the swing
        double eased = _depth * _depth * (3 - 2 * _depth);
        var mats = _parts.Matrices(new RigInput(0, Depth: Math.Max(0, eased)));
        var facing = Mat4.Facing(_be.Side);
        var rapi = _capi.Render;
        rapi.GlDisableCullFace();
        IStandardShaderProgram? prog = null;
        if (stage == EnumRenderStage.Opaque)
        {
            // light from over the hole's front row, which nothing fills but the swinging lid
            var light = _be.CellPos(new Int3(0, 2, 0));
            prog = MachineShading.Prepare(_capi, light);
            prog.ViewMatrix = rapi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rapi.CurrentProjectionMatrix;
        }
        for (int i = 0; i < _parts.Parts.Count; i++)
        {
            if (Shown(i)?.Meshes[i] is not { } mesh)
                continue;
            int glow = prog != null ? Glow(i) : 0;
            if (prog != null)
                prog.ExtraGlow = glow;
            MachineMeshes.Draw(_capi, _model, mesh, Mat4.Multiply(facing, mats[i]), prog, camPos, _be.Pos);
        }
        if (prog != null)
            prog.ExtraGlow = 0;
        prog?.Stop();
        rapi.GlEnableCullFace();
    }

    public void Dispose()
    {
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.ShadowFar);
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.ShadowNear);
    }

    /// <summary>A block's textures with some codes drawn in another texture: <paramref name="code"/>
    /// (and <paramref name="more"/>) as <paramref name="pos"/>, when there is one.</summary>
    private sealed class CodeTextureSource(Block block, ICoreClientAPI capi, string? code, TextureAtlasPosition? pos, params string[] more) : ITexPositionSource
    {
        private readonly ITexPositionSource _inner = capi.Tesselator.GetTextureSource(block);
        public Size2i AtlasSize => _inner.AtlasSize!;
        public TextureAtlasPosition this[string textureCode] =>
            pos != null && (textureCode == code || more.Contains(textureCode)) ? pos : _inner[textureCode]!;
    }
}
