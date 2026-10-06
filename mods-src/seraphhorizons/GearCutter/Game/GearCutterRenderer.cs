using SeraphHorizons.Mod.GearCutter.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.GearCutter;

/// <summary>
/// Draws the gear cutter's moving parts (client only; created and disposed by
/// <see cref="BEGearCutter"/>, and only while its block is the cutter). One mesh per rig part from
/// <c>shapes/block/gearcutter.json</c> (<see cref="MachineMeshes.PartMesh"/>), each drawn with its rig
/// matrix turned to the cutter's facing, and only when its <c>requires</c> is fitted (the cover
/// always, a blank while it is on the arbor, a master while it is the one fitted). The static frame
/// part is the block's own shape (gearcutter_frame.json) and is not drawn here.
/// <para>The rig is posed every frame from: θ, the shaft angle about the entry shaft's native x axis
/// (<see cref="MillMotion.NativeShaftAngle"/>), accumulated; ψ, the total angle it turned either way;
/// W, the teeth cut, advanced with the shaft at 1 / TurnsPerTooth a turn while the cut runs and held
/// to the server's value (synced every tenth of a tooth), at the work's end with no blank on; k, the
/// master's class, held while its presence p eases out; and the oil tank's fill for the
/// reservoir's level.</para>
/// <para>While the cut runs: chips and sparks at the cutter (<c>chips</c>), and, with oil in the
/// tank, a spray from the injection valve's nozzle (<c>drip</c>).</para>
/// </summary>
public sealed class GearCutterRenderer : IRenderer
{
    public static readonly AssetLocation ShapeLoc = new(GearCutterSystem.Domain, "shapes/block/gearcutter.json");
    private const int DrawRange = 48;
    private const float ChipInterval = 0.12f;
    private const float SprayInterval = 0.2f;
    // How far the shown W may stray from the server's before it snaps back, teeth.
    private const double Snap = 0.3;

    private readonly ICoreClientAPI _capi;
    private readonly BEGearCutter _be;
    private readonly GearCutterRig _rig;
    private readonly RigParts _parts;
    private readonly MultiTextureMeshRef?[] _meshes;
    private readonly bool[] _drawn;
    private bool _built;

    private double _theta;
    private double _psi;
    private double _lastAngle;
    private bool _angleSeeded;
    private double _work;
    private float _presence;
    private int _shownClass;
    private float _chipTimer;
    private float _sprayTimer;

    private readonly Matrixf _model = new();

    public double RenderOrder => 0.5;
    public int RenderRange => DrawRange;

    public GearCutterRenderer(ICoreClientAPI capi, BEGearCutter be, GearCutterRig rig)
    {
        _capi = capi;
        _be = be;
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
        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "gearcutter");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowFar, "gearcutter");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowNear, "gearcutter");
    }

    private void BuildMeshes()
    {
        _built = true;
        var master = Shape.TryGet(_capi, ShapeLoc);
        if (master == null)
        {
            _capi.Logger.Error("[seraphhorizons] Gear cutter: {0} is missing; its moving parts are not drawn", ShapeLoc);
            return;
        }
        var tex = _capi.Tesselator.GetTextureSource(_be.Block);
        for (int i = 0; i < _meshes.Length; i++)
        {
            if (!_drawn[i])
                continue;
            var mesh = MachineMeshes.PartMesh(_capi, master, _parts, i, tex, "gearcutter");
            if (mesh != null)
                _meshes[i] = _capi.Render.UploadMultiTextureMesh(mesh);
        }
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        var camPos = _capi.World.Player?.Entity?.CameraPos;
        if (camPos == null)
            return;
        var middle = _be.WorldPoint(new Float3(0, 1, 1));
        bool far = camPos.SquareDistanceTo(middle.X, middle.Y, middle.Z) > DrawRange * DrawRange;
        if (stage == EnumRenderStage.Opaque)
        {
            if (!_built)
                BuildMeshes();
            AdvanceClocks(deltaTime, far);
        }
        if (far)
            return;

        var input = new RigInput(_theta, Travel: _psi, Work: _work, Class: _shownClass, Presence: _presence, Oil: _be.OilFill);
        var mats = _parts.Matrices(input);
        var facing = Mat4.Facing(_be.Side);
        var rapi = _capi.Render;
        rapi.GlDisableCullFace();
        IStandardShaderProgram? prog = null;
        if (stage == EnumRenderStage.Opaque)
        {
            // light from the cell over the operator's side, outside the model
            var light = _be.CellPos(new Int3(0, 2, 0));
            prog = rapi.PreparedStandardShader(light.X, light.Y, light.Z);
            prog.ViewMatrix = rapi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rapi.CurrentProjectionMatrix;
        }
        for (int i = 0; i < _meshes.Length; i++)
            if (_meshes[i] is { } mesh && _be.Fitted(_parts.Parts[i].Requires))
                MachineMeshes.Draw(_capi, _model, mesh, Mat4.Multiply(facing, mats[i]), prog, camPos, _be.Pos);
        prog?.Stop();
        rapi.GlEnableCullFace();
    }

    private void AdvanceClocks(float dt, bool far)
    {
        float speed = _be.ShaftSpeed;
        double angle = MillMotion.NativeShaftAngle(_be.Side, _be.ShaftAngle, Axis.X);
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

        // the master: k held while p eases out
        int master = _be.Parts.Master;
        _presence = Math.Clamp(_presence + (master > 0 ? dt : -dt) * 2.5f, 0, 1);
        if (master > 0)
            _shownClass = master;
        else if (_presence <= 0)
            _shownClass = 0;

        var job = _be.Job;
        bool running = _be.Running;
        if (!_be.BlankOn)
            _work = GearCut.Teeth(_shownClass);
        else
        {
            if (running)
                _work += GearCut.TeethFor(Math.Abs(delta), _be.TurnsPerTooth);
            // the server's W is the truth: never more than a little ahead of it, never behind it
            if (_work < job.Work || _work > job.Work + Snap)
                _work = job.Work;
            _work = Math.Min(_work, job.End);
        }

        if (far || !running)
            return;
        if ((_chipTimer += dt) >= ChipInterval)
        {
            _chipTimer = 0;
            SpawnChips();
        }
        if (_be.OilFill > 0 && (_sprayTimer += dt) >= SprayInterval)
        {
            _sprayTimer = 0;
            SpawnSpray();
        }
    }

    private void SpawnChips()
    {
        var at = _be.WorldPoint(_rig.Chips);
        // steel chips, and now and then a spark
        _capi.World.SpawnParticles(2, ColorUtil.ToRgba(255, 150, 150, 160),
            at.AddCopy(-0.04, -0.04, -0.04), at.AddCopy(0.04, 0.04, 0.04),
            new Vec3f(-0.8f, -0.2f, -0.8f), new Vec3f(0.8f, 0.6f, 0.8f), 0.8f, 1f, 0.25f, EnumParticleModel.Cube, null);
        if (_capi.World.Rand.NextDouble() < 0.3)
            _capi.World.SpawnParticles(1, ColorUtil.ToRgba(255, 60, 190, 255),
                at, at.AddCopy(0.02, 0.02, 0.02),
                new Vec3f(-1.5f, 0.2f, -1.5f), new Vec3f(1.5f, 1.2f, 1.5f), 0.3f, 1f, 0.15f, EnumParticleModel.Quad, null);
    }

    private void SpawnSpray()
    {
        var at = _be.WorldPoint(_rig.Drip);
        _capi.World.SpawnParticles(2, ColorUtil.ToRgba(170, 40, 140, 190),
            at.AddCopy(-0.02, -0.05, -0.02), at.AddCopy(0.02, 0, 0.02),
            new Vec3f(-0.15f, -0.6f, -0.15f), new Vec3f(0.15f, -0.3f, 0.15f), 0.5f, 1f, 0.2f, EnumParticleModel.Quad, null);
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
