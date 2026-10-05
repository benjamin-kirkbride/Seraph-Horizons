using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Rosser;

/// <summary>
/// Water for the rosser's drip from a Pipes and Power Expanded pipe on its water face, found by
/// name (research §1). The pipes moved between namespaces from ppex 0.6.8 / exlib 0.7.2 (pinned)
/// to ppex 0.7.1 / exlib 0.8.4, with the same members, so both sets are tried, the newer first:
/// <list type="bullet">
/// <item>the network manager <c>BlockNetworkModSystem</c> (<c>ExpandedLib.Networks</c> or
/// <c>ExpandedLib.Blocks.Networks</c>) and its <c>GetConnectedNetworkAcross(IBlockAccessor, BlockPos,
/// BlockFacing)</c>, which finds the network of the pipe in the cell beyond a face whose connector
/// points back (it never looks at the asking cell, so the water ghost needs no ppex type);</item>
/// <item>the pipe network <c>PipeNetwork</c> (<c>ExpandedLib.Industry.Pipes</c> or
/// <c>PipesAndPowerExpanded.BlockNetworkPipe</c>), its <c>State.MediumType</c> (only
/// <c>"Water"</c> is drawn) and <c>TryConsumeLiquid(float, IBlockAccessor)</c>, the litres taken
/// (<c>IPipeNode.TryConsume</c> is gas only).</item>
/// </list>
/// Without the mods, or with members not as expected (one warning), the rosser runs dry: its
/// bark roll uses the dry multiplier and there is no drip. Server side only: networks live there.
/// </summary>
public static class PpexWater
{
    private static readonly (string Manager, string Network)[] Names =
    [
        ("ExpandedLib.Networks.BlockNetworkModSystem", "ExpandedLib.Industry.Pipes.PipeNetwork"),
        ("ExpandedLib.Blocks.Networks.BlockNetworkModSystem", "PipesAndPowerExpanded.BlockNetworkPipe.PipeNetwork"),
    ];
    public const string Water = "Water";

    private static readonly object Lock = new();
    private static ICoreAPI? _boundFor;
    private static bool _bound;
    private static string? _managerName;
    private static MethodInfo? _across;
    private static Type? _network;
    private static PropertyInfo? _state;
    private static PropertyInfo? _medium;
    private static MethodInfo? _consume;

    /// <summary>Whether a pipe can feed the rosser on <paramref name="api"/>'s side (bound once per
    /// API; logs one warning when the mods are there but not as expected).</summary>
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
            return TryBind(api);
        }
        catch (Exception e)
        {
            api.Logger.Warning("[seraphhorizons] Rosser: Pipes and Power Expanded's pipes could not be bound, so no pipe can water a rosser: {0}", e.Message);
            return false;
        }
    }

    private static bool TryBind(ICoreAPI api)
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
            // the most derived State (a generic base may declare one of its own type too)
            var state = MostDerived(networkType, "State");
            var medium = state == null ? null : MostDerived(state.PropertyType, "MediumType");
            var consume = AccessTools.Method(networkType, "TryConsumeLiquid", [typeof(float), typeof(IBlockAccessor)]);
            if (across is not { IsStatic: false } || across.ReturnType == typeof(void)
                || state?.GetMethod is not { IsStatic: false } || medium?.GetMethod == null || medium.PropertyType != typeof(string)
                || consume is not { IsStatic: false } || consume.ReturnType != typeof(float))
            {
                problems.Add($"{manager}.GetConnectedNetworkAcross, {network}.State.MediumType or {network}.TryConsumeLiquid is not as expected");
                continue;
            }
            (_managerName, _across, _network, _state, _medium, _consume) = (managerType.FullName, across, networkType, state, medium, consume);
            return true;
        }
        if (installed)
            api.Logger.Warning("[seraphhorizons] Rosser: Pipes and Power Expanded's pipes are not as expected, so no pipe can water a rosser: {0}",
                string.Join("; ", problems));
        return false;
    }

    /// <summary>Takes up to <paramref name="litres"/> of water from the pipe network connected to
    /// <paramref name="cell"/>'s <paramref name="face"/> (server side); the litres taken, 0 without
    /// a water pipe there. Any failure inside the mods counts as none, logged once.</summary>
    public static double Draw(ICoreAPI api, BlockPos cell, BlockFacing face, double litres)
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
            if (state == null || (string?)_medium!.GetValue(state) != Water)
                return 0;
            return Math.Max(0, (float)_consume!.Invoke(net, [(float)litres, ba])!);
        }
        catch (Exception e)
        {
            lock (Lock)
            {
                if (_bound)
                    api.Logger.Warning("[seraphhorizons] Rosser: drawing water from Pipes and Power Expanded failed, so rossers run dry from now on: {0}",
                        (e.InnerException ?? e).Message);
                _bound = false;
            }
            return 0;
        }
    }
}
