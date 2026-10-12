using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.HandbookPlace.Game;

/// <summary>
/// The handbook keeps its place across a close and an open (README, "The handbook keeps your place"; switch
/// <see cref="SeraphHorizonsConfig.HandbookKeepsPlace"/>): <see cref="KeepHandbookPlace"/>, through
/// <see cref="HandbookPlacePatches"/>, applied by hand here with their own id, on the client only and only
/// with the switch on; never <c>PatchAll</c>. State is in memory only and goes with the world.
/// </summary>
public sealed class HandbookPlaceModSystem : ModSystem
{
    public const string HarmonyId = "seraphhorizons.handbookplace";

    private Harmony? _harmony;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        if (!SeraphHorizonsSystem.ConfigFor(api).HandbookKeepsPlace || !KeepHandbookPlace.Init(api)) return;
        int applied = HandbookPlacePatches.Apply(_harmony = new Harmony(HarmonyId), api.Logger);
        api.Logger.Notification("[seraphhorizons] Handbook keeps its place: {0}/{1} patches applied",
            applied, HandbookPlacePatches.Count);
    }

    public override void Dispose()
    {
        try { _harmony?.UnpatchAll(HarmonyId); }
        catch { /* shutting down */ }
        _harmony = null;
        KeepHandbookPlace.Reset();
    }
}
