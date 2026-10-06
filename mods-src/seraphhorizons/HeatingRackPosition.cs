using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod;

/// <summary>
/// Logging Expanded (<c>loggingmod</c>): a Trunk Heating Rack (<c>loggingmod:resinrack-*</c>,
/// <c>BlockResinRack</c>) placed from a picked-up stack knows where it stands (#374).
///
/// The rack's <c>OnPickBlock</c> writes its block entity's whole tree into the stack, and the base
/// <c>BlockEntity.ToTreeAttributes</c> puts <c>posx</c>, <c>posy</c> and <c>posz</c> in it. Its
/// <c>DoPlaceBlock</c> loads that tree back into the new block entity, so the entity's <c>Pos</c>
/// becomes the spot the rack was picked up from. Carry On's server puts it right afterwards; its
/// client, and a creative pick and place, do not.
///
/// A postfix on <c>OnPickBlock</c> takes the three out of the stack it returns, and a prefix on
/// <c>DoPlaceBlock</c> takes them out of the stack being placed, for stacks picked up before the
/// tweak. With no position in the tree, <c>BEResinRack.FromTreeAttributes</c> keeps the one it has.
///
/// Logging Expanded is not referenced at build time: if the rack or either method is gone, the tweak
/// logs a warning and leaves the rack as it ships. Patched on both sides (picking and placing run on
/// each), once per process with its own Harmony id, since a singleplayer game runs both in one.
/// </summary>
public static class HeatingRackPosition
{
    public const string ModId = "loggingmod";
    public const string HarmonyId = "seraphhorizons.heatingrackposition";
    public const string RackBlockType = "LoggingMod.BlockResinRack";

    /// <summary>The position keys <c>BlockEntity.ToTreeAttributes</c> writes.</summary>
    public static readonly string[] PositionKeys = ["posx", "posy", "posz"];

    private static MethodInfo? _onPickBlock, _doPlaceBlock;

    public static bool Applies(ICoreAPI api) => api.ModLoader.IsModEnabled(ModId);

    /// <summary>Finds the rack's <c>OnPickBlock</c> and <c>DoPlaceBlock</c>. Returns whether both are
    /// there as expected; if not, logs that Logging Expanded changed.</summary>
    public static bool Bind(ILogger logger)
    {
        var rack = AccessTools.TypeByName(RackBlockType);
        _onPickBlock = rack == null ? null
            : AccessTools.DeclaredMethod(rack, nameof(Block.OnPickBlock), [typeof(IWorldAccessor), typeof(BlockPos)]);
        _doPlaceBlock = rack == null ? null
            : AccessTools.DeclaredMethod(rack, nameof(Block.DoPlaceBlock),
                [typeof(IWorldAccessor), typeof(IPlayer), typeof(BlockSelection), typeof(ItemStack)]);
        if (rack != null && typeof(Block).IsAssignableFrom(rack)
            && _onPickBlock?.ReturnType == typeof(ItemStack) && _doPlaceBlock?.ReturnType == typeof(bool))
            return true;
        logger.Warning("[seraphhorizons] Logging Expanded's heating rack does not look as expected; Logging Expanded "
                       + "changed, so a heating rack placed from a picked-up stack may keep its old position");
        return false;
    }

    /// <summary>Applies the patches with <paramref name="harmony"/>, unless they are in already
    /// (the other side of a singleplayer game applied them).</summary>
    public static void Patch(Harmony harmony)
    {
        if (Harmony.HasAnyPatches(HarmonyId))
            return;
        harmony.Patch(_onPickBlock, postfix: new HarmonyMethod(typeof(HeatingRackPosition), nameof(OnPickBlockPostfix)));
        harmony.Patch(_doPlaceBlock, prefix: new HarmonyMethod(typeof(HeatingRackPosition), nameof(DoPlaceBlockPrefix)));
    }

    /// <summary>Takes the block entity position out of a rack stack's attributes.</summary>
    public static void RemovePosition(ItemStack? stack)
    {
        if (stack?.Attributes == null)
            return;
        foreach (string key in PositionKeys)
            stack.Attributes.RemoveAttribute(key);
    }

    public static void OnPickBlockPostfix(ItemStack? __result) => RemovePosition(__result);

    public static void DoPlaceBlockPrefix(ItemStack? __3) => RemovePosition(__3);
}
