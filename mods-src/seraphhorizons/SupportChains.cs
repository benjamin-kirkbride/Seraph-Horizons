using Vintagestory.API.Common;

namespace SeraphHorizons.Mod;

/// <summary>
/// Better Ruins (<c>betterruins</c>): its Machinist's Mechanism Blueprint makes <see cref="Yield"/>
/// support chains a craft instead of 64, by a JSON patch, <c>patches/supportchains-betterruins.json</c>,
/// that replaces the output quantity of the four chain recipes in
/// <c>recipes/grid/schematic-mechanical/mechanical.json</c> (0.6.4: <c>/26</c> to <c>/29</c>).
///
/// No code runs: with the switch off, or without Better Ruins, <see cref="DisablePatches"/> empties
/// the patch file in <c>Start</c>, before the game's patch loader runs in <c>AssetsLoaded</c>.
/// </summary>
public static class SupportChains
{
    public const string ModId = "betterruins";
    public const int Yield = 4;
    public const int Shipped = 64;

    /// <summary>The chains the blueprint makes.</summary>
    public static readonly string[] Codes =
        ["game:supportchain-twonew", "game:supportchain-fournew", "game:supportchain-two", "game:supportchain-four"];

    public static readonly AssetLocation RecipeAsset = new(ModId, "recipes/grid/schematic-mechanical/mechanical.json");

    public static readonly AssetLocation PatchAsset = new("seraphhorizons", "patches/supportchains-betterruins.json");

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
