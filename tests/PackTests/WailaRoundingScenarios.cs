using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons, WailaRounding: the info HUD's numbers are rounded. Atlas runs no client,
/// so this patches the HUD's compose method on the server's process (the HUD's class is in
/// VintagestoryLib, loaded on both sides) under a Harmony id of its own: the method and its two text
/// calls must still be there, once each, and the patch must go in. Then the rounding runs with the
/// game's own lang loaded, so the health line is found by vanilla's format string.
/// </summary>
public partial class SharedWorldScenarios
{
    [AtlasScenario]
    public void Info_HUD_rounding_patches_the_HUD_and_rounds_health_to_whole_numbers()
    {
        var harmony = new Harmony("seraphhorizons.wailarounding.atlas");
        try
        {
            Assert.True(WailaRounding.Patch(harmony, World.Api.Logger), "the HUD's compose method is missing or changed");
            Assert.Single(Harmony.GetPatchInfo(WailaRounding.Target!)!.Transpilers, p => p.owner == harmony.Id);
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            WailaRounding.Unbind();
        }

        Assert.Equal("Health: 13/20\nTemperature: 36.7°C\nGame version 1.22.7",
            WailaRounding.Round("Health: 13.4567/20\nTemperature: 36.666°C\nGame version 1.22.7"));
    }
}
