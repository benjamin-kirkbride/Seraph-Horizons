using Vintagestory.API.Common;

namespace SeraphHorizons.Mod;

/// <summary>
/// Fresh fruit dries into Expanded Foods' dried fruit (README, "Fruit dries into Expanded Foods' dried
/// fruit"; switch <see cref="SeraphHorizonsConfig.FruitDries"/>): the game's fruit gets back the four
/// <c>Dry</c> transitions Expanded Foods 2.0.0-dev.16 dropped, by one JSON patch,
/// <c>patches/fruitdrying-expandedfoods.json</c>, so <c>expandedfoods:dryfruit-*</c>, which its
/// recipes still take, is made again.
///
/// No code runs: with the switch off, or without Expanded Foods, <see cref="DisablePatches"/> empties
/// the patch file in <c>Start</c>, before the game's patch loader runs in <c>AssetsLoaded</c>.
/// </summary>
public static class FruitDrying
{
    public const string ExpandedFoodsId = "expandedfoods";

    public static readonly AssetLocation PatchAsset = new("seraphhorizons", "patches/fruitdrying-expandedfoods.json");

    public static bool Applies(ICoreAPI api) => api.ModLoader.IsModEnabled(ExpandedFoodsId);

    /// <summary>Empties the patch file, so the patch loader applies none of it.</summary>
    public static void DisablePatches(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(PatchAsset);
        if (asset != null)
            asset.Data = "[]"u8.ToArray();
    }
}
