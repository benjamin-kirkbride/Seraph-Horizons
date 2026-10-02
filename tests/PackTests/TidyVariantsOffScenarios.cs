using Atlas.XUnit;
using HarmonyLib;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphtweaks, Tidy Variants with its switch off (<c>"TidyVariants": false</c> in
/// ModConfig/seraphtweaks.json, seeded from fixtures/tidyvariants-off before the server boots): the
/// feature resolves nothing, so the client side has nothing to act on, and the mod's other tweaks are
/// unaffected. Its own server: a class boots a fresh one with its own data path.
/// </summary>
[AtlasWorld]
[AtlasDataFiles("fixtures/tidyvariants-off", TargetPath = "ModConfig")]
public class TidyVariantsOffScenarios : AtlasScenarioBase
{
    private const string ModId = "seraphtweaks";

    [AtlasScenario]
    public async Task Switched_off_the_feature_resolves_nothing_and_the_other_tweaks_still_apply()
    {
        await World.Ticks(2);   // past the WorldReady run phase, where the server would resolve
        var system = World.Api.ModLoader.GetMod(ModId).Systems
            .Single(s => s.GetType().FullName == "SeraphHorizons.SeraphTweaks.TidyVariants.TidyVariantsModSystem");
        Assert.Null(system.GetType().GetProperty("Bridge")!.GetValue(system));

        // The mod read the seeded file (and wrote back the settings it lacked, all on by default).
        var config = World.Api.LoadModConfig("seraphtweaks.json");
        Assert.False(config["TidyVariants"].AsBool(true));
        Assert.True(config["BoilerLidBlowsOpen"].AsBool(false));

        // The boiler tweak's patch is still applied; nothing of the creative inventory's is.
        var owners = Harmony.GetAllPatchedMethods().SelectMany(m => Harmony.GetPatchInfo(m)?.Owners ?? []).ToHashSet();
        Assert.Contains("seraphtweaks", owners);
        Assert.DoesNotContain("seraphtweaks.creative", owners);
    }
}
