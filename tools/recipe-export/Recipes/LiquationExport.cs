using System.Collections;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One full pan of one ore: its roasted concentrate, the metal poured (tin) and the residue
/// left in the pan (lead), in units.</summary>
public sealed record LiquationFill(string Ore, Item Concentrate, int Items, Item Ingot, double Units,
    IReadOnlyList<(string Metal, double Units)> Residue);

public sealed class LiquationData
{
    public required string Mod;
    public required Block Pan;
    public Block? Firepit;
    public double CapacityUnits, TinPoint, LeadPoint, SecondsPerIngot;
    public List<LiquationFill> Charges = new();
}

/// <summary>
/// Liquation in the clay pan (seraphhorizons, OreProcessing, #724; the mod's README "Liquation"): one
/// record per ore the pan takes, a full pan of it, kept under the lead point. The figures and the yield
/// are the mod's own (<c>OreProcessingSystem.RecoveryFor</c>, <c>Liquation.Liquates</c> and
/// <c>Liquation.Yield</c>), called by reflection as <see cref="CupellationExport"/> does. Null when the
/// mod is not loaded or its switch is off (no pan is registered).
/// </summary>
public static class LiquationExport
{
    public const string SystemType = "SeraphHorizons.Mod.Ore.Processing.OreProcessingSystem";
    public const string PanCode = "seraphhorizons:liquationpan-fired";
    public const string FirepitCode = "game:firepit-cold";
    public const double UnitsPerItem = 5;

    private static Item? ItemOf(ICoreServerAPI api, string code) =>
        api.World.GetItem(new AssetLocation(code)) is { IsMissing: false } i && i.Code != null ? i : null;

    private static Block? BlockOf(ICoreServerAPI api, string code) =>
        api.World.GetBlock(new AssetLocation(code)) is { IsMissing: false, Id: > 0 } b && b.Code != null ? b : null;

    private static double Dbl(object? o, string name) => GearChain.Prop(o, name) is { } v ? Convert.ToDouble(v) : 0;

    public static LiquationData? Read(ICoreServerAPI api)
    {
        if (GearChain.System(api, SystemType) is not { } system) return null;
        if (BlockOf(api, PanCode) is not { } pan) return null;
        var asm = system.GetType().Assembly;
        var liquation = asm.GetType("SeraphHorizons.Mod.Ore.Core.Liquation");
        var chargeType = asm.GetType("SeraphHorizons.Mod.Ore.Core.LiquationCharge");
        var products = asm.GetType("SeraphHorizons.Mod.Ore.Core.OreProducts");
        var recovery = system.GetType().GetMethod("RecoveryFor")?.Invoke(null, [api]);
        if (liquation == null || chargeType == null || products == null || recovery == null)
        {
            api.Logger.Warning("[seraphexport] the liquation pan is registered but the mod's liquation is not as expected; it is not exported");
            return null;
        }
        var settings = GearChain.Prop(recovery, "Liquation");
        var data = new LiquationData
        {
            Mod = GearChain.Mod,
            Pan = pan,
            Firepit = BlockOf(api, FirepitCode),
            CapacityUnits = Dbl(settings, "CapacityUnits"),
            TinPoint = Dbl(settings, "TinPoint"),
            LeadPoint = Dbl(settings, "LeadPoint"),
            SecondsPerIngot = Dbl(settings, "SecondsPerIngot"),
        };
        var oreOf = recovery.GetType().GetMethod("Ore", [typeof(string)])!;
        var liquates = liquation.GetMethod("Liquates")!;
        var yieldOf = liquation.GetMethod("Yield")!;
        var ores = (IEnumerable<string>)products.GetField("Ores")!.GetValue(null)!;
        foreach (var ore in ores)
        {
            var spec = oreOf.Invoke(recovery, [ore]);
            if (liquates.Invoke(null, [spec]) is not true) continue;
            if (ItemOf(api, $"seraphhorizons:roastedconcentrate-{ore}") is not { } concentrate) continue;
            int items = (int)Math.Floor(data.CapacityUnits / UnitsPerItem);
            if (items <= 0) continue;
            var charge = Array.CreateInstance(chargeType, 1);
            charge.SetValue(Activator.CreateInstance(chargeType, ore, items * UnitsPerItem, false), 0);
            var y = yieldOf.Invoke(null, [recovery, charge, false]);
            if (ItemOf(api, "game:ingot-" + (string)GearChain.Prop(y, "Metal")!) is not { } ingot) continue;
            var residue = ((IEnumerable)GearChain.Prop(y, "Residue")!).Cast<object>()
                .Select(m => ((string)GearChain.Prop(m, "Metal")!, Dbl(m, "Units"))).ToList();
            data.Charges.Add(new LiquationFill(ore, concentrate, items, ingot, Dbl(y, "Units"), residue));
        }
        return data.Charges.Count == 0 ? null : data;
    }
}
