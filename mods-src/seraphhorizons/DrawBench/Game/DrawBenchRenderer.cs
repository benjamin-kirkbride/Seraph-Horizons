using SeraphHorizons.Mod.DrawBench.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.DrawBench;

/// <summary>
/// Draws the draw bench's moving parts (client only; created and disposed by
/// <see cref="BEDrawBench"/>, and only while its block is the bench). One mesh per rig part from
/// <c>shapes/block/drawbench.json</c> (<see cref="MachineMeshes.PartMesh"/>), each drawn with its rig
/// matrix turned to the bench's facing, and only when its <c>requires</c> is fitted, or, for the
/// billets, while the clock shows that metal. The die's elements use the texture code <c>die</c>,
/// set to the fitted die's metal (one mesh per metal). The static frame part is the block's own
/// shape (drawbench_frame.json) and is not drawn here.
/// <para>The rig is posed every frame from the view (<see cref="IDrawBenchView"/>): θ, the shaft
/// angle about the entry shaft's native x axis (<see cref="MillMotion.NativeShaftAngle"/>),
/// accumulated; ψ, the total angle it turned either way; W, k and p from
/// <see cref="DrawBenchClock"/>; and the oil tank's fill for the oiler's level.</para>
/// <para>While the bench draws: metal dust at the die's mouth (<c>die</c>), and, with oil in the
/// tank, drips from the oiler's spout (<c>drip</c>).</para>
/// </summary>
public sealed class DrawBenchRenderer : IRenderer
{
    public static readonly AssetLocation ShapeLoc = new(DrawBenchSystem.Domain, "shapes/block/drawbench.json");
    private const int DrawRange = 48;
    private const float DustInterval = 0.15f;
    private const float DripInterval = 0.35f;

    private readonly ICoreClientAPI _capi;
    private readonly BEDrawBench _be;
    private readonly IDrawBenchView _view;
    private readonly DrawBenchRig _rig;
    private readonly RigParts _parts;
    private readonly MultiTextureMeshRef?[] _meshes;
    private readonly bool[] _drawn;
    private readonly bool[] _isDie;
    // the die's parts, one mesh each per die metal
    private readonly Dictionary<string, MultiTextureMeshRef?[]> _dieMeshes = [];
    private Shape? _master;
    private bool _built;

    private readonly DrawBenchClock _clock = new();
    private double _theta;
    private double _psi;
    private double _lastAngle;
    private bool _angleSeeded;
    private float _dustTimer;
    private float _dripTimer;

    private readonly Matrixf _model = new();

    public double RenderOrder => 0.5;
    public int RenderRange => DrawRange;

    public DrawBenchRenderer(ICoreClientAPI capi, BEDrawBench be, IDrawBenchView view, DrawBenchRig rig)
    {
        _capi = capi;
        _be = be;
        _view = view;
        _rig = rig;
        _parts = rig.MovingParts;
        int n = _parts.Parts.Count;
        _meshes = new MultiTextureMeshRef?[n];
        _drawn = new bool[n];
        _isDie = new bool[n];
        for (int i = 0; i < n; i++)
        {
            var p = _parts.Parts[i];
            _drawn[i] = p.Requires != null || p.Ride != null || p.Drivers.Count > 0;
            _isDie[i] = p.Requires == DrawBenchRequires.Name(DrawBenchStage.Die);
        }
        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "drawbench");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowFar, "drawbench");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowNear, "drawbench");
    }

    private void BuildMeshes()
    {
        _built = true;
        _master = Shape.TryGet(_capi, ShapeLoc);
        if (_master == null)
        {
            _capi.Logger.Error("[seraphhorizons] Draw bench: {0} is missing; its moving parts are not drawn", ShapeLoc);
            return;
        }
        var tex = _capi.Tesselator.GetTextureSource(_be.Block);
        for (int i = 0; i < _meshes.Length; i++)
        {
            if (!_drawn[i] || _isDie[i])
                continue;
            var mesh = MachineMeshes.PartMesh(_capi, _master, _parts, i, tex, "drawbench");
            if (mesh != null)
                _meshes[i] = _capi.Render.UploadMultiTextureMesh(mesh);
        }
    }

    /// <summary>The die's meshes in <paramref name="metal"/>, built the first time they are asked for.</summary>
    private MultiTextureMeshRef?[] DieMeshes(string metal)
    {
        if (_dieMeshes.TryGetValue(metal, out var meshes))
            return meshes;
        meshes = new MultiTextureMeshRef?[_meshes.Length];
        _dieMeshes[metal] = meshes;
        if (_master == null)
            return meshes;
        var tex = new DieTextureSource(_capi.Tesselator.GetTextureSource(_be.Block), MachineMeshes.MetalTexture(_capi, metal));
        for (int i = 0; i < meshes.Length; i++)
            if (_isDie[i] && MachineMeshes.PartMesh(_capi, _master, _parts, i, tex, "drawbench") is { } mesh)
                meshes[i] = _capi.Render.UploadMultiTextureMesh(mesh);
        return meshes;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        var camPos = _capi.World.Player?.Entity?.CameraPos;
        if (camPos == null)
            return;
        var middle = _be.WorldPoint(new Float3(0.5f, 0.5f, 2));
        bool far = camPos.SquareDistanceTo(middle.X, middle.Y, middle.Z) > DrawRange * DrawRange;
        if (stage == EnumRenderStage.Opaque)
        {
            if (!_built)
                BuildMeshes();
            AdvanceClocks(deltaTime, far);
        }
        if (far)
            return;

        var mats = _parts.Matrices(_clock.Input(_theta, _psi, _view.OilFill));
        var facing = Mat4.Facing(_view.Side);
        var rapi = _capi.Render;
        rapi.GlDisableCullFace();
        IStandardShaderProgram? prog = null;
        if (stage == EnumRenderStage.Opaque)
        {
            // light from the cell over the middle of the bench, outside the model
            var light = _be.CellPos(new Int3(0, 1, 2));
            prog = rapi.PreparedStandardShader(light.X, light.Y, light.Z);
            prog.ViewMatrix = rapi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rapi.CurrentProjectionMatrix;
        }
        var die = _view.DieMetal is { } metal ? DieMeshes(metal) : null;
        for (int i = 0; i < _meshes.Length; i++)
        {
            var mesh = _isDie[i] ? die?[i] : _meshes[i];
            if (mesh != null && Shown(_parts.Parts[i].Requires))
                MachineMeshes.Draw(_capi, _model, mesh, Mat4.Multiply(facing, mats[i]), prog, camPos, _be.Pos);
        }
        prog?.Stop();
        rapi.GlEnableCullFace();
    }

    private bool Shown(string? requires) =>
        requires is DrawBenchRequires.BilletLead or DrawBenchRequires.BilletCopper ? _clock.ShowsBillet(requires) : _view.PartFitted(requires);

    private void AdvanceClocks(float dt, bool far)
    {
        float speed = _view.ShaftSpeed;
        double angle = MillMotion.NativeShaftAngle(_view.Side, _view.ShaftAngle, Axis.X);
        double delta = 0;
        if (!_angleSeeded || speed <= 0)
        {
            _lastAngle = angle;
            _angleSeeded = true;
        }
        else
        {
            delta = MillMotion.WrappedDelta(_lastAngle, angle);
            _theta += delta;
            _psi += Math.Abs(delta);
            _lastAngle = angle;
            if (Math.Abs(_theta) > 1e6)
                _theta = 0;
        }

        int k = _view.JobClass;
        bool running = _view.Running;
        _clock.Advance(dt, Math.Abs(delta), k, _view.JobWork, running, _view.TurnsPerSection(k));

        if (far || !running)
            return;
        if ((_dustTimer += dt) >= DustInterval)
        {
            _dustTimer = 0;
            SpawnDust(k);
        }
        if (_view.OilFill > 0 && (_dripTimer += dt) >= DripInterval)
        {
            _dripTimer = 0;
            SpawnDrip();
        }
    }

    private void SpawnDust(int k)
    {
        var at = _be.WorldPoint(_rig.Die);
        // fine metal from the die's mouth: lead grey, copper red
        int colour = k == 2 ? ColorUtil.ToRgba(220, 70, 120, 190) : ColorUtil.ToRgba(220, 120, 110, 105);
        _capi.World.SpawnParticles(2, colour,
            at.AddCopy(-0.03, -0.03, -0.03), at.AddCopy(0.03, 0.03, 0.03),
            new Vec3f(-0.3f, -0.1f, -0.3f), new Vec3f(0.3f, 0.3f, 0.3f), 0.6f, 1f, 0.15f, EnumParticleModel.Cube, null);
        // and a wisp of the lubricant burning off
        if (_be.OilFill > 0 && _capi.World.Rand.NextDouble() < 0.25)
            _capi.World.SpawnParticles(1, ColorUtil.ToRgba(60, 200, 200, 200),
                at, at.AddCopy(0.02, 0.02, 0.02),
                new Vec3f(-0.05f, 0.1f, -0.05f), new Vec3f(0.05f, 0.25f, 0.05f), 1.2f, -0.02f, 0.4f, EnumParticleModel.Quad, null);
    }

    private void SpawnDrip()
    {
        var at = _be.WorldPoint(_rig.Drip);
        _capi.World.SpawnParticles(1, ColorUtil.ToRgba(190, 40, 140, 190),
            at.AddCopy(-0.01, -0.02, -0.01), at.AddCopy(0.01, 0, 0.01),
            new Vec3f(0, -0.4f, 0), new Vec3f(0, -0.2f, 0), 0.6f, 1f, 0.12f, EnumParticleModel.Quad, null);
    }

    public void Dispose()
    {
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.ShadowFar);
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.ShadowNear);
        foreach (var mesh in _meshes)
            mesh?.Dispose();
        foreach (var meshes in _dieMeshes.Values)
            foreach (var mesh in meshes)
                mesh?.Dispose();
    }

    /// <summary>The block's textures with the die's metal in place of <c>die</c>.</summary>
    private sealed class DieTextureSource(ITexPositionSource inner, TextureAtlasPosition? metal) : ITexPositionSource
    {
        public Size2i AtlasSize => inner.AtlasSize!;
        public TextureAtlasPosition this[string textureCode] =>
            textureCode == "die" && metal != null ? metal : inner[textureCode]!;
    }
}
