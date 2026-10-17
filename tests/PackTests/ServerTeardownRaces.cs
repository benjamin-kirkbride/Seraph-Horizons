using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.Server;
using Xunit.Sdk;

[assembly: SeraphHorizons.PackTests.ServerTeardownRacesAttribute]

namespace SeraphHorizons.PackTests;

/// <summary>
/// Keeps one server's teardown from running under live work. Atlas boots every scenario class's
/// server in this one process, and the game's <c>ServerMain.Dispose()</c> nulls the process-wide
/// statics <c>ServerMain.Logger</c> and <c>ServerMain.ClassRegistry</c>, so anything still running
/// when a server is disposed (its own work, or the next server) can find them null.
/// <list type="bullet">
/// <item><b>A late teardown under the next boot (#1075).</b> Atlas waits 30 s for a stopping
/// server's game thread, then abandons it and boots the next class's server. Stopping a server
/// disconnects its players, and the last disconnect runs <c>GC.Collect()</c> with the large object
/// heap compacted, on the game thread: in the ore shard that took 31 to 52 s on CI, so the
/// abandoned thread saved its world and ran <c>Dispose()</c> while the next server was in
/// <c>OnLoadAssets</c>, which threw a NullReferenceException on the static logger. A server being
/// stopped has no use for that collection, so it is skipped once <c>Stop()</c> has begun. And
/// whatever else makes a teardown run long, a new host does not start while a stopped server is
/// not yet disposed: <c>ServerHost.StartAsync</c> waits for it.</item>
/// <item><b>The prospecting pick's deposit task (#315).</b> <c>ProPickWorkSpace.OnLoaded</c>
/// queues the deposit generators' asset init on the thread pool and returns, and Interesting Ore
/// Gen's deposits log warnings through the server's logger as they resolve. Disposed under that
/// task (by its own server stopping first, or a late teardown as above), its NullReferenceException
/// on a pool thread took down the whole test host. So a boot ends only once the task has: every
/// scenario then also gets a prospecting pick that works from its first tick.</item>
/// </list>
/// The patches go in before the first scenario and stay for the process. Drop them if the game
/// stops nulling its statics in Dispose() and Atlas waits for a teardown before the next boot.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class ServerTeardownRacesAttribute : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest) => ServerTeardownRaces.Apply();
}

internal static class ServerTeardownRaces
{
    private const string HarmonyId = "seraphhorizons.packtests.teardownraces";

    /// <summary>The name ProPickWorkSpace.OnLoaded gives its task.</summary>
    private const string PropickTask = "propickonloaded";

    /// <summary>How long a wait for a teardown or the pick's task goes before giving up on it.</summary>
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(5);

    private static readonly object Gate = new();
    private static readonly HashSet<ServerMain> stopping = [];
    private static bool applied;
    private static int pendingPropick;

    public static void Apply()
    {
        lock (Gate)
        {
            if (applied) return;
            var harmony = new Harmony(HarmonyId);
            harmony.Patch(
                Find(AccessTools.Method(AccessTools.TypeByName("Vintagestory.Server.ServerSystemUnloadChunks"), "OnPlayerDisconnect"), "ServerSystemUnloadChunks.OnPlayerDisconnect"),
                prefix: Hook(nameof(SkipCollectWhenStopping)));
            harmony.Patch(Find(AccessTools.Method(typeof(ServerMain), nameof(ServerMain.Stop)), "ServerMain.Stop"),
                prefix: Hook(nameof(Stopping)));
            harmony.Patch(Find(AccessTools.Method(typeof(ServerMain), nameof(ServerMain.Dispose)), "ServerMain.Dispose"),
                finalizer: Hook(nameof(Disposed)));
            harmony.Patch(
                Find(AccessTools.Method(AccessTools.TypeByName("Atlas.Internal.Hosting.ServerHost"), "StartAsync"), "Atlas ServerHost.StartAsync"),
                prefix: Hook(nameof(AwaitTeardowns)));
            harmony.Patch(
                Find(AccessTools.Method(typeof(TyronThreadPool), nameof(TyronThreadPool.QueueTask), [typeof(Action), typeof(string)]), "TyronThreadPool.QueueTask(Action, string)"),
                prefix: Hook(nameof(TrackPropickTask)));
            harmony.Patch(Find(AccessTools.Method(typeof(ServerMain), nameof(ServerMain.Launch)), "ServerMain.Launch"),
                postfix: Hook(nameof(AwaitPropickTask)));
            applied = true;
        }
    }

    private static MethodBase Find(MethodBase? method, string name) =>
        method ?? throw new InvalidOperationException($"PackTests: {name} not found to patch (ServerTeardownRaces.cs)");

    private static HarmonyMethod Hook(string name) => new(typeof(ServerTeardownRaces), name);

    private static bool SkipCollectWhenStopping(ServerMain ___server) => !___server.stopped;

    /// <summary>Atlas disposes every server it stops, on the same game thread, right after.</summary>
    private static void Stopping(ServerMain __instance)
    {
        lock (Gate) stopping.Add(__instance);
    }

    private static Exception? Disposed(ServerMain __instance, Exception? __exception)
    {
        lock (Gate)
        {
            stopping.Remove(__instance);
            Monitor.PulseAll(Gate);
        }
        return __exception;
    }

    /// <summary>Runs on the test thread, before the new host's game thread exists.</summary>
    private static void AwaitTeardowns()
    {
        if (!WaitUntil(() => stopping.Count == 0, out var waited))
        {
            Console.Error.WriteLine($"PackTests: {stopping.Count} stopped server(s) still not disposed after {MaxWait}; booting the next one anyway");
            lock (Gate) stopping.Clear();
        }
        else if (waited > TimeSpan.FromSeconds(1))
            Console.Error.WriteLine($"PackTests: waited {waited.TotalSeconds:0.0} s for the previous server's teardown before booting the next one");
    }

    private static void TrackPropickTask(ref Action callback, string caller)
    {
        if (caller != PropickTask) return;
        var inner = callback;
        lock (Gate) pendingPropick++;
        callback = () =>
        {
            try
            {
                inner();
            }
            finally
            {
                lock (Gate)
                {
                    pendingPropick--;
                    Monitor.PulseAll(Gate);
                }
            }
        };
    }

    private static void AwaitPropickTask()
    {
        if (!WaitUntil(() => pendingPropick == 0, out var waited))
            ServerMain.Logger?.Warning("[PackTests] The prospecting pick's deposit task is still running {0} after boot; going on without it", MaxWait);
        else if (waited > TimeSpan.Zero)
            ServerMain.Logger?.Notification("[PackTests] Waited {0:0.0} s after boot for the prospecting pick's deposit task", waited.TotalSeconds);
    }

    private static bool WaitUntil(Func<bool> done, out TimeSpan waited)
    {
        var start = DateTime.UtcNow;
        lock (Gate)
        {
            if (done())
            {
                waited = TimeSpan.Zero;
                return true;
            }
            while (!done())
            {
                var left = start + MaxWait - DateTime.UtcNow;
                if (left <= TimeSpan.Zero)
                {
                    waited = MaxWait;
                    return false;
                }
                Monitor.Wait(Gate, left);
            }
        }
        waited = DateTime.UtcNow - start;
        return true;
    }
}
