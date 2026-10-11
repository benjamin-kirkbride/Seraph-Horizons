using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Ore.Processing;

/// <summary>
/// The game's alloy maths (<c>AlloyRecipe.mergeAndCompareStacks</c>) counts a smelted stack as its
/// stack size over <c>smeltedRatio</c>, leaving out <c>smeltedStack</c>'s own stack size, which its
/// single-metal maths (<c>BlockSmeltingContainer.GetSingleSmeltableStack</c>), the bloomery,
/// crucibulum and smex all count. Ore processing gives a chunk whose half is not a whole number of
/// items per ingot a stack of more than one (7 ingots per 40 chunks for 17.5 units), so a transpiler
/// divides by <see cref="EffectiveRatio"/> there instead, ratio over output. Every item the game and
/// the other mods ship smelts to a stack of one, for which it is the same.
/// </summary>
public static class AlloyStackSize
{
    public static MethodInfo? Target =>
        AccessTools.DeclaredMethod(typeof(AlloyRecipe), "mergeAndCompareStacks", [typeof(ItemStack[]), typeof(bool)]);

    private static readonly FieldInfo Ratio = AccessTools.Field(typeof(CombustibleProperties), nameof(CombustibleProperties.SmeltedRatio));

    /// <summary>Items smelted per item of output: the ratio over the smelted stack's size.</summary>
    public static float EffectiveRatio(CombustibleProperties props) =>
        props.SmeltedRatio / (float)Math.Max(1, props.SmeltedStack?.StackSize ?? 1);

    /// <summary>Patches the alloy maths; false (nothing patched) if the method is not as expected.</summary>
    public static bool Patch(Harmony harmony)
    {
        if (Target is not { } target || !Transpilable(target))
            return false;
        harmony.Patch(target, transpiler: new HarmonyMethod(typeof(AlloyStackSize), nameof(Transpiler)));
        return true;
    }

    private static bool Transpilable(MethodInfo target) =>
        PatchProcessor.GetOriginalInstructions(target).Count(i => i.LoadsField(Ratio)) == 1;

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var list = instructions.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            if (!list[i].LoadsField(Ratio))
                continue;
            list[i] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(AlloyStackSize), nameof(EffectiveRatio))).MoveLabelsFrom(list[i]);
            if (i + 1 < list.Count && list[i + 1].opcode == OpCodes.Conv_R4)
                list.RemoveAt(i + 1);
            break;
        }
        return list;
    }
}
