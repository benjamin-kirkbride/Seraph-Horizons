using System.Collections;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One full cupel of one ore: its roasted concentrate, the lead added (none for a lead ore),
/// and what the bead breaks into, in units (litharge and metal bits are 5 units each).</summary>
public sealed record CupelFill(string Ore, Item Concentrate, int Items, int LeadBits, double LeadUnits,
    IReadOnlyList<(string Metal, double Units)> Metals);

public sealed class CupellationData
{
    public required string Mod;
    public required Block Cupel;
    public required Item Litharge;
    public Item? LeadBit;
    public Block? Forge;
    public double CapacityUnits, LeadPerOreUnit, MeltingPoint, SecondsPerIngot;
    public List<CupelFill> Charges = new();
}

/// <summary>
/// Cupellation in the bone-ash cupel (seraphhorizons, OreProcessing, #722; the mod's README
/// "Cupellation"): one record per ore the cupel takes, a full cupel of it. The figures and the yield
/// are the mod's own (<c>OreProcessingSystem.RecoveryFor</c>, <c>Cupellation.Cupels</c> and
/// <c>Cupellation.Yield</c>), called by reflection as <see cref="GearChain"/> reads the mod. Null
/// when the mod is not loaded or its switch is off (no cupel is registered).
/// </summary>
public static class CupellationExport
{
    public const string SystemType = "SeraphHorizons.Mod.Ore.Processing.OreProcessingSystem";
    public const string CupelCode = "seraphhorizons:cupel-fired";
    public const string LithargeCode = "seraphhorizons:litharge";
    public const string LeadBitCode = "game:metalbit-lead";
    public const double UnitsPerItem = 5;

    private static Item? ItemOf(ICoreServerAPI api, string code) =>
        api.World.GetItem(new AssetLocation(code)) is { IsMissing: false } i && i.Code != null ? i : null;

    private static Block? BlockOf(ICoreServerAPI api, string code) =>
        api.World.GetBlock(new AssetLocation(code)) is { IsMissing: false, Id: > 0 } b && b.Code != null ? b : null;

    private static double Dbl(object? o, string name) => GearChain.Prop(o, name) is { } v ? Convert.ToDouble(v) : 0;

    public static CupellationData? Read(ICoreServerAPI api)
    {
        if (GearChain.System(api, SystemType) is not { } system) return null;
        if (BlockOf(api, CupelCode) is not { } cupel || ItemOf(api, LithargeCode) is not { } litharge) return null;
        var asm = system.GetType().Assembly;
        var cupellation = asm.GetType("SeraphHorizons.Mod.Ore.Core.Cupellation");
        var chargeType = asm.GetType("SeraphHorizons.Mod.Ore.Core.CupelCharge");
        var products = asm.GetType("SeraphHorizons.Mod.Ore.Core.OreProducts");
        var recovery = system.GetType().GetMethod("RecoveryFor")?.Invoke(null, [api]);
        if (cupellation == null || chargeType == null || products == null || recovery == null)
        {
            api.Logger.Warning("[seraphexport] the cupel is registered but the mod's cupellation is not as expected; it is not exported");
            return null;
        }
        var settings = GearChain.Prop(recovery, "Cupel");
        var data = new CupellationData
        {
            Mod = GearChain.Mod,
            Cupel = cupel,
            Litharge = litharge,
            LeadBit = ItemOf(api, LeadBitCode),
            Forge = BlockOf(api, "game:forge"),
            CapacityUnits = Dbl(settings, "CapacityUnits"),
            LeadPerOreUnit = Dbl(settings, "LeadPerOreUnit"),
            MeltingPoint = Dbl(settings, "MeltingPoint"),
            SecondsPerIngot = Dbl(settings, "SecondsPerIngot"),
        };
        var oreOf = recovery.GetType().GetMethod("Ore", [typeof(string)])!;
        var cupels = cupellation.GetMethod("Cupels")!;
        var yieldOf = cupellation.GetMethod("Yield")!;
        var ores = (IEnumerable<string>)products.GetField("Ores")!.GetValue(null)!;
        foreach (var ore in ores)
        {
            var spec = oreOf.Invoke(recovery, [ore]);
            if (cupels.Invoke(null, [spec]) is not true) continue;
            if (ItemOf(api, $"seraphhorizons:roastedconcentrate-{ore}") is not { } concentrate) continue;
            bool leadOre = GearChain.Prop(spec, "Metal") as string == "lead";
            int items = (int)Math.Floor(data.CapacityUnits / (leadOre ? 1 : 1 + data.LeadPerOreUnit) / UnitsPerItem);
            int leadBits = leadOre ? 0 : (int)Math.Ceiling(items * data.LeadPerOreUnit);
            if (items <= 0 || leadBits > 0 && data.LeadBit == null) continue;
            var charge = Array.CreateInstance(chargeType, leadBits > 0 ? 2 : 1);
            charge.SetValue(Activator.CreateInstance(chargeType, ore, items * UnitsPerItem, false), 0);
            if (leadBits > 0) charge.SetValue(Activator.CreateInstance(chargeType, null, leadBits * UnitsPerItem, false), 1);
            var y = yieldOf.Invoke(null, [recovery, charge]);
            var metals = ((IEnumerable)GearChain.Prop(y, "Metals")!).Cast<object>()
                .Select(m => ((string)GearChain.Prop(m, "Metal")!, Dbl(m, "Units"))).ToList();
            data.Charges.Add(new CupelFill(ore, concentrate, items, leadBits, Dbl(y, "LeadUnits"), metals));
        }
        return data.Charges.Count == 0 ? null : data;
    }
}
