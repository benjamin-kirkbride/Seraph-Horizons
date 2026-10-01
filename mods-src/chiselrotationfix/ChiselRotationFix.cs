using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.ChiselRotationFix;

/// <summary>
/// A rotated schematic (worldgen's 90/180/270° copies, WorldEdit's rotated imports) is built by
/// BlockSchematic.TransformWhilePacked. It hands each chiseled block's stored tree to
/// BlockEntityMicroBlock.OnTransformed, which rewrites "materials" from schematic ids to world
/// block ids. Placement later calls OnLoadCollectibleMappings, which reads the same ids as
/// schematic ids and maps them through the schematic's BlockCodes again. A world id that is
/// also a different key in BlockCodes becomes that block: in this pack, BetterRuins' aged granite
/// turns into overlay-damagedstone and renders see-through
/// (https://github.com/anegostudios/VintageStory-Issues/issues/9495).
///
/// The patch puts the ids OnTransformed resolved back into schematic id space, so placement
/// maps them once, as it does for an unrotated schematic. /chiselfix (ChiselRepair) repairs
/// chiseled blocks that worldgen placed before the mod was installed.
/// </summary>
public class ChiselRotationFixSystem : ModSystem
{
    public const string HarmonyId = "chiselrotationfix";

    private Harmony? _harmony;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartPre(ICoreAPI api)
    {
        var target = OnTransformedPatch.Target();
        if (target == null)
        {
            api.Logger.Warning("[chiselrotationfix] BlockEntityMicroBlock.OnTransformed not found; "
                               + "the game changed, so rotated chiseled blocks are left as the game builds them");
            return;
        }
        _harmony = new Harmony(HarmonyId);
        _harmony.Patch(target,
            prefix: new HarmonyMethod(typeof(OnTransformedPatch), nameof(OnTransformedPatch.Prefix)),
            postfix: new HarmonyMethod(typeof(OnTransformedPatch), nameof(OnTransformedPatch.Postfix)));
        OnTransformedPatch.Applied = true;
    }

    public override void StartServerSide(ICoreServerAPI api) => ChiselRepairCommand.Register(api);

    public override void Dispose()
    {
        _harmony?.UnpatchAll(HarmonyId);
        OnTransformedPatch.Applied = false;
    }
}

public static class OnTransformedPatch
{
    /// <summary>Whether the patch is on, so rotating a schematic gives the right materials.</summary>
    public static bool Applied { get; internal set; }

    // Set by RunUnpatched, on this thread only: worldgen rotates schematics on its own thread.
    [ThreadStatic] private static bool _bypass;

    /// <summary>
    /// Runs <paramref name="action"/> with the patch skipped on this thread, so a schematic it
    /// rotates comes out as the unpatched game builds it.
    /// </summary>
    public static void RunUnpatched(Action action)
    {
        bool was = _bypass;
        _bypass = true;
        try { action(); }
        finally { _bypass = was; }
    }

    public static MethodInfo? Target() =>
        AccessTools.DeclaredMethod(typeof(BlockEntityMicroBlock), nameof(BlockEntityMicroBlock.OnTransformed), new[]
        {
            typeof(IWorldAccessor), typeof(ITreeAttribute), typeof(int),
            typeof(Dictionary<int, AssetLocation>), typeof(Dictionary<int, AssetLocation>), typeof(EnumAxis?),
        });

    /// <summary>
    /// Which entries of "materials" OnTransformed is about to resolve to a world id: those whose
    /// schematic id is in the mapping and names a registered block. It leaves the others as they are.
    /// </summary>
    public static void Prefix(IWorldAccessor worldAccessor, ITreeAttribute tree,
                              Dictionary<int, AssetLocation>? oldBlockIdMapping, out bool[]? __state)
    {
        __state = null;
        if (_bypass || oldBlockIdMapping == null || (tree["materials"] as IntArrayAttribute)?.value is not int[] materials)
            return;
        __state = new bool[materials.Length];
        for (int i = 0; i < materials.Length; i++)
            __state[i] = oldBlockIdMapping.TryGetValue(materials[i], out var code) && worldAccessor.GetBlock(code) != null;
    }

    /// <summary>
    /// Swaps each world id OnTransformed wrote for a schematic id of the same block, so that
    /// OnLoadCollectibleMappings resolves it to that block.
    /// </summary>
    public static void Postfix(IWorldAccessor worldAccessor, ITreeAttribute tree,
                               Dictionary<int, AssetLocation>? oldBlockIdMapping, bool[]? __state)
    {
        if (__state == null || oldBlockIdMapping == null
            || (tree["materials"] as IntArrayAttribute)?.value is not int[] materials || materials.Length != __state.Length)
            return;

        int floor = worldAccessor.Blocks.Count;
        var fixedIds = (int[])materials.Clone();
        for (int i = 0; i < fixedIds.Length; i++)
        {
            if (!__state[i])
                continue;
            var block = worldAccessor.GetBlock(fixedIds[i]);
            if (block?.Code == null)
                continue;
            fixedIds[i] = SchematicIdFor(oldBlockIdMapping, block.Code, floor);
        }
        tree["materials"] = new IntArrayAttribute(fixedIds);
    }

    /// <summary>
    /// A key of <paramref name="mapping"/> that names <paramref name="code"/>, adding one if needed.
    /// The key is at least <paramref name="floor"/> (the world's block count). TransformWhilePacked
    /// ends with BlockSchematic.Pack, which writes a key for the world id of every block placed in
    /// the grid; keys at or above the block count are never world ids, so Pack cannot overwrite them.
    /// </summary>
    public static int SchematicIdFor(Dictionary<int, AssetLocation> mapping, AssetLocation code, int floor)
    {
        int max = floor - 1;
        foreach (var (key, value) in mapping)
        {
            if (key >= floor && code.Equals(value))
                return key;
            max = Math.Max(max, key);
        }
        int added = max + 1;
        mapping[added] = code;
        return added;
    }
}
