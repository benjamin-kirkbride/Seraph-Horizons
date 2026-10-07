using System.Collections;
using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Pipes.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Pipes;

/// <summary>
/// Hydrate or Diedrate's hand pump on Pipes and Power Expanded's pipes (the <c>UnifiedPipes</c>
/// switch; README "Unified pipes", "The hand pump and wells"). Hydrate's own pipes were only ever
/// the hand pump's way to a wellspring: the pump searched through them, on demand, for a spring
/// beside a pipe, and primed for as many strokes as the pipes were long over
/// <c>HandPumpPrimingBlocksPerStroke</c>. With its pipes gone (<see cref="HydratePipes"/>) the pump
/// searches ppex's instead:
/// <list type="bullet">
/// <item>a postfix on <c>BlockEntityHandPump.FindWellViaNetwork</c>: when Hydrate's own search
/// finds nothing (it still finds a spring right under the pump), the walk goes from the cell under
/// the pump through ppex pipe cells that couple to each other (exlib's
/// <c>NetworkMembership.CouplesAt</c> both ways, the rule ppex's network is built by; a closed
/// valve, <c>IsConnectionBroken</c>, stops it) to the nearest wellspring beside any of them
/// (<see cref="HandPumpSearch.Nearest"/>). On the server the run must also be empty or hold water
/// (its <c>PipeNetwork.State.MediumType</c>), and only its graph's nodes count;</item>
/// <item>a prefix on <c>ComputePrimingStrokes</c>: for a spring found that way, the pipes walked
/// over Hydrate's setting (<see cref="HandPumpSearch.PrimingStrokes"/>);</item>
/// <item>a postfix on exlib's <c>PipeNetwork.OnTopologyChanged</c> (server) calls Hydrate's
/// <c>FluidNetworkState.InvalidateNetwork()</c>, so a pump drops its cached spring when a ppex run
/// changes (a valve turned, a pipe placed or broken), as Hydrate's pipes did; on the client, which
/// has no ppex networks, the pump looks again each time it is asked (its look-at text);</item>
/// <item>a postfix on exlib's <c>BlockNetworkNode.IsValidNonNetworkConnection</c> (server): a pipe
/// end against a hand pump's underside or a wellspring is sealed, not an open end leaking the
/// run's water;</item>
/// <item>prefix and postfix on ppex's <c>BlockEntityFluidIntake.ProduceWater</c> (server): an intake
/// drawing well water takes it from the spring governing that water
/// (<c>WellBlockUtils.FindGoverningSpring</c>), at most what the spring holds, so a well is a finite
/// ppex source; well water no spring governs gives none.</item>
/// </list>
/// Hydrate's licence forbids redistributing it, so nothing of it is referenced at build time: every
/// type and member is found by name, with parameter types for overloaded names. With a member
/// missing the part that needs it is left out, with one warning.
/// </summary>
public static class HandPumpBridge
{
    // Both sides, its own id, patched once per process (the client asks the pump for its look-at
    // text, and singleplayer runs both sides in one process).
    public const string HarmonyId = "seraphhorizons.handpump";

    public const string HodModId = "hydrateordiedrate";
    public const string PumpTypeName = "HydrateOrDiedrate.Piping.HandPump.BlockEntityHandPump";
    public const string SpringTypeName = "HydrateOrDiedrate.Wells.WellWater.BlockEntityWellSpring";
    public const string NetworkStateTypeName = "HydrateOrDiedrate.Piping.FluidNetwork.FluidNetworkState";
    public const string HodConfigTypeName = "HydrateOrDiedrate.Config.ModConfig";
    public const string WellUtilsTypeName = "HydrateOrDiedrate.Wells.WellBlockUtils";
    public const string MembershipTypeName = "ExpandedLib.Networks.NetworkMembership";
    public const string MemberTypeName = "ExpandedLib.Networks.INetworkMember";
    public const string NodeBlockTypeName = "ExpandedLib.Networks.BlockNetworkNode";
    public const string BlockNetworkTypeName = "ExpandedLib.Networks.BlockNetwork";
    public const string IntakeTypeName = "PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities.BlockEntityFluidIntake";
    public const string PpexValuesTypeName = "PipesAndPowerExpanded.PpexValues";
    public const string PipeNetworkType = "pipe";

    // The pinned layout first, then ppex 0.6.8 / exlib 0.7.2's (as PpexWater).
    public static readonly string[] ManagerNames =
        ["ExpandedLib.Networks.BlockNetworkModSystem", "ExpandedLib.Blocks.Networks.BlockNetworkModSystem"];
    public static readonly string[] PipeNetworkNames =
        ["ExpandedLib.Industry.Pipes.PipeNetwork", "PipesAndPowerExpanded.BlockNetworkPipe.PipeNetwork"];

    private static readonly object Lock = new();
    private static int _users;
    private static Pump? _pump;

    private sealed class Pump
    {
        public required MethodInfo Find, Prime, GetOrFind;
        public required FieldInfo LastVersion;
        public required Type SpringType;
        public required PropertyInfo ConfigInstance, ConfigPump, PrimingEnabled, BlocksPerStroke;
        public required MethodInfo CouplesAt, NetworkTypeAt, IsBroken;
        public required Type MemberType;
        // The server's network checks (null when ppex's networks are not as expected: then the
        // walk alone decides, as on the client).
        public string? ManagerName;
        public MethodInfo? Across;
        public Type? PipeNetwork;
        public PropertyInfo? State, Medium, Nodes;
    }

    // ---- binding ----

    private static PropertyInfo? MostDerived(Type type, string name)
    {
        for (var t = type; t != null; t = t.BaseType)
            if (t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly) is { } property)
                return property;
        return null;
    }

    private static Pump? BindPump(ILogger logger)
    {
        var pumpType = AccessTools.TypeByName(PumpTypeName);
        var springType = AccessTools.TypeByName(SpringTypeName);
        var configType = AccessTools.TypeByName(HodConfigTypeName);
        var membership = AccessTools.TypeByName(MembershipTypeName);
        var memberType = AccessTools.TypeByName(MemberTypeName);
        if (pumpType == null || springType == null || configType == null)
        {
            logger.Warning("[seraphhorizons] Unified pipes: Hydrate or Diedrate's hand pump ({0}) is not there, so it cannot find a wellspring through Pipes and Power Expanded's pipes", PumpTypeName);
            return null;
        }
        if (membership == null || memberType == null)
        {
            logger.Warning("[seraphhorizons] Unified pipes: ExpandedLib's {0} or {1} is missing, so the hand pump cannot find a wellspring through Pipes and Power Expanded's pipes", MembershipTypeName, MemberTypeName);
            return null;
        }
        var find = AccessTools.DeclaredMethod(pumpType, "FindWellViaNetwork", Type.EmptyTypes);
        var prime = AccessTools.DeclaredMethod(pumpType, "ComputePrimingStrokes", [typeof(IWorldAccessor), typeof(BlockPos), typeof(BlockPos)]);
        var getOrFind = AccessTools.DeclaredMethod(pumpType, "GetOrFindSpring", Type.EmptyTypes);
        var lastVersion = AccessTools.DeclaredField(pumpType, "lastNetworkVersion");
        var instance = AccessTools.DeclaredProperty(configType, "Instance");
        var pumpConfig = AccessTools.DeclaredProperty(configType, "Pump");
        var enabled = pumpConfig == null ? null : AccessTools.DeclaredProperty(pumpConfig.PropertyType, "HandPumpEnablePriming");
        var perStroke = pumpConfig == null ? null : AccessTools.DeclaredProperty(pumpConfig.PropertyType, "HandPumpPrimingBlocksPerStroke");
        var couplesAt = AccessTools.DeclaredMethod(membership, "CouplesAt", [typeof(IBlockAccessor), typeof(BlockPos), typeof(BlockFacing)]);
        var typeAt = AccessTools.DeclaredMethod(memberType, "NetworkTypeAt", [typeof(IBlockAccessor), typeof(BlockPos)]);
        var broken = AccessTools.DeclaredMethod(memberType, "IsConnectionBroken", [typeof(IBlockAccessor), typeof(BlockPos)]);
        if (find == null || !springType.IsAssignableFrom(find.ReturnType)
            || prime == null || prime.ReturnType != typeof(int)
            || getOrFind == null || lastVersion?.FieldType != typeof(int)
            || instance?.GetMethod is not { IsStatic: true } || pumpConfig == null
            || enabled?.PropertyType != typeof(bool) || perStroke?.PropertyType != typeof(int)
            || couplesAt is not { IsStatic: true } || couplesAt.ReturnType != typeof(bool)
            || typeAt?.ReturnType != typeof(string) || broken?.ReturnType != typeof(bool))
        {
            logger.Warning("[seraphhorizons] Unified pipes: Hydrate or Diedrate's hand pump or ExpandedLib's network membership is not as expected (FindWellViaNetwork, ComputePrimingStrokes, GetOrFindSpring, lastNetworkVersion, the Pump settings, CouplesAt, NetworkTypeAt, IsConnectionBroken), so the hand pump cannot find a wellspring through Pipes and Power Expanded's pipes");
            return null;
        }
        var pump = new Pump
        {
            Find = find, Prime = prime, GetOrFind = getOrFind, LastVersion = lastVersion, SpringType = springType,
            ConfigInstance = instance, ConfigPump = pumpConfig, PrimingEnabled = enabled, BlocksPerStroke = perStroke,
            CouplesAt = couplesAt, NetworkTypeAt = typeAt, IsBroken = broken, MemberType = memberType,
        };
        var manager = NameCandidates.First(ManagerNames, AccessTools.TypeByName);
        var network = NameCandidates.First(PipeNetworkNames, AccessTools.TypeByName);
        var blockNetwork = AccessTools.TypeByName(BlockNetworkTypeName);
        var across = manager == null ? null
            : AccessTools.Method(manager.Value.Value, "GetConnectedNetworkAcross", [typeof(IBlockAccessor), typeof(BlockPos), typeof(BlockFacing)]);
        var state = network == null ? null : MostDerived(network.Value.Value, "State");
        var medium = state == null ? null : MostDerived(state.PropertyType, "MediumType");
        var nodes = MostDerived(network?.Value ?? blockNetwork ?? typeof(object), "Nodes");
        if (across is { IsStatic: false } && state?.GetMethod != null && medium?.PropertyType == typeof(string)
            && nodes != null && typeof(IEnumerable).IsAssignableFrom(nodes.PropertyType))
        {
            pump.ManagerName = manager!.Value.Value.FullName;
            pump.Across = across;
            pump.PipeNetwork = network!.Value.Value;
            pump.State = state;
            pump.Medium = medium;
            pump.Nodes = nodes;
        }
        else
            logger.Warning("[seraphhorizons] Unified pipes: Pipes and Power Expanded's pipe network is not as expected (GetConnectedNetworkAcross, PipeNetwork.State.MediumType, Nodes), so a hand pump does not check what its pipes carry");
        return pump;
    }

    /// <summary>Patches the hand pump's search and priming, once per process, on the side that
    /// gets here first; false (one warning) when Hydrate or Diedrate's pump or ExpandedLib's
    /// membership is not as expected. <see cref="Unpatch"/> undoes it when the last side goes.</summary>
    public static bool Patch(ILogger logger)
    {
        lock (Lock)
        {
            if (_users > 0)
            {
                _users++;
                return true;
            }
            var pump = BindPump(logger);
            if (pump == null)
                return false;
            var harmony = new Harmony(HarmonyId);
            harmony.Patch(pump.Find, postfix: new HarmonyMethod(typeof(HandPumpBridge), nameof(FindPostfix)));
            harmony.Patch(pump.Prime, prefix: new HarmonyMethod(typeof(HandPumpBridge), nameof(PrimePrefix)));
            harmony.Patch(pump.GetOrFind, prefix: new HarmonyMethod(typeof(HandPumpBridge), nameof(GetOrFindPrefix)));
            _pump = pump;
            _users = 1;
            return true;
        }
    }

    public static void Unpatch()
    {
        lock (Lock)
        {
            if (_users > 0 && --_users == 0)
            {
                new Harmony(HarmonyId).UnpatchAll(HarmonyId);
                _pump = null;
            }
        }
    }

    // ---- the search ----

    private static PipeCell CellOf(BlockPos pos) => new(pos.X, pos.Y, pos.Z);

    private static bool IsPipe(Pump pump, IBlockAccessor ba, BlockPos pos)
    {
        var block = ba.GetBlock(pos);
        if (!pump.MemberType.IsInstanceOfType(block))
            return false;
        return (string?)pump.NetworkTypeAt.Invoke(block, [ba, pos]) == PipeNetworkType
               && !(bool)pump.IsBroken.Invoke(block, [ba, pos])!;
    }

    private static bool Couples(Pump pump, IBlockAccessor ba, BlockPos pos, BlockFacing face) =>
        (bool)pump.CouplesAt.Invoke(null, [ba, pos, face])!;

    /// <summary>The nearest wellspring a hand pump at <paramref name="pumpPos"/> reaches through
    /// ppex pipes, with the pipes walked; null when none (or the bridge is not bound).</summary>
    public static (BlockEntity Spring, int Pipes)? Search(ICoreAPI api, BlockPos pumpPos)
    {
        var pump = _pump;
        if (pump == null || api?.World?.BlockAccessor is not { } ba)
            return null;
        var dim = pumpPos.dimension;
        BlockPos At(PipeCell c) => new(c.X, c.Y, c.Z, dim);
        var start = pumpPos.DownCopy();
        if (!IsPipe(pump, ba, start) || !Couples(pump, ba, start, BlockFacing.UP))
            return null;
        HashSet<BlockPos>? graph = null;
        if (api.Side == EnumAppSide.Server && pump.Across != null)
        {
            var manager = api.ModLoader.GetModSystem(pump.ManagerName!);
            var network = manager == null ? null : pump.Across.Invoke(manager, [ba, pumpPos, BlockFacing.DOWN]);
            // On the server the graph decides: no run under the pump, or one carrying steam,
            // exhaust or air, reaches no well.
            if (network == null || !pump.PipeNetwork!.IsInstanceOfType(network))
                return null;
            var state = pump.State!.GetValue(network);
            if (!HandPumpSearch.CarriesWater(state == null ? null : (string?)pump.Medium!.GetValue(state)))
                return null;
            if (pump.Nodes!.GetValue(network) is HashSet<BlockPos> nodes)
                graph = nodes;
        }
        BlockEntity? found = null;
        var path = HandPumpSearch.Nearest(CellOf(start),
            c =>
            {
                var pos = At(c);
                return (graph == null || graph.Contains(pos)) && IsPipe(pump, ba, pos);
            },
            (c, face) =>
            {
                var facing = BlockFacing.ALLFACES[face];
                var pos = At(c);
                return Couples(pump, ba, pos, facing) && Couples(pump, ba, pos.AddCopy(facing), facing.Opposite);
            },
            c =>
            {
                if (ba.GetBlockEntity(At(c)) is { } be && pump.SpringType.IsInstanceOfType(be))
                {
                    found = be;
                    return true;
                }
                return false;
            });
        return path is { } p && found != null ? (found, p.Pipes) : null;
    }

    private static bool _failed;

    private static void Failed(ICoreAPI? api, Exception e)
    {
        lock (Lock)
        {
            if (_failed)
                return;
            _failed = true;
        }
        api?.Logger.Warning("[seraphhorizons] Unified pipes: the hand pump's search through Pipes and Power Expanded's pipes failed, so it finds no spring through them from now on: {0}",
            (e.InnerException ?? e).Message);
    }

    // BlockEntityHandPump.FindWellViaNetwork(): Hydrate's result when it has one (a spring right
    // under the pump), else the nearest through ppex pipes.
    private static void FindPostfix(BlockEntity __instance, ref BlockEntity? __result)
    {
        if (__result != null || _failed || __instance?.Api == null)
            return;
        try
        {
            if (Search(__instance.Api, __instance.Pos) is { } found)
                __result = found.Spring;
        }
        catch (Exception e)
        {
            Failed(__instance.Api, e);
        }
    }

    // BlockEntityHandPump.ComputePrimingStrokes(world, start, targetSpringPos): for a spring the
    // ppex search reaches, the pipes walked over Hydrate's setting; anything else is Hydrate's.
    private static bool PrimePrefix(BlockEntity __instance, BlockPos __2, ref int __result)
    {
        var pump = _pump;
        if (pump == null || _failed || __instance?.Api == null || __2 == null)
            return true;
        try
        {
            if (Search(__instance.Api, __instance.Pos) is not { } found || found.Spring.Pos != __2)
                return true;
            var settings = pump.ConfigPump.GetValue(pump.ConfigInstance.GetValue(null));
            bool enabled = settings != null && (bool)pump.PrimingEnabled.GetValue(settings)!;
            int perStroke = settings == null ? 0 : (int)pump.BlocksPerStroke.GetValue(settings)!;
            __result = HandPumpSearch.PrimingStrokes(found.Pipes, enabled, perStroke);
            return false;
        }
        catch (Exception e)
        {
            Failed(__instance.Api, e);
            return true;
        }
    }

    // BlockEntityHandPump.GetOrFindSpring(): the client has no ppex networks to say when a run
    // changed, so its pump always looks again (its look-at text; the walk is short).
    private static void GetOrFindPrefix(BlockEntity __instance)
    {
        if (_pump is { } pump && __instance?.Api?.Side == EnumAppSide.Client)
            pump.LastVersion.SetValue(__instance, int.MinValue);
    }

    // ---- server: cache, seals, the intake ----

    private static MethodInfo? _invalidate;
    private static Type? _springType;
    private static MethodInfo? _isWellWater, _governingSpring, _changeVolume;
    private static PropertyInfo? _totalLiters, _intakeDepth;
    private static ICoreAPI? _serverApi;

    /// <summary>Server side, into <paramref name="harmony"/> (the mod's own id): the cache
    /// invalidation, the seals and the intake's drain, each left out with one warning when what it
    /// needs is not as expected.</summary>
    public static void PatchServer(Harmony harmony, ICoreAPI api)
    {
        var logger = api.Logger;
        _serverApi = api;
        var network = NameCandidates.First(PipeNetworkNames, AccessTools.TypeByName);
        var stateType = AccessTools.TypeByName(NetworkStateTypeName);
        _invalidate = stateType == null ? null : AccessTools.DeclaredMethod(stateType, "InvalidateNetwork", Type.EmptyTypes);
        var topology = network == null ? null : AccessTools.DeclaredMethod(network.Value.Value, "OnTopologyChanged", Type.EmptyTypes);
        if (_invalidate is { IsStatic: true } && topology != null)
            harmony.Patch(topology, postfix: new HarmonyMethod(typeof(HandPumpBridge), nameof(TopologyPostfix)));
        else
            logger.Warning("[seraphhorizons] Unified pipes: Hydrate or Diedrate's FluidNetworkState.InvalidateNetwork or ppex's PipeNetwork.OnTopologyChanged is not as expected, so a hand pump keeps the spring it found until it is placed again");

        var nodeType = AccessTools.TypeByName(NodeBlockTypeName);
        var seal = nodeType == null ? null : AccessTools.DeclaredMethod(nodeType, "IsValidNonNetworkConnection", [typeof(Block), typeof(BlockFacing)]);
        if (seal?.ReturnType == typeof(bool))
            harmony.Patch(seal, postfix: new HarmonyMethod(typeof(HandPumpBridge), nameof(SealPostfix)));
        else
            logger.Warning("[seraphhorizons] Unified pipes: ExpandedLib's BlockNetworkNode.IsValidNonNetworkConnection is not as expected, so a pipe end under a hand pump or against a wellspring is an open end");

        _springType = AccessTools.TypeByName(SpringTypeName);
        var utils = AccessTools.TypeByName(WellUtilsTypeName);
        var intake = AccessTools.TypeByName(IntakeTypeName);
        _isWellWater = utils == null ? null : AccessTools.DeclaredMethod(utils, "IsOurWellwater", [typeof(Block)]);
        _governingSpring = utils == null ? null : AccessTools.DeclaredMethod(utils, "FindGoverningSpring", [typeof(ICoreAPI), typeof(Block), typeof(BlockPos)]);
        _changeVolume = _springType == null ? null : AccessTools.DeclaredMethod(_springType, "TryChangeVolume", [typeof(float), typeof(bool)]);
        _totalLiters = _springType == null ? null : AccessTools.DeclaredProperty(_springType, "TotalLiters");
        var produce = intake == null ? null : AccessTools.DeclaredMethod(intake, "ProduceWater", [typeof(float), typeof(float), typeof(IBlockAccessor)]);
        var values = AccessTools.TypeByName(PpexValuesTypeName);
        _intakeDepth = values == null ? null : AccessTools.DeclaredProperty(values, "FluidIntakeWaterDepth");
        if (_intakeDepth?.PropertyType != typeof(int))
            _intakeDepth = null;
        if (_isWellWater is { IsStatic: true } && _isWellWater.ReturnType == typeof(bool)
            && _governingSpring is { IsStatic: true } && _springType!.IsAssignableFrom(_governingSpring.ReturnType)
            && _changeVolume != null && _totalLiters?.PropertyType == typeof(float)
            && produce?.ReturnType == typeof(float))
            harmony.Patch(produce, prefix: new HarmonyMethod(typeof(HandPumpBridge), nameof(IntakePrefix)),
                postfix: new HarmonyMethod(typeof(HandPumpBridge), nameof(IntakePostfix)));
        else
            logger.Warning("[seraphhorizons] Unified pipes: Hydrate or Diedrate's wells (WellBlockUtils, BlockEntityWellSpring) or ppex's BlockEntityFluidIntake.ProduceWater are not as expected, so a fluid intake over a well does not drain it");
    }

    public static void UnbindServer()
    {
        _serverApi = null;
        _invalidate = null;
    }

    private static void TopologyPostfix()
    {
        try
        {
            _invalidate?.Invoke(null, null);
        }
        catch (Exception e)
        {
            _invalidate = null;
            _serverApi?.Logger.Warning("[seraphhorizons] Unified pipes: Hydrate or Diedrate's InvalidateNetwork failed, so hand pumps keep their springs from now on: {0}", (e.InnerException ?? e).Message);
        }
    }

    /// <summary>Whether a ppex pipe's connector on <paramref name="face"/> against
    /// <paramref name="neighbour"/> is sealed: a hand pump above (its only connector is underneath),
    /// or a wellspring on any side.</summary>
    public static bool SealsAgainst(Block? neighbour, BlockFacing? face)
    {
        if (neighbour?.Code is not { Domain: HodModId } code)
            return false;
        return code.Path == "wellspring" || (code.Path.StartsWith("handpump-", StringComparison.Ordinal) && face == BlockFacing.UP);
    }

    private static void SealPostfix(Block __0, BlockFacing __1, ref bool __result)
    {
        if (!__result && SealsAgainst(__0, __1))
            __result = true;
    }

    /// <summary>The spring governing well water under the intake at <paramref name="pos"/>, where
    /// ppex's intake looks for water; <paramref name="wellWater"/> says whether any well water was
    /// there.</summary>
    public static BlockEntity? SpringUnderIntake(ICoreAPI api, BlockPos pos, out bool wellWater)
    {
        wellWater = false;
        if (_isWellWater == null || _governingSpring == null)
            return null;
        var ba = api.World.BlockAccessor;
        int depth = _intakeDepth?.GetValue(null) is int d && d > 0 ? d : 3;
        foreach (var offset in HandPumpSearch.IntakeCells(depth))
        {
            var at = pos.AddCopy(offset.X, offset.Y, offset.Z);
            var fluid = ba.GetBlock(at, BlockLayersAccess.Fluid);
            if (fluid == null || fluid.Id == 0 || !(bool)_isWellWater.Invoke(null, [fluid])!)
                continue;
            wellWater = true;
            if (_governingSpring.Invoke(null, [api, fluid, at]) is BlockEntity spring)
                return spring;
        }
        return null;
    }

    // BlockEntityFluidIntake.ProduceWater(amount, temperature, ba): over well water, ask for no
    // more than the spring holds (none without a spring); the postfix takes what went in.
    private static void IntakePrefix(BlockEntity __instance, ref float __0, out object? __state)
    {
        __state = null;
        if (__0 <= 0 || _totalLiters == null || __instance?.Api is not { Side: EnumAppSide.Server } api)
            return;
        try
        {
            var spring = SpringUnderIntake(api, __instance.Pos, out bool wellWater);
            if (spring == null)
            {
                if (wellWater)
                    __0 = 0;
                return;
            }
            __0 = HandPumpSearch.IntakeAllowance(__0, (float)_totalLiters.GetValue(spring)!);
            __state = spring;
        }
        catch (Exception e)
        {
            _totalLiters = null;
            api.Logger.Warning("[seraphhorizons] Unified pipes: reading a well under a fluid intake failed, so intakes no longer drain wells: {0}", (e.InnerException ?? e).Message);
        }
    }

    private static void IntakePostfix(float __result, object? __state)
    {
        if (__state == null || __result <= 0 || _changeVolume == null)
            return;
        try
        {
            _changeVolume.Invoke(__state, [-__result, true]);
        }
        catch (Exception e)
        {
            _changeVolume = null;
            _serverApi?.Logger.Warning("[seraphhorizons] Unified pipes: draining a well for a fluid intake failed, so intakes no longer drain wells: {0}", (e.InnerException ?? e).Message);
        }
    }
}
