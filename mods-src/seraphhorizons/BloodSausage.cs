using Vintagestory.API.Common;

namespace SeraphHorizons.Mod;

/// <summary>
/// Butchering (<c>butchering</c>) makes raw blood sausage and raw black pudding
/// (<c>butchering:sausage-bloodsausage-raw</c>, <c>butchering:sausage-blackpudding-raw</c>) two ways:
/// four grid recipes (<c>recipes/grid/bloodsausage.json</c>, three, by meat;
/// <c>recipes/grid/blackpudding.json</c>, one), and three kneading recipes for A Culinary Artillery's
/// mixing bowl (<c>recipes/kneading/bloodmeatnuggetsausages.json</c>), which it enables when Expanded
/// Foods is installed. The pack keeps the mixing bowl's: this tweak takes the grid recipes away
/// (<c>enabled: false</c>) by a JSON patch, <c>patches/bloodsausage-butchering.json</c>.
///
/// No code runs: with the switch off, or without Butchering, Expanded Foods or A Culinary Artillery
/// (without which the mixing bowl cannot make them), <see cref="DisablePatches"/> empties the patch
/// file in <c>Start</c>, before the game's patch loader runs in <c>AssetsLoaded</c>.
/// </summary>
public static class BloodSausage
{
    public const string ModId = "butchering";
    public const string BloodSausageRaw = "butchering:sausage-bloodsausage-raw";
    public const string BlackPuddingRaw = "butchering:sausage-blackpudding-raw";

    public static readonly AssetLocation PatchAsset = new("seraphhorizons", "patches/bloodsausage-butchering.json");

    public static bool Applies(ICoreAPI api) =>
        api.ModLoader.IsModEnabled(ModId) && api.ModLoader.IsModEnabled("expandedfoods")
                                          && api.ModLoader.IsModEnabled("aculinaryartillery");

    /// <summary>Empties the patch file, so the patch loader applies none of it. Runs in Start: the
    /// assets are there, and the patches are applied in AssetsLoaded.</summary>
    public static void DisablePatches(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(PatchAsset);
        if (asset != null)
            asset.Data = "[]"u8.ToArray();
    }
}
