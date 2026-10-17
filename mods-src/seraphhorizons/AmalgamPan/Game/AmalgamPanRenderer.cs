using SeraphHorizons.Mod.AmalgamPan.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.AmalgamPan;

/// <summary>
/// Draws the amalgam pan's muller, shoes and contents (client only; created and disposed by
/// <see cref="BEAmalgamPan"/>). One mesh per rig part from <c>shapes/block/amalgampan.json</c>
/// (<see cref="MachineMeshes.PartMesh"/>), each drawn with its rig matrix turned to the pan's facing:
/// the muller and the shoes once fitted (the texture code <c>shoe</c> set to the fitted plate's metal),
/// and one of the contents as the batch stands, the water film over the charge. The frame is the
/// block's own shape. θ is the axle's angle while one turns the pan, else the hand's clock
/// (<see cref="AmalgamPanClock"/>).
/// </summary>
public sealed class AmalgamPanRenderer : IRenderer
{
    public static readonly AssetLocation ShapeLoc = new(AmalgamPanSystem.Domain, "shapes/block/amalgampan.json");
    private const int DrawRange = 48;

    private readonly ICoreClientAPI _capi;
    private readonly BEAmalgamPan _be;
    private readonly AmalgamPanRig _rig;
    private readonly RigParts _parts;
    private readonly bool[] _drawn;
    private readonly Dictionary<string, MultiTextureMeshRef?[]> _meshes = [];
    private readonly AmalgamPanClock _clock = new();
    private readonly Matrixf _model = new();
    private Shape? _master;
    private bool _built;

    public double RenderOrder => 0.5;
    public int RenderRange => DrawRange;

    public AmalgamPanRenderer(ICoreClientAPI capi, BEAmalgamPan be, AmalgamPanRig rig)
    {
        _capi = capi;
        _be = be;
        _rig = rig;
        _parts = rig.MovingParts;
        _drawn = _parts.Parts.Select(p => p.Requires != null).ToArray();
        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "amalgampan");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowFar, "amalgampan");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowNear, "amalgampan");
    }

    private MultiTextureMeshRef?[] Meshes(string? shoe)
    {
        if (_meshes.TryGetValue(shoe ?? "", out var meshes))
            return meshes;
        meshes = new MultiTextureMeshRef?[_drawn.Length];
        _meshes[shoe ?? ""] = meshes;
        if (!_built)
        {
            _built = true;
            _master = Shape.TryGet(_capi, ShapeLoc);
            if (_master == null)
                _capi.Logger.Error("[seraphhorizons] Amalgam pan: {0} is missing; its muller and contents are not drawn", ShapeLoc);
        }
        if (_master == null)
            return meshes;
        var tex = new ShoeTextureSource(_capi.Tesselator.GetTextureSource(_be.Block), PlateTexture(_capi, shoe));
        for (int i = 0; i < meshes.Length; i++)
            if (_drawn[i] && MachineMeshes.PartMesh(_capi, _master, _parts, i, tex, "amalgampan") is { } mesh)
                meshes[i] = _capi.Render.UploadMultiTextureMesh(mesh);
        return meshes;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        var camPos = _capi.World.Player?.Entity?.CameraPos;
        if (camPos == null)
            return;
        bool far = camPos.SquareDistanceTo(_be.Pos.X + 0.5, _be.Pos.Y + 0.5, _be.Pos.Z + 0.5) > DrawRange * DrawRange;
        if (stage == EnumRenderStage.Opaque)
        {
            double? axle = _be.Powered && _be.Power is { } mp ? mp.ShaftAngle : null;
            _clock.Advance(deltaTime, axle, _be.HandHeld && _be.Complete, _be.Config.HandTurnsPerSecond);
        }
        if (far)
            return;

        var meshes = Meshes(_be.ShoeMetal);
        var mats = _parts.Matrices(_clock.Input());
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
        for (int i = 0; i < meshes.Length; i++)
            if (meshes[i] is { } mesh && Shown(_parts.Parts[i].Requires))
                MachineMeshes.Draw(_capi, _model, mesh, Mat4.Multiply(facing, mats[i]), prog, camPos, _be.Pos);
        prog?.Stop();
        rapi.GlEnableCullFace();
    }

    private bool Shown(string? requires) =>
        AmalgamPanRequires.IsContents(requires) ? _be.Complete && _be.Batch.Shows(requires) : _be.Assembly.Shows(requires);

    /// <summary>The game's plate texture of <paramref name="metal"/> in the block atlas (inserted if it
    /// is not there yet), or null: the shoes are cut from a plate.</summary>
    private static TextureAtlasPosition? PlateTexture(ICoreClientAPI capi, string? metal)
    {
        if (metal == null)
            return null;
        var loc = new AssetLocation("game", "block/metal/plate/" + metal);
        var pos = capi.BlockTextureAtlas[loc];
        if (pos == null && capi.Assets.Exists(loc.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png")))
            capi.BlockTextureAtlas.GetOrInsertTexture(loc, out _, out pos);
        return pos;
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

    /// <summary>The block's textures with the shoes' metal in place of <c>shoe</c>.</summary>
    private sealed class ShoeTextureSource(ITexPositionSource inner, TextureAtlasPosition? shoe) : ITexPositionSource
    {
        public Size2i AtlasSize => inner.AtlasSize!;
        public TextureAtlasPosition this[string textureCode] =>
            textureCode == "shoe" && shoe != null ? shoe : inner[textureCode]!;
    }
}
