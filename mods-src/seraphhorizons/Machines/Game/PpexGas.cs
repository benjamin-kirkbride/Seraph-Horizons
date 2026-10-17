using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Machines;

/// <summary>
/// Gas for a machine (the cupellation furnace's blast, #740) from a Pipes and Power Expanded pipe on one of its faces,
/// found by name as <see cref="PpexWater"/> finds water (the same two layouts, the pinned first): the network of the
/// pipe beyond the face whose connector points back (<c>BlockNetworkModSystem.GetConnectedNetworkAcross</c>), its
/// <c>State.MediumType</c> (only the asked gas, Steelmaking Expanded's engine air blower's <c>"Air"</c>, is drawn),
/// its <c>State.Pressure</c> (nothing is drawn under the machine's least pressure) and <c>TryConsumeGas(float,
/// IBlockAccessor)</c>, the litres taken. The face is a fixed connector, as Steelmaking Expanded's converter intake
/// port is: a structure port, not a node of the network. Without the mods, or with members not as expected (one
/// warning), no machine gets any. Server side only: networks live there.
/// </summary>
public static class PpexGas
{
    private static readonly (string Manager, string Network)[] Names =
    [
        ("ExpandedLib.Networks.BlockNetworkModSystem", "ExpandedLib.Industry.Pipes.PipeNetwork"),
        ("ExpandedLib.Blocks.Networks.BlockNetworkModSystem", "PipesAndPowerExpanded.BlockNetworkPipe.PipeNetwork"),
    ];

    private static readonly object Lock = new();
    private static ICoreAPI? _boundFor;
    private static bool _bound;
    private static string? _managerName;
    private static MethodInfo? _across;
    private static Type? _network;
    private static PropertyInfo? _state;
    private static PropertyInfo? _medium;
    private static PropertyInfo? _pressure;
    private static MethodInfo? _consume;

    /// <summary>Whether a pipe can feed gas to a machine on <paramref name="api"/>'s side (bound once per API).</summary>
    public static bool Bound(ICoreAPI api)
    {
        lock (Lock)
        {
            if (!ReferenceEquals(_boundFor, api))
            {
                _boundFor = api;
                _bound = Bind(api);
            }
            return _bound;
        }
    }

    private static PropertyInfo? MostDerived(Type type, string name)
    {
        for (var t = type; t != null; t = t.BaseType)
            if (t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly) is { } property)
                return property;
        return null;
    }

    private static bool Bind(ICoreAPI api)
    {
        try
        {
            bool installed = api.ModLoader.IsModEnabled("ppex") || api.ModLoader.IsModEnabled("exlib");
            var problems = new List<string>();
            foreach (var (manager, network) in Names)
            {
                var managerType = AccessTools.TypeByName(manager);
                var networkType = AccessTools.TypeByName(network);
                if (managerType == null || networkType == null)
                {
                    problems.Add($"{manager} or {network} is missing");
                    continue;
                }
                var across = AccessTools.Method(managerType, "GetConnectedNetworkAcross", [typeof(IBlockAccessor), typeof(BlockPos), typeof(BlockFacing)]);
                var state = MostDerived(networkType, "State");
                var medium = state == null ? null : MostDerived(state.PropertyType, "MediumType");
                var pressure = state == null ? null : MostDerived(state.PropertyType, "Pressure");
                var consume = AccessTools.Method(networkType, "TryConsumeGas", [typeof(float), typeof(IBlockAccessor)]);
                if (across is not { IsStatic: false } || across.ReturnType == typeof(void)
                    || state?.GetMethod is not { IsStatic: false } || medium?.GetMethod == null || medium.PropertyType != typeof(string)
                    || pressure?.GetMethod == null || pressure.PropertyType != typeof(float)
                    || consume is not { IsStatic: false } || consume.ReturnType != typeof(float))
                {
                    problems.Add($"{manager}.GetConnectedNetworkAcross, {network}.State.MediumType, .Pressure or {network}.TryConsumeGas is not as expected");
                    continue;
                }
                (_managerName, _across, _network, _state, _medium, _pressure, _consume) =
                    (managerType.FullName, across, networkType, state, medium, pressure, consume);
                return true;
            }
            if (installed)
                api.Logger.Warning("[seraphhorizons] Machine gas: Pipes and Power Expanded's pipes are not as expected, so no pipe can blow a machine: {0}",
                    string.Join("; ", problems));
            return false;
        }
        catch (Exception e)
        {
            api.Logger.Warning("[seraphhorizons] Machine gas: Pipes and Power Expanded's pipes could not be bound, so no pipe can blow a machine: {0}", e.Message);
            return false;
        }
    }

    /// <summary>Takes up to <paramref name="litres"/> of <paramref name="gas"/> from the pipe network connected to
    /// <paramref name="cell"/>'s <paramref name="face"/>, if its pressure is <paramref name="minPressure"/> or more
    /// (server side); the litres taken, 0 without such a pipe there. Any failure inside the mods counts as none,
    /// logged once.</summary>
    public static double Draw(ICoreAPI api, BlockPos cell, BlockFacing face, string gas, double litres, double minPressure)
    {
        if (litres <= 0 || !Bound(api))
            return 0;
        try
        {
            var manager = api.ModLoader.GetModSystem(_managerName!);
            if (manager == null)
                return 0;
            var ba = api.World.BlockAccessor;
            var net = _across!.Invoke(manager, [ba, cell, face]);
            if (net == null || !_network!.IsInstanceOfType(net))
                return 0;
            var state = _state!.GetValue(net);
            if (state == null || (string?)_medium!.GetValue(state) != gas || (float)_pressure!.GetValue(state)! < minPressure)
                return 0;
            return Math.Max(0, (float)_consume!.Invoke(net, [(float)litres, ba])!);
        }
        catch (Exception e)
        {
            lock (Lock)
            {
                if (_bound)
                    api.Logger.Warning("[seraphhorizons] Machine gas: drawing gas from Pipes and Power Expanded failed, so no machine gets any from now on: {0}",
                        (e.InnerException ?? e).Message);
                _bound = false;
            }
            return 0;
        }
    }
}
