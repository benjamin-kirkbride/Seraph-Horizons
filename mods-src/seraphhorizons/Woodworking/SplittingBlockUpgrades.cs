using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace SeraphHorizons.Mod.Woodworking;

/// <summary>
/// The splitting block's upgrades, made on the placed block (<see cref="SplittingBlockRules"/> says
/// which one what the player holds makes, and its cost):
/// - primitive to debarked: Immersive Woodworking's debarking, with a bark spud or an axe with a
///   hammer in the offhand, held as long as Immersive Woodworking takes for a log with that tool
///   (its <c>DebarkSeconds</c> over the tool's speed), with its debarking animation. It drops
///   Immersive Woodworking's bark for the block's wood, one log's roll (<see cref="BarkDrops"/>, as
///   the sawhorses' debarking), and the tools lose its <c>DebarkDurabilityPerLog</c>.
/// - debarked to bound: 2 iron hoops, at once, as Logging Expanded.
/// - bound to advanced: 8 iron nails with a hammer in the offhand, held 3 s; the hammer loses 1,
///   as Logging Expanded.
///
/// An upgrade is made only on an empty block and without Shift or Ctrl, which is where Immersive
/// Woodworking's own interactions on the block do nothing with these items; everywhere else
/// Immersive Woodworking's interaction runs as it ships. Prefixes on <c>BlockChoppingBlock</c>'s
/// <c>OnBlockInteractStart</c>, <c>Step</c>, <c>Stop</c> and <c>Cancel</c> take the interaction
/// when an upgrade applies, on both sides (the client predicts and drives the hold, the server
/// makes the upgrade when the hold is released at its full time). The server counts a hold's
/// seconds through Immersive Woodworking's <c>HonestHoldSeconds</c>, as its own holds do: at most
/// the time since the hold began on the server, so a client cannot claim it held long enough. In
/// singleplayer both sides' prefixes sit on each method; the second does nothing once the first
/// has skipped the original.
/// A postfix on its <c>GetPlacedBlockInteractionHelp</c> (client) adds the next upgrade's help to
/// an empty block's.
/// </summary>
public static class SplittingBlockUpgrades
{
    public const string DebarkAxeAnimation = "debarking-axe";
    public const string DebarkSpudAnimation = "debarking-barkspud";
    public const string HelpKeyPrefix = "seraphhorizons:splittingblock-help-";

    public static readonly AssetLocation[] DebarkSounds =
    [
        new(WoodworkingMods.IwModId, "sounds/debark/debarking1"),
        new(WoodworkingMods.IwModId, "sounds/debark/debarking2"),
        new(WoodworkingMods.IwModId, "sounds/debark/debarking3"),
    ];

    public static readonly AssetLocation MetalSound = new("game", "sounds/block/anvil");

    private static PropertyInfo? _iwConfig;
    private static MethodInfo? _sharpnessSpeed;
    private static MethodInfo? _toolTierSpeed;
    private static PropertyInfo? _hasContent;
    private static PropertyInfo? _hasTool;
    private static PropertyInfo? _wood;
    private static MethodInfo? _honestHold;
    // Replaced whole by Bind, never changed: in singleplayer the server may read it while the
    // client binds.
    private static IReadOnlyDictionary<string, FieldInfo> _settings = new Dictionary<string, FieldInfo>();

    // Interaction help by step, client.
    private static readonly Dictionary<SplittingBlockStep, WorldInteraction[]> Help = [];

    /// <summary>Immersive Woodworking's members the upgrades use. Called from
    /// <see cref="SplittingBlock.Bind"/>; returns what is missing, or null.</summary>
    public static string? Bind(WoodworkingMods mods, Type blockEntity)
    {
        var system = mods.IwSystem.GetType();
        _iwConfig = AccessTools.DeclaredProperty(system, "Config");
        _sharpnessSpeed = AccessTools.DeclaredMethod(system, "SharpnessSpeedFactor", [typeof(ItemStack)]);
        _toolTierSpeed = AccessTools.DeclaredMethod(system, "ToolTierSpeedFactor", [typeof(ItemStack)]);
        _hasContent = AccessTools.DeclaredProperty(blockEntity, "HasContent");
        _hasTool = AccessTools.DeclaredProperty(blockEntity, "HasTool");
        _wood = AccessTools.DeclaredProperty(blockEntity, "Wood");
        _honestHold = AccessTools.DeclaredMethod(system, "HonestHoldSeconds", [typeof(IWorldAccessor), typeof(IPlayer), typeof(float)]);
        if (_sharpnessSpeed?.ReturnType != typeof(float) || _toolTierSpeed?.ReturnType != typeof(float)
            || _hasContent?.PropertyType != typeof(bool) || _hasTool?.PropertyType != typeof(bool)
            || _wood?.PropertyType != typeof(string) || _honestHold is not { IsStatic: true } || _honestHold.ReturnType != typeof(float))
            return $"{WoodworkingMods.IwSystemType}'s tool factors or HonestHoldSeconds, or the chopping block's HasContent, "
                   + "HasTool or Wood, is not as expected";
        if (BarkDrops.Bind(mods) is { } bark)
            return bark;

        var settings = new Dictionary<string, FieldInfo>();
        foreach (var (name, type) in new[]
                 {
                     ("DebarkSeconds", typeof(float)), ("AxeHammerDebarkSpeedMultiplier", typeof(float)),
                     ("DebarkDurabilityPerLog", typeof(int)),
                 })
        {
            var field = AccessTools.DeclaredField(mods.IwConfigClass, name);
            if (field == null || field.IsStatic || field.FieldType != type)
                return $"{WoodworkingMods.IwConfigType}.{name} ({type.Name}) is missing";
            settings[name] = field;
        }
        _settings = settings;
        return null;
    }

    /// <summary>Forgets the interaction help (client).</summary>
    public static void Clear() => Help.Clear();

    private static ModSystem IwSystem(ICoreAPI api) => api.ModLoader.GetModSystem(WoodworkingMods.IwSystemType);

    private static T Setting<T>(ICoreAPI api, string name) => (T)_settings[name].GetValue(_iwConfig!.GetValue(IwSystem(api)))!;

    /// <summary>The seconds a hold has lasted, as the server may believe them: Immersive
    /// Woodworking's <c>HonestHoldSeconds</c> (no change on a client).</summary>
    private static float HonestSeconds(IWorldAccessor world, IPlayer player, float seconds) =>
        (float)_honestHold!.Invoke(null, [world, player, seconds])!;

    private static float Factor(MethodInfo method, ICoreAPI api, ItemStack? stack) => (float)method.Invoke(IwSystem(api), [stack])!;

    private static bool IsEmpty(BlockEntity be) => !(bool)_hasContent!.GetValue(be)! && !(bool)_hasTool!.GetValue(be)!;

    private static bool IsHammer(ItemStack? stack) => stack?.Item?.Tool == EnumTool.Hammer;

    private static SplittingBlockHeld Held(ItemStack? stack) =>
        SplittingBlockRules.Classify(stack?.Collectible?.Code?.ToString(), stack?.Item?.Tool == EnumTool.Axe);

    /// <summary>The upgrade the player's hands make on the block now, or null: none for what they
    /// hold, or the block is not empty, or Shift or Ctrl is down.</summary>
    private static SplittingBlockUpgrade? UpgradeFor(IPlayer player, BlockEntity be, BEBehaviorSplittingBlockTier behavior)
    {
        var controls = player.Entity.Controls;
        if (controls.ShiftKey || controls.CtrlKey || !IsEmpty(be))
            return null;
        var main = player.InventoryManager.ActiveHotbarSlot?.Itemstack;
        var held = Held(main);
        float debarkSeconds = 0;
        if (behavior.Tier == SplittingBlockTier.Primitive && held is SplittingBlockHeld.BarkSpud or SplittingBlockHeld.Axe)
        {
            // Immersive Woodworking's sawhorse: a spud strips at its sharpness and metal tier, an
            // axe with a hammer at a fixed multiplier.
            float speed = held == SplittingBlockHeld.BarkSpud
                ? Factor(_sharpnessSpeed!, be.Api, main) * Factor(_toolTierSpeed!, be.Api, main)
                : Setting<float>(be.Api, "AxeHammerDebarkSpeedMultiplier");
            debarkSeconds = SplittingBlockRules.DebarkSeconds(Setting<float>(be.Api, "DebarkSeconds"), speed);
        }
        return SplittingBlockRules.For(behavior.Tier, held, main?.StackSize ?? 0,
            IsHammer(player.Entity.LeftHandItemSlot?.Itemstack), debarkSeconds,
            Setting<int>(be.Api, "DebarkDurabilityPerLog"));
    }

    private static string? Animation(IPlayer player, SplittingBlockUpgrade upgrade) =>
        upgrade.Step != SplittingBlockStep.Debark ? null
        : Held(player.InventoryManager.ActiveHotbarSlot?.Itemstack) == SplittingBlockHeld.BarkSpud ? DebarkSpudAnimation
        : DebarkAxeAnimation;

    private static void StopAnimations(IPlayer player)
    {
        player.Entity.StopAnimation(DebarkAxeAnimation);
        player.Entity.StopAnimation(DebarkSpudAnimation);
    }

    private static (BlockEntity, BEBehaviorSplittingBlockTier)? Find(IWorldAccessor world, BlockSelection? selection) =>
        selection == null || world.BlockAccessor.GetBlockEntity(selection.Position) is not { } be
        || be.GetBehavior<BEBehaviorSplittingBlockTier>() is not { } behavior
            ? null
            : (be, behavior);

    /// <summary>Prefix on <c>BlockChoppingBlock.OnBlockInteractStart</c>: starts an upgrade's hold,
    /// or makes an instant one, in place of Immersive Woodworking's interaction.</summary>
    public static bool InteractStartPrefix(IWorldAccessor __0, IPlayer __1, BlockSelection __2, ref bool __result,
        bool __runOriginal)
    {
        if (!__runOriginal || Find(__0, __2) is not ({ } be, { } behavior) || UpgradeFor(__1, be, behavior) is not { } upgrade)
            return __runOriginal;
        if (upgrade.IsHold)
        {
            behavior.Holds[__1.PlayerUID] = upgrade;
            if (Animation(__1, upgrade) is { } animation)
                __1.Entity.StartAnimation(animation);
        }
        else if (__0.Side == EnumAppSide.Server)
            Complete(__0, __1, be, behavior, upgrade);
        (__1 as IClientPlayer)?.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
        __result = true;
        return false;
    }

    /// <summary>Prefix on <c>BlockChoppingBlock.OnBlockInteractStep</c>: an upgrade's hold goes on
    /// until its time is up, and ends early if the player's hands or the block no longer fit it.</summary>
    public static bool InteractStepPrefix(float __0, IWorldAccessor __1, IPlayer __2, BlockSelection __3, ref bool __result,
        bool __runOriginal)
    {
        if (!__runOriginal || Find(__1, __3) is not ({ } be, { } behavior)
            || !behavior.Holds.TryGetValue(__2.PlayerUID, out var upgrade))
            return __runOriginal;
        if (UpgradeFor(__2, be, behavior)?.Step != upgrade.Step)
        {
            behavior.Holds.Remove(__2.PlayerUID);
            StopAnimations(__2);
            __result = false;
            return false;
        }
        __result = HonestSeconds(__1, __2, __0) < upgrade.HoldSeconds;
        return false;
    }

    /// <summary>Prefix on <c>BlockChoppingBlock.OnBlockInteractStop</c>: a hold released at its full
    /// time makes the upgrade (server), if the hands and block still fit it.</summary>
    public static bool InteractStopPrefix(float __0, IWorldAccessor __1, IPlayer __2, BlockSelection __3, bool __runOriginal)
    {
        if (!__runOriginal || Find(__1, __3) is not ({ } be, { } behavior)
            || !behavior.Holds.Remove(__2.PlayerUID, out var upgrade))
            return __runOriginal;
        StopAnimations(__2);
        if (__1.Side == EnumAppSide.Server && SplittingBlockRules.IsHoldDone(HonestSeconds(__1, __2, __0), upgrade.HoldSeconds)
            && UpgradeFor(__2, be, behavior) is { } now && now.Step == upgrade.Step)
            Complete(__1, __2, be, behavior, now);
        return false;
    }

    /// <summary>Prefix on <c>BlockChoppingBlock.OnBlockInteractCancel</c>: an upgrade's hold may
    /// always be cancelled, and nothing is made.</summary>
    public static bool InteractCancelPrefix(IWorldAccessor __1, IPlayer __2, BlockSelection __3, ref bool __result,
        bool __runOriginal)
    {
        if (!__runOriginal || Find(__1, __3) is not (_, { } behavior) || !behavior.Holds.Remove(__2.PlayerUID))
            return __runOriginal;
        StopAnimations(__2);
        __result = true;
        return false;
    }

    /// <summary>Makes the upgrade, on the server: takes its cost and wear, drops the bark, sounds,
    /// and sets the tier. A player in creative mode pays nothing.</summary>
    private static void Complete(IWorldAccessor world, IPlayer player, BlockEntity be, BEBehaviorSplittingBlockTier behavior,
        SplittingBlockUpgrade upgrade)
    {
        var main = player.InventoryManager.ActiveHotbarSlot;
        var offhand = player.Entity.LeftHandItemSlot;
        BlockPos pos = be.Pos;
        if (upgrade.Step == SplittingBlockStep.Debark)
            DropBark(world, be, main.Itemstack, offhand?.Itemstack);
        if (player.WorldData.CurrentGameMode != EnumGameMode.Creative)
        {
            if (upgrade.Consumes > 0)
            {
                main.TakeOut(upgrade.Consumes);
                main.MarkDirty();
            }
            Wear(world, player, main, upgrade.MainWear);
            Wear(world, player, offhand, upgrade.HammerWear);
        }
        var sound = upgrade.Step == SplittingBlockStep.Debark ? DebarkSounds[world.Rand.Next(DebarkSounds.Length)] : MetalSound;
        world.PlaySoundAt(sound, pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, null, true, 16f);
        behavior.SetTier(upgrade.To);
    }

    private static void Wear(IWorldAccessor world, IPlayer player, ItemSlot? slot, int amount)
    {
        if (amount <= 0 || slot?.Itemstack == null)
            return;
        slot.Itemstack.Collectible.DamageItem(world, player.Entity, slot, amount);
        slot.MarkDirty();
    }

    /// <summary>Immersive Woodworking's bark for one log of the block's wood, rolled for the tool
    /// set (<see cref="BarkDrops"/>), dropped on top of the block.</summary>
    private static void DropBark(IWorldAccessor world, BlockEntity be, ItemStack? tool, ItemStack? offhand)
    {
        if (_wood!.GetValue(be) is string wood
            && BarkDrops.Roll(world, wood, BarkDrops.ChanceMultiplier(be.Api, tool, offhand)) is { } bark)
            world.SpawnItemEntity(bark, be.Pos.ToVec3d().Add(0.5, 1.0, 0.5));
    }

    /// <summary>Postfix on <c>BlockChoppingBlock.GetPlacedBlockInteractionHelp</c> (client): an empty
    /// block below the advanced tier also shows its next upgrade.</summary>
    public static void InteractionHelpPostfix(IWorldAccessor __0, BlockSelection __1, ref WorldInteraction[] __result)
    {
        if (__0.Api is not ICoreClientAPI capi || Find(__0, __1) is not ({ } be, { } behavior) || !IsEmpty(be)
            || SplittingBlockRules.NextStep(behavior.Tier) is not { } step)
            return;
        if (!Help.TryGetValue(step, out var help))
            Help[step] = help = BuildHelp(capi, step);
        __result = __result.Append(help);
    }

    private static WorldInteraction[] BuildHelp(ICoreClientAPI capi, SplittingBlockStep step)
    {
        ItemStack[] Stacks(System.Func<Item, bool> where, int size = 1) =>
            capi.World.Items.Where(item => item?.Code != null && where(item)).Select(item => new ItemStack(item, size)).ToArray();
        WorldInteraction Line(string key, ItemStack[] stacks) =>
            new() { ActionLangCode = HelpKeyPrefix + key, MouseButton = EnumMouseButton.Right, Itemstacks = stacks };

        return step switch
        {
            SplittingBlockStep.Debark =>
            [
                Line("debark", Stacks(item => SplittingBlockRules.Classify(item.Code.ToString(), false) == SplittingBlockHeld.BarkSpud)),
                Line("debark-axe", Stacks(item => item.Tool == EnumTool.Axe)),
            ],
            SplittingBlockStep.Bind =>
                [Line("bind", Stacks(item => item.Code.ToString() == SplittingBlockRules.HoopCode, SplittingBlockRules.HoopsToBind))],
            _ => [Line("nail", Stacks(item => item.Code.ToString() == SplittingBlockRules.NailCode, SplittingBlockRules.NailsToFinish))],
        };
    }
}
