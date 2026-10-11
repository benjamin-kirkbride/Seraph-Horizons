using System.Text.Json;

namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>
/// Which of the eidolon shape's elements each layer is (<c>config/eidolon-stages.json</c>, written by
/// Eidolon/tools/make_shape.py: every element in exactly one stage), so a layer that is off is not drawn
/// on the entity: a stripped eidolon hangs in the dock without it (Eidolon/README.md, "Docking"). The
/// game removes an element with its children, and no element hangs from one of a later stage (the
/// generator's rig order; a test holds the shape to it), so a layer taken off takes with it only the
/// later layers, already off, and with the joints the mind's eye light on its bracket (<c>Eye-out</c>
/// on <c>Eye-bracket</c>).
/// </summary>
public sealed class EidolonLayerElements
{
    private readonly Dictionary<FittingLayer, IReadOnlyList<string>> _byLayer;

    private EidolonLayerElements(Dictionary<FittingLayer, IReadOnlyList<string>> byLayer) => _byLayer = byLayer;

    /// <summary>The elements of <paramref name="layer"/> (none when the file lists none).</summary>
    public IReadOnlyList<string> Of(FittingLayer layer) => _byLayer.GetValueOrDefault(layer) ?? [];

    /// <summary>The elements of every layer <paramref name="build"/> does not wear, outermost first.</summary>
    public IReadOnlyList<string> Hidden(EidolonBuild build) =>
        EidolonFittings.StripOrder.Where(l => build[l] == null).SelectMany(Of).ToList();

    /// <summary>The stages file's layers (by their texture code) and their elements.</summary>
    public static EidolonLayerElements Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var byLayer = new Dictionary<FittingLayer, IReadOnlyList<string>>();
        foreach (var stage in doc.RootElement.GetProperty("stages").EnumerateArray())
        {
            if (!stage.TryGetProperty("code", out var code) || EidolonFittings.LayerOfTextureCode(code.GetString()) is not { } layer
                || !stage.TryGetProperty("elements", out var elements) || elements.ValueKind != JsonValueKind.Array)
                continue;
            byLayer[layer] = elements.EnumerateArray().Select(e => e.GetString()).OfType<string>().ToList();
        }
        return new EidolonLayerElements(byLayer);
    }
}
