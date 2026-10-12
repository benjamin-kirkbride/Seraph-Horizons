using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod;

/// <summary>
/// Stone Quarry (<c>stonequarryrepckfipil</c>, 3.6.3) under rock and earth stack to 16
/// (<see cref="RockStackSystem"/>): its rubble storage, drenched with a bucket of water
/// (<c>BERubbleStorage.TryDrench</c>), turns its gravel into muddy gravel a quarter of muddy
/// gravel's stack at a time, 16 a bucket at the stack of 64 it ships with; cut to 16, it would make
/// 4. Stone Quarry has no setting for it, so a transpiler swaps the one stack size read in
/// <c>TryDrench</c> for <see cref="DrenchStack"/>, the stack muddy gravel had before the cut (its
/// trade stack, 64), and a bucket makes 16 again. Without the cut the two are the same.
///
/// Stone Quarry is not referenced at build time: the method is found by name, and if it is gone or
/// no longer reads the stack size exactly once, the tweak logs a warning and leaves it as it ships.
/// Patched on both sides (the client runs the drench too), once per process.
/// </summary>
public static class StoneQuarryDrench
{
    public const string ModId = "stonequarryrepckfipil";
    public const string HarmonyId = "seraphhorizons.stonequarrydrench";
    public const string RubbleStorageType = "StoneQuarry.BERubbleStorage";

    private static readonly FieldInfo StackSize =
        AccessTools.Field(typeof(CollectibleObject), nameof(CollectibleObject.MaxStackSize));

    public static bool Applies(ICoreAPI api) => api.ModLoader.IsModEnabled(ModId);

    public static MethodInfo? Target => AccessTools.TypeByName(RubbleStorageType) is { } type
        ? AccessTools.DeclaredMethod(type, "TryDrench")
        : null;

    /// <summary>Whether <c>TryDrench</c> is patched (by this side or the other in singleplayer).</summary>
    public static bool Patched => Harmony.HasAnyPatches(HarmonyId);

    /// <summary>The stack <c>TryDrench</c> makes a quarter of: the muddy gravel's stack before the cut.</summary>
    public static int DrenchStack(CollectibleObject muddyGravel) => RockStackSystem.TradeStack(muddyGravel);

    /// <summary>Patches <c>TryDrench</c>, unless the other side of a singleplayer game already did;
    /// logs and leaves it when it is not as expected.</summary>
    public static void Patch(Harmony harmony, ILogger logger)
    {
        if (Patched)
            return;
        if (Target is not { } target || PatchProcessor.GetOriginalInstructions(target).Count(Reads) != 1)
        {
            logger.Warning("[seraphhorizons] Stone Quarry drench: {0}.TryDrench is missing or changed; a bucket of water makes a quarter of muddy gravel's stack, as Stone Quarry ships it",
                RubbleStorageType);
            return;
        }
        harmony.Patch(target, transpiler: new HarmonyMethod(typeof(StoneQuarryDrench), nameof(Transpiler)));
        logger.Notification("[seraphhorizons] Stone Quarry drench: a bucket of water makes muddy gravel by its stack before the cut");
    }

    private static bool Reads(CodeInstruction i) => i.LoadsField(StackSize);

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var i in instructions)
            yield return Reads(i)
                ? new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(StoneQuarryDrench), nameof(DrenchStack))).MoveLabelsFrom(i)
                : i;
    }
}
