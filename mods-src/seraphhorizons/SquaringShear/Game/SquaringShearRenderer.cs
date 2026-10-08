using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.SquaringShear.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.SquaringShear;

/// <summary>
/// Draws the squaring shear's moving parts (client only; created and disposed by
/// <see cref="BESquaringShear"/>, and only while its block is the shear). One mesh per rig part from
/// <c>shapes/block/squaringshear.json</c> (<see cref="MachineMeshes.PartMesh"/>), each drawn with its rig
/// matrix turned to the shear's facing, and only when its <c>requires</c> is fitted, or, for the
/// sheet, while that metal's plate is on the table. The texture codes <c>blade</c> and <c>gauge</c> are
/// set to the fitted blades' and gauge rods' metals (one mesh per part and pair of metals). The static
/// frame part is the block's own shape (squaringshear_frame.json) and is not drawn here.
/// <para>The rig is posed every frame from the view (<see cref="ISquaringShearView"/>) through
/// <see cref="SquaringShearClock"/>: θ the treadle clock, turning while the treadle is worked; W, k and
/// p. While the blade moves, metal dust at the cut (<c>edge</c>).</para>
/// </summary>
public sealed class SquaringShearRenderer : IRenderer
{
    public static readonly AssetLocation ShapeLoc = new(SquaringShearSystem.Domain, "shapes/block/squaringshear.json");
    private const int DrawRange = 48;
    private const float DustInterval = 0.12f;

    private readonly ICoreClientAPI _capi;
    private readonly BESquaringShear _be;
    private readonly ISquaringShearView _view;
    private readonly SquaringShearRig _rig;
    private readonly RigParts _parts;
    private readonly bool[] _drawn;
    // each part's meshes by the (blade metal, gauge metal) they were textured with
    private readonly Dictionary<(string?, string?), MultiTextureMeshRef?[]> _meshes = [];
    private Shape? _master;
    private bool _built;

    private readonly SquaringShearClock _clock = new();
    private float _dustTimer;

    private readonly Matrixf _model = new();

    public double RenderOrder => 0.5;
    public int RenderRange => DrawRange;

    public SquaringShearRenderer(ICoreClientAPI capi, BESquaringShear be, ISquaringShearView view, SquaringShearRig rig)
    {
        _capi = capi;
        _be = be;
        _view = view;
        _rig = rig;
        _parts = rig.MovingParts;
        _drawn = _parts.Parts.Select(p => p.Requires != null || p.Ride != null || p.Drivers.Count > 0).ToArray();
        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "squaringshear");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowFar, "squaringshear");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowNear, "squaringshear");
    }

    /// <summary>The parts' meshes with the blades in <paramref name="blade"/> and the gauge in
    /// <paramref name="gauge"/>, built the first time they are asked for.</summary>
    private MultiTextureMeshRef?[] Meshes(string? blade, string? gauge)
    {
        if (_meshes.TryGetValue((blade, gauge), out var meshes))
            return meshes;
        meshes = new MultiTextureMeshRef?[_drawn.Length];
        _meshes[(blade, gauge)] = meshes;
        if (!_built)
        {
            _built = true;
            _master = Shape.TryGet(_capi, ShapeLoc);
            if (_master == null)
                _capi.Logger.Error("[seraphhorizons] Squaring shear: {0} is missing; its moving parts are not drawn", ShapeLoc);
        }
        if (_master == null)
            return meshes;
        var tex = new FittedTextureSource(_capi.Tesselator.GetTextureSource(_be.Block),
            MachineMeshes.MetalTexture(_capi, blade), MachineMeshes.MetalTexture(_capi, gauge));
        for (int i = 0; i < meshes.Length; i++)
            if (_drawn[i] && MachineMeshes.PartMesh(_capi, _master, _parts, i, tex, "squaringshear") is { } mesh)
                meshes[i] = _capi.Render.UploadMultiTextureMesh(mesh);
        return meshes;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        var camPos = _capi.World.Player?.Entity?.CameraPos;
        if (camPos == null)
            return;
        var middle = _be.WorldPoint(new Float3(0.5f, 0.5f, 1));
        bool far = camPos.SquareDistanceTo(middle.X, middle.Y, middle.Z) > DrawRange * DrawRange;
        if (stage == EnumRenderStage.Opaque)
            AdvanceClock(deltaTime, far);
        if (far)
            return;

        var meshes = Meshes(_view.BladeMetal, _view.GaugeMetal);
        var mats = _parts.Matrices(_clock.Input());
        var facing = Mat4.Facing(_view.Side);
        var rapi = _capi.Render;
        rapi.GlDisableCullFace();
        IStandardShaderProgram? prog = null;
        if (stage == EnumRenderStage.Opaque)
        {
            // light from the cell over the table, outside the model
            var light = _be.CellPos(new Int3(0, 1, 1));
            prog = rapi.PreparedStandardShader(light.X, light.Y, light.Z);
            prog.ViewMatrix = rapi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rapi.CurrentProjectionMatrix;
        }
        for (int i = 0; i < meshes.Length; i++)
            if (meshes[i] is { } mesh && Shown(_parts.Parts[i].Requires))
                MachineMeshes.Draw(_capi, _model, mesh, Mat4.Multiply(facing, mats[i]), prog, camPos, _be.Pos);
        prog?.Stop();
        rapi.GlEnableCullFace();
    }

    private bool Shown(string? requires) =>
        requires is SquaringShearRequires.PlateLead or SquaringShearRequires.PlateCopper ? _clock.ShowsPlate(requires) : _view.PartFitted(requires);

    private void AdvanceClock(float dt, bool far)
    {
        int k = _view.PlateClass;
        bool held = _view.Held && k != 0;
        _clock.Advance(dt, k, _view.CutWork, held, _view.StrokesPerPlate(k));
        if (far || !held || !_rig.IsCutting(_clock.Work))
            return;
        if ((_dustTimer += dt) >= DustInterval)
        {
            _dustTimer = 0;
            SpawnDust(k);
        }
    }

    private void SpawnDust(int k)
    {
        var at = _be.WorldPoint(_rig.Edge);
        // fine metal off the blades: lead grey, copper red
        int colour = k == 2 ? ColorUtil.ToRgba(200, 70, 120, 190) : ColorUtil.ToRgba(200, 120, 110, 105);
        _capi.World.SpawnParticles(2, colour,
            at.AddCopy(-0.35, -0.02, -0.03), at.AddCopy(0.35, 0.02, 0.03),
            new Vec3f(-0.15f, 0f, -0.15f), new Vec3f(0.15f, 0.25f, 0.15f), 0.6f, 1f, 0.12f, EnumParticleModel.Cube, null);
    }

    public void Dispose()
    {
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.ShadowFar);
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.ShadowNear);
        foreach (var meshes in _meshes.Values)
            foreach (var mesh in meshes)
                mesh?.Dispose();
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
}
