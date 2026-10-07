using System.Text;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod;

/// <summary>
/// Battle Towers (<c>battletowers</c>, #519): its towers rarer. Battle Towers adds its three tower
/// structures to <c>game:worldgen/structures.json</c> by appending them (<c>/structures/-</c>) in its
/// own patch file, so their index there depends on every other mod adding structures, and a JSON
/// patch of ours cannot address them; nor can it patch Battle Towers' patch file, since the game's
/// patch loader reads every patch file before it applies any. So, on the server in <c>Start</c>,
/// before the patch loader runs in <c>AssetsLoaded</c>, this rewrites Battle Towers' patch file in
/// memory: each tower, found by its code, gets the chance and minimum group distance in
/// <c>config/battletowers-rates.json</c>, whose header has the engine facts and the maths.
///
/// With the switch off, or without Battle Towers, nothing is touched. Worldgen only: it changes the
/// chunks generated from then on, in any world, and never towers already placed.
/// </summary>
public static class BattleTowers
{
    public const string ModId = "battletowers";

    public static readonly AssetLocation PatchAsset = new(ModId, "patches/survival-worldgen-structures.json");
    public static readonly AssetLocation RatesAsset = new("seraphhorizons", "config/battletowers-rates.json");

    public static bool Applies(ICoreAPI api) => api.ModLoader.IsModEnabled(ModId);

    /// <summary>Rewrites Battle Towers' patch file with the pack's rates. Runs in Start, server side:
    /// the assets are there, and the patches are applied in AssetsLoaded. A tower missing from the
    /// file, or a file that no longer parses, is logged and left as Battle Towers ships it.</summary>
    public static void MakeRarer(ICoreAPI api)
    {
        var patch = api.Assets.TryGet(PatchAsset);
        var ratesAsset = api.Assets.TryGet(RatesAsset);
        if (patch == null || ratesAsset == null)
        {
            api.Logger.Warning("[seraphhorizons] Rarer battle towers: {0} not found; towers are as Battle Towers ships them",
                patch == null ? PatchAsset : RatesAsset);
            return;
        }
        try
        {
            var rates = BattleTowerRates.ParseRates(ratesAsset.ToText());
            var (text, applied) = BattleTowerRates.Apply(patch.ToText(), rates);
            patch.Data = Encoding.UTF8.GetBytes(text);
            var missing = rates.Keys.Except(applied).ToList();
            if (missing.Count > 0)
                api.Logger.Warning("[seraphhorizons] Rarer battle towers: Battle Towers no longer adds {0}; left alone",
                    string.Join(", ", missing));
            api.Logger.Notification("[seraphhorizons] Rarer battle towers: {0}",
                string.Join(", ", applied.Select(code => $"{code} chance {rates[code].Chance}"
                                                         + (rates[code].MinGroupDistance is { } d ? $", {d} blocks apart" : ""))));
        }
        catch (Exception e) when (e is FormatException or System.Text.Json.JsonException or InvalidOperationException)
        {
            api.Logger.Warning("[seraphhorizons] Rarer battle towers: could not rewrite Battle Towers' structures ({0}); towers are as it ships them",
                e.Message);
        }
    }
}
