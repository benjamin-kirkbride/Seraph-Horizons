using System.Reflection;
using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons, NoBiodiversityWelcome: Biodiversity's join message (with its Discord
/// link) is not shown. Atlas runs no client, so this patches the welcome handler on the server's
/// process (Biodiversity's assembly is loaded on both sides) under a Harmony id of its own, then calls
/// it on an unstarted instance: before the patch its body runs (and fails on the client API it was
/// never given), after it the body is skipped.
/// </summary>
public partial class SharedWorldScenarios
{
    [AtlasScenario]
    public void Biodiversity_welcome_is_skipped()
    {
        Assert.True(BiodiversityWelcome.Applies(World.Api), "Biodiversity is not loaded");
        var target = BiodiversityWelcome.Target;
        Assert.NotNull(target);
        var system = Activator.CreateInstance(target!.DeclaringType!);

        var unpatched = Assert.Throws<TargetInvocationException>(() => target.Invoke(system, [null]));
        Assert.IsType<NullReferenceException>(unpatched.InnerException);

        var harmony = new Harmony("seraphhorizons.biodiversitywelcome.atlas");
        try
        {
            Assert.True(BiodiversityWelcome.Patch(harmony, World.Api.Logger));
            Assert.Single(Harmony.GetPatchInfo(target)!.Prefixes, p => p.owner == harmony.Id);
            target.Invoke(system, [null]);
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
        }
    }
}
