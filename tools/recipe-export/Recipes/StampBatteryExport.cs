using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One kind of rock the stamp battery takes: its pattern (every host rock an alternative for
/// raw ore), its form (<c>raw</c>, <c>crystallised</c> or <c>crushed</c>), the crushed ore it comes out
/// as and how many.</summary>
public sealed record StampClass(string Pattern, string Form, string Grade, string Ore, List<Item> Inputs, Item Crushed, int Count);

public sealed class StampBatteryData
{
    public required string Mod;
    public required string FrameCode;
    public Block? Frame;
    /// <summary>Shaft turns a rock (<c>OreMachineSpec.TurnsPerItem</c>).</summary>
    public required double TurnsPerRock;
    /// <summary>Litres of water a rock, from the battery's reservoir.</summary>
    public required double WaterPerRock;
    public Item? Water;
    public double WaterItemsPerLitre = 100;
    /// <summary>The shoes: their item, how many a set and the rocks a set crushes.</summary>
    public Item? Shoe;
    public int ShoesPerSet;
    public double RocksPerSet;
    public List<StampClass> Classes = new();
}

/// <summary>
/// The stamp battery (seraphhorizons, StampBattery/, #728): raw and crystallised ore stamped into crushed
/// ore of its grade, classified (crystallised ore two), and crushed ore not yet classified screened. The
/// figures are the mod's own, read by reflection as <see cref="GearChain"/> reads the mod:
/// <c>OreProcessingSystem.RecoveryFor(api).Machine(OreMachine.StampBattery)</c> (turns and water a rock)
/// and the battery's build file (its shoes, <c>config/builds/stampbattery.json</c>). Null when the mod is
/// not loaded, or the StampBattery or OreProcessing switch is off.
/// </summary>
public static class StampBatteryExport
{
    public const string SystemType = "SeraphHorizons.Mod.Ore.Processing.OreProcessingSystem";
    public const string FrameCode = "seraphhorizons:stampbattery-north";
    public const string WaterCode = "game:waterportion";
    public static readonly AssetLocation BuildAsset = new("seraphhorizons", "config/builds/stampbattery.json");

    private static double Dbl(object? o, string name) => GearChain.Prop(o, name) is { } v ? Convert.ToDouble(v) : 0;

    public static StampBatteryData? Read(ICoreServerAPI api)
    {
        if (GearChain.System(api, GearChain.MainSystem) is not { } mainSystem) return null;
        var config = mainSystem.GetType().GetMethod("ConfigFor")?.Invoke(null, new object[] { api });
        if (GearChain.Prop(config, "OreProcessing") is not true || GearChain.Prop(config, "StampBattery") is not true) return null;
        if (GearChain.System(api, SystemType) is not { } system) return null;
        var asm = system.GetType().Assembly;
        var recovery = system.GetType().GetMethod("RecoveryFor")?.Invoke(null, [api]);
        var machineEnum = asm.GetType("SeraphHorizons.Mod.Ore.Core.OreMachine");
        if (recovery == null || machineEnum == null)
        {
            api.Logger.Warning("[seraphexport] the stamp battery is on but the mod's ore recovery is not as expected; it is not exported");
            return null;
        }
        var spec = recovery.GetType().GetMethod("Machine", [machineEnum])!.Invoke(recovery, [Enum.Parse(machineEnum, "StampBattery")]);
        var frame = api.World.GetBlock(new AssetLocation(FrameCode)) is { IsMissing: false, Id: > 0 } b ? b : null;
        var data = new StampBatteryData
        {
            Mod = GearChain.Mod,
            FrameCode = FrameCode,
            Frame = frame,
            TurnsPerRock = Dbl(spec, "TurnsPerItem"),
            WaterPerRock = Dbl(spec, "WaterPerItem"),
            Water = api.World.GetItem(new AssetLocation(WaterCode)) is { IsMissing: false } w ? w : null,
        };
        if (data.Water?.Attributes?["waterTightContainerProps"]?["itemsPerLitre"].AsFloat(0) is > 0 and var perLitre)
            data.WaterItemsPerLitre = perLitre;
        ReadShoes(api, data);

        var classes = new SortedDictionary<string, StampClass>(StringComparer.Ordinal);
        foreach (var item in api.World.Items)
        {
            if (item?.Code is not { } code || item.IsMissing) continue;
            if (code.Domain == "game")
            {
                var parts = code.Path.Split('-');
                if (parts.Length != 4 || parts[0] is not ("ore" or "crystalizedore") || parts[1] is not ("poor" or "medium" or "rich" or "bountiful")) continue;
                var crushed = api.World.GetItem(new AssetLocation(GearChain.Mod, $"crushedore-{parts[2]}-{parts[1]}"));
                if (crushed == null || crushed.IsMissing) continue;
                string pattern = $"game:{parts[0]}-{parts[1]}-{parts[2]}-*";
                if (!classes.TryGetValue(pattern, out var k))
                    classes[pattern] = k = new StampClass(pattern, parts[0] == "crystalizedore" ? "crystallised" : "raw", parts[1], parts[2],
                        new List<Item>(), crushed, parts[0] == "crystalizedore" ? 2 : 1);
                k.Inputs.Add(item);
            }
            else if (code.Domain == GearChain.Mod && code.Path.StartsWith("crushedore-", StringComparison.Ordinal))
            {
                var rest = code.Path["crushedore-".Length..];
                int dash = rest.LastIndexOf('-');
                if (dash <= 0) continue;
                classes[code.ToString()] = new StampClass(code.ToString(), "crushed", rest[(dash + 1)..], rest[..dash], new List<Item> { item }, item, 1);
            }
        }
        foreach (var k in classes.Values)
            k.Inputs.Sort((a, b) => string.CompareOrdinal(a.Code.ToString(), b.Code.ToString()));
        data.Classes = classes.Values.ToList();
        return data.Classes.Count == 0 || !(data.TurnsPerRock > 0) ? null : data;
    }

    /// <summary>The shoes from the build file's <c>wearing</c> part <c>shoes</c>: its first code, a set's
    /// count and the rocks it lasts.</summary>
    private static void ReadShoes(ICoreServerAPI api, StampBatteryData data)
    {
        if (api.Assets.TryGet(BuildAsset) is not { } asset) return;
        var build = Newtonsoft.Json.Linq.JObject.Parse(asset.ToText());
        foreach (var w in build["wearing"] as Newtonsoft.Json.Linq.JArray ?? [])
        {
            if ((string?)w["id"] != "shoes" || (string?)w["codes"]?[0] is not { } shoe) continue;
            data.Shoe = api.World.GetItem(new AssetLocation(shoe)) is { IsMissing: false } i ? i : null;
            data.ShoesPerSet = (int?)w["count"] ?? 0;
            data.RocksPerSet = (double?)w["uses"] ?? 0;
        }
    }
}
