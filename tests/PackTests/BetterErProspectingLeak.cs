using System.Reflection;
using Xunit.Sdk;

[assembly: SeraphHorizons.PackTests.ReleaseBetterErProspectingServers]

namespace SeraphHorizons.PackTests;

/// <summary>
/// BetterEr Prospecting 3.4.10 subscribes every prospecting pick item's RegenerateToolModes, and so
/// the whole server through the item's api, to the static event ConfigManager.ReloadTools, and never
/// unsubscribes. Like ConfigKit's leak (ConfigKitLeak.cs), each stopped server stayed reachable, and
/// the 16 GB CI runner died in the `rest` shard's ninth boot. After each scenario this empties that
/// event, so a stopped server can be collected. The handlers only rebuild the pick's tool modes when
/// the mod's settings change, which no scenario does. Drop this once the item unsubscribes in
/// OnUnloaded() (#403, upstream https://github.com/KnewOne/BetterErProspecting/issues/8).
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class ReleaseBetterErProspectingServersAttribute : BeforeAfterTestAttribute
{
    public override void After(MethodInfo methodUnderTest)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetName().Name != "BetterErProspecting") continue;
            assembly.GetType("BetterErProspecting.Config.ConfigManager")
                ?.GetField("ReloadTools", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, null);
        }
    }
}
