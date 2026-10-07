using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One metal the press brake folds: the plate (consumed) and the angle it becomes, one to
/// a plate, at its lever turns a plate.</summary>
public sealed record FoldClass(string Name, string Metal, Item Plate, Item Angle, double LeverTurnsPerPlate);

public sealed class PressBrakeData
{
    public required string Mod;
    public required string FrameCode;
    public Block? Frame;
    /// <summary>The fitted parts the brake keeps, each stage's alternatives: the screws, the edges.</summary>
    public List<(string Template, List<Item> Items)> Kept = new();
    public required int AnglesPerPlate;
    public List<FoldClass> Classes = new();
}

/// <summary>
/// The press brake (seraphhorizons, PressBrake/): its process, read as the draw bench's is
/// (<see cref="DrawBenchExport"/>): the rig (config/pressbrake-rig.json) gives the plate and angle
/// of each class and the angles a plate; the gameplay's PressBrakeSettings
/// (LeverTurnsPerPlateLead, LeverTurnsPerPlateCopper) are read live from the server's config. A hand
/// machine: no oil and no wear. Null when the mod is not loaded, its switch is off or what it names
/// is not registered.
/// </summary>
public static class PressBrakeExport
{
    public static readonly AssetLocation RigAsset = new(GearChain.Mod, "config/pressbrake-rig.json");
    public const string FrameCode = "seraphhorizons:pressbrake-frame-north";

    // PressBrake/Core/PressBrakeParts.cs: what each kept stage takes.
    public static readonly (string Template, string[] Codes)[] KeptStages =
    [
        ("game:rod-iron", ["game:rod-iron", "game:rod-meteoriciron", "game:rod-steel"]),
        ("game:metalplate-iron", ["game:metalplate-iron", "game:metalplate-steel"]),
    ];

    private static Item? ItemOf(ICoreServerAPI api, string code) =>
        api.World.GetItem(new AssetLocation(code)) is { IsMissing: false } i && i.Code != null ? i : null;

    private static double Dbl(object? o, string name, double fallback) =>
        GearChain.Prop(o, name) is { } v ? Convert.ToDouble(v) : fallback;

    public static PressBrakeData? Read(ICoreServerAPI api)
    {
        if (GearChain.System(api, GearChain.MainSystem) is not { } mainSystem || api.Assets.TryGet(RigAsset) is not { } asset) return null;
        var config = mainSystem.GetType().GetMethod("ConfigFor")?.Invoke(null, new object[] { api });
        if (GearChain.Prop(config, "PressBrake") is false) return null;
        var settings = GearChain.Prop(config, "PressBrakeSettings");

        JObject rig;
        try { rig = JObject.Parse(asset.ToText()); }
        catch (Exception e)
        {
            api.Logger.Warning("[seraphexport] cannot read {0}: {1}; the press brake is not exported", RigAsset, e.Message);
            return null;
        }
        var fold = rig["fold"] as JObject;
        var data = new PressBrakeData
        {
            Mod = GearChain.Mod,
            FrameCode = FrameCode,
            Frame = api.World.GetBlock(new AssetLocation(FrameCode)) is { IsMissing: false, Code: not null } f ? f : null,
            AnglesPerPlate = (int?)fold?["anglesPerPlate"] ?? 1,
        };
        foreach (var (template, codes) in KeptStages)
        {
            var items = codes.Select(c => ItemOf(api, c)).OfType<Item>().ToList();
            if (items.Count == 0) return null;
            data.Kept.Add((template, items));
        }
        foreach (var (name, metal, turnsKey) in new[] { ("thin", "lead", "LeverTurnsPerPlateLead"), ("thick", "copper", "LeverTurnsPerPlateCopper") })
        {
            if (ItemOf(api, (string?)fold?["plates"]?[name] ?? "game:metalplate-" + metal) is not { } plate) continue;
            if (ItemOf(api, (string?)fold?["angles"]?[name] ?? "seraphhorizons:angle-" + metal) is not { } angle) continue;
            double turns = Dbl(settings, turnsKey, (double?)fold?["leverTurnsPerPlate"]?[name] ?? 0);
            if (!(turns > 0)) continue;
            data.Classes.Add(new FoldClass(name, metal, plate, angle, turns));
        }
        return data.Classes.Count == 0 ? null : data;
    }
}
