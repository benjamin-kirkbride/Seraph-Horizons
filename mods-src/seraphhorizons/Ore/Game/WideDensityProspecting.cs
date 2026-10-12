using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Ore;

/// <summary>A density walk handed back to the main thread (for scenarios).</summary>
public sealed record DensityWalk(string PlayerUid, BlockPos Pos, IReadOnlyDictionary<string, int> Counts, int ThreadId, double Milliseconds);

/// <summary>
/// BetterEr Prospecting's block-based density mode (<c>NewDensityMode</c>, the pack's) reads twice as
/// far, and its block walk runs off the server's main thread (<c>WideDensityProspecting</c>, README
/// "Density prospecting reads 64 blocks, off the main thread"). Server side, by name; BetterEr is not
/// referenced at build time.
///
/// <para><b>Radius.</b> BetterEr's <c>ItemBetterErProspectingPick.densityRadius</c> is a
/// <c>const int</c> (the chunk size, 32), so the compiler copied it into its users. Transpilers swap
/// the copies: the four <c>32</c>s of the walk's corners in <c>ProspectingSystem.GenerateBlockData</c>,
/// the folded zone area <c>(2 * 32)^2 = 4096</c> a reading is normalised by in
/// <c>ProspectingSystem.generateReadigs</c> (without it, parts per thousand would read 4x high), and
/// the chunk offsets <c>{ -32, 0, 32 }</c> (twice: x and z) that the admin <c>/btrpr reprospect</c>
/// loads around each saved reading before it walks it again (its async state machine's
/// <c>MoveNext</c>): at 64 the walk spans five chunks each way, so they become
/// <see cref="DensityProspecting.ChunkOffsets"/>, <c>{ -64, -32, 0, 32, 64 }</c>.</para>
///
/// <para><b>Off the main thread.</b> <c>ItemBetterErProspectingPick.ProbeDensity</c> (the mode's
/// handler, from <c>OnBlockBrokenWith</c>, on the main thread) gets a prefix that, in the new density
/// mode, takes the hit's position and player, hands the walk to a task and returns BetterEr's
/// <c>NewDensityDmg</c> at once, so the pick is charged on the hit. The task runs BetterEr's own
/// <c>GenerateBlockData</c> with a block accessor of its own (<c>GetBlockAccessor(false, false,
/// false)</c>, made on the main thread; it unpacks each chunk under the chunk's lock, as the
/// reprospect's thread-pool walks already rely on, where the lock-free accessor answers air for a
/// packed chunk): a third transpiler sends that method's <c>World.BlockAccessor</c> through
/// <see cref="Accessor"/>, which answers the task's accessor on the task's thread and the world's
/// anywhere else. The counts then go back to the main thread
/// (<c>EnqueueMainThreadTask</c>), which runs BetterEr's own <c>ProbeBlockDensityMode</c> with a
/// prefix on <c>GenerateBlockData</c> answering the counts already made, so the readings, the ppt
/// tracker, the messages and the ore map entry are BetterEr's, unchanged. One walk per player at a
/// time (<see cref="ProbeGate"/>): a hit while one is running is turned away with a message and costs
/// nothing. A result for a player who has left, or a server shutting down, is dropped; a failure in
/// the task is logged. With BetterEr's old density modes the original runs.</para>
///
/// <para>Each rewrite must find exactly what it expects (<see cref="Unsupported"/> counts them on the
/// original IL before anything is patched, and the transpilers again as they apply); if BetterEr
/// changed any of it, nothing is patched, with one warning, and density prospecting is as it
/// ships.</para>
/// </summary>
public class WideDensityProspectingSystem : ModSystem
{
    public const string ModId = "bettererprospecting";
    public const string HarmonyId = "seraphhorizons.densityprospecting";
    public const string BusyLangKey = "seraphhorizons:densityprospecting-busy";

    private const string PickType = "BetterErProspecting.Item.ItemBetterErProspectingPick";
    private const string SystemType = "BetterErProspecting.Prospecting.ProspectingSystem";
    private const string ConfigType = "BetterErProspecting.Config.ModConfig";

    // The rewrites each method needs, in BetterEr 3.4.10.
    public const int WalkRadiusSwaps = 4;
    public const int AccessorSwaps = 2;
    public const int ZoneAreaSwaps = 1;
    public const int OffsetArraySwaps = 2;

    private static readonly ProbeGate Gate = new();

    private static ICoreServerAPI? _sapi;
    private static MethodInfo? _generateBlockData, _generateReadings, _probeDensity, _probeBlockDensityMode, _reprospect;
    private static FieldInfo? _newDensityMode, _newDensityDmg;
    private static PropertyInfo? _configInstance;
    private static Type? _debugListType;

    // The walk's accessor, on the task's thread only.
    [ThreadStatic] private static IBlockAccessor? _walkAccessor;
    // The counts handed back, on the main thread, while BetterEr's own density mode runs on them.
    [ThreadStatic] private static Dictionary<string, int>? _handedCounts;
    [ThreadStatic] private static IList? _handedDebug;

    private Harmony? _harmony;

    /// <summary>Whether the patches are in.</summary>
    public static bool Patched { get; private set; }

    /// <summary>The radius the patched methods read, <see cref="DensityProspecting.WideRadius"/>.
    /// Settable so a scenario can walk the same spot at BetterEr's own radius to compare.</summary>
    public static int Radius { get; set; } = DensityProspecting.WideRadius;

    /// <summary>How many walks are in flight.</summary>
    public static int WalksInFlight => Gate.Count;

    /// <summary>Raised on the main thread with each walk's counts, before BetterEr turns them into a
    /// reading (scenarios watch it).</summary>
    public static event Action<DensityWalk>? Delivered;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        if (!SeraphHorizonsSystem.ConfigFor(api).WideDensityProspecting || !api.ModLoader.IsModEnabled(ModId))
            return;
        if (Unsupported() is { } why)
        {
            api.Logger.Warning("[seraphhorizons] Wide density prospecting: {0}; BetterEr Prospecting changed, so its density mode is as it ships", why);
            return;
        }
        _harmony = new Harmony(HarmonyId);
        try
        {
            Patch(_harmony);
        }
        catch (Exception e)
        {
            _harmony.UnpatchAll(HarmonyId);
            _harmony = null;
            api.Logger.Warning("[seraphhorizons] Wide density prospecting: patching failed, so BetterEr Prospecting's density mode is as it ships: {0}", e.Message);
            return;
        }
        _sapi = api;
        Patched = true;
        api.Logger.Notification("[seraphhorizons] Wide density prospecting: density mode reads {0} blocks each way, walked off the main thread",
            DensityProspecting.WideRadius);
    }

    public override void Dispose()
    {
        Radius = DensityProspecting.WideRadius;
        _harmony?.UnpatchAll(HarmonyId);
        _harmony = null;
        _sapi = null;
        Patched = false;
        Gate.Clear();
    }

    /// <summary>Why it can't bind to the loaded BetterEr, or null if it can.</summary>
    public static string? Unsupported()
    {
        var pick = AccessTools.TypeByName(PickType);
        var system = AccessTools.TypeByName(SystemType);
        var config = AccessTools.TypeByName(ConfigType);
        if (pick == null || system == null || config == null) return "its pick, prospecting system or config type is missing";

        if (AccessTools.Field(pick, "densityRadius") is not { IsLiteral: true } radius
            || radius.GetRawConstantValue() is not int r || r != DensityProspecting.ShippedRadius)
            return $"ItemBetterErProspectingPick.densityRadius is no longer the constant {DensityProspecting.ShippedRadius}";

        _probeDensity = AccessTools.Method(pick, "ProbeDensity", [typeof(IWorldAccessor), typeof(IServerPlayer), typeof(ItemSlot), typeof(BlockSelection)]);
        if (_probeDensity?.ReturnType != typeof(int)) return "ItemBetterErProspectingPick.ProbeDensity(world, player, slot, selection) is gone";
        _probeBlockDensityMode = AccessTools.Method(pick, "ProbeBlockDensityMode", [typeof(IServerPlayer), typeof(BlockSelection)]);
        if (_probeBlockDensityMode?.ReturnType != typeof(int)) return "ItemBetterErProspectingPick.ProbeBlockDensityMode(player, selection) is gone";

        _generateBlockData = system.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(m => m.Name == "GenerateBlockData");
        var gbParams = _generateBlockData?.GetParameters();
        if (_generateBlockData?.ReturnType != typeof(Dictionary<string, int>) || gbParams is not { Length: 3 }
            || gbParams[0].ParameterType != typeof(ICoreServerAPI) || gbParams[1].ParameterType != typeof(BlockPos)
            || !typeof(IList).IsAssignableFrom(gbParams[2].ParameterType) || gbParams[2].ParameterType.IsAbstract)
            return "ProspectingSystem.GenerateBlockData(api, pos, debugMessages) is not as expected";
        _debugListType = gbParams[2].ParameterType;
        _generateReadings = AccessTools.Method(system, "generateReadigs");
        if (_generateReadings == null || !_generateReadings.IsStatic) return "ProspectingSystem.generateReadigs is gone";
        _reprospect = AccessTools.Method(system, "ReprospectTask") is { } task ? AccessTools.AsyncMoveNext(task) : null;
        if (_reprospect == null) return "ProspectingSystem.ReprospectTask is no longer an async method";

        _configInstance = AccessTools.Property(config, "Instance");
        _newDensityMode = AccessTools.Field(config, "NewDensityMode");
        _newDensityDmg = AccessTools.Field(config, "NewDensityDmg");
        if (_configInstance?.GetGetMethod()?.IsStatic != true || _newDensityMode?.FieldType != typeof(bool) || _newDensityDmg?.FieldType != typeof(int))
            return "its ModConfig has no static Instance with NewDensityMode and NewDensityDmg";

        try
        {
            if (Count(_generateBlockData, SwapWalkRadius) is var walk && walk != WalkRadiusSwaps)
                return $"GenerateBlockData loads the radius {walk} times, not {WalkRadiusSwaps}";
            if (Count(_generateBlockData, SwapAccessor) is var acc && acc != AccessorSwaps)
                return $"GenerateBlockData reads World.BlockAccessor {acc} times, not {AccessorSwaps}";
            if (Count(_generateReadings, SwapZoneArea) is var zone && zone != ZoneAreaSwaps)
                return $"generateReadigs loads the zone area {zone} times, not {ZoneAreaSwaps}";
            if (Count(_reprospect, SwapOffsets) is var arrays && arrays != OffsetArraySwaps)
                return $"ReprospectTask builds its chunk offsets {arrays} times, not {OffsetArraySwaps}";
        }
        catch (Exception e)
        {
            return "its IL could not be read: " + e.Message;
        }
        return null;
    }

    private static int Count(MethodBase method, System.Func<List<CodeInstruction>, int> rewrite) =>
        rewrite(PatchProcessor.GetOriginalInstructions(method));

    private static void Patch(Harmony harmony)
    {
        harmony.Patch(_generateBlockData,
            prefix: new HarmonyMethod(typeof(WideDensityProspectingSystem), nameof(HandedCountsPrefix)),
            transpiler: new HarmonyMethod(typeof(WideDensityProspectingSystem), nameof(GenerateBlockDataTranspiler)));
        harmony.Patch(_generateReadings, transpiler: new HarmonyMethod(typeof(WideDensityProspectingSystem), nameof(GenerateReadingsTranspiler)));
        harmony.Patch(_reprospect, transpiler: new HarmonyMethod(typeof(WideDensityProspectingSystem), nameof(ReprospectTranspiler)));
        harmony.Patch(_probeDensity, prefix: new HarmonyMethod(typeof(WideDensityProspectingSystem), nameof(ProbeDensityPrefix)));
    }

    // --- The rewrites: each returns how many it made, and the transpilers insist on the count. ---

    /// <summary>The walk's corners: <c>32</c> becomes <see cref="Radius"/>.</summary>
    internal static int SwapWalkRadius(List<CodeInstruction> code)
    {
        int n = 0;
        foreach (var ci in code)
        {
            if (!ci.LoadsConstant(DensityProspecting.ShippedRadius)) continue;
            ci.opcode = OpCodes.Call;
            ci.operand = AccessTools.PropertyGetter(typeof(WideDensityProspectingSystem), nameof(Radius));
            n++;
        }
        return n;
    }

    private static readonly MethodInfo WorldBlockAccessor = AccessTools.PropertyGetter(typeof(IWorldAccessor), nameof(IWorldAccessor.BlockAccessor));

    /// <summary><c>World.BlockAccessor</c> becomes <see cref="Accessor"/>.</summary>
    internal static int SwapAccessor(List<CodeInstruction> code)
    {
        int n = 0;
        foreach (var ci in code)
        {
            if ((ci.opcode != OpCodes.Callvirt && ci.opcode != OpCodes.Call) || !Equals(ci.operand, WorldBlockAccessor)) continue;
            ci.opcode = OpCodes.Call;
            ci.operand = AccessTools.Method(typeof(WideDensityProspectingSystem), nameof(Accessor));
            n++;
        }
        return n;
    }

    /// <summary>The zone area <c>4096</c> becomes <see cref="ZoneArea"/>.</summary>
    internal static int SwapZoneArea(List<CodeInstruction> code)
    {
        int n = 0;
        foreach (var ci in code)
        {
            if (!ci.LoadsConstant(DensityProspecting.ZoneArea(DensityProspecting.ShippedRadius))) continue;
            ci.opcode = OpCodes.Call;
            ci.operand = AccessTools.Method(typeof(WideDensityProspectingSystem), nameof(ZoneArea));
            n++;
        }
        return n;
    }

    /// <summary><c>new int[3] { -32, 0, 32 }</c> (<c>ldc.i4.3; newarr int; dup; ldc.i4.0; ldc.i4.s -32;
    /// stelem.i4; dup; ldc.i4.2; ldc.i4.s 32; stelem.i4</c>: the zero element is left at its default)
    /// becomes a call to <see cref="ReprospectOffsets"/>.</summary>
    internal static int SwapOffsets(List<CodeInstruction> code)
    {
        int n = 0;
        for (int i = 0; i + 9 < code.Count; i++)
        {
            if (!(code[i].LoadsConstant(3) && code[i + 1].opcode == OpCodes.Newarr && Equals(code[i + 1].operand, typeof(int))
                  && code[i + 2].opcode == OpCodes.Dup && code[i + 3].LoadsConstant(0) && code[i + 4].LoadsConstant(-DensityProspecting.ShippedRadius)
                  && code[i + 5].opcode == OpCodes.Stelem_I4 && code[i + 6].opcode == OpCodes.Dup && code[i + 7].LoadsConstant(2)
                  && code[i + 8].LoadsConstant(DensityProspecting.ShippedRadius) && code[i + 9].opcode == OpCodes.Stelem_I4))
                continue;
            if (code.Skip(i + 1).Take(9).Any(c => c.labels.Count > 0 || c.blocks.Count > 0)) continue;
            code[i].opcode = OpCodes.Call;
            code[i].operand = AccessTools.Method(typeof(WideDensityProspectingSystem), nameof(ReprospectOffsets));
            code.RemoveRange(i + 1, 9);
            n++;
        }
        return n;
    }

    private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions, System.Func<List<CodeInstruction>, int> rewrite, int expected, string what)
    {
        var code = instructions.ToList();
        int n = rewrite(code);
        if (n != expected)
            throw new InvalidOperationException($"{what}: {n} rewrites, expected {expected}");
        return code;
    }

    private static IEnumerable<CodeInstruction> GenerateBlockDataTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = Rewrite(instructions, SwapWalkRadius, WalkRadiusSwaps, "GenerateBlockData radius").ToList();
        return Rewrite(code, SwapAccessor, AccessorSwaps, "GenerateBlockData accessor");
    }

    private static IEnumerable<CodeInstruction> GenerateReadingsTranspiler(IEnumerable<CodeInstruction> instructions) =>
        Rewrite(instructions, SwapZoneArea, ZoneAreaSwaps, "generateReadigs zone");

    private static IEnumerable<CodeInstruction> ReprospectTranspiler(IEnumerable<CodeInstruction> instructions) =>
        Rewrite(instructions, SwapOffsets, OffsetArraySwaps, "ReprospectTask offsets");

    /// <summary>The block accessor <c>GenerateBlockData</c> walks with: the task's own on a walk's
    /// thread, the world's anywhere else.</summary>
    public static IBlockAccessor Accessor(IWorldAccessor world) => _walkAccessor ?? world.BlockAccessor;

    /// <summary>The area a reading is normalised over.</summary>
    public static int ZoneArea() => DensityProspecting.ZoneArea(Radius);

    /// <summary>The chunk offsets reprospect loads around a reading (a fresh array, as BetterEr's).</summary>
    public static int[] ReprospectOffsets() => DensityProspecting.ChunkOffsets(Radius);

    // --- The walk off the main thread. ---

    // GenerateBlockData(api, pos, debugMessages): while BetterEr's density mode runs on counts handed
    // back, answer those.
    private static bool HandedCountsPrefix(object[] __args, ref Dictionary<string, int> __result)
    {
        if (_handedCounts is not { } counts) return true;
        if (__args.Length > 2 && __args[2] is IList debug && _handedDebug is { } handed)
            foreach (var message in handed)
                debug.Add(message);
        __result = counts;
        return false;
    }

    // ItemBetterErProspectingPick.ProbeDensity(world, serverPlayer, itemslot, blockSel), main thread.
    private static bool ProbeDensityPrefix(Item __instance, IServerPlayer __1, BlockSelection __3, ref int __result)
    {
        var sapi = _sapi;
        if (sapi == null || __1 == null || __3?.Position == null || ShuttingDown(sapi)) return true;
        object? config = _configInstance!.GetValue(null);
        if (config == null || !(bool)_newDensityMode!.GetValue(config)!) return true;

        var player = __1;
        string uid = player.PlayerUID;
        if (!Gate.TryBegin(uid))
        {
            player.SendMessage(GlobalConstants.InfoLogChatGroup, Lang.GetL(player.LanguageCode, BusyLangKey), EnumChatType.Notification);
            __result = 0;
            return false;
        }
        __result = (int)_newDensityDmg!.GetValue(config)!;

        var selection = __3.Clone();
        var pos = selection.Position.Copy();
        var accessor = sapi.World.GetBlockAccessor(false, false, false);
        var pick = __instance;
        try
        {
            Task.Run(() => Walk(sapi, pick, uid, selection, pos, accessor));
        }
        catch (Exception)
        {
            Gate.End(uid);
            throw;
        }
        return false;
    }

    private static void Walk(ICoreServerAPI sapi, Item pick, string uid, BlockSelection selection, BlockPos pos, IBlockAccessor accessor)
    {
        try
        {
            if (_sapi != sapi || ShuttingDown(sapi))
            {
                Gate.End(uid);
                return;
            }
            var watch = Stopwatch.StartNew();
            var debug = (IList)Activator.CreateInstance(_debugListType!)!;
            Dictionary<string, int> counts;
            _walkAccessor = accessor;
            try
            {
                counts = (Dictionary<string, int>)_generateBlockData!.Invoke(null, [sapi, pos, debug])!;
            }
            finally
            {
                _walkAccessor = null;
            }
            var walk = new DensityWalk(uid, pos, counts, Environment.CurrentManagedThreadId, watch.Elapsed.TotalMilliseconds);
            if (_sapi != sapi || ShuttingDown(sapi))
            {
                Gate.End(uid);
                return;
            }
            sapi.Event.EnqueueMainThreadTask(() => Deliver(sapi, pick, selection, walk, debug), "seraphhorizons-densityprospecting");
        }
        catch (Exception e)
        {
            Gate.End(uid);
            if (_sapi != sapi || ShuttingDown(sapi)) return;
            try
            {
                sapi.Logger.Error("[seraphhorizons] Wide density prospecting: the walk at {0} failed: {1}", pos, (e as TargetInvocationException)?.InnerException ?? e);
            }
            catch (Exception)
            {
                // The server is going away under us.
            }
        }
    }

    // Main thread: BetterEr's own density mode, on the counts already made.
    private static void Deliver(ICoreServerAPI sapi, Item pick, BlockSelection selection, DensityWalk walk, IList debug)
    {
        try
        {
            if (_sapi != sapi || ShuttingDown(sapi)) return;
            if (sapi.World.PlayerByUid(walk.PlayerUid) is not IServerPlayer online || online.ConnectionState != EnumClientState.Playing)
                return;
            Delivered?.Invoke(walk);
            _handedCounts = walk.Counts as Dictionary<string, int> ?? new Dictionary<string, int>(walk.Counts);
            _handedDebug = debug;
            try
            {
                _probeBlockDensityMode!.Invoke(pick, [online, selection]);
            }
            finally
            {
                _handedCounts = null;
                _handedDebug = null;
            }
        }
        catch (Exception e)
        {
            sapi.Logger.Error("[seraphhorizons] Wide density prospecting: the reading at {0} failed: {1}", walk.Pos, (e as TargetInvocationException)?.InnerException ?? e);
        }
        finally
        {
            Gate.End(walk.PlayerUid);
        }
    }

    // A thread-pool exception after the server's Dispose kills the process (ProPickShutdownGuard), so
    // nothing starts or continues once it is stopping.
    private static bool ShuttingDown(ICoreServerAPI sapi)
    {
        try
        {
            return sapi.Server.CurrentRunPhase >= EnumServerRunPhase.Shutdown || sapi.World.Logger is null;
        }
        catch (Exception)
        {
            return true;
        }
    }
}
