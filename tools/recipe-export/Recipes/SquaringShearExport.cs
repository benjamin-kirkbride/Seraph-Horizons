using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One metal the squaring shear cuts: the plate (consumed) and the half plate it becomes,
/// two to a plate, at its treadle strokes a plate.</summary>
public sealed record CutClass(string Name, string Metal, Item Plate, Item HalfPlate, double StrokesPerPlate);

public sealed class SquaringShearData
{
    public required string Mod;
    public required string FrameCode;
    public Block? Frame;
    /// <summary>The fitted parts the shear keeps, each stage's alternatives: the blades, the gauge.</summary>
    public List<(string Template, List<Item> Items)> Kept = new();
    public required int HalfPlatesPerPlate;
    public List<CutClass> Classes = new();
}

/// <summary>
/// The squaring shear (seraphhorizons, SquaringShear/): its process, read as the press brake's is
/// (<see cref="PressBrakeExport"/>): the rig (config/squaringshear-rig.json) gives the plate and half
/// plate of each class and the half plates a plate; the gameplay's SquaringShearSettings
/// (StrokesPerPlateLead, StrokesPerPlateCopper) are read live from the server's config. A hand
/// machine: no oil and no wear. Null when the mod is not loaded, its switch is off or what it names
/// is not registered.
/// </summary>
public static class SquaringShearExport
{
    public static readonly AssetLocation RigAsset = new(GearChain.Mod, "config/squaringshear-rig.json");
    public const string FrameCode = "seraphhorizons:squaringshear-frame-north";

    // SquaringShear/Core/SquaringShearParts.cs: what each kept stage takes.
    public static readonly (string Template, string[] Codes)[] KeptStages =
    [
        ("game:metalplate-iron", ["game:metalplate-iron", "game:metalplate-steel"]),
        ("game:rod-iron", ["game:rod-iron", "game:rod-meteoriciron", "game:rod-steel"]),
    ];

    private static Item? ItemOf(ICoreServerAPI api, string code) =>
        api.World.GetItem(new AssetLocation(code)) is { IsMissing: false } i && i.Code != null ? i : null;

    private static double Dbl(object? o, string name, double fallback) =>
        GearChain.Prop(o, name) is { } v ? Convert.ToDouble(v) : fallback;

    public static SquaringShearData? Read(ICoreServerAPI api)
    {
        if (GearChain.System(api, GearChain.MainSystem) is not { } mainSystem || api.Assets.TryGet(RigAsset) is not { } asset) return null;
        var config = mainSystem.GetType().GetMethod("ConfigFor")?.Invoke(null, new object[] { api });
        if (GearChain.Prop(config, "SquaringShear") is false) return null;
        var settings = GearChain.Prop(config, "SquaringShearSettings");

        JObject rig;
        try { rig = JObject.Parse(asset.ToText()); }
        catch (Exception e)
        {
            api.Logger.Warning("[seraphexport] cannot read {0}: {1}; the squaring shear is not exported", RigAsset, e.Message);
            return null;
        }
        var cut = rig["cut"] as JObject;
        var data = new SquaringShearData
        {
            Mod = GearChain.Mod,
            FrameCode = FrameCode,
            Frame = api.World.GetBlock(new AssetLocation(FrameCode)) is { IsMissing: false, Code: not null } f ? f : null,
            HalfPlatesPerPlate = (int?)cut?["halfPlatesPerPlate"] ?? 2,
        };
        foreach (var (template, codes) in KeptStages)
        {
            var items = codes.Select(c => ItemOf(api, c)).OfType<Item>().ToList();
            if (items.Count == 0) return null;
            data.Kept.Add((template, items));
        }
        foreach (var (name, metal, strokesKey) in new[] { ("thin", "lead", "StrokesPerPlateLead"), ("thick", "copper", "StrokesPerPlateCopper") })
        {
            if (ItemOf(api, (string?)cut?["plates"]?[name] ?? "game:metalplate-" + metal) is not { } plate) continue;
            if (ItemOf(api, (string?)cut?["halfPlates"]?[name] ?? "seraphhorizons:halfplate-" + metal) is not { } half) continue;
            double strokes = Dbl(settings, strokesKey, (double?)cut?["strokesPerPlate"]?[name] ?? 0);
            if (!(strokes > 0)) continue;
            data.Classes.Add(new CutClass(name, metal, plate, half, strokes));
        }
        return data.Classes.Count == 0 ? null : data;
    }
}
