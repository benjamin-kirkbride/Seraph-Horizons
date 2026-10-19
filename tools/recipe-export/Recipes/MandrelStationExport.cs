using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One metal the mandrel station forges: the hollow section (consumed) and the pipe section
/// it becomes, two to a hollow, at its blows a hollow.</summary>
public sealed record ForgeClass(string Name, string Metal, Item Hollow, Item Section, int BlowsPerHollow);

/// <summary>The spring job: the steel rod (consumed, hot) wound into one spring, at its turns of the held
/// clock; the tongs it is worked with and their wear a spring; the rod's working temperature.</summary>
public sealed record WindClass(Item Rod, Item Spring, double TurnsPerRod, List<Item> Tongs, int TongsWearPerRod, int WorkableTemperature);

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
    /// <summary>The spring job, or null with springs off (MandrelSprings) or its items not registered.</summary>
    public WindClass? Wind;
}

/// <summary>
/// The mandrel forging station (seraphhorizons, MandrelStation/): its process, read as the press
/// brake's is (<see cref="PressBrakeExport"/>): the rig (config/mandrelstation-rig.json) gives the
/// hollow and section of each class and the sections a hollow; the gameplay's MandrelStationSettings
/// (BlowsPerHollowLead, BlowsPerHollowCopper, HammerWearPerBlow; TurnsPerRod and TongsWearPerRod for the
/// spring job, the rig's wind) are read live from the server's config.
/// A hand station: no oil, and nothing of the station wears; the hammer pays a point a blow. Null when
/// the mod is not loaded, its switch is off or what it names is not registered.
/// </summary>
public static class MandrelStationExport
{
    public static readonly AssetLocation RigAsset = new(GearChain.Mod, "config/mandrelstation-rig.json");
    public const string FrameCode = "seraphhorizons:mandrelstation-frame-north";

    // config/builds/mandrelstation.json (the shared staged build): the mandrel's alternatives.
    public static readonly string[] MandrelCodes = ["game:rod-iron", "game:rod-meteoriciron", "game:rod-steel"];
    /// <summary>The hammer the definition names: the base hammer (MandrelStationSettings.BaseHammerTier,
    /// the copper one's tier), whose blows the record's turns count. The variant lists every hammer; a
    /// better one forges in fewer blows, by its tool tier, which the handbook says.</summary>
    public const string HammerTemplate = "game:hammer-copper";

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
        data.Wind = ReadWind(api, config, settings, rig["wind"] as JObject);
        return data.Classes.Count == 0 && data.Wind == null ? null : data;
    }

    // MandrelStation/Core/Winding.cs: the spring job's rod and spring.
    public const string RodCode = "game:rod-steel";
    public const string SpringCode = "seraphhorizons:spring-steel";

    /// <summary>The spring job: the rig's <c>wind</c> (rod, spring) and MandrelStationSettings' TurnsPerRod and
    /// TongsWearPerRod; null with MandrelSprings off or the rod or spring not registered.</summary>
    private static WindClass? ReadWind(ICoreServerAPI api, object? config, object? settings, JObject? wind)
    {
        if (wind == null || GearChain.Prop(config, "MandrelSprings") is false) return null;
        if (ItemOf(api, (string?)wind["rod"] ?? RodCode) is not { } rod) return null;
        if (ItemOf(api, (string?)wind["spring"] ?? SpringCode) is not { } spring) return null;
        double turns = GearChain.Prop(settings, "TurnsPerRod") is { } t ? Convert.ToDouble(t) : (double?)wind["turnsPerRod"] ?? 0;
        if (!(turns > 0)) return null;
        var tongs = api.World.Items
            .Where(i => i?.Code != null && !i.IsMissing && i.Code.Domain == "game" && i.Tool == EnumTool.Tongs)
            .OrderBy(i => i.Code.ToString(), StringComparer.Ordinal).ToList();
        // the anvil's rule (ItemIngot.CanWork): workableTemperature, else half the melting point
        float melt = rod.CombustibleProps?.MeltingPoint ?? 0;
        float workable = rod.Attributes?["workableTemperature"] is { Exists: true } w ? w.AsFloat(melt / 2) : melt / 2;
        return new WindClass(rod, spring, turns, tongs, Math.Max(0, Int(settings, "TongsWearPerRod", 1)), (int)Math.Round(workable));
    }
}
