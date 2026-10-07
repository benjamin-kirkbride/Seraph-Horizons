using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.MandrelStation.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.MandrelStation;

/// <summary>
/// Draws the mandrel station's moving parts (client only; created and disposed by
/// <see cref="BEMandrelStation"/>, and only while its block is the station). One mesh per rig part
/// from <c>shapes/block/mandrelstation.json</c> (<see cref="MachineMeshes.PartMesh"/>), each drawn
/// with its rig matrix turned to the station's facing, and only when its <c>requires</c> is fitted
/// (the mandrel), or, for the work, while that metal's hollow is on the mandrel. The texture code
/// <c>mandrel</c> is set to the fitted rod's metal (one mesh set per metal). The static frame part is
/// the block's own shape (mandrelstation_frame.json) and is not drawn here.
/// <para>The rig is posed every frame from the view (<see cref="IMandrelStationView"/>) through
/// <see cref="MandrelStationClock"/>: θ the hammer's clock, W eased to the server's after each blow,
/// k and p.</para>
/// </summary>
public sealed class MandrelStationRenderer : IRenderer
{
    public static readonly AssetLocation ShapeLoc = new(MandrelStationSystem.Domain, "shapes/block/mandrelstation.json");
    private const int DrawRange = 48;

    private readonly ICoreClientAPI _capi;
    private readonly BEMandrelStation _be;
    private readonly IMandrelStationView _view;
    private readonly RigParts _parts;
    private readonly bool[] _drawn;
    // each part's meshes by the mandrel metal they were textured with ("" for none)
    private readonly Dictionary<string, MultiTextureMeshRef?[]> _meshes = [];
    private Shape? _master;
    private bool _built;

    private readonly MandrelStationClock _clock = new();
    private readonly Matrixf _model = new();

    public double RenderOrder => 0.5;
    public int RenderRange => DrawRange;

    public MandrelStationRenderer(ICoreClientAPI capi, BEMandrelStation be, IMandrelStationView view, MandrelStationRig rig)
    {
        _capi = capi;
        _be = be;
        _view = view;
        _parts = rig.MovingParts;
        _drawn = _parts.Parts.Select(p => p.Requires != null || p.Ride != null || p.Drivers.Count > 0).ToArray();
        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "mandrelstation");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowFar, "mandrelstation");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowNear, "mandrelstation");
    }

    /// <summary>The parts' meshes with the mandrel in <paramref name="metal"/>, built the first time
    /// they are asked for.</summary>
    private MultiTextureMeshRef?[] Meshes(string? metal)
    {
        if (_meshes.TryGetValue(metal ?? "", out var meshes))
            return meshes;
        meshes = new MultiTextureMeshRef?[_drawn.Length];
        _meshes[metal ?? ""] = meshes;
        if (!_built)
        {
            _built = true;
            _master = Shape.TryGet(_capi, ShapeLoc);
            if (_master == null)
                _capi.Logger.Error("[seraphhorizons] Mandrel station: {0} is missing; its moving parts are not drawn", ShapeLoc);
        }
        if (_master == null)
            return meshes;
        var tex = new MandrelTextureSource(_capi.Tesselator.GetTextureSource(_be.Block), MachineMeshes.MetalTexture(_capi, metal));
        for (int i = 0; i < meshes.Length; i++)
            if (_drawn[i] && MachineMeshes.PartMesh(_capi, _master, _parts, i, tex, "mandrelstation") is { } mesh)
                meshes[i] = _capi.Render.UploadMultiTextureMesh(mesh);
        return meshes;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        var camPos = _capi.World.Player?.Entity?.CameraPos;
        if (camPos == null)
            return;
        if (stage == EnumRenderStage.Opaque)
            _clock.Advance(deltaTime, _view.HollowClass, _view.ForgeWork, _view.Blows);
        var middle = _be.WorldPoint(new Float3(0.5f, 0.5f, 1));
        if (camPos.SquareDistanceTo(middle.X, middle.Y, middle.Z) > DrawRange * DrawRange)
            return;

        var meshes = Meshes(_view.MandrelMetal);
        var mats = _parts.Matrices(_clock.Input());
        var facing = Mat4.Facing(_view.Side);
        var rapi = _capi.Render;
        rapi.GlDisableCullFace();
        IStandardShaderProgram? prog = null;
        if (stage == EnumRenderStage.Opaque)
        {
            // light from the cell over the mandrel, outside the model
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
        requires is MandrelRequires.HollowLead or MandrelRequires.HollowCopper ? _clock.ShowsHollow(requires) : _view.PartFitted(requires);

    public void Dispose()
    {
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.ShadowFar);
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.ShadowNear);
        foreach (var meshes in _meshes.Values)
            foreach (var mesh in meshes)
                mesh?.Dispose();
    }

    /// <summary>The block's textures with the mandrel's metal in place of <c>mandrel</c>.</summary>
    private sealed class MandrelTextureSource(ITexPositionSource inner, TextureAtlasPosition? mandrel) : ITexPositionSource
    {
        public Size2i AtlasSize => inner.AtlasSize!;
        public TextureAtlasPosition this[string textureCode] =>
            textureCode == "mandrel" && mandrel != null ? mandrel : inner[textureCode]!;
    }
}
