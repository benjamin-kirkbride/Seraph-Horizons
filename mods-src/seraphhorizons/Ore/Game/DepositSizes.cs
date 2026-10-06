using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.ServerMods;

namespace SeraphHorizons.Mod.Ore;

/// <summary>
/// Smaller deposits (#439): each Interesting Ore Gen vein variant of a managed metal is scaled by
/// its metal's factor from <c>config/ore-sizes.json</c> (<see cref="VeinScaling"/>). It acts on
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

    public static void Bind(ICoreAPI api, Harmony harmony)
    {
        _table = LoadTable(api);
        _logger = api.Logger;
        harmony.Patch(InitAssets, postfix: new HarmonyMethod(typeof(DepositSizes), nameof(InitAssetsPostfix)));
    }

    public static void Unbind()
    {
        _table = null;
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
            if (OreMetals.MetalOf(variant.Code) is not { } metal || table.FactorFor(metal) is not { } factor || factor >= 1) continue;
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
            scaled[metal] = factor;
            variants++;
        }
        _logger?.Notification("[seraphhorizons] Smaller deposits: scaled {0} Interesting Ore Gen vein variants ({1})",
            variants, string.Join(", ", scaled.Select(kv => $"{kv.Key} x{kv.Value:0.###}")));
    }

    private static SizeRange Of(NatFloat f) => new(f.avg, f.var);

    private static void Set(NatFloat f, SizeRange r)
    {
        f.avg = r.Avg;
        f.var = r.Var;
    }
}
