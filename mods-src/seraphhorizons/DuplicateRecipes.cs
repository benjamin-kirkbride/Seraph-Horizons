using Vintagestory.API.Common;

namespace SeraphHorizons.Mod;

/// <summary>
/// Recipes that duplicate or undercut another recipe for the same thing are switched off
/// (<c>enabled: false</c>) by one JSON patch, <c>patches/duplicaterecipes.json</c>: Expanded Foods'
/// kneading sausages without offal (Butchering's, with clean offal, stay), Expanded Foods' scrap
/// brazier (HQZ Lights' own stays), Material Needs' re-declared aged roofing, raft, oar and round
/// shield (the game's stay), the game's barrel cottage cheese (Expanded Foods' mixing bowl recipe
/// stays) and the game's sandstone daub (GeoAddons' stays). Each patch <c>dependsOn</c> the mod it
/// patches and the mod whose recipe replaces it, so the patch loader skips it when either is missing.
///
/// No code runs: with the switch off, <see cref="DisablePatches"/> empties the patch file in
/// <c>Start</c>, before the game's patch loader runs in <c>AssetsLoaded</c>.
/// </summary>
public static class DuplicateRecipes
{
    public static readonly AssetLocation PatchAsset = new("seraphhorizons", "patches/duplicaterecipes.json");

    /// <summary>Empties the patch file, so the patch loader applies none of it. Runs in Start: the
    /// assets are there, and the patches are applied in AssetsLoaded.</summary>
    public static void DisablePatches(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(PatchAsset);
        if (asset != null)
            asset.Data = "[]"u8.ToArray();
    }
}
