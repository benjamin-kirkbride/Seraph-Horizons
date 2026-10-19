using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Machines;

/// <summary>
/// Draws a machine's parts that are not its frame block's shape (client only; created and disposed by
/// <see cref="BEMachine"/>, <see cref="BEMachine.CreateRenderer"/>): one mesh per rig part of
/// <c>shapes/block/{machine}.json</c> (<see cref="MachineMeshes.PartMeshes"/>) with the block's textures,
/// each drawn with its rig matrix turned to the machine's facing, and only when
/// <see cref="BEMachine.ShowsPart"/> says so (its stage fitted, by default). A static part with no
/// <c>requires</c> is the frame, the block's own shape, and is not drawn here. The textures are
/// <see cref="BEMachine.PartTextures"/> (the block's, by default), the meshes built again when
/// <see cref="BEMachine.TextureKey"/> changes (the stall roaster's brick type, the press brake's edges).
/// <para>The rig is posed every frame from <see cref="BEMachine.FrameInput"/>, asked once a frame in the
/// opaque pass: by default the shaft's θ and ψ and the machine's depth; a machine with a clock of its own
/// (the work's W, a hand lever) gives its own. The parts are lit from the cell above the footprint's middle
/// (<see cref="MachineRigs.LightCell"/>), outside the model.</para>
/// </summary>
public class MachineRenderer : IRenderer
{
    private readonly ICoreClientAPI _capi;
    private readonly BEMachine _be;
    private readonly RigParts _parts;
    private readonly MultiTextureMeshRef?[] _meshes;
    private readonly bool[] _drawn;
    private bool _built;
    private string _textureKey = "";
    private RigInput _input = new(0);
    private readonly Matrixf _model = new();
    private readonly Vec3d _middle;
    private readonly Int3 _lightCell;

    public double RenderOrder => 0.5;
    public int RenderRange => _be.RenderRange;

    public MachineRenderer(ICoreClientAPI capi, BEMachine be, IMachineRig rig)
    {
        _capi = capi;
        _be = be;
        _parts = rig.Parts;
        int n = _parts.Parts.Count;
        _meshes = new MultiTextureMeshRef?[n];
        _drawn = new bool[n];
        for (int i = 0; i < n; i++)
        {
            var p = _parts.Parts[i];
            _drawn[i] = p.Requires != null || p.Ride != null || p.Drivers.Count > 0;
        }
        _middle = be.WorldPoint(rig.Middle());
        _lightCell = rig.LightCell();
        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "machine");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowFar, "machine");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowNear, "machine");
    }

    private void BuildMeshes()
    {
        _built = true;
        _textureKey = _be.TextureKey;
        for (int i = 0; i < _meshes.Length; i++)
        {
            _meshes[i]?.Dispose();
            _meshes[i] = null;
        }
        var loc = new AssetLocation(MachinesSystem.Domain, $"shapes/block/{_be.Machine}.json");
        var master = Shape.TryGet(_capi, loc);
        if (master == null)
        {
            _capi.Logger.Error("[seraphhorizons] {0} is missing; the {1}'s moving parts are not drawn", loc, _be.Machine);
            return;
        }
        var tex = _be.PartTextures(_capi);
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
        int range = _be.RenderRange;
        bool far = camPos.SquareDistanceTo(_middle.X, _middle.Y, _middle.Z) > range * range;
        if (stage == EnumRenderStage.Opaque)
        {
            if (!_built || _textureKey != _be.TextureKey)
                BuildMeshes();
            _input = _be.FrameInput(deltaTime, far);
        }
        if (far)
            return;
        var mats = _parts.Matrices(_input);
        var facing = Mat4.Facing(_be.Side);
        var rapi = _capi.Render;
        rapi.GlDisableCullFace();
        IStandardShaderProgram? prog = null;
        if (stage == EnumRenderStage.Opaque)
        {
            var light = _be.CellPos(_lightCell);
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

    public void Dispose()
    {
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.ShadowFar);
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.ShadowNear);
        foreach (var mesh in _meshes)
            mesh?.Dispose();
    }
}
