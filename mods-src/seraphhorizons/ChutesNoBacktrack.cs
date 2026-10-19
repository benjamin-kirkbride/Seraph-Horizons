using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod;

/// <summary>
/// Chutes never send an item back the way it came (#1348).
///
/// The game's <c>BlockEntityItemFlow.MoveItem</c> lets an item leave through the face it came in by:
/// an elbow (<c>chute-elbow-up-*</c>) or T (<c>chute-t-ud-*</c>) whose push and throw fail pushes into
/// a random horizontal pull face, its intake; a two-way chute (straight, <c>t-ns</c>/<c>we</c>,
/// <c>cross-ground</c>) with a tagged direction (<c>chuteDir</c>) can still throw out of the back into
/// air (<c>TrySpitOut</c> runs before its guard), and one without picks a face at random; a ported
/// Archimedes screw pushes back out of its port. Behind a machine that refuses the next item the item
/// bounces until it has made the game's three sideways moves (<c>MaxHorizontalTravel</c>), and an item
/// at the limit with only a sideways way out never moves again.
///
/// The face an item came in by is recorded in the game's own <c>LastReceivedFromDir</c> (saved and
/// loaded by the game, never set by it): on the receiving item flow after a push (<c>TryPushInto</c>),
/// on the item flow itself after a pull (<c>TryPullFrom</c>), and by a machine pushing into a chute
/// (<c>MachineChutes.TryPush</c>, which also tags the item with its travel direction). Prefixes on
/// <c>TryPushInto</c> and <c>TrySpitOut</c> refuse that face while the item flow holds an item
/// (<see cref="ChuteEntry.Refuses"/>), so a blocked item waits. It is cleared when the item flow
/// empties (its private <c>OnSlotModified</c>), so an item put in by hand moves as the game moves it.
/// Hoppers (four slots) are left alone.
///
/// The members are found by name at load; if one is missing or changed the tweak logs a warning and
/// chutes are left as the game ships them.
/// </summary>
public static class ChutesNoBacktrack
{
    /// <summary>Whether the patches went in: <c>MachineChutes.TryPush</c> records and tags only then.</summary>
    public static bool Enabled { get; private set; }

    public static MethodInfo? TryPushInto =>
        AccessTools.DeclaredMethod(typeof(BlockEntityItemFlow), "TryPushInto", [typeof(BlockFacing)]);

    public static MethodInfo? TrySpitOut =>
        AccessTools.DeclaredMethod(typeof(BlockEntityItemFlow), "TrySpitOut", [typeof(BlockFacing)]);

    public static MethodInfo? TryPullFrom =>
        AccessTools.DeclaredMethod(typeof(BlockEntityItemFlow), "TryPullFrom", [typeof(BlockFacing)]);

    public static MethodInfo? OnSlotModified =>
        AccessTools.DeclaredMethod(typeof(BlockEntityItemFlow), "OnSlotModified", [typeof(int)]);

    /// <summary>Patches the item flow. Returns whether the patches went in.</summary>
    public static bool Patch(Harmony harmony, ILogger logger)
    {
        var push = TryPushInto;
        var spit = TrySpitOut;
        var pull = TryPullFrom;
        var modified = OnSlotModified;
        if (push?.ReturnType != typeof(bool) || spit?.ReturnType != typeof(bool) || pull?.ReturnType != typeof(void)
            || modified?.ReturnType != typeof(void)
            || AccessTools.DeclaredField(typeof(BlockEntityItemFlow), nameof(BlockEntityItemFlow.LastReceivedFromDir))?.FieldType != typeof(BlockFacing))
        {
            logger.Warning("[seraphhorizons] BlockEntityItemFlow does not have TryPushInto, TrySpitOut, TryPullFrom, "
                           + "OnSlotModified and LastReceivedFromDir as expected; the game changed, so chutes can "
                           + "still send items back the way they came (#1348)");
            return false;
        }
        harmony.Patch(push, prefix: new HarmonyMethod(typeof(ChutesNoBacktrack), nameof(PushPrefix)),
            postfix: new HarmonyMethod(typeof(ChutesNoBacktrack), nameof(PushPostfix)));
        harmony.Patch(spit, prefix: new HarmonyMethod(typeof(ChutesNoBacktrack), nameof(SpitPrefix)));
        harmony.Patch(pull, prefix: new HarmonyMethod(typeof(ChutesNoBacktrack), nameof(PullPrefix)),
            postfix: new HarmonyMethod(typeof(ChutesNoBacktrack), nameof(PullPostfix)));
        harmony.Patch(modified, postfix: new HarmonyMethod(typeof(ChutesNoBacktrack), nameof(ModifiedPostfix)));
        Enabled = true;
        return true;
    }

    public static void Unbind() => Enabled = false;

    /// <summary>Records on <paramref name="flow"/> that its item came in by <paramref name="face"/>
    /// (its own face). One-slot item flows only.</summary>
    public static void Received(BlockEntityItemFlow flow, BlockFacing face)
    {
        if (flow.Inventory.Count == 1)
            flow.LastReceivedFromDir = face;
    }

    private static bool Refuses(BlockEntityItemFlow flow, BlockFacing face) =>
        ChuteEntry.Refuses(flow.LastReceivedFromDir?.Index, face.Index, !flow.Inventory.Empty, flow.Inventory.Count);

    public static bool PushPrefix(BlockEntityItemFlow __instance, BlockFacing outputFace, ref bool __result)
    {
        if (!Refuses(__instance, outputFace))
            return true;
        __result = false;
        return false;
    }

    public static void PushPostfix(BlockEntityItemFlow __instance, BlockFacing outputFace, bool __result)
    {
        if (!__result)
            return;
        var pos = __instance.Pos.AddCopy(outputFace);
        if (__instance.Api.World.BlockAccessor.GetBlockEntity(pos) is BlockEntityItemFlow target)
            Received(target, outputFace.Opposite);
    }

    public static bool SpitPrefix(BlockEntityItemFlow __instance, BlockFacing outputFace, ref bool __result)
    {
        if (!Refuses(__instance, outputFace))
            return true;
        __result = false;
        return false;
    }

    public static void PullPrefix(BlockEntityItemFlow __instance, out bool __state) => __state = __instance.Inventory.Empty;

    public static void PullPostfix(BlockEntityItemFlow __instance, BlockFacing inputFace, bool __state)
    {
        if (__state && !__instance.Inventory.Empty)
            Received(__instance, inputFace);
    }

    /// <summary>An emptied item flow forgets where its last item came from, so the next one, if put
    /// in by hand, moves as the game moves it.</summary>
    public static void ModifiedPostfix(BlockEntityItemFlow __instance)
    {
        if (__instance.Inventory.Empty)
            __instance.LastReceivedFromDir = null;
    }
}
