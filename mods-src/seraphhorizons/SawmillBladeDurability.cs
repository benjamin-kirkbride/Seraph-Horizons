using Vintagestory.API.Common;

namespace SeraphHorizons.Mod;

/// <summary>
/// Immersive Woodworking (<c>immersivewoodworking</c>): its sawmill blade kits
/// (<c>immersivewoodworking:sawmillblade-{metal}</c>) last <see cref="Factor"/> times as long, by a
/// JSON patch, <c>patches/sawmillblade-durability.json</c>, that sets each metal's durability to
/// three times Immersive Woodworking's (1.3.11: gold 70, silver 90, copper 250, tin bronze 400,
/// bismuth bronze 450, black bronze 500, iron 900, meteoric iron 1200, steel 2250). The bucking
/// sawmill wears its one kit by a log's worth for every log in a trunk; the patch is the kit's, so
/// Immersive Woodworking's own plank sawmill gets the longer life too.
///
/// No code runs: with the switch off, or without Immersive Woodworking, <see cref="DisablePatches"/>
/// empties the patch file in <c>Start</c>, before the game's patch loader runs in
/// <c>AssetsLoaded</c>.
/// </summary>
public static class SawmillBladeDurability
{
    public const string ModId = "immersivewoodworking";
    public const int Factor = 3;

    /// <summary>Immersive Woodworking's own durabilities, which the patch triples.</summary>
    public static readonly IReadOnlyDictionary<string, int> Shipped = new Dictionary<string, int>
    {
        ["gold"] = 70,
        ["silver"] = 90,
        ["copper"] = 250,
        ["tinbronze"] = 400,
        ["bismuthbronze"] = 450,
        ["blackbronze"] = 500,
        ["iron"] = 900,
        ["meteoriciron"] = 1200,
        ["steel"] = 2250,
    };

    public static readonly AssetLocation PatchAsset = new("seraphhorizons", "patches/sawmillblade-durability.json");

    public static bool Applies(ICoreAPI api) => api.ModLoader.IsModEnabled(ModId);

    /// <summary>Empties the patch file, so the patch loader applies none of it. Runs in Start: the
    /// assets are there, and the patches are applied in AssetsLoaded.</summary>
    public static void DisablePatches(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(PatchAsset);
        if (asset != null)
            asset.Data = "[]"u8.ToArray();
    }
}
