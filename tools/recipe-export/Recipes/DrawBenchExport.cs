using System.Collections;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One metal the draw bench draws: the ingot (consumed), the dies that draw it and the
/// chute section it becomes, three to an ingot.</summary>
public sealed record DrawClass(string Name, string Metal, Item Ingot, List<Item> Dies, Item Section, double TurnsPerSection);

public sealed class DrawBenchData
{
    public required string Mod;
    public required string FrameCode;
    public Block? Frame;
    /// <summary>The fitted parts the bench keeps, each stage's alternatives: gearbox, chain, dog, mandrel.</summary>
    public List<(string Template, List<Item> Items)> Kept = new();
    public required int SectionsPerIngot;
    public required int DieWear;
    public required double DrainPerSection;
    public required double Tank;
    public List<Oil> Oils = new();
    public List<DrawClass> Classes = new();
}

/// <summary>
/// The draw bench (seraphhorizons, DrawBench/): its process, read as the gear cutter's is
/// (<see cref="GearChain.Cutter"/>): the rig (config/drawbench-rig.json) gives the sections an ingot
/// and the ingot of each class; the gameplay's DrawBenchSettings (TurnsPerSectionLead,
/// TurnsPerSectionCopper, DieWearPerIngot, DieMetals) and the MachineOil entry DrawBench (Tank,
/// DrainPerJob, per section) are read live from the server's config. Null when the mod is not loaded,
/// its switch is off or what it names is not registered.
/// </summary>
public static class DrawBenchExport
{
    public static readonly AssetLocation RigAsset = new(GearChain.Mod, "config/drawbench-rig.json");
    public const string FrameCode = "seraphhorizons:drawbench-frame-north";
    public const string DieCodePrefix = "seraphhorizons:drawdie-";
    public const string SectionCodePrefix = "game:chutesection-";

    // DrawBench/Core/DrawBenchParts.cs: what each kept stage takes.
    public static readonly (string Template, string[] Codes)[] KeptStages =
    [
        ("game:jonasframes-gearbox01", ["game:jonasframes-gearbox01"]),
        ("game:metalchain-iron", ["game:metalchain-iron", "game:metalchain-meteoriciron", "game:metalchain-steel"]),
        ("game:bracket-heavy-iron", ["game:bracket-heavy-iron", "game:bracket-heavy-meteoriciron", "game:bracket-heavy-steel"]),
        ("game:rod-iron", ["game:rod-iron", "game:rod-meteoriciron", "game:rod-steel"]),
    ];

    private static Item? ItemOf(ICoreServerAPI api, string code) =>
        api.World.GetItem(new AssetLocation(code)) is { IsMissing: false } i && i.Code != null ? i : null;

    private static double Dbl(object? o, string name, double fallback) =>
        GearChain.Prop(o, name) is { } v ? Convert.ToDouble(v) : fallback;

    public static DrawBenchData? Read(ICoreServerAPI api)
    {
        if (GearChain.System(api, GearChain.MainSystem) == null || api.Assets.TryGet(RigAsset) is not { } asset) return null;
        var mainSystem = GearChain.System(api, GearChain.MainSystem)!;
        var config = mainSystem.GetType().GetMethod("ConfigFor")?.Invoke(null, new object[] { api });
        if (GearChain.Prop(config, "DrawBench") is false) return null;
        var settings = GearChain.Prop(config, "DrawBenchSettings");
        var oilSettings = GearChain.Prop(GearChain.System(api, GearChain.OilSystem), "Config");
        var benchOil = GearChain.Prop(oilSettings, "DrawBench");

        JObject rig;
        try { rig = JObject.Parse(asset.ToText()); }
        catch (Exception e)
        {
            api.Logger.Warning("[seraphexport] cannot read {0}: {1}; the draw bench is not exported", RigAsset, e.Message);
            return null;
        }
        var draw = rig["draw"] as JObject;
        var data = new DrawBenchData
        {
            Mod = GearChain.Mod,
            FrameCode = FrameCode,
            Frame = api.World.GetBlock(new AssetLocation(FrameCode)) is { IsMissing: false, Code: not null } f ? f : null,
            SectionsPerIngot = (int?)draw?["sectionsPerIngot"] ?? 3,
            DieWear = (int)Dbl(settings, "DieWearPerIngot", 1),
            DrainPerSection = Dbl(benchOil, "DrainPerJob", 2),
            Tank = Dbl(benchOil, "Tank", GearChain.DefaultTank),
        };
        foreach (var (template, codes) in KeptStages)
        {
            var items = codes.Select(c => ItemOf(api, c)).OfType<Item>().ToList();
            if (items.Count == 0) return null;
            data.Kept.Add((template, items));
        }
        var dieMetals = GearChain.Prop(settings, "DieMetals") as IDictionary;
        foreach (var (name, metal, turnsKey) in new[] { ("thin", "lead", "TurnsPerSectionLead"), ("thick", "copper", "TurnsPerSectionCopper") })
        {
            var ingotCode = (string?)draw?["ingots"]?[name] ?? "game:ingot-" + metal;
            if (ItemOf(api, ingotCode) is not { } ingot) continue;
            if (ItemOf(api, SectionCodePrefix + metal) is not { } section) continue;
            var dies = new List<Item>();
            if (dieMetals != null)
                foreach (DictionaryEntry e in dieMetals)
                    if (e.Value is IEnumerable<string> draws && draws.Contains(metal) && ItemOf(api, DieCodePrefix + e.Key) is { } die)
                        dies.Add(die);
            if (dies.Count == 0) continue;
            dies.Sort((a, b) => string.CompareOrdinal(a.Code.ToString(), b.Code.ToString()));
            double turns = Dbl(settings, turnsKey, (double?)draw?["turnsPerSection"]?[name] ?? 0);
            if (!(turns > 0)) continue;
            data.Classes.Add(new DrawClass(name, metal, ingot, dies, section, turns));
        }
        if (data.Classes.Count == 0) return null;
        data.Oils.AddRange(OilsOf(api, oilSettings));
        return data;
    }

    private static List<Oil> OilsOf(ICoreServerAPI api, object? oilSettings)
    {
        var oils = new List<Oil>();
        foreach (var pattern in GearChain.Prop(oilSettings, "OilLiquids") as IEnumerable<string> ?? Array.Empty<string>())
        foreach (var item in GearChain.Matching(api, pattern))
        {
            var perLitre = item.Attributes?["waterTightContainerProps"]?["itemsPerLitre"].AsFloat(0) ?? 0;
            if (perLitre > 0 && oils.All(o => o.Item != item)) oils.Add(new Oil(item, 1 / perLitre));
        }
        if (GearChain.Prop(oilSettings, "OilLumps") is IDictionary lumps)
            foreach (DictionaryEntry e in lumps)
            foreach (var item in GearChain.Matching(api, (string)e.Key))
                if (oils.All(o => o.Item != item)) oils.Add(new Oil(item, Convert.ToDouble(e.Value)));
        return oils;
    }
}
