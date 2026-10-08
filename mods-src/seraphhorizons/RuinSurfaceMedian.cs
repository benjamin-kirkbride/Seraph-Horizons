using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.ServerMods;

namespace SeraphHorizons.Mod;

/// <summary>
/// Surface ruins sit on the median of the ground the game sampled under them, not its lowest point
/// (<c>RuinsOnMedianGround</c>).
///
/// The game's <c>WorldGenStructure.TryGenerateRuinAtSurface</c> (every structure placed
/// <c>surfaceruin</c>: BetterRuins' and the game's own ruins) samples the worldgen terrain height
/// (<c>IBlockAccessor.GetTerrainMapheightAt</c>) at the footprint's four corners, which lie one block
/// outside it (start, +SizeX, +SizeZ, both), at the start position once more (the code sets a
/// position at the centre and then samples the start: a slip in the game), and, for a side of 15 or
/// more, at its edge midpoints and the centre line (more for 30 or more). It gives up when the
/// highest and lowest samples differ by more than the schematic's <c>MaxYDiff</c> (3 by default),
/// and otherwise seats the ruin on the lowest: <c>startPos.Y = min + OffsetY</c>. On a slope the
/// uphill side is then buried by up to <c>MaxYDiff</c> blocks; a 25-wide BetterRuins bakery on ground
/// at 136 to 137 with its south edge at 134 was seated at 134, its floor 2 to 3 blocks under the
/// ground around it.
///
/// A transpiler changes only the height chosen: each <c>GetTerrainMapheightAt</c> call in the method
/// goes through <see cref="Sample"/>, which returns the game's own answer and notes it, and the
/// minimum read at that one assignment goes through <see cref="BaseHeight"/>, which answers the
/// median of the noted samples (<see cref="RuinSurfaceHeight"/>); a prefix clears the notes. The
/// sampling, the <c>MaxYDiff</c> rejection on highest minus lowest, the liquid checks, the overlap
/// and distance checks and the placement are the game's, unchanged. A transpiler rather than a
/// prefix that reimplements the method: the method is long, private-state heavy (its random,
/// <c>tmpPos</c>, <c>FindClearEntranceRotation</c>, the placement and the bookkeeping after), and a
/// copy would silently drift from the game's on an update, where this either finds the exact
/// pattern it rewrites (the minimum's local, read once before <c>OffsetY</c> and stored into
/// <c>BlockPos.Y</c>) or, finding anything else, logs a warning and patches nothing. The notes are
/// per thread, as worldgen runs on its own thread.
///
/// Seated on the median, a ruin on a slope has its low side off the ground; a postfix, when the
/// method placed the ruin, fills the air under it down to the ground (<see cref="RuinFoundations"/>).
/// Part of the same switch: the median without the foundation leaves ruins floating, and the
/// foundation without the median has little to fill (on the lowest sample a ruin is off the ground
/// only where the samples missed a dip).
///
/// Server side only. Worldgen only: it changes the ruins of chunks generated from then on, never a
/// ruin already placed. With the switch off nothing is patched.
/// </summary>
public static class RuinSurfaceMedian
{
    public const string MethodName = "TryGenerateRuinAtSurface";

    [ThreadStatic] private static List<int>? _samples;

    /// <summary>Whether the patch is in.</summary>
    public static bool Patched { get; private set; }

    public static MethodInfo? Target => AccessTools.DeclaredMethod(typeof(WorldGenStructure), MethodName);

    private static readonly MethodInfo HeightAt =
        AccessTools.Method(typeof(IBlockAccessor), nameof(IBlockAccessor.GetTerrainMapheightAt), [typeof(BlockPos)]);
    private static readonly MethodInfo MinOf = AccessTools.Method(typeof(GameMath), nameof(GameMath.Min), [typeof(int[])]);
    private static readonly MethodInfo OffsetY = AccessTools.PropertyGetter(typeof(BlockSchematicStructure), nameof(BlockSchematicStructure.OffsetY));
    private static readonly FieldInfo PosY = AccessTools.Field(typeof(BlockPos), nameof(BlockPos.Y));
    private static readonly MethodInfo SampleMethod = AccessTools.Method(typeof(RuinSurfaceMedian), nameof(Sample));
    private static readonly MethodInfo BaseHeightMethod = AccessTools.Method(typeof(RuinSurfaceMedian), nameof(BaseHeight));

    /// <summary>Patches the method when it has the expected shape; otherwise one warning and
    /// nothing patched. Returns whether the patch went in.</summary>
    public static bool Patch(Harmony harmony, ILogger logger)
    {
        var target = Target;
        string? problem = target == null
            ? $"WorldGenStructure.{MethodName} not found"
            : Rewrite(PatchProcessor.GetOriginalInstructions(target), out _);
        if (problem != null)
        {
            logger.Warning($"[seraphhorizons] Ruins on median ground: {problem}; the game changed, so surface ruins sit on the lowest ground as the game places them");
            return false;
        }
        try
        {
            harmony.Patch(target,
                prefix: new HarmonyMethod(typeof(RuinSurfaceMedian), nameof(Prefix)),
                postfix: new HarmonyMethod(typeof(RuinSurfaceMedian), nameof(Postfix)),
                transpiler: new HarmonyMethod(typeof(RuinSurfaceMedian), nameof(Transpiler)));
        }
        catch (Exception e)
        {
            logger.Error($"[seraphhorizons] Ruins on median ground: could not patch WorldGenStructure.{MethodName} ({e.Message}); surface ruins sit on the lowest ground as the game places them");
            return false;
        }
        Patched = true;
        logger.Notification("[seraphhorizons] Ruins on median ground: surface ruins sit on the median of the sampled ground, on a foundation");
        return true;
    }

    /// <summary>Forgets the patch (the patch itself goes with the Harmony id).</summary>
    public static void Unbind() => Patched = false;

    public static void Prefix() => (_samples ??= []).Clear();

    /// <summary>The foundation under a ruin the method placed.</summary>
    public static void Postfix(WorldGenStructure __instance, bool __result, IBlockAccessor blockAccessor)
    {
        if (__result && __instance.LastPlacedSchematic is { } schematic)
            RuinFoundations.Lay(blockAccessor, __instance.LastPlacedSchematicLocation, schematic);
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var codes = instructions.ToList();
        // Checked against the original before patching; another mod's transpiler may since have
        // changed it, and then the method is left as it is.
        return Rewrite(codes, out var rewritten) == null ? rewritten! : codes;
    }

    /// <summary>The game's <c>GetTerrainMapheightAt</c>, noted.</summary>
    public static int Sample(IBlockAccessor accessor, BlockPos pos)
    {
        int height = accessor.GetTerrainMapheightAt(pos);
        (_samples ??= []).Add(height);
        return height;
    }

    /// <summary>The height to seat the ruin on, in place of the game's minimum.</summary>
    public static int BaseHeight(int min) => RuinSurfaceHeight.Base(_samples ?? [], min);

    /// <summary>The method with its samples noted and the minimum at the seating replaced, or why
    /// it does not have the expected shape.</summary>
    public static string? Rewrite(IEnumerable<CodeInstruction> instructions, out List<CodeInstruction>? rewritten)
    {
        rewritten = null;
        // Copies with their labels and exception blocks (CodeInstruction.Clone drops them).
        var codes = instructions.Select(c => new CodeInstruction(c)).ToList();

        int heightCalls = codes.Count(c => c.Calls(HeightAt));
        if (heightCalls < 5)
            return $"{heightCalls} terrain height samples where at least 5 were expected";

        var minLocals = new HashSet<int>();
        for (int i = 0; i + 1 < codes.Count; i++)
            if (codes[i].Calls(MinOf) && Local(codes[i + 1], store: true) is { } local)
                minLocals.Add(local);
        if (minLocals.Count != 1)
            return $"{minLocals.Count} locals hold the lowest sample where one was expected";
        int min = minLocals.Single();

        // startPos.Y = min + schematic.OffsetY: ldloc min, ldloc schematic, callvirt get_OffsetY, add, stfld Y.
        var seats = Enumerable.Range(0, Math.Max(0, codes.Count - 4))
            .Where(i => Local(codes[i], store: false) == min
                        && Local(codes[i + 1], store: false) != null
                        && codes[i + 2].Calls(OffsetY)
                        && codes[i + 3].opcode == OpCodes.Add
                        && codes[i + 4].StoresField(PosY))
            .ToList();
        if (seats.Count != 1)
            return $"{seats.Count} places seat the ruin on the lowest sample where one was expected";

        foreach (var code in codes.Where(c => c.Calls(HeightAt)))
        {
            code.opcode = OpCodes.Call;
            code.operand = SampleMethod;
        }
        codes.Insert(seats[0] + 1, new CodeInstruction(OpCodes.Call, BaseHeightMethod));
        rewritten = codes;
        return null;
    }

    /// <summary>The local a <c>stloc</c> (or <c>ldloc</c>) names, or null for any other instruction.</summary>
    private static int? Local(CodeInstruction code, bool store)
    {
        var op = code.opcode;
        if (store)
        {
            if (op == OpCodes.Stloc_0) return 0;
            if (op == OpCodes.Stloc_1) return 1;
            if (op == OpCodes.Stloc_2) return 2;
            if (op == OpCodes.Stloc_3) return 3;
            if (op != OpCodes.Stloc_S && op != OpCodes.Stloc) return null;
        }
        else
        {
            if (op == OpCodes.Ldloc_0) return 0;
            if (op == OpCodes.Ldloc_1) return 1;
            if (op == OpCodes.Ldloc_2) return 2;
            if (op == OpCodes.Ldloc_3) return 3;
            if (op != OpCodes.Ldloc_S && op != OpCodes.Ldloc) return null;
        }
        return code.operand switch
        {
            LocalVariableInfo v => v.LocalIndex,
            byte b => b,
            sbyte s => s,
            short s => s,
            ushort u => u,
            int n => n,
            _ => null,
        };
    }
}
