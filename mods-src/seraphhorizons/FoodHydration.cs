using Vintagestory.API.Common;

namespace SeraphHorizons.Mod;

/// <summary>
/// Hydrate or Diedrate (<c>hydrateordiedrate</c>) gives a food its hydration from pattern lists,
/// and the pack has foods none of them match (#319): they neither quench nor cost thirst. This
/// tweak sets a <c>hydration</c> attribute on each, modelled on Hydrate or Diedrate's value for a
/// similar food, by plain JSON patches in <c>patches/hydration-*.json</c> (one per mod patched),
/// each <c>dependsOn</c> hydrateordiedrate and the patched mod. Hydrate or Diedrate never overwrites
/// a hydration attribute that is already set, so a value it adds later does not replace these.
///
/// No code runs: with the switch off, or without Hydrate or Diedrate, <see cref="DisablePatches"/>
/// empties the patch files in <c>Start</c>, before the game's patch loader runs in
/// <c>AssetsLoaded</c>.
/// </summary>
public static class FoodHydration
{
    public const string ModId = "hydrateordiedrate";

    public static readonly AssetLocation[] PatchAssets =
    [
        new("seraphhorizons", "patches/hydration-game.json"),
        new("seraphhorizons", "patches/hydration-bdcrop.json"),
        new("seraphhorizons", "patches/hydration-butchering.json"),
        new("seraphhorizons", "patches/hydration-efchefstricks.json"),
        new("seraphhorizons", "patches/hydration-expandedfoods.json"),
        new("seraphhorizons", "patches/hydration-primitivesurvival.json"),
    ];

    public static bool Applies(ICoreAPI api) => api.ModLoader.IsModEnabled(ModId);

    /// <summary>Empties this mod's hydration patch files, so the patch loader applies none of
    /// them. Runs in Start: the assets are there, and the patches are applied in AssetsLoaded.</summary>
    public static void DisablePatches(ICoreAPI api)
    {
        foreach (var location in PatchAssets)
        {
            var asset = api.Assets.TryGet(location);
            if (asset != null)
                asset.Data = "[]"u8.ToArray();
        }
    }
}
