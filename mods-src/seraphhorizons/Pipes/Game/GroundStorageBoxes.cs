using System.Runtime.CompilerServices;
using HarmonyLib;
using SeraphHorizons.Mod.Pipes.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Pipes;

/// <summary>
/// A ground-stored pipe section's boxes turn with its model (README "Unified pipes", "On the
/// ground"). The game's ground storage (<see cref="BlockEntityGroundStorage"/>) draws its stack
/// turned by <c>MeshAngle</c>, a quarter turn for each way the player can face when setting it down,
/// but its <c>GetCollisionBoxes</c> and <c>GetSelectionBoxes</c> return the box the item type
/// declares, unturned: a pipe section set down facing east or west is drawn east-west and boxed
/// north-south. Postfixes on both (with Unified pipes' Harmony, so on both sides: the client moves
/// its player against them and aims with them) turn the boxes about the block's centre by the same
/// angle, as the game's <see cref="Cuboidf.RotatedCopy(float, float, float, Vec3d)"/> turns a block's,
/// when the stack's item has <see cref="GroundBoxTurns.Attribute"/> and the angle is a whole number of
/// quarter turns (<see cref="GroundBoxTurns.QuarterTurns"/>). Every other stack's boxes are the
/// game's. The turned boxes are kept per block entity and remade only when its box or angle changes,
/// since the game asks for collision boxes every physics tick.
/// </summary>
public static class GroundStorageBoxes
{
    private static readonly Vec3d Centre = new(0.5, 0.5, 0.5);

    private sealed class Turned
    {
        public Cuboidf? Collision, Selection;
        public int CollisionTurns, SelectionTurns;
        public Cuboidf[]? CollisionBoxes, SelectionBoxes;
    }

    private static readonly ConditionalWeakTable<BlockEntityGroundStorage, Turned> Cache = new();

    public static void Patch(Harmony harmony)
    {
        harmony.Patch(AccessTools.DeclaredMethod(typeof(BlockEntityGroundStorage), nameof(BlockEntityGroundStorage.GetCollisionBoxes), Type.EmptyTypes),
            postfix: new HarmonyMethod(typeof(GroundStorageBoxes), nameof(CollisionPostfix)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(BlockEntityGroundStorage), nameof(BlockEntityGroundStorage.GetSelectionBoxes), Type.EmptyTypes),
            postfix: new HarmonyMethod(typeof(GroundStorageBoxes), nameof(SelectionPostfix)));
    }

    /// <summary>The quarter turns the stack's boxes take, or 0 when they stay as they are.</summary>
    public static int TurnsFor(BlockEntityGroundStorage storage)
    {
        var stack = storage.Inventory?.FirstNonEmptySlot?.Itemstack;
        if (stack?.Collectible?.Attributes?[GroundBoxTurns.Attribute].AsBool(false) != true)
            return 0;
        return GroundBoxTurns.QuarterTurns(storage.MeshAngle) ?? 0;
    }

    public static void CollisionPostfix(BlockEntityGroundStorage __instance, ref Cuboidf[] __result)
    {
        int turns = TurnsFor(__instance);
        if (turns == 0 || __result is not [{ } box])
            return;
        var turned = Cache.GetOrCreateValue(__instance);
        if (turned.CollisionBoxes == null || !ReferenceEquals(turned.Collision, box) || turned.CollisionTurns != turns)
        {
            turned.Collision = box;
            turned.CollisionTurns = turns;
            turned.CollisionBoxes = [Turn(box, turns)];
        }
        __result = turned.CollisionBoxes;
    }

    public static void SelectionPostfix(BlockEntityGroundStorage __instance, ref Cuboidf[] __result)
    {
        int turns = TurnsFor(__instance);
        if (turns == 0 || __result is not [{ } box])
            return;
        var turned = Cache.GetOrCreateValue(__instance);
        if (turned.SelectionBoxes == null || !ReferenceEquals(turned.Selection, box) || turned.SelectionTurns != turns)
        {
            turned.Selection = box;
            turned.SelectionTurns = turns;
            turned.SelectionBoxes = [Turn(box, turns)];
        }
        __result = turned.SelectionBoxes;
    }

    /// <summary><paramref name="box"/> turned by <paramref name="turns"/> quarter turns about the
    /// block's centre, as the model is (<c>Matrixf.RotateY(MeshAngle)</c>).</summary>
    public static Cuboidf Turn(Cuboidf box, int turns) => box.RotatedCopy(0, turns * 90, 0, Centre);
}
