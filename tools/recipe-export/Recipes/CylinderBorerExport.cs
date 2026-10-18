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
    /// <summary>What the borer keeps fitted, by item (the stages' materials summed): each one's kind, the code
    /// that stands for it, the items it may be and how many.</summary>
    public List<(string Kind, string Template, List<CollectibleObject> Stacks, int Quantity)> Kept = new();
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

    // CylinderBorer/Core/CylinderBorerParts.cs (MaterialsFor): the kept stages' materials, summed by item in the
    // order they first go in: the bushings' metal parts (a block) and the bar's (three rods, metal parts, the large
    // stainless gear), then the feed's (two oak planks, a rod, two ropes, a plate); rods and plates of iron, meteoric
    // iron or steel.
    private static readonly string[] Rods = ["game:rod-iron", "game:rod-meteoriciron", "game:rod-steel"];
    private static readonly string[] Plates = ["game:metalplate-iron", "game:metalplate-meteoriciron", "game:metalplate-steel"];

    public static readonly (string Kind, string Template, string[] Codes, int Quantity)[] KeptMaterials =
    [
        ("block", "game:metal-parts", ["game:metal-parts"], 2),
        ("item", "game:rod-iron", Rods, 4),
        ("item", "seraphhorizons:largegear-stainless", ["seraphhorizons:largegear-stainless"], 1),
        ("item", "game:plank-oak", ["game:plank-oak"], 2),
        ("item", "game:rope", ["game:rope"], 2),
        ("item", "game:metalplate-iron", Plates, 1),
    ];

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
        foreach (var (kind, template, codes, quantity) in KeptMaterials)
        {
            var stacks = codes.Select(code => kind == "block"
                    ? api.World.GetBlock(new AssetLocation(code)) is { IsMissing: false, Id: > 0, Code: not null } b ? b : null
                    : (CollectibleObject?)ItemOf(api, code))
                .OfType<CollectibleObject>().ToList();
            if (stacks.Count == 0) return null;
            data.Kept.Add((kind, template, stacks, quantity));
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
