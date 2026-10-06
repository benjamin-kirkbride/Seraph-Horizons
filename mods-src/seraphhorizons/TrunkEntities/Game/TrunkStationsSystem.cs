using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.TrunkEntities;

/// <summary>
/// Applies <see cref="TrunkStations"/>' prefixes on Logging Expanded's stations while trunk
/// entities run (<see cref="TrunkEntitySystem.Enabled"/>, decided before this runs) and Carry On
/// is there, on each side (once per process: a single-player game runs both in one), under its own
/// Harmony id. Otherwise it empties <see cref="PatchAsset"/>, which lets a click through Carry On's
/// hands to the stations, the rosser and the bucking mill.
/// </summary>
public class TrunkStationsSystem : ModSystem
{
    public const string HarmonyId = "seraphhorizons.trunkstations";

    /// <summary>Adds Carry On's <c>CarryableInteract</c> to the stations, the rosser and the mill.</summary>
    public static readonly AssetLocation PatchAsset = new(TrunkEntitySystem.Domain, "patches/trunkentities-stations.json");

    private Harmony? _harmony;

    // After TrunkEntitySystem (0.15) has decided.
    public override double ExecuteOrder() => 0.16;

    public override void Start(ICoreAPI api)
    {
        var trunks = TrunkEntitySystem.Of(api);
        if (!trunks.Enabled || !trunks.CarryOn)
            if (api.Assets.TryGet(PatchAsset) is { } asset)
                asset.Data = "[]"u8.ToArray();
    }

    public override void StartServerSide(ICoreServerAPI api) => Apply(api);

    public override void StartClientSide(ICoreClientAPI api) => Apply(api);

    private void Apply(ICoreAPI api)
    {
        var trunks = TrunkEntitySystem.Of(api);
        if (!trunks.Enabled || !trunks.CarryOn || Harmony.HasAnyPatches(HarmonyId))
            return;
        _harmony = new Harmony(HarmonyId);
        int patched = TrunkStations.Patch(_harmony, api);
        api.Logger.Notification("[seraphhorizons] Trunk entities: {0} of Logging Expanded's 3 trunk stations load from and unload into Carry On's hands", patched);
    }

    public override void Dispose()
    {
        _harmony?.UnpatchAll(HarmonyId);
        _harmony = null;
    }
}
