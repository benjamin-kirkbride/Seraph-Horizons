using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Ore;

/// <summary>
/// Grade pockets in the world (#944, under <c>GradePockets</c>): a prefix on Interesting Ore Gen
/// 2.3.8's <c>TiltedDiscDepositGenerator.TryPlaceMainOre(IBlockAccessor, BlockPos, int)</c>
/// (private, instance), which every vein shape calls per block with the grade index drawn once for
/// the vein (or a chimney's core grade), rewrites that index from <see cref="GradePockets"/>. IOG
/// then takes <c>Blocks[min(len - 1, index)]</c> from the grades the host rock allows
/// (<c>placeBlockByInBlockId</c>, by the block there), so the prefix reads the same entry to know
/// that range and the ore's units (<see cref="OreProducts.RawUnits"/>, from the blocks' codes
/// <c>ore-{grade}-{ore}-{rock}</c>). It draws nothing from IOG's <c>DepositRand</c>, which every
/// chunk pass over a vein must replay alike. Prospect veins (<c>TryPlaceTracerOre</c>, always the
/// poorest grade), sub-deposits (<c>TryPlaceSubOre</c>) and hydrothermal district ore, which never
/// comes through here, keep their grades.
/// </summary>
public static class GradePocketPlacement
{
    public const string MethodName = "TryPlaceMainOre";
    public const string FieldName = "placeBlockByInBlockId";

    private static GradePockets? _pockets;

    /// <summary>The pockets in force, if bound (the Atlas scenario reads them).</summary>
    public static GradePockets? Pockets => _pockets;

    // Per IOG ResolvedDepositBlock: its grades' units, poorest first (null: not a graded ore).
    private static readonly ConditionalWeakTable<object, double[]?> UnitsByEntry = new();
    private static FieldInfo? _blocksField;

    private static MethodInfo? Method => OreCellPlacement.GeneratorType is { } type
        && AccessTools.Method(type, MethodName, [typeof(IBlockAccessor), typeof(BlockPos), typeof(int)]) is { } m
        && !m.IsStatic
            ? m
            : null;

    /// <summary>Why it can't bind here, or null if it can.</summary>
    public static string? Unsupported(ICoreAPI api)
    {
        if (!api.ModLoader.IsModEnabled(OreCellPlacement.IogModId)) return "Interesting Ore Gen is not loaded";
        if (OreCellPlacement.GeneratorType is not { } type) return $"{OreCellPlacement.GeneratorTypeName} is gone";
        if (Method is null) return $"{OreCellPlacement.GeneratorTypeName}.{MethodName}(IBlockAccessor, BlockPos, int) is gone or changed";
        if (AccessTools.Field(type, FieldName) is not { } f || !typeof(IDictionary).IsAssignableFrom(f.FieldType))
            return $"{OreCellPlacement.GeneratorTypeName}.{FieldName} is gone or changed";
        var entry = f.FieldType.IsGenericType ? f.FieldType.GetGenericArguments()[^1] : null;
        if (entry == null || AccessTools.Field(entry, "Blocks")?.FieldType != typeof(Block[]))
            return "ResolvedDepositBlock.Blocks is gone or changed";
        return null;
    }

    public static void Bind(ICoreServerAPI api, Harmony harmony, GradePocketSettings settings)
    {
        _pockets = new GradePockets(api.WorldManager.Seed, settings);
        var field = AccessTools.Field(OreCellPlacement.GeneratorType!, FieldName);
        _blocksField = AccessTools.Field(field.FieldType.GetGenericArguments()[^1], "Blocks");
        harmony.Patch(Method, prefix: new HarmonyMethod(typeof(GradePocketPlacement), nameof(Prefix)));
        var s = _pockets.Settings;
        api.Logger.Notification(
            "[seraphhorizons] Grade pockets: bound {0}.{1}; pockets of {2:0.#} blocks, {3:0%} of a vein's blocks a grade off ({4:0%} at an end), weighted by {5}",
            OreCellPlacement.GeneratorTypeName, MethodName, s.PocketSize, s.Spread, s.EndSpread, s.Weighting.ToString().ToLowerInvariant());
    }

    public static void Unbind()
    {
        _pockets = null;
        _blocksField = null;
    }

    /// <summary>The config's settings: an unknown weighting falls back to units.</summary>
    public static GradePocketSettings SettingsFrom(SeraphHorizonsConfig config) => new GradePocketSettings(
        config.GradePocketSizeBlocks, config.GradePocketSpread, config.GradePocketEndSpread,
        string.Equals(config.GradePocketWeighting, "index", StringComparison.OrdinalIgnoreCase) ? GradeWeighting.Index : GradeWeighting.Units).Clamped();

    private static void Prefix(IBlockAccessor blockAccessor, BlockPos pos, ref int oldGradeIndex, object ___placeBlockByInBlockId)
    {
        if (_pockets is not { } pockets || ___placeBlockByInBlockId is not IDictionary byInBlock) return;
        // The block IOG will read, as it reads it.
        if (!blockAccessor.IsValidPos(pos) || blockAccessor.GetChunkAtBlockPos(pos) is not IServerChunk { Data: { } data }) return;
        int index3d = (pos.Y % 32 * 32 + pos.Z % 32) * 32 + pos.X % 32;
        var entry = byInBlock[data.GetBlockIdUnsafe(index3d)];
        if (entry == null || UnitsOf(entry) is not { } units) return;
        oldGradeIndex = pockets.GradeAt(units, oldGradeIndex, pos.X, pos.Y, pos.Z);
    }

    private static double[]? UnitsOf(object entry) =>
        UnitsByEntry.GetValue(entry, e => _blocksField?.GetValue(e) is Block[] blocks ? Units(blocks) : null);

    /// <summary>Each grade's units, poorest first, from the blocks' codes; by index for an ore whose
    /// units aren't known; null unless every block is a graded ore.</summary>
    public static double[]? Units(Block[] blocks)
    {
        if (blocks.Length <= 1) return null;
        var units = new double[blocks.Length];
        bool known = true;
        for (int i = 0; i < blocks.Length; i++)
        {
            if (blocks[i]?.Code is not { } code) return null;
            if (OreTally.Parse(code.Path) is not { } kind || !OreProducts.IsGrade(kind.Grade)) return null;
            if (OreProducts.RawUnits(kind.Ore, kind.Grade) is { } u) units[i] = u;
            else known = false;
        }
        if (!known)
            for (int i = 0; i < units.Length; i++) units[i] = i + 1;
        return units;
    }
}
