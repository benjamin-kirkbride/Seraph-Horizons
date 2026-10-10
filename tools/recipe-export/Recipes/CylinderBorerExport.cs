using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One metal the cylinder borer bores: the cast cylinder (consumed), the bored cylinder it
/// becomes, and the axle turns a block of the cutter head's travel takes.</summary>
public sealed record BoreClass(string Name, string Metal, Item Blank, Item Cylinder, double TurnsPerBlock);

public sealed class CylinderBorerData
{
    public required string Mod;
    public required string FrameCode;
    public Block? Frame;
    /// <summary>The fitted parts the borer keeps: bushings, bar, feed.</summary>
    public List<Item> Kept = new();
    public required string HeadCode;
    public Item? Head;
    /// <summary>The cutter head's travel through a bore, blocks (the rig's work.end).</summary>
    public required double Travel;
    public required int HeadWear;
    public required double DrainPerCylinder;
    public required double Tank;
    public List<Oil> Oils = new();
    public List<BoreClass> Classes = new();
}

/// <summary>
/// The cylinder borer (seraphhorizons, CylinderBorer/; #639): its process, read as the gear cutter's
/// is (<see cref="GearChain.Cutter"/>): the rig (config/cylinderborer-rig.json) gives the head's travel
/// and the casting and cylinder of each class; the gameplay's CylinderBorerSettings
/// (TurnsPerBlockIron, TurnsPerBlockSteel, HeadWearPerCylinder) and the MachineOil entry CylinderBorer
/// (Tank, DrainPerJob, per cylinder) are read live from the server's config. Null when the mod is not
/// loaded, its switch is off or what it names is not registered.
/// </summary>
public static class CylinderBorerExport
{
    public static readonly AssetLocation RigAsset = new(GearChain.Mod, "config/cylinderborer-rig.json");
    public const string FrameCode = "seraphhorizons:cylinderborer-frame-north";
    public const string HeadCode = "seraphhorizons:cylinderborerhead-steel";

    // CylinderBorer/Core/CylinderBorerParts.cs: the kept stages, in order.
    public static readonly string[] KeptCodes =
        ["seraphhorizons:cylinderborerbushings", "seraphhorizons:cylinderborerbar", "seraphhorizons:cylinderborerfeed"];

    private static Item? ItemOf(ICoreServerAPI api, string code) =>
        api.World.GetItem(new AssetLocation(code)) is { IsMissing: false } i && i.Code != null ? i : null;

    private static double Dbl(object? o, string name, double fallback) =>
        GearChain.Prop(o, name) is { } v ? Convert.ToDouble(v) : fallback;

    public static CylinderBorerData? Read(ICoreServerAPI api)
    {
        if (GearChain.System(api, GearChain.MainSystem) is not { } mainSystem || api.Assets.TryGet(RigAsset) is not { } asset) return null;
        var config = mainSystem.GetType().GetMethod("ConfigFor")?.Invoke(null, new object[] { api });
        if (GearChain.Prop(config, "CylinderBorer") is false) return null;
        var settings = GearChain.Prop(config, "CylinderBorerSettings");
        var oilSettings = GearChain.Prop(GearChain.System(api, GearChain.OilSystem), "Config");
        var borerOil = GearChain.Prop(oilSettings, "CylinderBorer");

        JObject rig;
        try { rig = JObject.Parse(asset.ToText()); }
        catch (Exception e)
        {
            api.Logger.Warning("[seraphexport] cannot read {0}: {1}; the cylinder borer is not exported", RigAsset, e.Message);
            return null;
        }
        var bore = rig["bore"] as JObject;
        var data = new CylinderBorerData
        {
            Mod = GearChain.Mod,
            FrameCode = FrameCode,
            Frame = api.World.GetBlock(new AssetLocation(FrameCode)) is { IsMissing: false, Code: not null } f ? f : null,
            HeadCode = HeadCode,
            Head = ItemOf(api, HeadCode),
            Travel = (double?)rig["work"]?["end"]?["thin"] ?? 18.0 / 16,
            HeadWear = (int)Dbl(settings, "HeadWearPerCylinder", 1),
            DrainPerCylinder = Dbl(borerOil, "DrainPerJob", 10),
            Tank = Dbl(borerOil, "Tank", GearChain.DefaultTank),
        };
        foreach (var code in KeptCodes)
        {
            if (ItemOf(api, code) is not { } item) return null;
            data.Kept.Add(item);
        }
        foreach (var (name, metal, turnsKey) in new[] { ("thin", "iron", "TurnsPerBlockIron"), ("thick", "steel", "TurnsPerBlockSteel") })
        {
            if ((string?)bore?["blanks"]?[name] is not { } blankCode || ItemOf(api, blankCode) is not { } blank) continue;
            if ((string?)bore?["cylinders"]?[name] is not { } cylinderCode || ItemOf(api, cylinderCode) is not { } cylinder) continue;
            double turns = Dbl(settings, turnsKey, (double?)bore?["turnsPerBlock"]?[name] ?? 0);
            if (!(turns > 0)) continue;
            data.Classes.Add(new BoreClass(name, metal, blank, cylinder, turns));
        }
        if (data.Classes.Count == 0) return null;
        data.Oils.AddRange(DrawBenchExport.OilsOf(api, oilSettings));
        return data;
    }
}
