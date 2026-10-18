using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.Mod;

/// <summary>
/// The game's ported Archimedes screw (<c>game:archimedesscrew-ported-*</c>, push faces up and its
/// port) sends its items out of the port, not out of the top.
///
/// <c>BlockEntityItemFlow.MoveItem</c>, every 500 ms on a screw, picks a random push face, but tries
/// the face an item is tagged with (<c>chuteDir</c>) first, throwing it out into air
/// (<c>TrySpitOut</c>) before pushing it into a container (<c>TryPushInto</c>). An item a screw
/// pushes into another screw is always tagged up. So the top screw of a stack, ported, throws every
/// item out of its top when there is air above it and the chest on its port gets nothing; capped
/// with a block, it tries the port only when the random pick lands on it, half its throughput at
/// speed (the flow accumulator is capped at twice the rate).
///
/// A prefix runs <c>MoveItem</c> for a screw with a horizontal push face itself, the game's own
/// accumulator and pull kept, and tries the faces in <see cref="ScrewPortRoute.Steps{TFace}"/>'s
/// order: into the container above, into the port, out of the port, the last only when there is
/// no container above (the bottom screw of a stack waits for the next one rather than throw its
/// item out of its port while that one is still full; the game would throw it out half the
/// time). It calls the game's own private <c>TryPushInto</c>, <c>TrySpitOut</c> and
/// <c>TryPullFrom</c>, so what each move does (stack sizes, the tags, the horizontal travel limit,
/// sounds) is the game's. The game's fallback of pushing out of a horizontal pull face is left out:
/// a screw pulls only from below. Straight screws and every other item flow block (chutes, hoppers)
/// run the game's method untouched.
///
/// The members are found by name at load; if one is missing or changed the tweak logs a warning and
/// the screw is left as the game ships it.
/// </summary>
public static class ScrewPortOutput
{
    private static AccessTools.FieldRef<BlockEntityItemFlow, float>? _accum;
    private static System.Func<BlockEntityItemFlow, BlockFacing, bool>? _pushInto;
    private static System.Func<BlockEntityItemFlow, BlockFacing, bool>? _spitOut;
    private static Action<BlockEntityItemFlow, BlockFacing>? _pullFrom;

    public static MethodInfo? MoveItem =>
        AccessTools.DeclaredMethod(typeof(BlockEntityItemFlow), nameof(BlockEntityItemFlow.MoveItem), [typeof(float)]);

    /// <summary>Prefixes <c>BlockEntityItemFlow.MoveItem</c>. Returns whether the patch went in.</summary>
    public static bool Patch(Harmony harmony, ILogger logger)
    {
        var type = typeof(BlockEntityItemFlow);
        var accum = AccessTools.DeclaredField(type, "itemFlowAccum");
        var push = AccessTools.DeclaredMethod(type, "TryPushInto", [typeof(BlockFacing)]);
        var spit = AccessTools.DeclaredMethod(type, "TrySpitOut", [typeof(BlockFacing)]);
        var pull = AccessTools.DeclaredMethod(type, "TryPullFrom", [typeof(BlockFacing)]);
        var move = MoveItem;
        if (accum?.FieldType != typeof(float) || push?.ReturnType != typeof(bool) || spit?.ReturnType != typeof(bool)
            || pull?.ReturnType != typeof(void) || move?.ReturnType != typeof(void))
        {
            logger.Warning("[seraphhorizons] BlockEntityItemFlow does not have itemFlowAccum, TryPushInto, TrySpitOut, "
                           + "TryPullFrom and MoveItem as expected; the game changed, so a ported Archimedes screw "
                           + "still throws its items out of the top");
            return false;
        }

        _accum = AccessTools.FieldRefAccess<BlockEntityItemFlow, float>(accum);
        _pushInto = AccessTools.MethodDelegate<System.Func<BlockEntityItemFlow, BlockFacing, bool>>(push);
        _spitOut = AccessTools.MethodDelegate<System.Func<BlockEntityItemFlow, BlockFacing, bool>>(spit);
        _pullFrom = AccessTools.MethodDelegate<Action<BlockEntityItemFlow, BlockFacing>>(pull);
        harmony.Patch(move, prefix: new HarmonyMethod(typeof(ScrewPortOutput), nameof(MoveItemPrefix)));
        return true;
    }

    /// <summary>The game's <c>MoveItem</c> with the ported screw's order; true (the game's own)
    /// for anything else.</summary>
    public static bool MoveItemPrefix(BlockEntityItemFlow __instance)
    {
        if (__instance is not BlockEntityArchimedesScrew || _accum == null)
            return true;
        var pushFaces = __instance.PushFaces;
        if (pushFaces == null || !pushFaces.Any(f => f.IsHorizontal))
            return true;

        ref float accum = ref _accum(__instance);
        float rate = __instance.ItemFlowRate;
        accum = Math.Min(accum + rate, Math.Max(1f, rate * 2f));
        if (accum < 1f)
            return false;
        var world = __instance.Api.World;
        var inventory = __instance.Inventory;
        if (!inventory.Empty)
        {
            // As the game does for the face it tries: nothing moves while a neighbour's chunk is unloaded.
            if (pushFaces.Any(f => world.BlockAccessor.GetChunkAtBlockPos(__instance.Pos.AddCopy(f)) == null))
                return false;
            var steps = ScrewPortRoute.Steps(pushFaces, f => f.IsHorizontal, f =>
            {
                var pos = __instance.Pos.AddCopy(f);
                return world.BlockAccessor.GetBlock(pos).GetBlockEntity<BlockEntityContainer>(pos) != null;
            })!;
            foreach (var (action, face) in steps)
                if (action == ScrewPortRoute.Action.PushInto ? _pushInto!(__instance, face) : _spitOut!(__instance, face))
                    break;
        }
        var pullFaces = __instance.PullFaces;
        if (pullFaces is { Length: > 0 } && inventory.Empty)
            _pullFrom!(__instance, pullFaces[world.Rand.Next(pullFaces.Length)]);
        return false;
    }
}
