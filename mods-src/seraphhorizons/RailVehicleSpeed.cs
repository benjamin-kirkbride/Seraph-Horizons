using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SeraphHorizons.Mod;

/// <summary>
/// Yang's Transport Tycoon (<c>yangtransport</c> 1.0.3): every one of its vehicles runs at
/// <c>RailVehicleSpeedFactor</c> (half) its top speed (<c>SlowerRailVehicles</c>, #847).
///
/// <para>Only a steam engine moves a convoy (an engine cart, <c>yangtransport.minecart</c>, on wagon
/// rails; a locomotive, <c>yangtransport.sglocomotive</c>, on standard gauge); minecarts and wagons are
/// pulled and only add weight. A moving convoy's top speed is the least of two figures, times the
/// track piece's own speed factor (curves, slopes): the engine's (<c>EngineCartDrive.TryComputeLoadedKinematicLimits</c>:
/// raw power x heat - convoy weight) and the track's material cap (<c>config/trackmaterialspeeds.json</c>,
/// per gauge and material). Each is scaled where Yang reads it, so their least is
/// (<see cref="RailSpeedScale"/>):</para>
/// <list type="bullet">
/// <item><b>The engine</b>: a postfix on <c>EntityMinecart.TryComputeConvoyDrive</c> and
/// <c>EntityStandardGaugeLocomotive.TryComputeConvoyDrive</c>, which a lead runs each tick, scales the
/// top speed the steam engine gave. A lead without Yang's <c>SteamPowered</c> behaviour (this mod's
/// handcar, which has its own figures) is left alone.</item>
/// <item><b>The track</b>: a postfix on <c>RailGraphLive.TryGetEdgeMaterialSpeedCapBPS</c> scales the
/// cap of the edge a vehicle is on. Patched where it is read, not in the asset: the rail graph keeps
/// each edge's cap in the savegame, so a patched asset would leave track laid before it at the old cap.</item>
/// <item><b>Offscreen</b>: a timetabled convoy no one is near is simulated without its entities
/// (<c>OffscreenConvoySimSystem</c>); it cruises at 80 % of its engine's figure capped by the track
/// (<c>VirtualConvoy.ComputeAutomationCruiseSpeed</c>). A postfix scales that, with the cap read
/// unscaled inside it.</item>
/// </list>
/// <para>Acceleration, braking and coasting drag are Yang's. Server side, where Yang simulates.
/// Yang's mod is not referenced at build time: each method is found by name and checked, and if one is
/// missing or changed the server logs an error and patches nothing, leaving Yang's speeds as it ships
/// them.</para>
/// </summary>
public static class RailVehicleSpeed
{
    public const string ModId = "yangtransport";
    public const string MinecartType = "YangTransport.EntityMinecart";
    public const string LocomotiveType = "YangTransport.EntityStandardGaugeLocomotive";
    public const string RailGraphType = "YangTransport.RailGraphLive";
    public const string ConvoySimType = "YangTransport.OffscreenConvoySimSystem+VirtualConvoy";
    public const string DriveMethod = "TryComputeConvoyDrive";
    public const string CapMethod = "TryGetEdgeMaterialSpeedCapBPS";
    public const string CruiseMethod = "ComputeAutomationCruiseSpeed";
    /// <summary>Yang's steam engine behaviour's code.</summary>
    public const string SteamBehaviour = "SteamPowered";

    private static double _factor = RailSpeedScale.DefaultFactor;
    // Inside ComputeAutomationCruiseSpeed, whose result is scaled whole.
    [ThreadStatic] private static int _cruising;

    public static bool Applies(ICoreAPI api) => api.ModLoader.IsModEnabled(ModId);

    /// <summary>The factor in force (for tests).</summary>
    public static double Factor => _factor;

    /// <summary>Whether this process's server patched Yang's methods.</summary>
    public static bool Patched { get; private set; }

    /// <summary>Finds and checks Yang's methods; patches them with <paramref name="factor"/> when all
    /// are as expected. Returns whether it patched.</summary>
    public static bool Patch(Harmony harmony, ILogger logger, double factor)
    {
        var problems = new List<string>();
        var drives = new List<MethodInfo>();
        foreach (var typeName in new[] { MinecartType, LocomotiveType })
        {
            var type = AccessTools.TypeByName(typeName);
            var drive = type == null ? null : AccessTools.DeclaredMethod(type, DriveMethod);
            var p = drive?.GetParameters();
            if (drive == null || drive.IsStatic || drive.ReturnType != typeof(bool) || p is not { Length: 6 }
                || !typeof(Entity).IsAssignableFrom(p[0].ParameterType) || p[1].ParameterType != typeof(int) || p[2].ParameterType != typeof(int)
                || p[3..].Any(o => !o.IsOut || o.ParameterType != typeof(double).MakeByRefType()) || p[5].Name != "maxSpeedBPS")
                problems.Add($"{typeName}.{DriveMethod}(lead, int, int, out double, out double, out double maxSpeedBPS)");
            else
                drives.Add(drive);
        }
        var graph = AccessTools.TypeByName(RailGraphType);
        var cap = graph == null ? null : AccessTools.DeclaredMethod(graph, CapMethod);
        if (cap == null || cap.ReturnType != typeof(bool)
            || cap.GetParameters() is not [{ ParameterType: var hash }, { IsOut: true, ParameterType: var capOut }]
            || hash != typeof(ulong) || capOut != typeof(ushort).MakeByRefType())
            problems.Add($"{RailGraphType}.{CapMethod}(ulong, out ushort)");
        var sim = AccessTools.TypeByName(ConvoySimType);
        var cruise = sim == null ? null : AccessTools.DeclaredMethod(sim, CruiseMethod);
        if (cruise == null || cruise.ReturnType != typeof(double) || cruise.GetParameters() is not [{ ParameterType: var g }] || g != graph)
            problems.Add($"{ConvoySimType}.{CruiseMethod}({RailGraphType})");
        if (problems.Count > 0)
        {
            logger.Error("[seraphhorizons] Slower rail vehicles: Yang's Transport Tycoon changed (no {0}), so its vehicles keep their full speed",
                string.Join("; no ", problems));
            return false;
        }

        _factor = RailSpeedScale.Sanitize(factor);
        foreach (var drive in drives)
            harmony.Patch(drive, postfix: new HarmonyMethod(typeof(RailVehicleSpeed), nameof(DrivePostfix)));
        harmony.Patch(cap, postfix: new HarmonyMethod(typeof(RailVehicleSpeed), nameof(CapPostfix)));
        harmony.Patch(cruise, prefix: new HarmonyMethod(typeof(RailVehicleSpeed), nameof(CruisePrefix)),
            postfix: new HarmonyMethod(typeof(RailVehicleSpeed), nameof(CruisePostfix)),
            finalizer: new HarmonyMethod(typeof(RailVehicleSpeed), nameof(CruiseFinalizer)));
        return Patched = true;
    }

    // Arguments by position: the two classes name the lead differently.
    public static void DrivePostfix(Entity __0, ref double __5, bool __result)
    {
        if (__result && __0?.GetBehavior(SteamBehaviour) != null)
            __5 = RailSpeedScale.Speed(__5, _factor);
    }

    public static void CapPostfix(ref ushort __1, bool __result)
    {
        if (__result && _cruising == 0)
            __1 = RailSpeedScale.Cap(__1, _factor);
    }

    public static void CruisePrefix() => _cruising++;

    public static void CruisePostfix(ref double __result) => __result = RailSpeedScale.Speed(__result, _factor);

    public static Exception? CruiseFinalizer(Exception? __exception)
    {
        _cruising--;
        return __exception;
    }
}
