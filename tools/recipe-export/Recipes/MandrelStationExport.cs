using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One metal the mandrel station forges: the hollow section (consumed) and the pipe section
/// it becomes, two to a hollow, at its blows a hollow.</summary>
public sealed record ForgeClass(string Name, string Metal, Item Hollow, Item Section, int BlowsPerHollow);

public sealed class MandrelStationData
{
    public required string Mod;
    public required string FrameCode;
    public Block? Frame;
    /// <summary>The fitted mandrel the station keeps: its alternatives.</summary>
    public List<Item> Mandrels = new();
    /// <summary>The hammers that strike, any of the game's.</summary>
    public List<Item> Hammers = new();
    public required int SectionsPerHollow;
    public required int HammerWearPerBlow;
    public List<ForgeClass> Classes = new();
}

/// <summary>
/// The mandrel forging station (seraphhorizons, MandrelStation/): its process, read as the press
/// brake's is (<see cref="PressBrakeExport"/>): the rig (config/mandrelstation-rig.json) gives the
/// hollow and section of each class and the sections a hollow; the gameplay's MandrelStationSettings
/// (BlowsPerHollowLead, BlowsPerHollowCopper, HammerWearPerBlow) are read live from the server's config.
/// A hand station: no oil, and nothing of the station wears; the hammer pays a point a blow. Null when
/// the mod is not loaded, its switch is off or what it names is not registered.
/// </summary>
public static class MandrelStationExport
{
    public static readonly AssetLocation RigAsset = new(GearChain.Mod, "config/mandrelstation-rig.json");
    public const string FrameCode = "seraphhorizons:mandrelstation-frame-north";

    // MandrelStation/Core/Forging.cs: the mandrel's alternatives.
    public static readonly string[] MandrelCodes = ["game:rod-iron", "game:rod-meteoriciron", "game:rod-steel"];
    /// <summary>The hammer the definition names; the variant lists every hammer.</summary>
    public const string HammerTemplate = "game:hammer-iron";

    private static Item? ItemOf(ICoreServerAPI api, string code) =>
        api.World.GetItem(new AssetLocation(code)) is { IsMissing: false } i && i.Code != null ? i : null;

    private static int Int(object? o, string name, int fallback) =>
        GearChain.Prop(o, name) is { } v ? Convert.ToInt32(v) : fallback;

    public static MandrelStationData? Read(ICoreServerAPI api)
    {
        if (GearChain.System(api, GearChain.MainSystem) is not { } mainSystem || api.Assets.TryGet(RigAsset) is not { } asset) return null;
        var config = mainSystem.GetType().GetMethod("ConfigFor")?.Invoke(null, new object[] { api });
        if (GearChain.Prop(config, "MandrelStation") is false) return null;
        var settings = GearChain.Prop(config, "MandrelStationSettings");

        JObject rig;
        try { rig = JObject.Parse(asset.ToText()); }
        catch (Exception e)
        {
            api.Logger.Warning("[seraphexport] cannot read {0}: {1}; the mandrel station is not exported", RigAsset, e.Message);
            return null;
        }
        var forge = rig["forge"] as JObject;
        var data = new MandrelStationData
        {
            Mod = GearChain.Mod,
            FrameCode = FrameCode,
            Frame = api.World.GetBlock(new AssetLocation(FrameCode)) is { IsMissing: false, Code: not null } f ? f : null,
            SectionsPerHollow = (int?)forge?["sectionsPerHollow"] ?? 2,
            HammerWearPerBlow = Math.Max(0, Int(settings, "HammerWearPerBlow", 1)),
        };
        data.Mandrels = MandrelCodes.Select(c => ItemOf(api, c)).OfType<Item>().ToList();
        if (data.Mandrels.Count == 0) return null;
        data.Hammers = api.World.Items
            .Where(i => i?.Code != null && !i.IsMissing && i.Code.Domain == "game" && i.Code.Path.StartsWith("hammer-", StringComparison.Ordinal))
            .OrderBy(i => i.Code.ToString(), StringComparer.Ordinal).ToList();
        foreach (var (name, metal, blowsKey) in new[] { ("thin", "lead", "BlowsPerHollowLead"), ("thick", "copper", "BlowsPerHollowCopper") })
        {
            if (ItemOf(api, (string?)forge?["hollows"]?[name] ?? "game:chutesection-" + metal) is not { } hollow) continue;
            if (ItemOf(api, (string?)forge?["sections"]?[name] ?? "seraphhorizons:pipesection-" + metal) is not { } section) continue;
            int blows = Int(settings, blowsKey, (int?)forge?["blowsPerHollow"]?[name] ?? 0);
            if (blows < 1) continue;
            data.Classes.Add(new ForgeClass(name, metal, hollow, section, blows));
        }
        return data.Classes.Count == 0 ? null : data;
    }
}
