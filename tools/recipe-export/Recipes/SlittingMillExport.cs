using System.Collections;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One metal the slitting mill slits: the plate (consumed), the nails and strips it becomes and the knife's
/// wear a plate of it.</summary>
public sealed record SlitClass(string Metal, Item Plate, Item Output, int KnifeWear);

public sealed class SlittingMillData
{
    public required string Mod;
    public required string FrameCode;
    public Block? Frame;
    /// <summary>What the mill keeps fitted, by item (the stages' materials summed): each one's kind, the code
    /// that stands for it, the items it may be and how many.</summary>
    public List<(string Kind, string Template, List<CollectibleObject> Stacks, int Quantity)> Kept = new();
    /// <summary>The cleavers the knife may be, steel first.</summary>
    public List<Item> Knives = new();
    /// <summary>Knife strokes a plate takes (the rig's work.end).</summary>
    public required double Strokes;
    public required double TurnsPerStroke;
    public required int NailsPerPlate;
    public required double DrainPerPlate;
    public required double Tank;
    public List<Oil> Oils = new();
    public List<SlitClass> Classes = new();
}

/// <summary>
/// The slitting mill (seraphhorizons, SlittingMill/): its process, read as the cylinder borer's is
/// (<see cref="CylinderBorerExport"/>): the rig (config/slittingmill-rig.json) gives the strokes a plate and the
/// plate and output codes; the gameplay's SlittingMillSettings (TurnsPerStroke, NailsPerPlate, Metals: each
/// metal's KnifeWear) and the MachineOil entry SlittingMill (Tank, DrainPerJob, per plate) are read live from the
/// server's config. Null when the mod is not loaded, its switch is off or what it names is not registered.
/// </summary>
public static class SlittingMillExport
{
    public static readonly AssetLocation RigAsset = new(GearChain.Mod, "config/slittingmill-rig.json");
    public const string FrameCode = "seraphhorizons:slittingmill-frame-north";

    /// <summary>The knife's cleavers (SlittingMillBuild.KnifeCodes), steel first.</summary>
    public static readonly string[] KnifeCodes = ["game:cleaver-steel", "game:cleaver-meteoriciron", "game:cleaver-iron"];

    /// <summary>The metals it slits (Slitting.Metals).</summary>
    public static readonly string[] Metals =
    [
        "copper", "tinbronze", "bismuthbronze", "blackbronze", "brass", "silver", "gold",
        "iron", "meteoriciron", "steel", "molybdochalkos", "electrum",
    ];

    // config/builds/slittingmill.json: the kept stages' materials, summed by item in the order they first go in:
    // the rolls' ten cutters and two rods, the pinions' Jonas gears and metal parts (a block), the drive's gearbox
    // and two rods, the shear's bracket and plate, the push bar's two rods and plank; rods, plates and brackets of
    // iron, meteoric iron or steel.
    private static readonly string[] Rods = ["game:rod-iron", "game:rod-meteoriciron", "game:rod-steel"];

    public static readonly (string Kind, string Template, string[] Codes, int Quantity)[] KeptMaterials =
    [
        ("item", "seraphhorizons:slittercutter-steel", ["seraphhorizons:slittercutter-steel"], 10),
        ("item", "game:rod-iron", Rods, 6),
        ("item", "game:jonasframes-gears01", ["game:jonasframes-gears01", "game:jonasframes-gears02"], 1),
        ("block", "game:metal-parts", ["game:metal-parts"], 1),
        ("item", "game:jonasframes-gearbox01", ["game:jonasframes-gearbox01", "game:jonasframes-gearbox02"], 1),
        ("item", "game:bracket-heavy-iron", ["game:bracket-heavy-iron", "game:bracket-heavy-meteoriciron", "game:bracket-heavy-steel"], 1),
        ("item", "game:metalplate-iron", ["game:metalplate-iron", "game:metalplate-meteoriciron", "game:metalplate-steel"], 1),
        ("item", "game:plank-oak", ["game:plank-oak"], 1),
    ];

    private static Item? ItemOf(ICoreServerAPI api, string code) =>
        api.World.GetItem(new AssetLocation(code)) is { IsMissing: false } i && i.Code != null ? i : null;

    private static double Dbl(object? o, string name, double fallback) =>
        GearChain.Prop(o, name) is { } v ? Convert.ToDouble(v) : fallback;

    public static SlittingMillData? Read(ICoreServerAPI api)
    {
        if (GearChain.System(api, GearChain.MainSystem) is not { } mainSystem || api.Assets.TryGet(RigAsset) is not { } asset) return null;
        var config = mainSystem.GetType().GetMethod("ConfigFor")?.Invoke(null, new object[] { api });
        if (GearChain.Prop(config, "SlittingMill") is false) return null;
        var settings = GearChain.Prop(config, "SlittingMillSettings");
        var oilSettings = GearChain.Prop(GearChain.System(api, GearChain.OilSystem), "Config");
        var millOil = GearChain.Prop(oilSettings, "SlittingMill");

        JObject rig;
        try { rig = JObject.Parse(asset.ToText()); }
        catch (Exception e)
        {
            api.Logger.Warning("[seraphexport] cannot read {0}: {1}; the slitting mill is not exported", RigAsset, e.Message);
            return null;
        }
        var slit = rig["slit"] as JObject;
        var data = new SlittingMillData
        {
            Mod = GearChain.Mod,
            FrameCode = FrameCode,
            Frame = api.World.GetBlock(new AssetLocation(FrameCode)) is { IsMissing: false, Code: not null } f ? f : null,
            Strokes = (double?)rig["work"]?["end"]?["thin"] ?? 5,
            TurnsPerStroke = Dbl(settings, "TurnsPerStroke", (double?)slit?["turnsPerStroke"] ?? 1),
            NailsPerPlate = (int)Dbl(settings, "NailsPerPlate", 20),
            DrainPerPlate = Dbl(millOil, "DrainPerJob", 5),
            Tank = Dbl(millOil, "Tank", GearChain.DefaultTank),
        };
        if (!(data.TurnsPerStroke > 0) || data.NailsPerPlate <= 0) return null;
        foreach (var (kind, template, codes, quantity) in KeptMaterials)
        {
            var stacks = codes.Select(code => kind == "block"
                    ? api.World.GetBlock(new AssetLocation(code)) is { IsMissing: false, Id: > 0, Code: not null } b ? b : null
                    : (CollectibleObject?)ItemOf(api, code))
                .OfType<CollectibleObject>().ToList();
            if (stacks.Count == 0) return null;
            data.Kept.Add((kind, template, stacks, quantity));
        }
        data.Knives.AddRange(KnifeCodes.Select(c => ItemOf(api, c)).OfType<Item>());
        if (data.Knives.Count == 0) return null;
        var platePattern = (string?)slit?["plate"] ?? "game:metalplate-{metal}";
        var outputPattern = (string?)slit?["output"] ?? "game:metalnailsandstrips-{metal}";
        var metals = GearChain.Prop(settings, "Metals") as IDictionary;
        foreach (var metal in Metals)
        {
            if (ItemOf(api, platePattern.Replace("{metal}", metal)) is not { } plate) continue;
            if (ItemOf(api, outputPattern.Replace("{metal}", metal)) is not { } output) continue;
            int wear = metals?[metal] is { } m ? (int)Dbl(m, "KnifeWear", 0) : 0;
            data.Classes.Add(new SlitClass(metal, plate, output, wear));
        }
        if (data.Classes.Count == 0) return null;
        data.Oils.AddRange(DrawBenchExport.OilsOf(api, oilSettings));
        return data;
    }
}
