using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod;

/// <summary>
/// Logging Expanded: felling a tree costs the axe a flat <see cref="FellingWearConfig.ThinTree"/>
/// (4), or <see cref="FellingWearConfig.ThickTree"/> (8) for a tree with a two-by-two trunk (the
/// game's log sections: the redwood), in place of the game's one durability per log block, when
/// the felling leaves a trunk. The trunk's logs are then worked at the stations and the machines
/// at their own costs, about one per log, so the whole chain costs about what the game's felling
/// alone did (<see cref="FellingWearRules"/>).
///
/// The game's axe (<c>ItemAxe.OnBlockBrokenWith</c>) breaks every block of the tree and calls
/// <c>DamageItem</c> once per wood block. Logging Expanded fells in the server's <c>BreakBlock</c>
/// event, which runs first: it marks the tree's blocks to drop nothing, throws the trunk, and
/// raises its public <c>FellingListener.OnTreeFelled</c> (the player, the stump, the wood and the
/// log count) when it made one. This tweak (server side, by name; Logging Expanded is not
/// referenced at build time):
/// - hears <c>OnTreeFelled</c> and notes the player and the stump;
/// - a prefix on <c>ItemAxe.OnBlockBrokenWith</c> for that player, when the tree it is about to
///   break holds that stump, takes the note, decides thick or thin from the tree's blocks, and
///   opens a window in which a prefix on <c>CollectibleObject.DamageItem</c> skips every hit on the
///   axe's slot;
/// - a finalizer closes the window and charges the flat cost, with the game's own
///   <c>DamageItem</c>, so an axe at or below it shatters after the tree is down, the way it
///   shatters on any last block. The game's own rule that an axe damaged by nothing
///   (<c>damagedby</c> without <c>blockbreaking</c>) loses nothing is kept.
/// A felling that leaves no trunk (Logging Expanded's <c>MinLogsForTrunk</c>, a wood it does not
/// know, a tree too big to fell in one swing) costs what the game charges. The game's client
/// predicts the per-log loss on its own copy of the axe, and the server's figure replaces it as the
/// slot syncs.
///
/// With the switch off, without Logging Expanded, or with its callback not as expected (one
/// warning), nothing is patched.
/// </summary>
public static class FellingWear
{
    public const string ModId = "loggingmod";
    public const string ListenerType = "LoggingMod.FellingListener";
    public const string CallbackField = "OnTreeFelled";

    private static FieldInfo? _callback;
    private static Action<IServerPlayer, BlockPos, string, int>? _handler;
    private static FellingWearConfig _config = FellingWearConfig.Defaults;

    // The server thread's, from Logging Expanded's felling to the axe's break of the same block.
    [ThreadStatic] private static string? _felledBy;
    [ThreadStatic] private static BlockPos? _stump;
    // Set while the axe breaks a tree whose trunk Logging Expanded made.
    [ThreadStatic] private static ItemSlot? _sparedSlot;
    [ThreadStatic] private static bool _thick;

    public static bool Applies(ICoreAPI api) => api.ModLoader.IsModEnabled(ModId);

    /// <summary>Whether the patches are in.</summary>
    public static bool Patched { get; private set; }

    /// <summary>Finds Logging Expanded's felled callback. False, with one warning, when it is not
    /// as expected.</summary>
    public static bool Bind(ILogger logger)
    {
        var listener = AccessTools.TypeByName(ListenerType);
        _callback = listener == null ? null : AccessTools.Field(listener, CallbackField);
        if (_callback == null || !_callback.IsStatic || _callback.FieldType != typeof(Action<IServerPlayer, BlockPos, string, int>))
        {
            _callback = null;
            logger.Warning($"[seraphhorizons] {ListenerType} has no static {CallbackField} of Action<IServerPlayer, BlockPos, string, int>; "
                           + "Logging Expanded changed, so felling a tree costs the axe one durability per log");
            return false;
        }
        return true;
    }

    /// <summary>Server side. Hears the callback and patches the axe and the damage.</summary>
    public static void Patch(Harmony harmony, ICoreServerAPI api, FellingWearConfig config)
    {
        foreach (var fix in config.Sanitise())
            api.Logger.Warning($"[seraphhorizons] Flat felling wear: ModConfig/{SeraphHorizonsSystem.ConfigFile}, FlatFellingWearSettings: {fix}");
        _config = config;
        _handler = OnTreeFelled;
        _callback!.SetValue(null, Delegate.Combine((Delegate?)_callback.GetValue(null), _handler));
        harmony.Patch(AccessTools.Method(typeof(ItemAxe), nameof(ItemAxe.OnBlockBrokenWith)),
            prefix: new HarmonyMethod(typeof(FellingWear), nameof(BreakPrefix)),
            finalizer: new HarmonyMethod(typeof(FellingWear), nameof(BreakFinalizer)));
        harmony.Patch(AccessTools.Method(typeof(CollectibleObject), nameof(CollectibleObject.DamageItem)),
            prefix: new HarmonyMethod(typeof(FellingWear), nameof(DamagePrefix)));
        Patched = true;
    }

    /// <summary>Stops hearing the callback (the patches go with the Harmony id).</summary>
    public static void Unbind()
    {
        if (_callback != null && _handler != null)
            _callback.SetValue(null, Delegate.Remove((Delegate?)_callback.GetValue(null), _handler));
        _handler = null;
        _felledBy = null;
        _stump = null;
        _sparedSlot = null;
        Patched = false;
    }

    private static void OnTreeFelled(IServerPlayer player, BlockPos stump, string wood, int logs)
    {
        _felledBy = player.PlayerUID;
        _stump = stump.Copy();
    }

    /// <summary>The felling cost of a tree of these blocks.</summary>
    public static int Cost(IEnumerable<Block> tree) =>
        FellingWearRules.Cost(FellingWearRules.IsThick(tree.Select(b => b.Code?.Path ?? "")), _config);

    // ItemAxe.OnBlockBrokenWith(IWorldAccessor world, Entity byEntity, ItemSlot itemslot, BlockSelection blockSel, float dropQuantityMultiplier)
    public static void BreakPrefix(ItemAxe __instance, IWorldAccessor __0, Entity __1, ItemSlot __2, BlockSelection __3)
    {
        string? felledBy = _felledBy;
        var stump = _stump;
        _felledBy = null;
        _stump = null;
        if (felledBy == null || stump == null || __0.Side != EnumAppSide.Server || __1 is not EntityPlayer { PlayerUID: var uid } || uid != felledBy)
            return;
        var tree = __instance.FindTree(__0, __3.Position, out _, out _);
        if (!tree.Contains(stump))
            return;
        _sparedSlot = __2;
        _thick = FellingWearRules.IsThick(tree.Select(pos => __0.BlockAccessor.GetBlock(pos).Code?.Path ?? ""));
    }

    public static Exception? BreakFinalizer(Exception? __exception, IWorldAccessor __0, Entity __1, ItemSlot __2)
    {
        var spared = _sparedSlot;
        _sparedSlot = null;
        if (spared == null || __exception != null)
            return __exception;
        if (spared.Itemstack?.Collectible is { } axe && (axe.GetDamagedBy(spared)?.Contains(EnumItemDamageSource.BlockBreaking) ?? false))
        {
            int cost = FellingWearRules.Cost(_thick, _config);
            if (cost > 0)
                axe.DamageItem(__0, __1, spared, cost);
        }
        return null;
    }

    // CollectibleObject.DamageItem(IWorldAccessor world, Entity byEntity, ItemSlot itemSlot, int amount, bool destroyOnZeroDurability)
    public static bool DamagePrefix(ItemSlot __2) => _sparedSlot == null || !ReferenceEquals(__2, _sparedSlot);
}
