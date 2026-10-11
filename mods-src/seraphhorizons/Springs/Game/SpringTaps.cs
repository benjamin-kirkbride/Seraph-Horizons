using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Pipes;
using SeraphHorizons.Mod.Pipes.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Springs;

/// <summary>
/// A spring's pipes (README "Springs"), on ExpandedLib's network (Pipes and Power Expanded's pipes),
/// found by name since nothing of it is referenced at build time:
/// <list type="bullet">
/// <item>a side is tapped when the cell beyond it is a <c>pipe</c> network member
/// (<c>NetworkMembership.Resolve</c>) with a connector facing the spring
/// (<c>INetworkMember.HasConnectorAt</c>), as the network's own graph decides coupling;</item>
/// <item>the network across a side is the manager's <c>GetConnectedNetworkAcross</c>, as ppex's pumps
/// find their lines, and the spring fills it with <c>PipeNetwork.ProduceLiquidMeasured</c>, as an
/// intake fills its line for a pump.</item>
/// </list>
/// Without ExpandedLib, or with a member not as expected (one warning), no side is ever tapped and
/// a spring always pours.
/// </summary>
public static class SpringTaps
{
    public const string MembershipType = "ExpandedLib.Networks.NetworkMembership";
    public const string MemberType = "ExpandedLib.Networks.INetworkMember";
    public const string PipeNetworkType = "pipe";

    /// <summary>The water's temperature, as ppex's pumps feed an intake's (20 °C).</summary>
    public const float WaterTemperature = 20f;

    /// <summary>The pressure the spring feeds its run at: a hand-cranked pump's delivery head,
    /// enough to lift water into a boiler (ppex's <c>ManualPumpDeliveryPressure</c>).</summary>
    public const float FeedPressure = 1f;

    private static MethodInfo? _resolve, _hasConnector, _across, _produce;
    private static Type? _pipeNetwork;
    private static string? _managerName;

    public static bool Bound => _resolve != null;

    /// <summary>Finds ExpandedLib's members; false (one warning) when they are not as expected.</summary>
    public static bool Bind(ILogger logger)
    {
        Unbind();
        var membership = AccessTools.TypeByName(MembershipType);
        var member = AccessTools.TypeByName(MemberType);
        if (membership == null || member == null)
            return false;
        var resolve = AccessTools.DeclaredMethod(membership, "Resolve", [typeof(IBlockAccessor), typeof(BlockPos), typeof(string)]);
        var hasConnector = AccessTools.DeclaredMethod(member, "HasConnectorAt", [typeof(IBlockAccessor), typeof(BlockPos), typeof(BlockFacing)]);
        var manager = NameCandidates.First(HandPumpBridge.ManagerNames, AccessTools.TypeByName);
        var network = NameCandidates.First(HandPumpBridge.PipeNetworkNames, AccessTools.TypeByName);
        var across = manager == null ? null
            : AccessTools.Method(manager.Value.Value, "GetConnectedNetworkAcross", [typeof(IBlockAccessor), typeof(BlockPos), typeof(BlockFacing)]);
        var produce = network == null ? null
            : AccessTools.Method(network.Value.Value, "ProduceLiquidMeasured", [typeof(float), typeof(float), typeof(float), typeof(IBlockAccessor)]);
        if (resolve is not { IsStatic: true } || !member.IsAssignableFrom(resolve.ReturnType)
            || hasConnector?.ReturnType != typeof(bool)
            || across is not { IsStatic: false } || produce?.ReturnType != typeof(float))
        {
            logger.Warning("[seraphhorizons] Springs: ExpandedLib's NetworkMembership.Resolve, INetworkMember.HasConnectorAt, GetConnectedNetworkAcross or PipeNetwork.ProduceLiquidMeasured is not as expected, so no pipe can tap a spring");
            return false;
        }
        _resolve = resolve;
        _hasConnector = hasConnector;
        _across = across;
        _produce = produce;
        _pipeNetwork = network!.Value.Value;
        _managerName = manager!.Value.Value.FullName;
        return true;
    }

    public static void Unbind()
    {
        _resolve = _hasConnector = _across = _produce = null;
        _pipeNetwork = null;
        _managerName = null;
    }

    private static bool _failed;

    private static void Failed(ILogger logger, Exception e)
    {
        if (_failed)
            return;
        _failed = true;
        logger.Warning("[seraphhorizons] Springs: reading a spring's pipes failed, so no pipe taps a spring from now on: {0}",
            (e.InnerException ?? e).Message);
    }

    /// <summary>Whether a pipe beyond <paramref name="face"/> of <paramref name="pos"/> couples to
    /// it.</summary>
    public static bool IsTapped(IWorldAccessor world, BlockPos pos, BlockFacing face)
    {
        if (_resolve == null || _hasConnector == null || _failed)
            return false;
        try
        {
            var ba = world.BlockAccessor;
            var beyond = pos.AddCopy(face);
            var member = _resolve.Invoke(null, [ba, beyond, PipeNetworkType]);
            return member != null && (bool)_hasConnector.Invoke(member, [ba, beyond, face.Opposite])!;
        }
        catch (Exception e)
        {
            Failed(world.Logger, e);
            return false;
        }
    }

    /// <summary>Whether any pipe couples to the spring at <paramref name="pos"/>.</summary>
    public static bool AnyTapped(IWorldAccessor world, BlockPos pos)
    {
        foreach (var face in BlockFacing.ALLFACES)
            if (IsTapped(world, pos, face))
                return true;
        return false;
    }

    /// <summary>The distinct pipe networks coupled to the spring at <paramref name="pos"/>.</summary>
    public static List<object> Networks(ICoreAPI api, BlockPos pos)
    {
        var found = new List<object>();
        if (_across == null || _pipeNetwork == null || _failed || api.ModLoader.GetModSystem(_managerName!) is not { } manager)
            return found;
        try
        {
            foreach (var face in BlockFacing.ALLFACES)
            {
                if (!IsTapped(api.World, pos, face))
                    continue;
                var network = _across.Invoke(manager, [api.World.BlockAccessor, pos, face]);
                if (network != null && _pipeNetwork.IsInstanceOfType(network) && !found.Any(n => ReferenceEquals(n, network)))
                    found.Add(network);
            }
        }
        catch (Exception e)
        {
            Failed(api.Logger, e);
        }
        return found;
    }

    /// <summary>Puts up to <paramref name="litres"/> of water into <paramref name="network"/>;
    /// returns what it took.</summary>
    public static float Produce(ICoreAPI api, object network, float litres)
    {
        if (_produce == null || _failed || !(litres > 0))
            return 0;
        try
        {
            return (float)_produce.Invoke(network, [litres, WaterTemperature, FeedPressure, api.World.BlockAccessor])!;
        }
        catch (Exception e)
        {
            Failed(api.Logger, e);
            return 0;
        }
    }
}
