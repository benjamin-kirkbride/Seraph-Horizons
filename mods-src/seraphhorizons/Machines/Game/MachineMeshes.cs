using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Machines;

/// <summary>
/// Client-side mesh work the machine renderers share: one mesh per rig part cut out of the
/// machine's shape (<see cref="PartMeshes"/>, cached for the session by <see cref="MachinePartMeshes"/>);
/// a metal texture swapped in for <c>metal</c> on some parts; a tessellated mesh cut into blocks
/// along its length; and drawing a mesh in the opaque pass or either shadow pass.
/// <para>The per-part meshes come from one tessellation of the whole shape, split afterwards. The
/// split rides on the engine's own per-element hook: <c>ITesselatorAPI.TesselateShapeWithJointIds</c>
/// writes each element's <c>JointId</c> into <c>MeshData.CustomInts</c>, once per vertex of every
/// face it emits (the hook animated entities use). Every element of a clone of the shape gets its
/// part + 1 as its JointId (0 for an element of no part, or of a part not wanted, whose faces are
/// also blanked so they are never built), so each face of the result names its part, and the
/// faces are copied out by that tag (<see cref="PartSplit.FacesByPart"/>). Which element belongs to
/// which part is exactly what the earlier way decided (<see cref="RigParts.PartOf"/> by the element's
/// name chain, <see cref="PartSplit.Walk"/>), and nothing depends on the order the tesselator
/// visits elements or skips faces in. The tags are checked before they are trusted (one per
/// vertex, the same on a face's four vertices, in range, and each face's six indices among its
/// own four vertices); if any check fails the parts are tessellated one by one as before
/// (<see cref="PartMesh"/>: a clone per part with every other part's elements blanked, as
/// Immersive Woodworking's sawmill renderer does), and that is logged once.</para>
/// </summary>
public static class MachineMeshes
{
    private static bool _splitFailedLogged;

    /// <summary>
    /// One mesh per rig part, by part index, in the native frame (blocks), from a single
    /// tessellation of <paramref name="master"/> with <paramref name="tex"/>: for each part
    /// <paramref name="wanted"/> accepts, its elements' faces (null when it has none); null for the
    /// others. Falls back to one tessellation per wanted part when the split's checks fail.
    /// </summary>
    public static MeshData?[] PartMeshes(ICoreClientAPI capi, Shape master, RigParts parts, System.Func<int, bool> wanted,
                                         ITexPositionSource tex, string name)
    {
        int n = parts.Parts.Count;
        var result = new MeshData?[n];
        var shape = master.Clone();
        int kept = 0;
        PartSplit.Walk(shape.Elements, el => el.Name, el => el.Children, parts, (el, part) =>
        {
            if (part >= 0 && wanted(part))
            {
                el.JointId = part + 1;
                kept++;
            }
            else
            {
                el.JointId = 0;
                el.FacesResolved = new ShapeElementFace[6];
            }
        });
        if (kept == 0)
            return result;
        capi.Tesselator.TesselateShapeWithJointIds(name, shape, out var mesh, tex, null);
        var faces = Split(mesh, n);
        if (faces == null)
        {
            if (!_splitFailedLogged)
            {
                _splitFailedLogged = true;
                capi.Logger.Warning("[seraphhorizons] {0}: the tessellated model could not be split by part; tessellating each part on its own", name);
            }
            for (int i = 0; i < n; i++)
                if (wanted(i))
                    result[i] = PartMesh(capi, master, parts, i, tex, name);
            return result;
        }
        for (int i = 0; i < n; i++)
            if (faces[i].Count > 0)
                result[i] = Extract(mesh, faces[i]);
        return result;
    }

    // Each part's faces, or null when the mesh's tags or indices cannot be trusted.
    private static List<int>[]? Split(MeshData mesh, int partCount)
    {
        var tags = mesh.CustomInts;
        int faces = mesh.VerticesCount / PartSplit.VerticesPerFace;
        if (tags == null || tags.Count != mesh.VerticesCount || mesh.VerticesPerFace != PartSplit.VerticesPerFace
            || mesh.IndicesPerFace != PartSplit.IndicesPerFace || mesh.TextureIndicesCount != faces
            || !PartSplit.FacesAreSelfContained(mesh.Indices, mesh.IndicesCount, mesh.VerticesCount))
            return null;
        return PartSplit.FacesByPart(tags.Values, mesh.VerticesCount, partCount);
    }

    // A new mesh of the given faces of the source, with everything the tesselator writes per vertex
    // (position, uv, colour, flags, normals if any) and per face (texture, xyz face, colour maps,
    // render pass), and no custom data.
    private static MeshData Extract(MeshData src, List<int> faces)
    {
        const int vpf = PartSplit.VerticesPerFace, ipf = PartSplit.IndicesPerFace;
        int srcFaces = src.VerticesCount / vpf;
        var dst = new MeshData(faces.Count * vpf, faces.Count * ipf, withNormals: src.Normals != null,
                               withUv: src.Uv != null, withRgba: src.Rgba != null, withFlags: src.Flags != null)
            .WithColorMaps().WithRenderpasses();
        dst.HasAnyWindModeSet = src.HasAnyWindModeSet;
        foreach (int f in faces)
        {
            int from = f * vpf, to = dst.VerticesCount;
            Array.Copy(src.xyz, from * 3, dst.xyz, to * 3, vpf * 3);
            if (src.Uv != null)
                Array.Copy(src.Uv, from * 2, dst.Uv!, to * 2, vpf * 2);
            if (src.Rgba != null)
                Array.Copy(src.Rgba, from * 4, dst.Rgba!, to * 4, vpf * 4);
            if (src.Flags != null)
                Array.Copy(src.Flags, from, dst.Flags!, to, vpf);
            if (src.Normals != null)
                Array.Copy(src.Normals, from, dst.Normals!, to, vpf);
            dst.VerticesCount += vpf;
            for (int k = 0; k < ipf; k++)
                dst.AddIndex(to + src.Indices[f * ipf + k] - from);
            if (src.Uv != null)
                dst.AddTextureId(src.TextureIds[src.TextureIndices[f]]);
            if (src.XyzFacesCount == srcFaces)
                dst.AddXyzFace(src.XyzFaces[f]);
            if (src.ColorMapIdsCount == srcFaces)
                dst.AddColorMapIndex(src.ClimateColorMapIds[f], src.SeasonColorMapIds[f]);
            if (src.RenderPassCount == srcFaces)
                dst.AddRenderPass(src.RenderPassesAndExtraBits[f]);
        }
        return dst;
    }

    /// <summary>The elements of rig part <paramref name="part"/> alone, from a clone of
    /// <paramref name="master"/>, in the native frame (blocks); null when it has none. One
    /// tessellation per part: <see cref="PartMeshes"/>' fallback.</summary>
    public static MeshData? PartMesh(ICoreClientAPI capi, Shape master, RigParts parts, int part, ITexPositionSource tex, string name)
    {
        var shape = master.Clone();
        int kept = 0;
        PartSplit.Walk(shape.Elements, el => el.Name, el => el.Children, parts, (el, p) =>
        {
            if (p == part)
                kept++;
            else
                el.FacesResolved = new ShapeElementFace[6];
        });
        if (kept == 0)
            return null;
        capi.Tesselator.TesselateShape(name, shape, out var mesh, tex, null, 0, 0, 0, null, null);
        return mesh.VerticesCount > 0 ? mesh : null;
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
