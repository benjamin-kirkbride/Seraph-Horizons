using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.CreativeSearch;

/// <summary>
/// Two client-only conveniences on the creative inventory's search (README, "The creative inventory keeps
/// your place" and "Right-click clears the search box"; hooks in docs/variant-grouping/hooks.md §7):
/// <see cref="KeepPlace"/> (switch <see cref="SeraphHorizonsConfig.CreativeKeepsPlace"/>) puts the search text and
/// the grid's scroll back when the dialog reopens, and <see cref="SearchClear"/> (switch
/// <see cref="SeraphHorizonsConfig.SearchRightClickClears"/>) empties the creative and handbook search boxes on a right-click.
///
/// Patches are applied by hand here, with their own id, on the client only and only for a switch that is on
/// (<see cref="CreativeSearchPatches"/>); never <c>PatchAll</c>. State is in memory only and goes with the world.
/// </summary>
public sealed class CreativeSearchModSystem : ModSystem
{
    public const string HarmonyId = "seraphhorizons.creativesearch";

    private Harmony? _harmony;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        var config = SeraphHorizonsSystem.ConfigFor(api);
        if (config.CreativeKeepsPlace && KeepPlace.Init(api))
        {
            int applied = CreativeSearchPatches.ApplyKeepPlace(_harmony ??= new Harmony(HarmonyId), api.Logger);
            api.Logger.Notification("[seraphhorizons] Creative inventory keeps its place: {0}/{1} patches applied",
                applied, CreativeSearchPatches.KeepPlaceCount);
        }
        if (config.SearchRightClickClears)
        {
            SearchClear.Init(api);
            if (CreativeSearchPatches.ApplyRightClick(_harmony ??= new Harmony(HarmonyId), api.Logger))
                api.Logger.Notification("[seraphhorizons] Right-click clears the search box: patch applied");
        }
    }

    public override void Dispose()
    {
        try { _harmony?.UnpatchAll(HarmonyId); }
        catch { /* shutting down */ }
        _harmony = null;
        KeepPlace.Reset();
        SearchClear.Reset();
    }
}
