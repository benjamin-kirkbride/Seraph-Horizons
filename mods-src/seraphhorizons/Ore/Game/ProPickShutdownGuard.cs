using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.ServerMods;

namespace SeraphHorizons.Mod.Ore;

/// <summary>
/// The prospecting pick's workspace (<c>ProPickWorkSpace.OnLoaded</c>, vanilla, which BetterEr
/// Prospecting reuses) builds its own <c>GenDeposits</c> in a task on the game's thread pool, so
/// every deposit generator is built and resolved a second time, and nothing waits for that task.
/// It takes seconds with this pack, and Interesting Ore Gen's block resolution logs dozens of
/// warnings through the server's logger on the way. If the server stops before the task ends,
/// <c>ServerMain.Dispose</c> has set that (static) logger to null, the next warning throws a
/// NullReferenceException on a thread-pool thread, and that kills the whole process: a test host
/// that ends a short scenario class early, or a server or singleplayer world left within seconds
/// of starting. This finalizer on the task drops what it throws once the server is shutting down
/// (it is building data for a server that is going away) and lets it through otherwise. It is
/// independent of the ore switches, which only change what that task computes, not whether it
/// finishes in time.
/// </summary>
public static class ProPickShutdownGuard
{
    /// <summary>The lambda in <c>OnLoaded</c> that runs <c>GenDeposits.initAssets</c>.</summary>
    public static MethodInfo? Task { get; } = FindTask();

    private static FieldInfo? ApiField => Task?.DeclaringType is { } type ? AccessTools.Field(type, "sapi") : null;

    /// <summary>Why it can't bind here, or null if it can.</summary>
    public static string? Unsupported()
    {
        if (Task is null) return "ProPickWorkSpace.OnLoaded no longer starts GenDeposits.initAssets in a task";
        if (ApiField?.FieldType != typeof(ICoreServerAPI)) return "the task no longer holds the server API";
        return null;
    }

    internal static void Bind(Harmony harmony) =>
        harmony.Patch(Task, finalizer: new HarmonyMethod(typeof(ProPickShutdownGuard), nameof(Finalizer)));

    private static MethodInfo? FindTask()
    {
        var initAssets = AccessTools.Method(typeof(GenDeposits), nameof(GenDeposits.initAssets));
        if (initAssets is null) return null;
        foreach (var type in typeof(ProPickWorkSpace).GetNestedTypes(AccessTools.all))
        foreach (var method in type.GetMethods(AccessTools.allDeclared))
        {
            if (!method.Name.StartsWith("<OnLoaded>", StringComparison.Ordinal)) continue;
            try
            {
                if (PatchProcessor.ReadMethodBody(method).Any(i => (i.Key == OpCodes.Call || i.Key == OpCodes.Callvirt)
                                                                   && Equals(i.Value, initAssets)))
                    return method;
            }
            catch (Exception)
            {
                // An unreadable body is not the task.
            }
        }
        return null;
    }

    private static Exception? Finalizer(Exception? __exception, object __instance)
    {
        if (__exception is null) return null;
        return ShuttingDown(ApiField?.GetValue(__instance) as ICoreServerAPI) ? null : __exception;
    }

    private static bool ShuttingDown(ICoreServerAPI? sapi)
    {
        if (sapi is null) return false;
        try
        {
            // The logger goes last in Dispose; the run phase may not have been moved on when a
            // test harness disposes the server without stopping it first.
            return sapi.Server.CurrentRunPhase >= EnumServerRunPhase.Shutdown || sapi.World.Logger is null;
        }
        catch (Exception)
        {
            return true;
        }
    }
}
