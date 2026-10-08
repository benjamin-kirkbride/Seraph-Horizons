namespace SeraphHorizons.Mod.Machines.Core;

/// <summary>
/// The game-free half of cutting a machine's model into one mesh per rig part in a single
/// tessellation (<c>MachineMeshes.PartMeshes</c>): which part claims each element of the shape's
/// tree, and, once the tesselator has written a tag per vertex (the element's part + 1, 0 for none),
/// which faces belong to which part. Both refuse anything they cannot split exactly, so the caller
/// can fall back to tessellating part by part.
/// </summary>
public static class PartSplit
{
    public const int VerticesPerFace = 4;
    public const int IndicesPerFace = 6;

    /// <summary>Walks an element tree depth-first in element order and calls
    /// <paramref name="assign"/> with every element and the part its name chain (its ancestors'
    /// names and its own) belongs to (<see cref="RigParts.PartOf"/>), or -1.</summary>
    public static void Walk<T>(IReadOnlyList<T>? elements, Func<T, string?> name, Func<T, IReadOnlyList<T>?> children,
                               RigParts parts, Action<T, int> assign) =>
        Walk(elements, name, children, parts, assign, []);

    private static void Walk<T>(IReadOnlyList<T>? elements, Func<T, string?> name, Func<T, IReadOnlyList<T>?> children,
                                RigParts parts, Action<T, int> assign, List<string> chain)
    {
        if (elements == null)
            return;
        foreach (var el in elements)
        {
            chain.Add(name(el) ?? "");
            assign(el, parts.PartOf(chain));
            Walk(children(el), name, children, parts, assign, chain);
            chain.RemoveAt(chain.Count - 1);
        }
    }

    /// <summary>
    /// Each part's faces, in mesh order, from a tag per vertex (<paramref name="tags"/>, the
    /// first <paramref name="vertices"/> entries: part + 1, or 0 for faces of no part, which are
    /// dropped). Null when the mesh cannot be split exactly: the vertices are not whole faces, a
    /// face's four tags disagree, or a tag is not 0..<paramref name="partCount"/>.
    /// </summary>
    public static List<int>[]? FacesByPart(IReadOnlyList<int> tags, int vertices, int partCount)
    {
        if (vertices < 0 || vertices % VerticesPerFace != 0 || tags.Count < vertices)
            return null;
        var result = new List<int>[partCount];
        for (int p = 0; p < partCount; p++)
            result[p] = [];
        for (int f = 0; f < vertices / VerticesPerFace; f++)
        {
            int tag = tags[f * VerticesPerFace];
            for (int v = 1; v < VerticesPerFace; v++)
                if (tags[f * VerticesPerFace + v] != tag)
                    return null;
            if (tag < 0 || tag > partCount)
                return null;
            if (tag > 0)
                result[tag - 1].Add(f);
        }
        return result;
    }

    /// <summary>Whether the first <paramref name="indexCount"/> indices draw the mesh's faces
    /// one by one: six per face, face f's all among its own four vertices, so a face can be moved
    /// to another mesh with its indices shifted by the same amount as its vertices.</summary>
    public static bool FacesAreSelfContained(IReadOnlyList<int> indices, int indexCount, int vertices)
    {
        if (vertices % VerticesPerFace != 0 || indexCount != vertices / VerticesPerFace * IndicesPerFace || indices.Count < indexCount)
            return false;
        for (int i = 0; i < indexCount; i++)
        {
            int f = i / IndicesPerFace, idx = indices[i];
            if (idx < f * VerticesPerFace || idx >= (f + 1) * VerticesPerFace)
                return false;
        }
        return true;
    }
}
