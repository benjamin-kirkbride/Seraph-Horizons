namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>
/// A build written out for a command (<c>/sh eidolon spawn</c> and <c>fit</c>): words of the form
/// <c>layer=material</c>, the layer by its texture code (<c>bones</c>, <c>joints</c>, <c>plates</c>,
/// <c>sash</c>, <c>skirt</c>), the material as <see cref="EidolonFittings"/> writes it, with
/// <c>stainless</c> for <c>stainlesssteel</c>. A layer not named keeps the build it starts from.
/// <c>all=metal</c> sets the joints, plates and skirt at once.
/// </summary>
public static class EidolonBuildSpec
{
    /// <summary>Whether a word is part of a spec (has an <c>=</c>).</summary>
    public static bool IsSpecWord(string word) => word.Contains('=');

    /// <summary><paramref name="from"/> with the layers <paramref name="words"/> name changed, or null
    /// and the reason when a word names no layer or a material its layer does not take.</summary>
    public static EidolonBuild? Parse(IEnumerable<string> words, EidolonBuild from, out string? error)
    {
        error = null;
        var materials = EidolonFittings.Layers.Select(l => from[l]).ToArray();
        foreach (var word in words)
        {
            int eq = word.IndexOf('=');
            if (eq <= 0)
            {
                error = $"'{word}' is not layer=material";
                return null;
            }
            string key = word[..eq].Trim().ToLowerInvariant();
            string material = Material(word[(eq + 1)..].Trim().ToLowerInvariant());
            var layers = key == "all"
                ? new[] { FittingLayer.Joints, FittingLayer.Plates, FittingLayer.Skirt }
                : EidolonFittings.LayerOfTextureCode(key) is { } one ? new[] { one } : null;
            if (layers == null)
            {
                error = $"no layer '{key}' (bones, joints, plates, sash, skirt, all)";
                return null;
            }
            foreach (var layer in layers)
            {
                if (!EidolonFittings.Allows(layer, material))
                {
                    error = $"{EidolonFittings.TextureCode(layer)} does not take '{material}'";
                    return null;
                }
                materials[(int)layer] = material;
            }
        }
        var build = EidolonBuild.Bare;
        foreach (var layer in EidolonFittings.Layers)
        {
            if (materials[(int)layer] is not { } m)
                break;
            build = build.With(layer, m)!;
        }
        return build;
    }

    private static string Material(string m) => m == "stainless" ? EidolonFittings.Stainless : m;
}
