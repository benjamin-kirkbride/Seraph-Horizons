using System.Text.Json;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.Machines.Core;
using static SeraphHorizons.Mod.Machines.Core.RigJson;

namespace SeraphHorizons.Mod.ParkesKettle.Core;

/// <summary>The Parkes kettle's part <c>requires</c> besides its stages' (<c>brickwork</c>, <c>lining</c>,
/// <c>kettle</c>, <c>stirrer</c>): gameplay's state (ParkesKettle/README.md, "Rig schema"). The frame block type
/// lists them as its <c>extraRequires</c>.</summary>
public static class KettleRequires
{
    public const string Brickwork = "brickwork", Lining = "lining", Kettle = "kettle", Stirrer = "stirrer";
    public const string Zinc = "zinc", Crust = "crust", OutletCap = "outletcap";
    public const string DamperOpen = "damperopen", DamperHalf = "damperhalf", DamperShut = "dampershut";

    public static string Lead(int level) => "lead" + level;
    public static string Fuel(int layer) => "fuel" + layer;
    public static string Fire(int layer) => "fire" + layer;

    /// <summary>Every one of them, for <c>extraRequires</c>.</summary>
    public static IReadOnlyList<string> Extra(int leadLevels, int fuelLayers) =>
    [
        .. Enumerable.Range(1, leadLevels).Select(Lead),
        Zinc, Crust,
        .. Enumerable.Range(1, fuelLayers).Select(Fuel),
        .. Enumerable.Range(1, fuelLayers).Select(Fire),
        DamperOpen, DamperHalf, DamperShut, OutletCap,
    ];
}

/// <summary>
/// What gameplay reads of <c>config/parkeskettle-rig.json</c> besides the shared ore machine rig's cells, anchors and
/// parts (written by ParkesKettle/tools/make_shape.py; ParkesKettle/README.md, "Rig schema"): the outlet's cell, the
/// charge as drawn, the bath's levels, the grate, the fuel a charge burns, the cap, the brick types and the row's
/// chimney rules. Throws <see cref="FormatException"/> naming what is wrong.
/// </summary>
/// <param name="Flue">The outlet's cell, with the chimney (or the cap) on its top, the cap, the brick types the
/// brickwork may be, and the row's chimney rules.</param>
/// <param name="ChargeUnits">Units of lead the kettle drawn full holds (ore-processing.json's <c>charge</c> is the rule).</param>
/// <param name="LeadLevels">The bath's drawn levels.</param>
/// <param name="FuelLayers">The grate's layers of coal.</param>
/// <param name="FuelPerLayer">Coal a layer.</param>
/// <param name="CoalPerCharge">Coal a full charge burns in its hours (#927's ruling: 6).</param>
public sealed record ParkesKettleRig(FurnaceFlue Flue, int ChargeUnits, int LeadLevels, int FuelLayers, int FuelPerLayer,
    int CoalPerCharge) : FiredRig(Flue)
{
    /// <summary>The coal the grate holds.</summary>
    public int Grate => FuelLayers * FuelPerLayer;

    public static ParkesKettleRig Parse(string json)
    {
        using var doc = RigJson.Parse(json);
        var root = doc.RootElement;
        var flue = FurnaceFlue.Parse(root, Cells(root), whose: "kettle");
        var furnace = Required(root, "furnace", JsonValueKind.Object);
        int Int(JsonElement obj, string key) => Required(obj, key, JsonValueKind.Number).GetInt32();
        var rig = new ParkesKettleRig(flue, Int(furnace, "chargeUnits"), Int(furnace, "leadLevels"), Int(furnace, "fuelLayers"),
            Int(furnace, "fuelPerLayer"), Int(furnace, "coalPerCharge"));
        if (rig.ChargeUnits < 1 || rig.LeadLevels < 1 || rig.FuelLayers < 1 || rig.FuelPerLayer < 1)
            throw new FormatException("furnace.chargeUnits, leadLevels, fuelLayers and fuelPerLayer must be positive");
        if (rig.CoalPerCharge < 1 || rig.CoalPerCharge > rig.Grate)
            throw new FormatException($"furnace.coalPerCharge {rig.CoalPerCharge} must be positive and fit the grate ({rig.Grate})");
        return rig;
    }
}
