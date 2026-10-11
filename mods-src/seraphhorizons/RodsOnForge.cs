using Vintagestory.API.Common;

namespace SeraphHorizons.Mod;

/// <summary>
/// The game's metal rods (<c>game:rod-*</c>) go on the forge and heat there, as an ingot does, so a steel
/// rod can be brought to working heat for the mandrel station's spring job. The forge
/// (<c>BlockEntityForge.OnPlayerInteract</c>, game 1.22.7) takes a work item when its code's first part is
/// <c>ingot</c>, <c>metalplate</c> or <c>workitem</c>, or when the item's <c>forgable</c> attribute is
/// true (as the tool heads, the iron bloom and the metal tongs are), one at a time; its renderer
/// (<c>ForgeContentsRenderer</c>) draws a forgeable item with its own shape, moved by its
/// <c>inForgeTransform</c> attribute, on the coals. The rod's type has neither, so a JSON patch,
/// <c>patches/rodsonforge.json</c>, adds both to <c>game:itemtypes/part/rod.json</c>: the rod lies flat on
/// the coals, turned a little, one at a time (a forgeable item does not stack on the forge as ingots do).
/// The forge heats whatever it holds, so nothing else is needed.
///
/// No code runs: with the switch off <see cref="DisablePatches"/> empties the patch file in <c>Start</c>,
/// before the game's patch loader runs in <c>AssetsLoaded</c>.
/// </summary>
public static class RodsOnForge
{
    public static readonly AssetLocation PatchAsset = new("seraphhorizons", "patches/rodsonforge.json");

    /// <summary>Empties the patch file, so the patch loader applies none of it.</summary>
    public static void DisablePatches(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(PatchAsset);
        if (asset != null)
            asset.Data = "[]"u8.ToArray();
    }
}
