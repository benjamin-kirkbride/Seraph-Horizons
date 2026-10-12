using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.ServerMods;

namespace SeraphHorizons.Mod.Ore;

/// <summary>
/// Smaller deposits (#439): each Interesting Ore Gen vein variant of a managed metal is scaled by
/// its metal's factor from <c>config/ore-sizes.json</c> (<see cref="VeinScaling"/>), and with ore
/// processing on, a graded ore's by <see cref="OreProducts.DepositBlocks"/> more (#863: its items
/// hold about a quarter of vanilla's units, so a deposit four times the volume holds the metal the
/// factor gave it; <see cref="OreSizeTable.VolumeFor"/>). With <c>SmallerDeposits</c> off and ore
/// processing on it still binds, without the metals' factors, so graded ore's veins hold vanilla's
/// metal. It acts on
/// the generator instances the game's <c>GenDeposits</c> builds from the deposit files (their
/// public <c>Radius</c>, <c>BranchCount</c> and <c>BranchLength</c>), in a postfix on its
/// <c>initAssets</c>, so it follows every variant whatever patches added it, and a worldgen
/// reload (<c>GenDeposits.reloadWorldGen</c>) scales the new generators again. The deposit files
/// themselves are left as they are.
/// </summary>
internal static class DepositSizes
{
    public static readonly AssetLocation SizesAsset = new("seraphhorizons", "config/ore-sizes.json");

    private static OreSizeTable? _table;
    private static double _gradedOreBlocks = 1;
    private static ILogger? _logger;

    private static MethodInfo? InitAssets => AccessTools.Method(typeof(GenDeposits), nameof(GenDeposits.initAssets));

    /// <summary>Why it can't bind here, or null if it can.</summary>
    public static string? Unsupported(ICoreAPI api)
    {
        if (!api.ModLoader.IsModEnabled(OreCellPlacement.IogModId)) return "Interesting Ore Gen is not loaded";
        if (OreCellPlacement.GeneratorType is not { } type) return $"{OreCellPlacement.GeneratorTypeName} is gone";
        foreach (var name in new[] { "Radius", "BranchCount", "BranchLength" })
            if (AccessTools.Field(type, name)?.FieldType != typeof(NatFloat)) return $"its {name} is gone or changed";
        if (InitAssets is null) return "GenDeposits.initAssets is gone";
        return null;
    }

    public static OreSizeTable LoadTable(ICoreAPI api) =>
        new(api.Assets.Get(SizesAsset).ToObject<Dictionary<string, OreSizeTable.Entry>>());

    /// <param name="gradedOreBlocks">The volume a graded ore's vein is scaled by over its metal's
    /// factor: <see cref="OreProducts.DepositBlocks"/> with ore processing on, else 1.</param>
    /// <param name="cut">Whether the metals' factors apply (<c>SmallerDeposits</c>); without them
    /// only graded ore is scaled, by <paramref name="gradedOreBlocks"/>.</param>
    public static void Bind(ICoreAPI api, Harmony harmony, double gradedOreBlocks, bool cut)
    {
        _table = cut ? LoadTable(api) : new OreSizeTable(new Dictionary<string, OreSizeTable.Entry>());
        _gradedOreBlocks = gradedOreBlocks;
        _logger = api.Logger;
        harmony.Patch(InitAssets, postfix: new HarmonyMethod(typeof(DepositSizes), nameof(InitAssetsPostfix)));
    }

    public static void Unbind()
    {
        _table = null;
        _gradedOreBlocks = 1;
        _logger = null;
    }

    private static void InitAssetsPostfix(GenDeposits __instance)
    {
        if (_table is not { } table || OreCellPlacement.GeneratorType is not { } type) return;
        var scaled = new SortedDictionary<string, double>(StringComparer.Ordinal);
        int variants = 0;
        foreach (var variant in __instance.Deposits ?? [])
        {
            if (variant.GeneratorInst is not { } gen || !type.IsInstanceOfType(gen)) continue;
            if (OreMetals.MetalOf(variant.Code) is not { } metal || table.VolumeFor(variant.Code, _gradedOreBlocks) is not { } factor) continue;
            var radius = (NatFloat)AccessTools.Field(type, "Radius").GetValue(gen)!;
            var count = (NatFloat)AccessTools.Field(type, "BranchCount").GetValue(gen)!;
            var length = (NatFloat)AccessTools.Field(type, "BranchLength").GetValue(gen)!;
            string veinType = variant.Attributes?["inblock"]?["veintype"]?.AsString() ?? "disc";
            var shape = new VeinShape(veinType, Of(radius), Of(count), Of(length));
            var result = VeinScaling.Scale(shape, factor);
            if (result == shape) continue;
            Set(radius, result.Radius);
            Set(count, result.BranchCount);
            Set(length, result.BranchLength);
            scaled[variant.Code] = factor;
            variants++;
        }
        _logger?.Notification("[seraphhorizons] Smaller deposits: scaled {0} Interesting Ore Gen vein variants, graded ore x{1} for its units ({2})",
            variants, _gradedOreBlocks, string.Join(", ", scaled.Select(kv => $"{kv.Key} x{kv.Value:0.###}")));
    }

    private static SizeRange Of(NatFloat f) => new(f.avg, f.var);

    private static void Set(NatFloat f, SizeRange r)
    {
        f.avg = r.Avg;
        f.var = r.Var;
    }
}
