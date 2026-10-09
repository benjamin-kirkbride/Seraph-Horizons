using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.PressBrake.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.PressBrake;

/// <summary>
/// Draws the press brake's moving parts (client only; created and disposed by
/// <see cref="BEPressBrake"/>, and only while its block is the brake). One mesh per rig part from
/// <c>shapes/block/pressbrake.json</c> (<see cref="MachineMeshes.PartMesh"/>), each drawn with its rig
/// matrix turned to the brake's facing, and only when its <c>requires</c> is fitted, or, for the
/// sheet, while that metal's plate is on the bed. The texture code <c>edge</c> is set to the fitted
/// edges' metal (one mesh per part and metal); the screws, made from metal parts, are always the
/// block's own <c>cupronickel</c>. The static frame part is
/// the block's own shape (pressbrake_frame.json) and is not drawn here.
/// <para>The rig is posed every frame from the view (<see cref="IPressBrakeView"/>) through
/// <see cref="PressBrakeClock"/>: θ the lever clock, turning while the lever is worked; W, k and p.
/// While the leaf swings, metal dust at the folding edge (<c>edge</c>).</para>
/// </summary>
public sealed class PressBrakeRenderer : IRenderer
{
    public static readonly AssetLocation ShapeLoc = new(PressBrakeSystem.Domain, "shapes/block/pressbrake.json");
    private const int DrawRange = 48;
    private const float DustInterval = 0.12f;

    private readonly ICoreClientAPI _capi;
    private readonly BEPressBrake _be;
    private readonly IPressBrakeView _view;
    private readonly PressBrakeRig _rig;
    private readonly RigParts _parts;
    private readonly bool[] _drawn;
    // each part's meshes by the edge metal they were textured with ("" for none)
    private readonly Dictionary<string, MultiTextureMeshRef?[]> _meshes = [];
    private Shape? _master;
    private bool _built;

    private readonly PressBrakeClock _clock = new();
    private float _dustTimer;

    private readonly Matrixf _model = new();

    public double RenderOrder => 0.5;
    public int RenderRange => DrawRange;

    public PressBrakeRenderer(ICoreClientAPI capi, BEPressBrake be, IPressBrakeView view, PressBrakeRig rig)
    {
        _capi = capi;
        _be = be;
        _view = view;
        _rig = rig;
        _parts = rig.MovingParts;
        _drawn = _parts.Parts.Select(p => p.Requires != null || p.Ride != null || p.Drivers.Count > 0).ToArray();
        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "pressbrake");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowFar, "pressbrake");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowNear, "pressbrake");
    }

    /// <summary>The parts' meshes with the edges in <paramref name="edge"/>, built the first time they
    /// are asked for.</summary>
    private MultiTextureMeshRef?[] Meshes(string? edge)
    {
        if (_meshes.TryGetValue(edge ?? "", out var meshes))
            return meshes;
        meshes = new MultiTextureMeshRef?[_drawn.Length];
        _meshes[edge ?? ""] = meshes;
        if (!_built)
        {
            _built = true;
            _master = Shape.TryGet(_capi, ShapeLoc);
            if (_master == null)
                _capi.Logger.Error("[seraphhorizons] Press brake: {0} is missing; its moving parts are not drawn", ShapeLoc);
        }
        if (_master == null)
            return meshes;
        var tex = new FittedTextureSource(_capi.Tesselator.GetTextureSource(_be.Block), MachineMeshes.MetalTexture(_capi, edge));
        for (int i = 0; i < meshes.Length; i++)
            if (_drawn[i] && MachineMeshes.PartMesh(_capi, _master, _parts, i, tex, "pressbrake") is { } mesh)
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

        var meshes = Meshes(_view.EdgeMetal);
        var mats = _parts.Matrices(_clock.Input());
        var facing = Mat4.Facing(_view.Side);
        var rapi = _capi.Render;
        rapi.GlDisableCullFace();
        IStandardShaderProgram? prog = null;
        if (stage == EnumRenderStage.Opaque)
        {
            // light from the cell over the bed, outside the model
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
        requires is PressBrakeRequires.PlateLead or PressBrakeRequires.PlateCopper ? _clock.ShowsPlate(requires) : _view.PartFitted(requires);

    private void AdvanceClock(float dt, bool far)
    {
        int k = _view.PlateClass;
        bool held = _view.Held && k != 0;
        _clock.Advance(dt, k, _view.FoldWork, held, _view.LeverTurnsPerPlate(k));
        if (far || !held || !_rig.IsFolding(_clock.Work))
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
        // fine metal off the folding edge: lead grey, copper red
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

    /// <summary>The block's textures with the edges' metal in place of <c>edge</c>.</summary>
    private sealed class FittedTextureSource(ITexPositionSource inner, TextureAtlasPosition? edge) : ITexPositionSource
    {
        public Size2i AtlasSize => inner.AtlasSize!;
        public TextureAtlasPosition this[string textureCode] =>
            textureCode == "edge" && edge != null ? edge : inner[textureCode]!;
    }
}
