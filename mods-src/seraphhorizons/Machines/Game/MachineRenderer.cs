using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Machines;

/// <summary>
/// Draws a machine's parts that are not its frame block's shape (client only; created and disposed by
/// <see cref="BEMachine"/>, <see cref="BEMachine.CreateRenderer"/>): one mesh per rig part of
/// <c>shapes/block/{machine}.json</c> (<see cref="MachineMeshes.PartMeshes"/>) with the block's textures, one
/// tessellation shared by every machine of the kind, frame variant (its side excepted) and texture key
/// (<see cref="MachinePartMeshes"/>: a second
/// machine, or a chunk reload, builds nothing),
/// each drawn with its rig matrix turned to the machine's facing, and only when
/// <see cref="BEMachine.ShowsPart"/> says so (its stage fitted, by default). A static part with no
/// <c>requires</c> is the frame, the block's own shape, and is not drawn here. The textures are
/// <see cref="BEMachine.PartTextures"/> (the block's, by default), the meshes taken again when
/// <see cref="BEMachine.TextureKey"/> changes (the stall roaster's brick type, the press brake's edges), each
/// key's set built once.
/// <para>The rig is posed every frame from <see cref="BEMachine.FrameInput"/>, asked once a frame in the
/// opaque pass: by default the shaft's θ and ψ and the machine's depth; a machine with a clock of its own
/// (the work's W, a hand lever) gives its own. The parts are lit from the cell above the footprint's middle
/// (<see cref="MachineRigs.LightCell"/>), outside the model, and drawn with <see cref="MachineShading.Prepare"/>.</para>
/// </summary>
public class MachineRenderer : IRenderer
{
    private readonly ICoreClientAPI _capi;
    private readonly BEMachine _be;
    private readonly RigParts _parts;
    private MultiTextureMeshRef?[] _meshes = [];   // MachinePartMeshes'; never disposed here
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
        _meshes = [];
        var loc = new AssetLocation(MachinesSystem.Domain, $"shapes/block/{_be.Machine}.json");
        // the frame's variant but its side (the gantry's wood) is in the block's own textures, so in the key too
        string variant = string.Join(",", _be.Block.Variant.Where(kv => kv.Key != "side").Select(kv => kv.Key + "=" + kv.Value));
        var set = MachinePartMeshes.Get(_capi, $"machine|{variant}|{_textureKey}", loc, _parts, i => _drawn[i], () => _be.PartTextures(_capi), _be.Machine);
        if (set == null)
        {
            _capi.Logger.Error("[seraphhorizons] {0} is missing; the {1}'s moving parts are not drawn", loc, _be.Machine);
            return;
        }
        _meshes = set.Meshes;
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
            // .sh machinelight cell off: the cell above the controller, as the ore machines' renderer once lit them
            var light = MachineShading.Cell ? _be.CellPos(_lightCell) : _be.Pos.UpCopy();
            prog = MachineShading.Prepare(_capi, light);
            prog.ViewMatrix = rapi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rapi.CurrentProjectionMatrix;
        }
        bool glowing = false;
        for (int i = 0; i < _meshes.Length; i++)
        {
            var requires = _parts.Parts[i].Requires;
            if (_meshes[i] is not { Disposed: false } mesh || !_be.ShowsPart(requires))
                continue;
            if (prog != null)
            {
                // a hot part glows (BEMachine.PartGlow); the uniforms are set only around one
                var glow = _be.PartGlow(requires);
                if (glow != null || glowing)
                {
                    prog.RgbaGlowIn = glow?.Colour ?? NoGlow;
                    prog.ExtraGlow = glow?.Extra ?? 0;
                    glowing = glow != null;
                }
            }
            MachineMeshes.Draw(_capi, _model, mesh, Mat4.Multiply(facing, mats[i]), prog, camPos, _be.Pos);
        }
        if (prog != null && glowing)
        {
            prog.RgbaGlowIn = NoGlow;
            prog.ExtraGlow = 0;
        }
        prog?.Stop();
        rapi.GlEnableCullFace();
    }

    private static readonly Vec4f NoGlow = new(0, 0, 0, 0);

    public void Dispose()
    {
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.ShadowFar);
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.ShadowNear);
        // the part meshes are MachinePartMeshes', shared with every other machine of the kind
    }
}
