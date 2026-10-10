using SeraphHorizons.Mod.EidolonGantry.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.EidolonGantry;

/// <summary>
/// Draws the eidolon gantry's fitted parts (client only; created and disposed by
/// <see cref="BEEidolonGantry"/>, and only while its block is the gantry): one mesh per rig part
/// from <c>shapes/block/eidolongantry.json</c>, in the gantry's wood (the block's textures), shared
/// by every gantry of that wood (<see cref="MachinePartMeshes"/>), each drawn with its rig matrix
/// turned to the gantry's facing. A part is drawn when its <c>requires</c> is fitted: a winch stage
/// or the spine by the winch's parts, a body stage (torso to mind) only when an extension
/// (<see cref="IEidolonGantryExtension.Shows"/>) says so. The static frame is the block's own
/// shape (eidolongantry_frame.json) and is not drawn here.
/// <para>The rig is posed by the winch's depth (<see cref="BEEidolonGantry.WinchDepth"/>), eased
/// at <see cref="DepthRate"/>: letting down turns the crank, layshaft, drum and sheave, throws
/// the pawl off, pays out the chain and brings the hook, the spine and the body down.</para>
/// </summary>
public sealed class EidolonGantryRenderer : IRenderer
{
    public static readonly AssetLocation ShapeLoc = new(EidolonGantrySystem.Domain, "shapes/block/eidolongantry.json");
    private const int DrawRange = 64;

    /// <summary>How fast the shown depth follows the gantry's, per second (a full let-down in 8 s).</summary>
    public const double DepthRate = 0.125;

    private readonly ICoreClientAPI _capi;
    private readonly BEEidolonGantry _be;
    private readonly RigParts _parts;
    private readonly bool[] _drawn;
    private PartMeshSet? _meshes;
    private bool _built;
    private double _depth;
    private readonly Matrixf _model = new();

    public double RenderOrder => 0.5;
    public int RenderRange => DrawRange;

    public EidolonGantryRenderer(ICoreClientAPI capi, BEEidolonGantry be, GantryRig rig)
    {
        _capi = capi;
        _be = be;
        _parts = rig.MovingParts;
        _depth = be.WinchDepth;
        _drawn = _parts.Parts.Select(p => p.Requires != null || p.Ride != null || p.Drivers.Count > 0).ToArray();
        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "eidolongantry");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowFar, "eidolongantry");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowNear, "eidolongantry");
    }

    private void BuildMeshes()
    {
        _built = true;
        _meshes = MachinePartMeshes.Get(_capi, "wood-" + _be.Wood, ShapeLoc, _parts, i => _drawn[i],
            () => _capi.Tesselator.GetTextureSource(_be.Block), "eidolongantry");
        if (_meshes == null)
            _capi.Logger.Error("[seraphhorizons] Eidolon gantry: {0} is missing; its winch and spine are not drawn", ShapeLoc);
    }

    /// <summary>Whether the parts needing <paramref name="requires"/> are drawn now.</summary>
    private bool Shown(string? requires)
    {
        if (requires == null || !GantryRequires.IsBodyStage(requires))
            return _be.Parts.Fitted(requires);
        foreach (var extension in _be.Extensions)
            if (extension.Shows(requires))
                return true;
        return false;
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
            double target = _be.WinchDepth, step = DepthRate * deltaTime;
            _depth = Math.Abs(target - _depth) <= step ? target : _depth + Math.Sign(target - _depth) * step;
        }
        if (_meshes == null)
            return;
        var middle = _be.WorldPoint(new Float3(3, 2.5f, 2.5f));
        if (camPos.SquareDistanceTo(middle.X, middle.Y, middle.Z) > DrawRange * DrawRange)
            return;

        var mats = _parts.Matrices(new RigInput(0, Depth: _depth));
        var facing = Mat4.Facing(_be.Side);
        var rapi = _capi.Render;
        rapi.GlDisableCullFace();
        IStandardShaderProgram? prog = null;
        if (stage == EnumRenderStage.Opaque)
        {
            // light from the open middle of the gantry, which nothing fills
            var light = _be.CellPos(new Int3(1, 2, 2));
            prog = rapi.PreparedStandardShader(light.X, light.Y, light.Z);
            prog.ViewMatrix = rapi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rapi.CurrentProjectionMatrix;
        }
        for (int i = 0; i < _meshes.Meshes.Length; i++)
        {
            if (_meshes.Meshes[i] is { } mesh && Shown(_parts.Parts[i].Requires))
                MachineMeshes.Draw(_capi, _model, mesh, Mat4.Multiply(facing, mats[i]), prog, camPos, _be.Pos);
        }
        prog?.Stop();
        rapi.GlEnableCullFace();
    }

    public void Dispose()
    {
        // The meshes are MachinePartMeshes', shared by every gantry of the wood.
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.ShadowFar);
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.ShadowNear);
    }
}
