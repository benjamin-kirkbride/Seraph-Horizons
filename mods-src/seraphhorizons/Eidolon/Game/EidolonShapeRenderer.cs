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
/// the game's shape renderer (which draws the axe at <c>RightHand</c>, as a held item), and after it,
/// in the same passes (shadows included), what it carries at its attachment points
/// (Eidolon/README.md, "Attachment points"), each with the point's matrix this frame
/// (<see cref="EidolonAttachmentRender.TryGetMatrix(EntityShapeRenderer, Entity, string, Matrixf)"/>)
/// and lit as the eidolon is:
/// <list type="bullet">
/// <item>the trunk (<see cref="EntityBehaviorEidolonTrunk"/>), <c>Trunk</c> on the left shoulder or
/// <c>ThickTrunk</c> in both arms (<see cref="HaulPlan.AttachmentPoint"/>), drawn as TrunkEntities
/// draws a trunk lying (its display class's block, its underside's middle at the point, its length
/// along the point's z);</item>
/// <item>the Carry On block (<see cref="EntityBehaviorEidolonCarry"/>) at <c>Carry</c>, its stack's
/// own mesh as the game draws it on the ground (a chest by its type), its corner origin put half a
/// block back so the point is the centre of its underside.</item>
/// </list>
/// </summary>
public class EidolonShapeRenderer(Entity entity, ICoreClientAPI api) : EntityShapeRenderer(entity, api)
{
    public const string ClassName = "seraphhorizons.EidolonShape";
    public const string CarryPoint = "Carry";

    private readonly Matrixf _model = new();
    private readonly Matrixf _mvp = new();
    private readonly DummySlot _loadSlot = new();
    private MultiTextureMeshRef? _trunkMesh;
    private int _trunkShownId;

    public override void DoRender3DOpaque(float dt, bool isShadowPass)
    {
        base.DoRender3DOpaque(dt, isShadowPass);
        RenderTrunk(isShadowPass);
        RenderLoad(dt, isShadowPass);
    }

    private void RenderTrunk(bool isShadowPass)
    {
        if (entity.GetBehavior<EntityBehaviorEidolonTrunk>()?.Trunk is not { } trunk)
            return;
        if (!SyncTrunk(Trunks.ShownBlock(capi.World, trunk)) || _trunkMesh == null
            || !EidolonAttachmentRender.TryGetMatrix(this, entity, HaulPlan.AttachmentPoint(EntityBehaviorEidolonTrunk.IsThick(trunk)), _model))
            return;
        Draw(_trunkMesh, isShadowPass, cullFaces: true);
    }

    private void RenderLoad(float dt, bool isShadowPass)
    {
        if (entity.GetBehavior<EntityBehaviorEidolonCarry>()?.LoadStack is not { } stack
            || !EidolonAttachmentRender.TryGetMatrix(this, entity, CarryPoint, _model))
            return;
        _loadSlot.Itemstack = stack;
        var info = capi.Render.GetItemStackRenderInfo(_loadSlot, EnumItemRenderTarget.Ground, dt);
        _loadSlot.Itemstack = null;
        if (info?.ModelRef == null)
            return;
        _model.Translate(-0.5f, 0f, -0.5f);
        Draw(info.ModelRef, isShadowPass, info.CullFaces);
    }

    /// <summary>Draws <paramref name="mesh"/> (in block units) with <see cref="_model"/>: in the shadow
    /// pass with the shadow shader the entity was drawn with, else with the standard shader lit as
    /// the entity.</summary>
    private void Draw(MultiTextureMeshRef mesh, bool isShadowPass, bool cullFaces)
    {
        var rapi = capi.Render;
        if (!cullFaces)
            rapi.GlDisableCullFace();
        if (isShadowPass)
        {
            _mvp.Set(rapi.CurrentProjectionMatrix).Mul(rapi.CurrentModelviewMatrix).Mul(_model.Values);
            rapi.CurrentActiveShader.UniformMatrix("mvpMatrix", _mvp.Values);
            rapi.CurrentActiveShader.Uniform("origin", new Vec3f(0, 0, 0));
            rapi.RenderMultiTextureMesh(mesh, "tex2d", 0);
        }
        else
        {
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
            prog.ModelMatrix = _model.Values;
            rapi.RenderMultiTextureMesh(mesh, "tex", 0);
            prog.Stop();
        }
        if (!cullFaces)
            rapi.GlEnableCullFace();
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

/// <summary>Registers the eidolon's renderer (<see cref="EidolonShapeRenderer"/>) on the client,
/// whatever the switch: with it off the entity does not exist.</summary>
public class EidolonRenderSystem : ModSystem
{
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api) =>
        api.RegisterEntityRendererClass(EidolonShapeRenderer.ClassName, typeof(EidolonShapeRenderer));
}
