using Vintagestory.API.Common;

namespace SeraphHorizons.Mod;

/// <summary>
/// Hydrate or Diedrate (<c>hydrateordiedrate</c>) has a tun (<c>hydrateordiedrate:tun-*</c>, a 2x2x2
/// liquid container), and so does Food Shelves (a tun in a tun rack, <see cref="TunRackCapacity"/>).
/// The pack keeps Food Shelves' one: this tweak takes Hydrate or Diedrate's tun's grid recipe away
/// (<c>enabled: false</c>) and leaves the block out of the creative inventory and the handbook, by a
/// JSON patch, <c>patches/tun-hydrateordiedrate.json</c>.
///
/// The block type itself stays registered: a tun already placed in a world keeps its liquid and its
/// block entity, and still works, breaks and drops as before. Only new ones can no longer be made.
///
/// No code runs: with the switch off, or without Hydrate or Diedrate, <see cref="DisablePatches"/>
/// empties the patch file in <c>Start</c>, before the game's patch loader runs in
/// <c>AssetsLoaded</c>.
/// </summary>
public static class HydrateTun
{
    public const string ModId = "hydrateordiedrate";
    public const string Block = "hydrateordiedrate:tun-east";

    public static readonly AssetLocation PatchAsset = new("seraphhorizons", "patches/tun-hydrateordiedrate.json");

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
