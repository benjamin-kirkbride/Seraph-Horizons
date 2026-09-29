namespace SeraphHorizons.IconExport.Core;

public enum UniformType
{
    Float,
    Int,
    Vec2,
    Vec4,
}

public sealed record UniformDefault(string Name, UniformType Type, float[] Value);

/// <summary>
/// The uniforms of the game's GUI shader (assets/game/shaders/gui.vsh and gui.fsh, 1.22.7) and
/// the value each has when an inventory icon draws correctly. The export sets all of them before
/// every draw, because any renderer earlier in the frame, the game's or a mod's, may have left
/// them otherwise. RenderItemstackToGui then sets the ones it cares about (matrices, alphaTest,
/// normalShaded, applyColor, the textures) and trusts the rest. lightPosition is left alone: it
/// has no fixed default, and a wrong value changes the shading, not the texture.
/// </summary>
public static class GuiUniforms
{
    public static readonly IReadOnlyList<UniformDefault> Defaults = new UniformDefault[]
    {
        // Fragment shader. noTexture > 0 draws the vertex colour alone: the white icons.
        new("noTexture", UniformType.Float, new[] { 0f }),
        new("alphaTest", UniformType.Float, new[] { 0f }),
        new("darkEdges", UniformType.Int, new[] { 0f }),
        new("tempGlowMode", UniformType.Int, new[] { 0f }),
        new("transparentCenter", UniformType.Int, new[] { 0f }),
        new("normalShaded", UniformType.Int, new[] { 0f }),
        new("sepiaLevel", UniformType.Float, new[] { 0f }),
        new("damageEffect", UniformType.Float, new[] { 0f }),
        new("overlayOpacity", UniformType.Float, new[] { 0f }),
        new("overlayTextureSize", UniformType.Vec2, new[] { 1f, 1f }),
        new("baseTextureSize", UniformType.Vec2, new[] { 1f, 1f }),
        new("baseUvOrigin", UniformType.Vec2, new[] { 0f, 0f }),
        // Samplers hold a texture unit: tex2d reads unit 0, the overlay unit 1.
        new("tex2d", UniformType.Int, new[] { 0f }),
        new("tex2dOverlay", UniformType.Int, new[] { 1f }),
        // Vertex shader. applyAnimation > 0 moves every vertex by the animation buffer.
        new("rgbaIn", UniformType.Vec4, new[] { 1f, 1f, 1f, 1f }),
        new("rgbaGlowIn", UniformType.Vec4, new[] { 0f, 0f, 0f, 0f }),
        new("extraGlow", UniformType.Int, new[] { 0f }),
        new("applyModelMat", UniformType.Int, new[] { 0f }),
        new("applyColor", UniformType.Int, new[] { 0f }),
        new("applyAnimation", UniformType.Int, new[] { 0f }),
    };

    /// <summary>
    /// The uniforms a <c>--reset</c> option selects: null for all, "none" for none, or one name.
    /// Returns false for a name that is not in the table.
    /// </summary>
    public static bool TrySelect(string? reset, out IReadOnlyList<UniformDefault> selected)
    {
        if (reset == null)
        {
            selected = Defaults;
            return true;
        }
        if (reset == "none")
        {
            selected = Array.Empty<UniformDefault>();
            return true;
        }
        UniformDefault? one = Defaults.FirstOrDefault(u => u.Name == reset);
        selected = one == null ? Array.Empty<UniformDefault>() : new[] { one };
        return one != null;
    }
}

/// <summary>
/// The job being rendered, written to a file before each draw and deleted when the run ends. If
/// the game crashes inside a draw, the file names the item, and the next run skips it (unless
/// forced) and records it as failed.
/// </summary>
public static class InflightMarker
{
    public const string FileName = "inflight.txt";

    public static string Format(IEnumerable<RenderJob> jobs) =>
        string.Concat(jobs.Select(j => $"{IconKinds.Name(j.Kind)} {j.Code}\n"));

    public static List<RenderTarget> Parse(string text)
    {
        var list = new List<RenderTarget>();
        foreach (string line in text.Split('\n'))
        {
            string[] parts = line.Trim().Split(' ', 2);
            if (parts.Length == 2 && IconKinds.TryParse(parts[0], out IconKind kind) && IconCode.IsValid(parts[1]))
            {
                list.Add(new RenderTarget(parts[1], kind));
            }
        }
        return list;
    }
}
