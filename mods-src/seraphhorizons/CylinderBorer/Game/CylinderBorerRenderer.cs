using SeraphHorizons.Mod.CylinderBorer.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.CylinderBorer;

/// <summary>
/// Draws the cylinder borer's moving parts (client only; created and disposed by
/// <see cref="BECylinderBorer"/>, and only while its block is the borer). One mesh per rig part from
/// <c>shapes/block/cylinderborer.json</c> (<see cref="MachineMeshes.PartMesh"/>), each drawn with its
/// rig matrix turned to the borer's facing, and only when its <c>requires</c> is fitted, or, for a
/// casting's parts (the casting, its skin and its chains), while the clock shows that metal's casting
/// in the cradle. Each metal's casting is its own parts, in ppex's iron or steel texture, so no
/// texture is swapped. The static frame part is the block's own shape (cylinderborer_frame.json)
/// and is not drawn here.
/// <para>The rig is posed every frame from the view (<see cref="ICylinderBorerView"/>): θ, the shaft
/// angle about the entry shaft's native z axis (<see cref="MillMotion.NativeShaftAngle"/>),
/// accumulated; ψ, the total angle it turned either way; W, k and p from
/// <see cref="CylinderBorerClock"/>; and the oil tank's fill for the oil cups' level.</para>
/// <para>While the borer cuts: metal chips at the bore's drive end (<c>chips</c>), and, with oil in
/// the tank, drips from the far bushing's oil cup (<c>drip</c>).</para>
/// </summary>
public sealed class CylinderBorerRenderer : IRenderer
{
    public static readonly AssetLocation ShapeLoc = new(CylinderBorerSystem.Domain, "shapes/block/cylinderborer.json");
    private const int DrawRange = 48;
    private const float ChipInterval = 0.2f;
    private const float DripInterval = 0.45f;

    private readonly ICoreClientAPI _capi;
    private readonly BECylinderBorer _be;
    private readonly ICylinderBorerView _view;
    private readonly CylinderBorerRig _rig;
    private readonly RigParts _parts;
    private readonly MultiTextureMeshRef?[] _meshes;
    private readonly bool[] _drawn;
    private bool _built;

    private readonly CylinderBorerClock _clock = new();
    private double _theta;
    private double _psi;
    private double _lastAngle;
    private bool _angleSeeded;
    private float _chipTimer;
    private float _dripTimer;

    private readonly Matrixf _model = new();

    public double RenderOrder => 0.5;
    public int RenderRange => DrawRange;

    public CylinderBorerRenderer(ICoreClientAPI capi, BECylinderBorer be, ICylinderBorerView view, CylinderBorerRig rig)
    {
        _capi = capi;
        _be = be;
        _view = view;
        _rig = rig;
        _parts = rig.MovingParts;
        int n = _parts.Parts.Count;
        _meshes = new MultiTextureMeshRef?[n];
        _drawn = new bool[n];
        for (int i = 0; i < n; i++)
        {
            var p = _parts.Parts[i];
            _drawn[i] = p.Requires != null || p.Ride != null || p.Drivers.Count > 0;
        }
        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "cylinderborer");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowFar, "cylinderborer");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowNear, "cylinderborer");
    }

    private void BuildMeshes()
    {
        _built = true;
        var master = Shape.TryGet(_capi, ShapeLoc);
        if (master == null)
        {
            _capi.Logger.Error("[seraphhorizons] Cylinder borer: {0} is missing; its moving parts are not drawn", ShapeLoc);
            return;
        }
        var tex = _capi.Tesselator.GetTextureSource(_be.Block);
        for (int i = 0; i < _meshes.Length; i++)
        {
            if (!_drawn[i])
                continue;
            var mesh = MachineMeshes.PartMesh(_capi, master, _parts, i, tex, "cylinderborer");
            if (mesh != null)
                _meshes[i] = _capi.Render.UploadMultiTextureMesh(mesh);
        }
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        var camPos = _capi.World.Player?.Entity?.CameraPos;
        if (camPos == null)
            return;
        var middle = _be.WorldPoint(new Float3(0.5f, 1f, 2));
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
            // light from the cell over the middle of the borer, outside the model
            var light = _be.CellPos(new Int3(0, 2, 2));
            prog = rapi.PreparedStandardShader(light.X, light.Y, light.Z);
            prog.ViewMatrix = rapi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rapi.CurrentProjectionMatrix;
        }
        for (int i = 0; i < _meshes.Length; i++)
        {
            var mesh = _meshes[i];
            if (mesh != null && Shown(_parts.Parts[i].Requires))
                MachineMeshes.Draw(_capi, _model, mesh, Mat4.Multiply(facing, mats[i]), prog, camPos, _be.Pos);
        }
        prog?.Stop();
        rapi.GlEnableCullFace();
    }

    private bool Shown(string? requires) =>
        requires is CylinderBorerRequires.BlankIron or CylinderBorerRequires.BlankSteel ? _clock.ShowsBlank(requires) : _view.PartFitted(requires);

    private void AdvanceClocks(float dt, bool far)
    {
        float speed = _view.ShaftSpeed;
        double angle = MillMotion.NativeShaftAngle(_view.Side, _view.ShaftAngle, Axis.Z);
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
        _clock.Advance(dt, Math.Abs(delta), k, _view.JobWork, running, _view.TurnsPerBlock(k));

        if (far || !running)
            return;
        if ((_chipTimer += dt) >= ChipInterval)
        {
            _chipTimer = 0;
            SpawnChips(k);
        }
        if (_view.OilFill > 0 && (_dripTimer += dt) >= DripInterval)
        {
            _dripTimer = 0;
            SpawnDrip();
        }
    }

    private void SpawnChips(int k)
    {
        var at = _be.WorldPoint(_rig.Chips);
        // turnings curling out of the bore's mouth: iron dark grey, steel bluish
        int colour = k == 2 ? ColorUtil.ToRgba(230, 150, 145, 130) : ColorUtil.ToRgba(230, 95, 100, 105);
        _capi.World.SpawnParticles(2, colour,
            at.AddCopy(-0.08, -0.08, -0.03), at.AddCopy(0.08, 0.08, 0.03),
            new Vec3f(-0.4f, 0f, -0.4f), new Vec3f(0.4f, 0.4f, 0.4f), 0.8f, 1f, 0.2f, EnumParticleModel.Cube, null);
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
    }
}
