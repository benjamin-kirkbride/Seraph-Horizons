using SeraphHorizons.Mod.Machines;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.TrunkEntities;

/// <summary>
/// Draws a trunk entity (client side): Logging Expanded's block of the trunk's wood in its display
/// class's size (<see cref="Trunks.ShownBlock"/>: thin as <c>lg</c>, thick as <c>xxl</c>, debarked
/// if the trunk is), tessellated from the block atlas and laid along z centred on the entity's
/// position with its underside there, as the collision boxes are; turned by the yaw as the game's
/// multi-box physics turns those boxes. Re-tessellated when the shown block changes.
/// </summary>
public class TrunkEntityRenderer : EntityRenderer
{
    private readonly EntityTrunk _trunk;
    private readonly Matrixf _model = new();
    private MultiTextureMeshRef? _mesh;
    private int _shownId;

    public TrunkEntityRenderer(Entity entity, ICoreClientAPI api) : base(entity, api)
    {
        _trunk = (EntityTrunk)entity;
    }

    private void Sync()
    {
        var shown = Trunks.ShownBlock(capi.World, _trunk.Trunk);
        int id = shown?.Id ?? 0;
        if (id == _shownId)
            return;
        _shownId = id;
        _mesh?.Dispose();
        _mesh = null;
        if (shown == null || id == 0)
            return;
        if (LyingMesh(capi, shown) is { } mesh)
            _mesh = capi.Render.UploadMultiTextureMesh(mesh);
    }

    /// <summary>The shown block's mesh laid as a trunk entity is drawn: its underside's middle at the
    /// origin, its length along z; null when it tessellates to nothing. Also the eidolon's carried
    /// trunk (<c>Eidolon/Game/EidolonShapeRenderer.cs</c>).</summary>
    public static MeshData? LyingMesh(ICoreClientAPI capi, Block shown)
    {
        capi.Tesselator.TesselateBlock(shown, out var mesh);
        if (mesh == null || mesh.VerticesCount == 0)
            return null;
        var (min, max) = MachineMeshes.Bounds(mesh);
        mesh.Translate(-(min.X + max.X) / 2, -min.Y, -(min.Z + max.Z) / 2);
        if (max.X - min.X > max.Z - min.Z)
            mesh.Rotate(new Vec3f(0, 0, 0), 0, GameMath.PIHALF, 0);
        return mesh;
    }

    public override void DoRender3DOpaque(float dt, bool isShadowPass)
    {
        if (isShadowPass && !entity.IsRendered)
            return;
        Sync();
        if (_mesh == null || capi.World.Player?.Entity is not { } player)
            return;
        var rapi = capi.Render;
        var cam = player.CameraPos;
        var pos = entity.Pos;
        _model.Identity().Translate(pos.X - cam.X, pos.InternalY - cam.Y, pos.Z - cam.Z).RotateY(pos.Yaw + GameMath.PI);
        if (isShadowPass)
        {
            var mvp = new Matrixf().Set(rapi.CurrentProjectionMatrix).Mul(rapi.CurrentModelviewMatrix).Mul(_model.Values);
            rapi.CurrentActiveShader.UniformMatrix("mvpMatrix", mvp.Values);
            rapi.CurrentActiveShader.Uniform("origin", new Vec3f(0, 0, 0));
            rapi.RenderMultiTextureMesh(_mesh, "tex2d", 0);
            return;
        }
        var prog = rapi.StandardShader;
        prog.Use();
        prog.RgbaTint = ColorUtil.WhiteArgbVec;
        prog.DontWarpVertices = 0;
        prog.AddRenderFlags = 0;
        prog.NormalShaded = 1;
        prog.AlphaTest = 0.05f;
        prog.ExtraGlow = 0;
        prog.OverlayOpacity = 0;
        prog.RgbaAmbientIn = rapi.AmbientColor;
        var light = pos.AsBlockPos;
        prog.RgbaLightIn = capi.World.BlockAccessor.GetLightRGBs(light.X, light.InternalY, light.Z);
        prog.RgbaGlowIn = new Vec4f(0, 0, 0, 0);
        prog.RgbaFogIn = rapi.FogColor;
        prog.FogMinIn = rapi.FogMin;
        prog.FogDensityIn = rapi.FogDensity;
        prog.ExtraGodray = 0;
        prog.ProjectionMatrix = rapi.CurrentProjectionMatrix;
        prog.ViewMatrix = rapi.CameraMatrixOriginf;
        prog.ModelMatrix = _model.Values;
        rapi.RenderMultiTextureMesh(_mesh, "tex", 0);
        prog.Stop();
    }

    public override void Dispose()
    {
        _mesh?.Dispose();
        _mesh = null;
    }
}
