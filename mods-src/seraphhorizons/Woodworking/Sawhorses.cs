using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace SeraphHorizons.Mod.Woodworking;

/// <summary>
/// Logging Expanded's three sawhorses (primitive <c>sawhorse</c>, <c>sawhorsestandard</c>,
/// <c>sawhorseadvanced</c>), the only sawhorses once Immersive Woodworking's is retired, take on
/// what that one did (<see cref="SawhorseWorks"/> says which tool set does what):
///
/// - Shift + saw makes support beams instead of boards: 2 / 2 / 3 per log by tier, of the loaded
///   log's wood, with the boards' hold, sound and saw wear (one log, 1 durability). The pack has
///   no other beam source before Immersive Woodworking's machines (its settings strip the grid
///   recipe), so this works on the primitive sawhorse. The beam is <c>supportbeam-&lt;wood&gt;</c>
///   in the log's domain, else the game's (every vanilla wood and <c>aged</c> has one); a wood
///   with none is sawn into boards, with one warning, and <see cref="AssetsFinalize"/> lists any
///   loadable wood without a beam on the server.
/// - Immersive Woodworking's bark spud debarks, as the axe with a hammer in the offhand does at
///   that tier (the advanced sawhorse's 3 per 2 stored included), costing the spud Immersive
///   Woodworking's <c>DebarkDurabilityPerLog</c> per log.
/// - Debarking with either drops Immersive Woodworking's bark for the wood
///   (<see cref="BarkDrops"/>), one roll per log taken off the sawhorse
///   (<see cref="SawhorseWorks.LogsTaken"/>), thrown toward the player with the debarked logs.
///
/// Patches, by name (Logging Expanded is not referenced at build time), all from <see cref="Start"/>:
/// - server: a prefix on <c>BlockSawhorse.ProcessWithTool</c> and on
///   <c>BlockSawhorseAdvanced.ProcessWithTool</c> (which overrides it without calling it; the
///   standard sawhorse inherits the primitive's) does the beams and the spud, and records the axe
///   and hammer's debark for a postfix that drops its bark. It runs when a hold completes, on the
///   server only.
/// - client: a postfix on <c>BlockSawhorse.GetPlacedBlockInteractionHelp</c> adds the beam and
///   spud lines. Logging Expanded's own lines stay as they are: none is wrong now.
/// - both sides: the spud has no tool type, and the hold only starts for a tool, so a postfix on
///   the hold's eligibility (a lambda compiled into <c>BlockWorkstation+&lt;&gt;c</c>, found by its
///   signature) lets the spud in on a loaded sawhorse. The client starts the hold and draws its
///   progress, the server completes it.
///
/// That lambda's name is the compiler's, so it is the likeliest thing to break, and only the spud
/// needs it. Losing it does not fail the tweak: the spud does nothing on a sawhorse (and its help
/// line is not shown), with one warning, and everything else runs, axe and hammer debarking with
/// bark included. Turning the whole tweak off over it would bring back Immersive Woodworking's
/// sawhorse and the splitting logs for the sake of one of two debarking tools.
///
/// Logging Expanded has no chisel debark on a sawhorse (its <c>wi-sawhorse-debark-chisel</c> line
/// is never used): its chisel only made a debarked splitting log, which the splitting block
/// replaces. Nothing to keep there.
/// </summary>
public sealed class Sawhorses : WoodworkingPart
{
    public const string ProcessMethod = "ProcessWithTool";
    public const string HelpMethod = "GetPlacedBlockInteractionHelp";

    private static Type? _sawhorse, _standard, _advanced, _workstationEntity;
    private static MethodInfo? _process, _processAdvanced, _help, _eligible, _takeOne, _debarkedLog;
    private static PropertyInfo? _inventory, _woodType, _logCount, _loadedFromLogs, _loadedFromFirewood, _debarkYield;
    private static PropertyInfo? _advancedDebarkYield;
    private static WoodworkingMods? _mods;
    private static string? _spudMissing;
    private static readonly HashSet<string> WarnedNoBeam = [];

    public override string Name => "the sawhorses";

    public override string? Bind(ICoreAPI api, WoodworkingMods mods)
    {
        _mods = mods;
        _sawhorse = WoodworkingMods.LeType("BlockSawhorse");
        _standard = WoodworkingMods.LeType("BlockSawhorseStandard");
        _advanced = WoodworkingMods.LeType("BlockSawhorseAdvanced");
        _workstationEntity = WoodworkingMods.LeType("BEWorkstation");
        var workstation = WoodworkingMods.LeType("BlockWorkstation");
        var trunkInventory = WoodworkingMods.LeType("TreeTrunkInventory");
        if (_sawhorse == null || _standard == null || _advanced == null || _workstationEntity == null
            || workstation == null || trunkInventory == null || !typeof(Block).IsAssignableFrom(_sawhorse)
            || !_sawhorse.IsAssignableFrom(_standard) || !_sawhorse.IsAssignableFrom(_advanced)
            || !typeof(InventoryBase).IsAssignableFrom(trunkInventory))
            return "its sawhorse, workstation or trunk inventory classes are missing";

        Type[] process = [typeof(IWorldAccessor), typeof(IPlayer), typeof(BlockSelection), _workstationEntity, typeof(ItemSlot), typeof(ItemStack)];
        _process = AccessTools.DeclaredMethod(_sawhorse, ProcessMethod, process);
        _processAdvanced = AccessTools.DeclaredMethod(_advanced, ProcessMethod, process);
        if (_process?.ReturnType != typeof(bool) || _processAdvanced?.ReturnType != typeof(bool))
            return $"BlockSawhorse or BlockSawhorseAdvanced.{ProcessMethod} is not as expected";
        _help = AccessTools.DeclaredMethod(_sawhorse, HelpMethod, [typeof(IWorldAccessor), typeof(BlockSelection), typeof(IPlayer)]);
        if (_help?.ReturnType != typeof(WorldInteraction[]))
            return $"BlockSawhorse.{HelpMethod} is missing";

        _inventory = AccessTools.DeclaredProperty(_workstationEntity, "Inventory");
        _takeOne = AccessTools.DeclaredMethod(_workstationEntity, "TakeOne", Type.EmptyTypes);
        _woodType = AccessTools.DeclaredProperty(trunkInventory, "WoodType");
        _logCount = AccessTools.DeclaredProperty(trunkInventory, "LogCount");
        _loadedFromLogs = AccessTools.DeclaredProperty(trunkInventory, "LoadedFromLogs");
        _loadedFromFirewood = AccessTools.DeclaredProperty(trunkInventory, "LoadedFromFirewood");
        if (_inventory?.PropertyType != trunkInventory || _takeOne == null || _woodType?.PropertyType != typeof(string)
            || _logCount?.PropertyType != typeof(int) || _loadedFromLogs?.PropertyType != typeof(bool)
            || _loadedFromFirewood?.PropertyType != typeof(bool))
            return "BEWorkstation.Inventory or TakeOne, or its trunk inventory, is not as expected";
        _debarkYield = AccessTools.DeclaredProperty(_sawhorse, "DebarkYield");
        _debarkedLog = AccessTools.DeclaredMethod(_sawhorse, "GetDebarkedLogOutput", [typeof(IWorldAccessor), typeof(string), typeof(int)]);
        _advancedDebarkYield = mods.LeSetting<int>("AdvancedSawhorseDebarkYield");
        if (_debarkYield?.PropertyType != typeof(int) || _debarkedLog is not { IsStatic: true }
            || _debarkedLog.ReturnType != typeof(ItemStack) || _advancedDebarkYield == null)
            return "BlockSawhorse's debark yield or output is not as expected";

        // The hold's eligibility: the one bool(IWorldAccessor, IPlayer, BlockSelection) lambda.
        var lambdas = AccessTools.Inner(workstation, "<>c") is { } closure
            ? AccessTools.GetDeclaredMethods(closure).Where(m => m.ReturnType == typeof(bool)
                && m.GetParameters().Select(p => p.ParameterType)
                    .SequenceEqual([typeof(IWorldAccessor), typeof(IPlayer), typeof(BlockSelection)])).ToList()
            : [];
        _eligible = lambdas.Count == 1 ? lambdas[0] : null;
        _spudMissing = _eligible == null
            ? $"BlockWorkstation's hold check ({lambdas.Count} lambdas of its shape, not 1), so the bark spud does nothing on a sawhorse"
            : null;
        return BarkDrops.Bind(mods);
    }

    public override void Start(ICoreAPI api, Harmony harmony)
    {
        if (_spudMissing != null)
            api.Logger.Warning($"[seraphhorizons] Unified woodworking: Logging Expanded changed {_spudMissing}");
        else
            harmony.Patch(_eligible, postfix: new HarmonyMethod(typeof(Sawhorses), nameof(EligiblePostfix)));
        if (api.Side == EnumAppSide.Client)
        {
            harmony.Patch(_help, postfix: new HarmonyMethod(typeof(Sawhorses), nameof(HelpPostfix)));
            return;
        }
        var prefix = new HarmonyMethod(typeof(Sawhorses), nameof(ProcessPrefix));
        var postfix = new HarmonyMethod(typeof(Sawhorses), nameof(ProcessPostfix));
        harmony.Patch(_process, prefix: prefix, postfix: postfix);
        harmony.Patch(_processAdvanced, prefix: prefix, postfix: postfix);
    }

    /// <summary>Server: lists, in one notification, every wood a sawhorse takes (an upright placed log,
    /// <c>log-placed-&lt;wood&gt;-ud</c>) that has no support beam, so Shift + saw makes boards of it.</summary>
    public override void AssetsFinalize(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Server)
            return;
        var missing = new SortedSet<string>();
        foreach (var block in api.World.Blocks)
            if (block?.Code is { } code && code.Path.StartsWith("log-placed-", StringComparison.Ordinal)
                && code.Path.EndsWith("-ud", StringComparison.Ordinal)
                && SawhorseWorks.Species(code.Path["log-placed-".Length..^"-ud".Length]) is { } wood
                && BeamBlock(api.World, code.Domain, wood) == null)
                missing.Add($"{code.Domain}:{wood}");
        // A notification: the pack has such woods (Material Needs' darkaged), and nothing is wrong
        // with a wood that saws into boards only.
        if (missing.Count > 0)
            api.Logger.Notification("[seraphhorizons] Unified woodworking: no support beam for "
                               + $"{string.Join(", ", missing)}; a sawhorse saws boards of them with Shift too");
    }

    // The member statics stay: in singleplayer the other side's patches may still run after this
    // side disposes, and the next Bind sets them again (to the same members).
    public override void Dispose() => WarnedNoBeam.Clear();

    /// <summary>A debark by axe and hammer that Logging Expanded does: what the postfix needs to
    /// drop its bark.</summary>
    public sealed record BarkState(string? Species, int LogsBefore, double ChanceMultiplier);

    /// <summary>The hold also starts, and completes, for a bark spud on a loaded sawhorse.
    /// Arguments by position: the lambda's parameter names are the compiler's.</summary>
    public static void EligiblePostfix(ref bool __result, IWorldAccessor __0, IPlayer __1, BlockSelection __2)
    {
        if (__result || __2?.Position == null
            || !BarkDrops.IsSpud(__1?.InventoryManager?.ActiveHotbarSlot?.Itemstack)
            || __0.BlockAccessor.GetBlockEntity(__2.Position) is not { } be || !_workstationEntity!.IsInstanceOfType(be)
            || !_sawhorse!.IsInstanceOfType(be.Block) || Inventory(be) is not { Empty: false })
            return;
        __result = true;
    }

    /// <summary>Beams (Shift + saw) and the spud's debark in place of Logging Expanded's work; for
    /// its own axe and hammer debark, notes what the postfix needs for the bark. Arguments by
    /// position: (world, byPlayer, blockSel, be, toolSlot, toolStack).</summary>
    public static bool ProcessPrefix(Block __instance, IWorldAccessor __0, IPlayer __1, BlockSelection __2,
        BlockEntity __3, ItemSlot __4, ItemStack __5, ref bool __result, out BarkState? __state)
    {
        __state = null;
        IWorldAccessor world = __0;
        IPlayer player = __1;
        if (world.Side != EnumAppSide.Server || Inventory(__3) is not { Empty: false } inventory)
            return true;
        ItemStack? offHand = player.Entity.LeftHandItemSlot?.Itemstack;
        var work = SawhorseWorks.Classify(ToolOf(__5), offHand?.Collectible?.Tool == EnumTool.Hammer,
            player.Entity.Controls.ShiftKey);
        var tier = TierOf(__instance);
        string? species = SawhorseWorks.Species((string?)_woodType!.GetValue(inventory));
        int before = (int)_logCount!.GetValue(inventory)!;
        switch (work)
        {
            case SawhorseWork.Beams when !(bool)_loadedFromFirewood!.GetValue(inventory)!:
                if (Beams(world, inventory, species, tier.BeamsPerLog()) is not { } beams)
                    return true;
                __result = Produce(world, player, __2, __3, __4, beams, 1, "sounds/tool/saw", 1);
                return false;
            case SawhorseWork.SpudDebark when _spudMissing == null:
                __result = SpudDebark(__instance, world, player, __2, __3, __4, inventory, tier, before);
                if (__result)
                    DropBark(world, player, __2.Position, species, SawhorseWorks.LogsTaken(before, LogCount(__3)),
                        BarkDrops.ChanceMultiplier(world.Api, __5, offHand));
                return false;
            case SawhorseWork.AxeAndHammerDebark:
                __state = new BarkState(species, before, BarkDrops.ChanceMultiplier(world.Api, __5, offHand));
                return true;
            default:
                return true;
        }
    }

    /// <summary>Drops the bark of the logs Logging Expanded's axe and hammer debark took.</summary>
    public static void ProcessPostfix(bool __result, IWorldAccessor __0, IPlayer __1, BlockSelection __2,
        BlockEntity __3, BarkState? __state)
    {
        if (__state == null || !__result)
            return;
        DropBark(__0, __1, __2.Position, __state.Species, SawhorseWorks.LogsTaken(__state.LogsBefore, LogCount(__3)),
            __state.ChanceMultiplier);
    }

    /// <summary>Adds the beam and spud lines to Logging Expanded's (cached per block, as its own).</summary>
    public static void HelpPostfix(Block __instance, IWorldAccessor __0, ref WorldInteraction[] __result)
    {
        IWorldAccessor world = __0;
        var tier = TierOf(__instance);
        var lines = ObjectCacheUtil.GetOrCreate(world.Api, "seraphhorizons-wi-" + __instance.Code, () =>
        {
            var saws = new List<ItemStack>();
            var spuds = new List<ItemStack>();
            foreach (var collectible in world.Collectibles)
            {
                if (collectible?.Code == null)
                    continue;
                if (collectible.Tool == EnumTool.Saw)
                    saws.Add(new ItemStack(collectible));
                if (_spudMissing == null && collectible is Item && BarkDrops.IsSpud(new ItemStack(collectible)))
                    spuds.Add(new ItemStack(collectible));
            }
            var help = new List<WorldInteraction>
            {
                new()
                {
                    ActionLangCode = tier.BeamsHelpKey(),
                    MouseButton = EnumMouseButton.Right,
                    HotKeyCode = "shift",
                    Itemstacks = saws.ToArray(),
                },
            };
            if (spuds.Count > 0)
                help.Add(new WorldInteraction
                {
                    ActionLangCode = tier.SpudHelpKey(),
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = spuds.ToArray(),
                });
            return help.ToArray();
        });
        __result = (__result ?? []).Append(lines);
    }

    private static SawhorseTier TierOf(Block block) =>
        _advanced!.IsInstanceOfType(block) ? SawhorseTier.Advanced
        : _standard!.IsInstanceOfType(block) ? SawhorseTier.Standard
        : SawhorseTier.Primitive;

    private static SawhorseTool ToolOf(ItemStack? stack) =>
        BarkDrops.IsSpud(stack) ? SawhorseTool.BarkSpud
        : stack?.Collectible?.Tool switch
        {
            EnumTool.Axe => SawhorseTool.Axe,
            EnumTool.Saw => SawhorseTool.Saw,
            _ => SawhorseTool.Other,
        };

    private static InventoryBase? Inventory(BlockEntity? be) =>
        be != null && _workstationEntity!.IsInstanceOfType(be) ? _inventory!.GetValue(be) as InventoryBase : null;

    private static int LogCount(BlockEntity be) =>
        Inventory(be) is { Empty: false } inventory ? (int)_logCount!.GetValue(inventory)! : 0;

    /// <summary>The spud's debark, Logging Expanded's axe and hammer one at that tier. Primitive
    /// and standard (<c>BlockSawhorse.ProduceDebarkedLog</c>): one log for its <c>DebarkYield</c>
    /// debarked logs (1 by default; 1 for the last log). Advanced (<c>BlockSawhorseAdvanced</c>):
    /// loose logs 1 for 1; a trunk 2 for <c>AdvancedSawhorseDebarkYield</c> (3) while it has 2, else
    /// 1 for 1. The spud loses <see cref="BarkDrops.DurabilityPerLog"/> per log.</summary>
    private static bool SpudDebark(Block sawhorse, IWorldAccessor world, IPlayer player, BlockSelection sel,
        BlockEntity be, ItemSlot toolSlot, InventoryBase inventory, SawhorseTier tier, int logs)
    {
        string? woodType = (string?)_woodType!.GetValue(inventory);
        int take = 1, quantity;
        if (tier != SawhorseTier.Advanced)
            quantity = logs < 2 ? 1 : (int)_debarkYield!.GetValue(sawhorse)!;
        else if ((bool)_loadedFromLogs!.GetValue(inventory)! || logs < 2)
            quantity = 1;
        else
        {
            take = 2;
            quantity = (int)_advancedDebarkYield!.GetValue(_mods!.LeConfig)!;
        }
        var output = (ItemStack?)_debarkedLog!.Invoke(null, [world, woodType, quantity]);
        return Produce(world, player, sel, be, toolSlot, output, take, "sounds/block/wood",
            BarkDrops.DurabilityPerLog(world.Api) * take);
    }

    /// <summary>Logging Expanded's <c>BlockSawhorse.ProduceOne</c> / <c>ProduceTwoConsumeTwo</c>,
    /// server side: takes <paramref name="take"/> logs, throws <paramref name="output"/> toward the
    /// player, wears the tool by <paramref name="damage"/> and plays <paramref name="sound"/>.
    /// False, with nothing taken, if there is no output.</summary>
    private static bool Produce(IWorldAccessor world, IPlayer player, BlockSelection sel, BlockEntity be,
        ItemSlot toolSlot, ItemStack? output, int take, string sound, int damage)
    {
        if (output == null)
            return false;
        for (int i = 0; i < take; i++)
            _takeOne!.Invoke(be, []);
        be.MarkDirty(redrawOnClient: true);
        SpawnTowardPlayer(world, player, sel.Position, output);
        if (damage > 0)
            toolSlot.Itemstack?.Collectible?.DamageItem(world, player.Entity, toolSlot, damage);
        world.PlaySoundAt(new AssetLocation(sound), sel.Position, 0.0, player, randomizePitch: true, 16f);
        return true;
    }

    /// <summary><paramref name="count"/> support beams of the loaded wood, or null (with one
    /// warning per wood) when it has none.</summary>
    private static ItemStack? Beams(IWorldAccessor world, InventoryBase inventory, string? species, int count)
    {
        string domain = inventory[0].Itemstack?.Collectible?.Code?.Domain ?? GlobalConstants.DefaultDomain;
        if (species != null && BeamBlock(world, domain, species) is { } beam)
            return new ItemStack(beam, count);
        if (WarnedNoBeam.Add($"{domain}:{species}"))
            world.Logger.Warning($"[seraphhorizons] Unified woodworking: no support beam for {domain}:{species} logs, "
                                 + "so a sawhorse saws them into boards");
        return null;
    }

    /// <summary><c>supportbeam-&lt;wood&gt;</c> in the log's domain, else the game's (as Logging
    /// Expanded's splitting log found its beams, by that code in any domain).</summary>
    private static Block? BeamBlock(IWorldAccessor world, string domain, string wood)
    {
        foreach (string d in domain == GlobalConstants.DefaultDomain ? [domain] : (string[])[domain, GlobalConstants.DefaultDomain])
            if (world.GetBlock(new AssetLocation(d, "supportbeam-" + wood)) is { Id: not 0 } beam)
                return beam;
        return null;
    }

    private static void DropBark(IWorldAccessor world, IPlayer player, BlockPos pos, string? species, int logs,
        double chanceMultiplier)
    {
        for (int i = 0; i < logs; i++)
            if (BarkDrops.Roll(world, species, chanceMultiplier) is { } bark)
                SpawnTowardPlayer(world, player, pos, bark);
    }

    /// <summary>Logging Expanded's <c>BlockWorkstation.SpawnTowardPlayer</c>: from above the
    /// sawhorse, gently toward the player's middle.</summary>
    private static void SpawnTowardPlayer(IWorldAccessor world, IPlayer player, BlockPos from, ItemStack stack)
    {
        Vec3d start = from.ToVec3d().Add(0.5, 1.2, 0.5);
        Vec3d target = player.Entity.Pos.XYZ.Add(0.0, player.Entity.LocalEyePos.Y * 0.5, 0.0);
        world.SpawnItemEntity(stack, start, target.Sub(start).Normalize().Mul(0.15));
    }
}
