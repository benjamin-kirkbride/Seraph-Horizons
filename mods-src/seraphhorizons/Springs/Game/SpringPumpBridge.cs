using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using SeraphHorizons.Mod.Pipes;
using SeraphHorizons.Mod.Pipes.Core;
using SeraphHorizons.Mod.Springs.Core;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.Springs;

/// <summary>
/// A pumped spring (README "Springs", "Pumped"), on Pipes and Power Expanded's mechanical pump
/// (<c>BlockEntityMpFluidPump</c>), found by name since nothing of it is referenced at build time.
/// ppex's pump moves water only from a run with a fluid intake on it (<c>FindIntake</c>), so with a
/// spring at the head of its source run it moved nothing. A prefix on its <c>DoWork(speed, dt)</c>
/// (server):
/// <list type="bullet">
/// <item>tells this bridge its source run's network and its axle speed (<see cref="Report"/>), which
/// the spring reads as it feeds (<see cref="FastestPump"/>) to give that run its extra;</item>
/// <item>with an intake on that run, lets ppex's own work run;</item>
/// <item>with none, but a spring feeding that run (<see cref="Fed"/>, within
/// <see cref="SpringPumps.FreshMs"/>), does the pump's work as ppex does it, without the intake's
/// refill (the spring refills the run as it feeds): its rate at that speed (<c>OutputAt</c>) for the
/// seconds, at most what the delivery run has room for, taken from the source run and put into the
/// delivery run at the pump's delivery pressure; a pump with no delivery run spills from its port
/// as ppex's does.</item>
/// </list>
/// With a member not as expected, one warning, and pumps on a spring's run move nothing, as before.
/// </summary>
public static class SpringPumpBridge
{
    public const string PumpTypeName = "PipesAndPowerExpanded.BlockStructures.MpPump.BlockEntities.BlockEntityMpFluidPump";
    public const string WaterLineTypeName = "PipesAndPowerExpanded.Helpers.WaterLine";

    private sealed class Pump
    {
        public required MethodInfo Source, Delivery, FindIntake, OutputAt, FreeCapacity, SetDrawing, Spill, Consume, Produce, Hold;
        public required PropertyInfo DeliveryPressure;
    }

    private static Pump? _pump;

    // Per pipe network object (exlib rebuilds them as runs change; a dropped one is collected).
    private sealed class RunState
    {
        public float Speed;
        public long PumpedMs = long.MinValue, FedMs = long.MinValue;
    }

    private static readonly ConditionalWeakTable<object, RunState> Runs = new();

    public static bool Bound => _pump != null;

    /// <summary>Patches the pump's <c>DoWork</c>; false (one warning) when its members are not as
    /// expected. No warning without ppex.</summary>
    public static bool Patch(Harmony harmony, ILogger logger)
    {
        _pump = null;
        var type = AccessTools.TypeByName(PumpTypeName);
        if (type == null)
            return false;
        var network = NameCandidates.First(HandPumpBridge.PipeNetworkNames, AccessTools.TypeByName)?.Value;
        var waterLine = AccessTools.TypeByName(WaterLineTypeName);
        var values = AccessTools.TypeByName(HandPumpBridge.PpexValuesTypeName);
        var doWork = AccessTools.DeclaredMethod(type, "DoWork", [typeof(float), typeof(float)]);
        var source = AccessTools.DeclaredMethod(type, "SourceNetwork", Type.EmptyTypes);
        var delivery = AccessTools.DeclaredMethod(type, "DeliveryNetwork", Type.EmptyTypes);
        var findIntake = AccessTools.DeclaredMethod(type, "FindIntake");
        var outputAt = AccessTools.DeclaredMethod(type, "OutputAt", [typeof(float)]);
        var free = AccessTools.DeclaredMethod(type, "OutputFreeCapacity");
        var setDrawing = AccessTools.DeclaredMethod(type, "SetDrawing", [typeof(bool)]);
        var spill = AccessTools.DeclaredMethod(type, "SpillFromOpenOutlet", Type.EmptyTypes);
        var consume = network == null ? null : AccessTools.Method(network, "TryConsumeLiquid", [typeof(float), typeof(IBlockAccessor)]);
        var produce = network == null ? null : AccessTools.Method(network, "TryProduceLiquid", [typeof(float), typeof(float), typeof(float), typeof(IBlockAccessor)]);
        var hold = network == null || waterLine == null ? null : AccessTools.DeclaredMethod(waterLine, "Hold", [typeof(BlockEntity), network, typeof(float)]);
        var pressure = values == null ? null : AccessTools.DeclaredProperty(values, "MpPumpDeliveryPressure");
        if (doWork?.ReturnType != typeof(float) || source == null || delivery == null || findIntake == null
            || outputAt is not { IsStatic: true } || outputAt.ReturnType != typeof(float)
            || free is not { IsStatic: true } || free.ReturnType != typeof(float)
            || setDrawing == null || spill == null || consume?.ReturnType != typeof(float) || produce == null
            || hold == null || pressure?.PropertyType != typeof(float))
        {
            logger.Warning("[seraphhorizons] Springs: Pipes and Power Expanded's mechanical pump ({0}.DoWork and the members it works with) is not as expected, so a pump draws nothing from a spring", PumpTypeName);
            return false;
        }
        _pump = new Pump
        {
            Source = source, Delivery = delivery, FindIntake = findIntake, OutputAt = outputAt, FreeCapacity = free,
            SetDrawing = setDrawing, Spill = spill, Consume = consume, Produce = produce, Hold = hold, DeliveryPressure = pressure,
        };
        harmony.Patch(doWork, prefix: new HarmonyMethod(typeof(SpringPumpBridge), nameof(DoWorkPrefix)));
        return true;
    }

    public static void Unbind() => _pump = null;

    private static RunState State(object network) => Runs.GetValue(network, _ => new RunState());

    /// <summary>A pump working at axle <paramref name="speed"/> draws on <paramref name="network"/>
    /// (at <paramref name="nowMs"/>). Several pumps on one run: the fastest this second counts.</summary>
    public static void Report(object network, float speed, long nowMs)
    {
        var state = State(network);
        if (nowMs - state.PumpedMs >= 1000 || speed > state.Speed)
            state.Speed = speed;
        state.PumpedMs = nowMs;
    }

    /// <summary>A spring fed <paramref name="network"/> at <paramref name="nowMs"/>.</summary>
    public static void Fed(object network, long nowMs) => State(network).FedMs = nowMs;

    /// <summary>The axle speed of the fastest pump drawing on <paramref name="network"/> lately, or 0.</summary>
    public static float FastestPump(object network, long nowMs) =>
        Runs.TryGetValue(network, out var state) && nowMs - state.PumpedMs <= SpringPumps.FreshMs ? state.Speed : 0;

    private static bool SpringFed(object network, long nowMs) =>
        Runs.TryGetValue(network, out var state) && nowMs - state.FedMs <= SpringPumps.FreshMs;

    private static bool _failed;

    // BlockEntityMpFluidPump.DoWork(float speed, float dt): float, the litres moved.
    public static bool DoWorkPrefix(BlockEntity __instance, float speed, float dt, ref float __result)
    {
        if (_pump is not { } p || _failed || __instance.Api is not { Side: EnumAppSide.Server } api || !SpringsSystem.On)
            return true;
        try
        {
            if (!(speed > 0) || !(dt > 0) || p.Source.Invoke(__instance, null) is not { } source)
                return true;
            long now = api.World.ElapsedMilliseconds;
            Report(source, speed, now);
            if (p.FindIntake.Invoke(__instance, [source]) != null || !SpringFed(source, now))
                return true;
            float rate = (float)p.OutputAt.Invoke(null, [speed])!;
            if (!(rate > 0))
                return true;
            var ba = api.World.BlockAccessor;
            var delivery = p.Delivery.Invoke(__instance, null);
            float pressure = (float)p.DeliveryPressure.GetValue(null)!;
            p.Hold.Invoke(null, [__instance, null, 0f]);
            p.SetDrawing.Invoke(__instance, [true]);
            if (delivery == null)
                p.Spill.Invoke(__instance, null);
            float want = Math.Min(rate * dt, (float)p.FreeCapacity.Invoke(null, [delivery])!);
            float moved = want > 0 ? (float)p.Consume.Invoke(source, [want, ba])! : 0;
            if (moved > 0 && delivery != null)
                p.Produce.Invoke(delivery, [moved, SpringTaps.WaterTemperature, pressure, ba]);
            p.Hold.Invoke(null, [__instance, delivery, pressure]);
            __result = moved;
            return false;
        }
        catch (Exception e)
        {
            _failed = true;
            api.Logger.Warning("[seraphhorizons] Springs: a mechanical pump on a spring's run failed, so pumps draw nothing from springs from now on: {0}",
                (e.InnerException ?? e).Message);
            return true;
        }
    }
}
