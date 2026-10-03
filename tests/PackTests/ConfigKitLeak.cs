using System.Reflection;
using Xunit.Sdk;

[assembly: SeraphHorizons.PackTests.ReleaseConfigKitServers]

namespace SeraphHorizons.PackTests;

/// <summary>
/// ConfigKit 1.4.1 subscribes a lambda that captures its mod system, and so the whole server
/// through its api, to the static event ConfigRegistry.OnToBytes, and never unsubscribes. Atlas
/// boots every scenario class's server in the same process, so each stopped server stayed
/// reachable: the `rest` shard climbed about 2 GB per class, to 27 GB, and the 16 GB CI runner
/// died mid-run. After each scenario this empties that event, so a stopped server can be
/// collected. The handler only sets a "too late to register a config" flag when configs are sent
/// to a client, which no scenario does. Drop this once ConfigKit unsubscribes in Dispose().
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class ReleaseConfigKitServersAttribute : BeforeAfterTestAttribute
{
    public override void After(MethodInfo methodUnderTest)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetName().Name != "ConfigKit") continue;
            assembly.GetType("ConfigKit.ConfigRegistry")
                ?.GetField("OnToBytes", BindingFlags.Static | BindingFlags.NonPublic)
                ?.SetValue(null, null);
        }
    }
}
