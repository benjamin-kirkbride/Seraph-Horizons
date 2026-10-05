using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Machines;

/// <summary>
/// Client-side mesh work a machine renderer shares (the rosser's; the bucking mill's
/// <c>MillRenderer</c> has its own copy of the same steps and could move onto these): one mesh per
/// rig part cut out of the machine's shape by blanking every other part's elements, as Immersive
/// Woodworking's sawmill renderer does; a metal texture swapped in for <c>metal</c> on some parts;
/// a tessellated mesh cut into blocks along its length; and drawing a mesh in the opaque pass or
/// either shadow pass.
/// </summary>
public static class MachineMeshes
{
    /// <summary>The elements of rig part <paramref name="part"/> alone, from a clone of
    /// <paramref name="master"/>, in the native frame (blocks); null when it has none.</summary>
    public static MeshData? PartMesh(ICoreClientAPI capi, Shape master, RigParts parts, int part, ITexPositionSource tex, string name)
    {
        var shape = master.Clone();
        if (Blank(shape.Elements, [], parts, part) == 0)
            return null;
        capi.Tesselator.TesselateShape(name, shape, out var mesh, tex, null, 0, 0, 0, null, null);
        return mesh.VerticesCount > 0 ? mesh : null;
    }

    private static int Blank(ShapeElement[]? elements, List<string> chain, RigParts parts, int keep)
    {
        if (elements == null)
            return 0;
        int kept = 0;
        foreach (var el in elements)
        {
            chain.Add(el.Name ?? "");
            if (parts.PartOf(chain) == keep)
                kept++;
            else
                el.FacesResolved = new ShapeElementFace[6];
            kept += Blank(el.Children, chain, parts, keep);
            chain.RemoveAt(chain.Count - 1);
        }
        return kept;
    }

    /// <summary>The game's ingot texture of <paramref name="metal"/> in the block atlas (inserted
    /// if it is not there yet), or null.</summary>
    public static TextureAtlasPosition? MetalTexture(ICoreClientAPI capi, string? metal)
    {
        if (metal == null)
            return null;
        var loc = new AssetLocation("game", "block/metal/ingot/" + metal);
        var pos = capi.BlockTextureAtlas[loc];
        if (pos == null && capi.Assets.Exists(loc.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png")))
            capi.BlockTextureAtlas.GetOrInsertTexture(loc, out _, out pos);
        return pos;
    }

    /// <summary>A mesh's bounding box, blocks.</summary>
    public static (Float3 Min, Float3 Max) Bounds(MeshData mesh)
    {
        float[] xyz = mesh.xyz;
        var min = new Float3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Float3(float.MinValue, float.MinValue, float.MinValue);
        for (int v = 0; v < mesh.VerticesCount; v++)
        {
            float x = xyz[v * 3], y = xyz[v * 3 + 1], z = xyz[v * 3 + 2];
            min = new Float3(Math.Min(min.X, x), Math.Min(min.Y, y), Math.Min(min.Z, z));
            max = new Float3(Math.Max(max.X, x), Math.Max(max.Y, y), Math.Max(max.Z, z));
        }
        return (min, max);
    }

    /// <summary>
    /// Cuts a tessellated mesh into <paramref name="count"/> one-block pieces along its longer
    /// horizontal axis, from its low end: piece i keeps the faces whose centre lies in block i of
    /// the length (faces of the others collapse to a point, so they draw nothing; nothing else of
    /// the mesh changes). Null when the mesh is not made of four-vertex faces.
    /// </summary>
    public static MeshData[]? Segments(MeshData mesh, int count)
    {
        if (count <= 0 || mesh.VerticesCount % 4 != 0)
            return null;
        var (min, max) = Bounds(mesh);
        bool alongZ = max.Z - min.Z > max.X - min.X;
        float start = alongZ ? min.Z : min.X;
        int axis = alongZ ? 2 : 0;
        var result = new MeshData[count];
        for (int s = 0; s < count; s++)
        {
            var piece = mesh.Clone();
            float[] xyz = piece.xyz;
            for (int q = 0; q < piece.VerticesCount / 4; q++)
            {
                float along = 0;
                for (int v = 0; v < 4; v++)
                    along += xyz[(q * 4 + v) * 3 + axis];
                int block = Math.Clamp((int)MathF.Floor(along / 4 - start), 0, count - 1);
                if (block == s)
                    continue;
                for (int v = 1; v < 4; v++)
                    for (int c = 0; c < 3; c++)
                        xyz[(q * 4 + v) * 3 + c] = xyz[q * 4 * 3 + c];
            }
            result[s] = piece;
        }
        return result;
    }

    /// <summary>The centre of block <paramref name="segment"/> of a mesh cut by
    /// <see cref="Segments"/>, in the mesh's own frame.</summary>
    public static Float3 SegmentCentre((Float3 Min, Float3 Max) bounds, int segment)
    {
        var (min, max) = bounds;
        bool alongZ = max.Z - min.Z > max.X - min.X;
        float y = (min.Y + max.Y) / 2;
        return alongZ
            ? new Float3((min.X + max.X) / 2, y, min.Z + segment + 0.5f)
            : new Float3(min.X + segment + 0.5f, y, (min.Z + max.Z) / 2);
    }

    /// <summary>Draws <paramref name="mesh"/> with <paramref name="native"/> (the machine's facing
    /// and the part's pose) at the controller <paramref name="pos"/>: through <paramref name="prog"/>
    /// in the opaque pass, else through the active shadow shader.</summary>
    public static void Draw(ICoreClientAPI capi, Matrixf model, MultiTextureMeshRef mesh, float[] native, IStandardShaderProgram? prog,
                            Vec3d cam, BlockPos pos)
    {
        model.Identity().Translate(pos.X - cam.X, pos.Y - cam.Y, pos.Z - cam.Z).Mul(native);
        if (prog != null)
        {
            prog.ModelMatrix = model.Values;
            capi.Render.RenderMultiTextureMesh(mesh, "tex", 0);
            return;
        }
        // Shadow passes: the active shadow shader wants the full model-view-projection.
        var rapi = capi.Render;
        var mvp = new Matrixf().Set(rapi.CurrentProjectionMatrix).Mul(rapi.CurrentModelviewMatrix).Mul(model.Values);
        rapi.CurrentActiveShader.UniformMatrix("mvpMatrix", mvp.Values);
        rapi.CurrentActiveShader.Uniform("origin", new Vec3f(0, 0, 0));
        rapi.RenderMultiTextureMesh(mesh, "tex2d", 0);
    }

    /// <summary>A block's textures with <paramref name="metal"/> in place of <c>metal</c>.</summary>
    public sealed class MetalTextureSource(ITexPositionSource inner, TextureAtlasPosition? metal) : ITexPositionSource
    {
        public Size2i AtlasSize => inner.AtlasSize!;
        public TextureAtlasPosition this[string textureCode] =>
            textureCode == "metal" && metal != null ? metal : inner[textureCode]!;
    }
}
