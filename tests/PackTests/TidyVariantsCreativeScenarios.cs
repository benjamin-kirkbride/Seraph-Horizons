using Atlas.XUnit;
using HarmonyLib;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons, Tidy Variants creative inventory side (#256, docs/variant-grouping/creative.md): its GUI patches are
/// client only and must never load or patch anything on a server. The GUI itself can only be checked in game
/// (the checklist in creative.md); its grouping logic is unit-tested in mods-src/seraphhorizons/tests.
/// </summary>
[AtlasWorld]
public class TidyVariantsCreativeScenarios : AtlasScenarioBase
{
    [AtlasScenario]
    public void Creative_client_system_does_not_load_or_patch_on_the_server()
    {
        const string name = "SeraphHorizons.Mod.TidyVariants.TidyCreativeModSystem";
        // Mod.Systems lists every system of the mod; the loader only enables those whose ShouldLoad(side) is true.
        Assert.Contains(World.Api.ModLoader.GetMod("seraphhorizons").Systems, s => s.GetType().FullName == name);
        Assert.False(World.Api.ModLoader.IsModSystemEnabled(name), "the creative UI system is enabled on the server");
        var ours = Harmony.GetAllPatchedMethods()
            .Where(m => Harmony.GetPatchInfo(m)?.Owners.Contains("seraphhorizons.creative") == true)
            .Select(m => m.DeclaringType?.Name + "." + m.Name)
            .ToList();
        Assert.True(ours.Count == 0, "seraphhorizons.creative patched on the server: " + string.Join(", ", ours));
    }
}
