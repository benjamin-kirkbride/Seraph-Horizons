using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Machines;

/// <summary>
/// Draws an ore machine's parts that are not its frame block's shape (client only; created and disposed
/// by <see cref="BEOreMachine"/>): one mesh per rig part of <c>shapes/block/{machine}.json</c>
/// (<see cref="MachineMeshes.PartMeshes"/>) with the block's textures, each drawn with its rig matrix
/// turned to the machine's facing, and only when <see cref="BEOreMachine.ShowsPart"/> says so (its stage
/// fitted, by default). A static part with no <c>requires</c> is the frame, the block's own shape, and is
/// not drawn here.
/// <para>θ is the power cell's shaft angle about the power face's native axis
/// (<see cref="MillMotion.NativeShaftAngle"/>), accumulated; ψ the total angle it turned either way. Both
/// hold still until <see cref="BEOreMachine.Animates"/> (built).</para>
/// </summary>
public sealed class OreMachineRenderer : IRenderer
{
    private const int DrawRange = 64;

    private readonly ICoreClientAPI _capi;
    private readonly BEOreMachine _be;
    private readonly OreMachineRig _rig;
    private readonly RigParts _parts;
    private readonly MultiTextureMeshRef?[] _meshes;
    private readonly bool[] _drawn;
    private bool _built;
    private double _theta;
    private double _psi;
    private double _lastAngle;
    private bool _angleSeeded;
    private readonly Matrixf _model = new();
    private readonly Vec3d _middle;

    public double RenderOrder => 0.5;
    public int RenderRange => DrawRange;

    public OreMachineRenderer(ICoreClientAPI capi, BEOreMachine be, OreMachineRig rig)
    {
        _capi = capi;
        _be = be;
        _rig = rig;
        _parts = rig.Parts;
        int n = _parts.Parts.Count;
        _meshes = new MultiTextureMeshRef?[n];
        _drawn = new bool[n];
        for (int i = 0; i < n; i++)
        {
            var p = _parts.Parts[i];
            _drawn[i] = p.Requires != null || p.Ride != null || p.Drivers.Count > 0;
        }
        float cx = (float)rig.Cells.Average(c => c.Pos.X) + 0.5f, cy = (float)rig.Cells.Average(c => c.Pos.Y) + 0.5f, cz = (float)rig.Cells.Average(c => c.Pos.Z) + 0.5f;
        _middle = be.WorldPoint(new Float3(cx, cy, cz));
        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "oremachine");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowFar, "oremachine");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowNear, "oremachine");
    }

    private void BuildMeshes()
    {
        _built = true;
        var loc = new AssetLocation(OreMachinesSystem.Domain, $"shapes/block/{_be.Machine}.json");
        var master = Shape.TryGet(_capi, loc);
        if (master == null)
        {
            _capi.Logger.Error("[seraphhorizons] {0} is missing; the {1}'s moving parts are not drawn", loc, _be.Machine);
            return;
        }
        var tex = _capi.Tesselator.GetTextureSource(_be.Block);
        var meshes = MachineMeshes.PartMeshes(_capi, master, _parts, i => _drawn[i], tex, _be.Machine);
        for (int i = 0; i < meshes.Length; i++)
            if (meshes[i] is { } mesh)
                _meshes[i] = _capi.Render.UploadMultiTextureMesh(mesh);
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        var camPos = _capi.World.Player?.Entity?.CameraPos;
        if (camPos == null)
            return;
        bool far = camPos.SquareDistanceTo(_middle.X, _middle.Y, _middle.Z) > DrawRange * DrawRange;
        if (stage == EnumRenderStage.Opaque)
        {
            if (!_built)
                BuildMeshes();
            AdvanceClock();
        }
        if (far)
            return;
        var mats = _parts.Matrices(new RigInput(_theta, Travel: _psi));
        var facing = Mat4.Facing(_be.Side);
        var rapi = _capi.Render;
        rapi.GlDisableCullFace();
        IStandardShaderProgram? prog = null;
        if (stage == EnumRenderStage.Opaque)
        {
            var light = _be.Pos.UpCopy();
            prog = rapi.PreparedStandardShader(light.X, light.Y, light.Z);
            prog.ViewMatrix = rapi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rapi.CurrentProjectionMatrix;
        }
        for (int i = 0; i < _meshes.Length; i++)
            if (_meshes[i] is { } mesh && _be.ShowsPart(_parts.Parts[i].Requires))
                MachineMeshes.Draw(_capi, _model, mesh, Mat4.Multiply(facing, mats[i]), prog, camPos, _be.Pos);
        prog?.Stop();
        rapi.GlEnableCullFace();
    }

    private void AdvanceClock()
    {
        double raw = _be.ShaftAngle;
        double angle = _rig.ShaftAxis is { } axis ? MillMotion.NativeShaftAngle(_be.Side, raw, axis) : raw;
        if (!_angleSeeded || !_be.Animates || _be.ShaftSpeed == 0)
        {
            _lastAngle = angle;
            _angleSeeded = true;
            return;
        }
        double delta = MillMotion.WrappedDelta(_lastAngle, angle);
        _lastAngle = angle;
        _theta += delta;
        _psi += Math.Abs(delta);
        if (Math.Abs(_theta) > 1e6)
            _theta %= 2 * Math.PI;
        if (_psi > 1e6)
            _psi %= 2 * Math.PI;
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
