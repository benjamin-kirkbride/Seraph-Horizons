using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace SeraphHorizons.Mod.Woodworking;

/// <summary>
/// The creative shortcut on Logging Expanded's frames (<see cref="CreativeUpgrades.Frames"/>), as
/// the game's water wheel has it: a player in creative mode who right-clicks a frame with Ctrl held
/// (not Shift) gets the stage it makes at once, with nothing in hand and nothing taken. The splitting
/// block's own shortcut is in <see cref="SplittingBlockUpgrades"/>; the finished sawhorses have no
/// next stage (each tier is built from its own frames), so they have none.
///
/// Logging Expanded makes each stage itself: on the server, a prefix on the frame's
/// <c>OnBlockInteractStart</c> puts the stage's items in the player's hands (and a hammer in the
/// offhand where it takes one), runs Logging Expanded's own completion (its
/// <c>OnBlockInteractStart</c> for a click, its <c>OnBlockInteractStop</c> at the full time for a
/// hold), and puts the hands back as they were, whatever Logging Expanded took from them. So the
/// stage comes out exactly as Logging Expanded builds it (its block, the frame's wood and facing, the
/// second block of a two-block frame, its space check and sound), and the player keeps what they
/// held. The server reads the game mode from its own player data. On the client the prefix only
/// takes the click (the client's own prediction would need the items), and a postfix on the frame's
/// <c>GetPlacedBlockInteractionHelp</c> adds the shortcut's line for a player in creative.
///
/// A frame's classes are found by name. Missing ones do not fail the tweak, as the bark spud's
/// lambda (<see cref="Sawhorses"/>) does not: those frames get no shortcut, with one warning, and
/// everything else runs. Turning the whole tweak off over a creative convenience would bring back
/// Immersive Woodworking's sawhorse and the splitting logs.
/// </summary>
public sealed class FrameUpgrades : WoodworkingPart
{
    public const string HelpMethod = "GetPlacedBlockInteractionHelp";

    // Frame stage by block class, bound; replaced whole by Bind.
    private static IReadOnlyDictionary<Type, FrameStage> _stages = new Dictionary<Type, FrameStage>();
    private readonly List<(MethodInfo Start, MethodInfo Help)> _patches = [];
    private string? _missing;

    // Set while the server runs Logging Expanded's completion from the prefix, which then lets the
    // original through (it is the same method, or calls one this prefix sits on).
    [ThreadStatic] private static bool _completing;

    public override string Name => "the frames' creative upgrades";

    public override string? Bind(ICoreAPI api, WoodworkingMods mods)
    {
        var stages = new Dictionary<Type, FrameStage>();
        var missing = new List<string>();
        _patches.Clear();
        Type[] interaction = [typeof(IWorldAccessor), typeof(IPlayer), typeof(BlockSelection)];
        Type[] stop = [typeof(float), .. interaction];
        foreach (var stage in CreativeUpgrades.Frames)
        {
            var type = WoodworkingMods.LeType(stage.FrameClass);
            var start = type == null ? null : AccessTools.DeclaredMethod(type, "OnBlockInteractStart", interaction);
            var help = type == null ? null
                : AccessTools.DeclaredMethod(type, HelpMethod, [typeof(IWorldAccessor), typeof(BlockSelection), typeof(IPlayer)]);
            if (type == null || !typeof(Block).IsAssignableFrom(type) || start?.ReturnType != typeof(bool)
                || help?.ReturnType != typeof(WorldInteraction[])
                || stage.IsHold && AccessTools.DeclaredMethod(type, "OnBlockInteractStop", stop)?.ReturnType != typeof(void))
            {
                missing.Add(stage.FrameClass);
                continue;
            }
            stages[type] = stage;
            _patches.Add((start, help));
        }
        _stages = stages;
        _missing = missing.Count == 0 ? null : string.Join(", ", missing);
        return null;
    }

    public override void Start(ICoreAPI api, Harmony harmony)
    {
        if (_missing != null)
            api.Logger.Warning("[seraphhorizons] Unified woodworking: Logging Expanded changed its frame classes "
                               + $"({_missing}), so those frames have no creative upgrade");
        foreach (var (start, help) in _patches)
        {
            harmony.Patch(start, prefix: new HarmonyMethod(typeof(FrameUpgrades), nameof(InteractStartPrefix)));
            if (api.Side == EnumAppSide.Client)
                harmony.Patch(help, postfix: new HarmonyMethod(typeof(FrameUpgrades), nameof(HelpPostfix)));
        }
    }

    // _stages stays: in singleplayer the other side's patches may still run after this side
    // disposes, and the next Bind sets it again.
    public override void Dispose() => _patches.Clear();

    private static bool IsCreative(IPlayer? player) => player?.WorldData?.CurrentGameMode == EnumGameMode.Creative;

    private static FrameStage? StageOf(Block block) => _stages.GetValueOrDefault(block.GetType());

    /// <summary>Prefix on a frame's <c>OnBlockInteractStart</c> (both sides): in creative with Ctrl,
    /// takes the click and, on the server, makes the frame's next stage.</summary>
    public static bool InteractStartPrefix(Block __instance, IWorldAccessor __0, IPlayer __1, BlockSelection __2,
        ref bool __result, bool __runOriginal)
    {
        var controls = __1?.Entity?.Controls;
        if (!__runOriginal || _completing || controls == null || StageOf(__instance) is not { } stage
            || !CreativeUpgrades.Applies(IsCreative(__1), controls.CtrlKey, controls.ShiftKey))
            return __runOriginal;
        if (__0.Side == EnumAppSide.Server)
            Complete(__instance, __0, __1!, __2, stage);
        (__1 as IClientPlayer)?.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
        __result = true;
        return false;
    }

    /// <summary>Logging Expanded's completion of the stage, on the server, with its items put in the
    /// player's hands for it and the hands put back after.</summary>
    private static void Complete(Block frame, IWorldAccessor world, IPlayer player, BlockSelection sel, FrameStage stage)
    {
        var main = player.InventoryManager.ActiveHotbarSlot;
        var offhand = player.Entity.LeftHandItemSlot;
        var items = StackOf(world, stage.MainCode, stage.Count);
        var hammer = stage.HammerInOffhand ? StackOf(world, CreativeUpgrades.HammerCode, 1) : null;
        if (main == null || stage.HammerInOffhand && offhand == null)
            return;
        if (items == null || stage.HammerInOffhand && hammer == null)
        {
            world.Logger.Warning($"[seraphhorizons] Unified woodworking: no {stage.MainCode} or {CreativeUpgrades.HammerCode} "
                                 + $"for the creative upgrade of {frame.Code}");
            return;
        }
        ItemStack? mainBefore = main.Itemstack, offhandBefore = offhand?.Itemstack;
        try
        {
            _completing = true;
            main.Itemstack = items;
            if (stage.HammerInOffhand)
                offhand!.Itemstack = hammer;
            if (stage.IsHold)
                frame.OnBlockInteractStop(stage.HoldSeconds, world, player, sel);
            else
                frame.OnBlockInteractStart(world, player, sel);
        }
        finally
        {
            _completing = false;
            main.Itemstack = mainBefore;
            main.MarkDirty();
            if (stage.HammerInOffhand)
            {
                offhand!.Itemstack = offhandBefore;
                offhand.MarkDirty();
            }
        }
    }

    private static ItemStack? StackOf(IWorldAccessor world, string code, int size)
    {
        var location = new AssetLocation(code);
        CollectibleObject? collectible = world.GetItem(location);
        collectible ??= world.GetBlock(location) is { Id: not 0 } block ? block : null;
        return collectible == null ? null : new ItemStack(collectible, size);
    }

    /// <summary>Postfix on a frame's <c>GetPlacedBlockInteractionHelp</c> (client): the creative
    /// shortcut's line, to a player in creative mode.</summary>
    public static void HelpPostfix(Block __instance, IPlayer __2, ref WorldInteraction[] __result)
    {
        if (IsCreative(__2) && StageOf(__instance) != null)
            __result = (__result ?? []).Append(SplittingBlockUpgrades.CreativeUpgradeHelp);
    }
}
