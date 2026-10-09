using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// Where an entity's attachment point is drawn this frame (client side): for drawing what the eidolon
/// carries at the shape's <c>Carry</c>, <c>Trunk</c> or <c>ThickTrunk</c> point (Eidolon/README.md,
/// "Attachment points"), following its animation. The matrix is the game's own for a held item
/// (<c>EntityShapeRenderer.RenderItem</c>): the entity renderer's model matrix as it drew the entity
/// this frame (camera relative), the animator's pose of the point's element, then the point's offset
/// and turn, in block units, so a mesh drawn with it has its origin on the point in the point's frame.
/// Valid in the opaque stage after entities are drawn (a renderer of order above 0.5), for an entity
/// drawn this frame (<see cref="Entity.IsRendered"/>).
/// </summary>
public static class EidolonAttachmentRender
{
    /// <summary>Sets <paramref name="into"/> to attachment point <paramref name="code"/>'s model matrix
    /// this frame; false when the entity is not drawn by a shape renderer this frame, or its shape has
    /// no such point.</summary>
    public static bool TryGetMatrix(Entity entity, string code, Matrixf into)
    {
        if (!entity.IsRendered || entity.Properties?.Client?.Renderer is not EntityShapeRenderer renderer
            || entity.AnimManager?.Animator?.GetAttachmentPointPose(code) is not { } pose || pose.AttachPoint is not { } point)
            return false;
        into.Set(renderer.ModelMat)
            .Mul(pose.AnimModelMatrix)
            .Translate(point.PosX / 16.0, point.PosY / 16.0, point.PosZ / 16.0)
            .Rotate((float)(point.RotationX * GameMath.DEG2RAD), (float)(point.RotationY * GameMath.DEG2RAD), (float)(point.RotationZ * GameMath.DEG2RAD));
        return true;
    }

    /// <summary>Draws <paramref name="model"/> (a block or item mesh, in block units) with
    /// <paramref name="modelMatrix"/>, lit as the block at <paramref name="litAt"/>, with the standard
    /// shader (opaque stage).</summary>
    public static void Draw(ICoreClientAPI capi, MultiTextureMeshRef model, float[] modelMatrix, BlockPos litAt)
    {
        var render = capi.Render;
        var shader = render.PreparedStandardShader(litAt.X, litAt.InternalY, litAt.Z);
        shader.ModelMatrix = modelMatrix;
        shader.ViewMatrix = render.CameraMatrixOriginf;
        shader.ProjectionMatrix = render.CurrentProjectionMatrix;
        render.RenderMultiTextureMesh(model, "tex");
        shader.Stop();
    }
}
