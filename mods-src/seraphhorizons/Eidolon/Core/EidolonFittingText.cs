namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>A piece of text as lang keys: <see cref="Key"/> filled with each of <see cref="ArgKeys"/>
/// looked up in turn.</summary>
public sealed record FittingName(string Key, params string[] ArgKeys);

/// <summary>
/// How an eidolon's fittings read in its info text (<c>EntityLaborEidolon.GetInfoText</c>), as lang
/// keys: each layer that is on as "{material} {layer}", the material the game's own name for the wood
/// (<c>game:material-oak</c>) or metal (<c>game:material-steel</c>), a sash its colour
/// (<c>game:color-red</c>) and leather or cloth.
/// </summary>
public static class EidolonFittingText
{
    /// <summary>The layer's name (<c>seraphhorizons:eidolon-layer-plates</c>, ...).</summary>
    public static string LayerKey(FittingLayer layer) => "seraphhorizons:eidolon-layer-" + EidolonFittings.TextureCode(layer);

    /// <summary>The material's name, or null when the layer does not take it.</summary>
    public static FittingName? Material(FittingLayer layer, string? material)
    {
        if (!EidolonFittings.Allows(layer, material))
            return null;
        string m = material!;
        if (layer != FittingLayer.Sash)
            return new FittingName("game:material-" + m);
        int dash = m.IndexOf('-');
        return new FittingName($"seraphhorizons:eidolon-fitting-{m[..dash]}", "game:color-" + m[(dash + 1)..]);
    }

    /// <summary>Each layer that is on, in build order: its material's name and the layer's.</summary>
    public static IReadOnlyList<(FittingName Material, string Layer)> Layers(EidolonBuild build) =>
        EidolonFittings.Layers.Where(l => build[l] != null)
            .Select(l => (Material(l, build[l])!, LayerKey(l)))
            .ToList();
}
