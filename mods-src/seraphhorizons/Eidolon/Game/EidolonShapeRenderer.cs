using SeraphHorizons.Mod.Eidolon.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.TrunkEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// The eidolon's renderer (client side; the entity type's <c>renderer</c>, <see cref="ClassName"/>):
/// the game's shape renderer, and after it, in the same passes (shadows included), the trunk it
/// carries (<see cref="EntityBehaviorEidolonTrunk"/>) at its attachment point, <c>Trunk</c> on the left
/// shoulder or <c>ThickTrunk</c> in both arms (<see cref="HaulPlan.AttachmentPoint"/>), drawn as
/// TrunkEntities draws a trunk lying (its display class's block, its underside's middle at the point,
/// its length along the point's z), with the same matrix the game builds for a held item
/// (<c>EntityShapeRenderer.RenderItem</c>): the entity's model matrix, the animated pose of the
/// point's element, the point's place and turn.
/// </summary>
public class EidolonShapeRenderer(Entity entity, ICoreClientAPI api) : EntityShapeRenderer(entity, api)
{
    public const string ClassName = "seraphhorizons.EidolonShape";

    private readonly Matrixf _trunkModel = new();
    private MultiTextureMeshRef? _trunkMesh;
    private int _trunkShownId;

    public override void DoRender3DOpaque(float dt, bool isShadowPass)
    {
        base.DoRender3DOpaque(dt, isShadowPass);
        RenderTrunk(isShadowPass);
    }

    private void RenderTrunk(bool isShadowPass)
    {
        if (entity.GetBehavior<EntityBehaviorEidolonTrunk>()?.Trunk is not { } trunk)
            return;
        var shown = Trunks.ShownBlock(capi.World, trunk);
        if (!SyncTrunk(shown) || _trunkMesh == null)
            return;
        bool thick = EntityBehaviorEidolonTrunk.IsThick(trunk);
        if (entity.AnimManager?.Animator?.GetAttachmentPointPose(HaulPlan.AttachmentPoint(thick)) is not { AttachPoint: { } ap } apap)
            return;
        _trunkModel.Set(ModelMat).Mul(apap.AnimModelMatrix)
            .Translate(ap.PosX / 16.0, ap.PosY / 16.0, ap.PosZ / 16.0)
            .Rotate((float)ap.RotationX * GameMath.DEG2RAD, (float)ap.RotationY * GameMath.DEG2RAD, (float)ap.RotationZ * GameMath.DEG2RAD);
        var rapi = capi.Render;
        if (isShadowPass)
        {
            var mvp = new Matrixf().Set(rapi.CurrentProjectionMatrix).Mul(rapi.CurrentModelviewMatrix).Mul(_trunkModel.Values);
            rapi.CurrentActiveShader.UniformMatrix("mvpMatrix", mvp.Values);
            rapi.CurrentActiveShader.Uniform("origin", new Vec3f(0, 0, 0));
            rapi.RenderMultiTextureMesh(_trunkMesh, "tex2d", 0);
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
        prog.RgbaLightIn = lightrgbs;
        prog.RgbaGlowIn = new Vec4f(0, 0, 0, 0);
        prog.RgbaFogIn = rapi.FogColor;
        prog.FogMinIn = rapi.FogMin;
        prog.FogDensityIn = rapi.FogDensity;
        prog.ExtraGodray = 0;
        prog.ProjectionMatrix = rapi.CurrentProjectionMatrix;
        prog.ViewMatrix = rapi.CameraMatrixOriginf;
        prog.ModelMatrix = _trunkModel.Values;
        rapi.RenderMultiTextureMesh(_trunkMesh, "tex", 0);
        prog.Stop();
    }

    // Tessellates the shown block when it changes; false when there is nothing to draw.
    private bool SyncTrunk(Block? shown)
    {
        int id = shown?.Id ?? 0;
        if (id == _trunkShownId)
            return id != 0;
        _trunkShownId = id;
        _trunkMesh?.Dispose();
        _trunkMesh = null;
        if (shown == null || id == 0 || TrunkEntityRenderer.LyingMesh(capi, shown) is not { } mesh)
            return false;
        _trunkMesh = capi.Render.UploadMultiTextureMesh(mesh);
        return true;
    }

    public override void Dispose()
    {
        base.Dispose();
        _trunkMesh?.Dispose();
        _trunkMesh = null;
    }
}
