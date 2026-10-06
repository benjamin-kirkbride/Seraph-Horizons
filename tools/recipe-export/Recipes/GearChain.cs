using System.Collections;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Util;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One row of the pickling tub's rule table: gears of <see cref="Input"/> in
/// <see cref="Liquids"/> become <see cref="Output"/> after <see cref="Hours"/>.</summary>
public sealed record TubRule(
    string Kind, string LiquidPattern, List<Item> Liquids, Item Input, Item Output, double Hours,
    double GraceHours, double LossEveryHours, double LossChance, Item? Failure, int FailureQuantity);

public sealed class TubData
{
    public required string Mod;
    public required Block Tub;
    public required int BatchSize;
    public required double LitresPerBatch;
    public List<TubRule> Rules = new();
}

/// <summary>An item resolved by chance when it lands in a player's inventory.</summary>
public sealed class LotteryData
{
    public required string Mod;
    public required Item Item;
    public required Item Win;
    public required Item Lose;
    public required double Chance;
    public required int LosePerItem;
}

/// <summary>One blank size the gear cutter takes: the master fitted decides it.</summary>
public sealed record CutterClass(string Name, Item Blank, Item Master, Item Gear, int Teeth, int Wear, double Drain);

/// <summary>An oil that fills a machine's tank, and the litres one of its items is worth.</summary>
public sealed record Oil(Item Item, double LitresPerItem);

public sealed class CutterData
{
    public required string Mod;
    public required int TurnsPerTooth;
    public required string KitCode;
    public Item? Kit;
    public required string FrameCode;
    public Block? Frame;
    public required double Tank;
    public List<Oil> Oils = new();
    public List<CutterClass> Classes = new();
}

/// <summary>
/// The gear chain of the pack's own mod (seraphhorizons, epic #484): the pickling tub's rules,
/// the oiled gear's lottery and the gear cutter's process. The exporter cannot reference the mod
/// (it is built on its own against the game), so its settings are read by reflection from the
/// mod's loaded systems: what the server runs with, ModConfig included. Each part is null when the
/// mod is not loaded, its switch is off or what it names is not registered.
/// </summary>
public static class GearChain
{
    public const string Mod = "seraphhorizons";
    public const string TubSystem = "SeraphHorizons.Mod.PicklingTub.PicklingTubSystem";
    public const string ReclamationSystem = "SeraphHorizons.Mod.GearReclamation.GearReclamationSystem";
    public const string OilSystem = "SeraphHorizons.Mod.MachineOil.MachineOilSystem";
    public const string MainSystem = "SeraphHorizons.Mod.SeraphHorizonsSystem";

    // GearReclamation/Core/GearReclamation.cs, GearCodes.
    public const string Oiled = "seraphhorizons:gear-oiled";
    public const string Steel = "seraphhorizons:gear-steel";
    public const string LargeSteel = "seraphhorizons:largegear-steel";
    public const string SteelBit = "game:metalbit-steel";
    public const string TubBlock = "seraphhorizons:picklingtub";

    // The gear cutter (#480, #481). The rig (config/gearcutter-rig.json) gives the turns per tooth,
    // the teeth and the masters; the rest is the gameplay's GearCutterSettings and the MachineOil
    // entry GearCutter, read when the mod has them. Until it does (the cutter's gameplay is built on
    // its own branch), these defaults, agreed with it, stand in: the gameplay is the source of truth.
    public static readonly AssetLocation RigAsset = new(Mod, "config/gearcutter-rig.json");
    public const string KitCode = "seraphhorizons:gearcutterkit-steel";
    public const string FrameCode = "seraphhorizons:gearcutter-frame-north";
    public const int DefaultWearPerGear = 10;
    public const double DefaultDrainPerGear = 10;
    public const double DefaultTank = 1000;
    public const double PointsPerLitre = 100;

    /// <summary>Blank and gear of each of the rig's classes (thin: the temporal gear master,
    /// thick: the large one).</summary>
    public static readonly Dictionary<string, (string Blank, string Gear)> ClassStock = new()
    {
        ["thin"] = ("seraphhorizons:gearblank-steel", Steel),
        ["thick"] = ("seraphhorizons:largegearblank-steel", LargeSteel),
    };

    public static ModSystem? System(ICoreServerAPI api, string fullName) =>
        api.ModLoader.Systems.FirstOrDefault(s => s.GetType().FullName == fullName);

    public static object? Prop(object? o, string name) =>
        o?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(o);

    private static double Dbl(object? o, string name, double fallback = 0) =>
        Prop(o, name) is { } v ? Convert.ToDouble(v) : fallback;

    private static bool On(object? system) => Prop(system, "On") is not false;

    /// <summary>The mod's whole config (SeraphHorizonsSystem.ConfigFor), or null.</summary>
    private static object? Config(ICoreServerAPI api)
    {
        var main = System(api, MainSystem);
        var method = main?.GetType().GetMethod("ConfigFor", BindingFlags.Public | BindingFlags.Static);
        return method?.Invoke(null, new object[] { api });
    }

    private static Item? ItemOf(ICoreServerAPI api, string code) =>
        api.World.GetItem(new AssetLocation(code)) is { IsMissing: false } i && i.Code != null ? i : null;

    private static string Normalize(string code)
    {
        code = code.Trim().ToLowerInvariant();
        return code.Contains(':') ? code : "game:" + code;
    }

    /// <summary>Every registered item a `domain:path` pattern with `*` wildcards matches, by code.</summary>
    public static List<Item> Matching(ICoreServerAPI api, string pattern)
    {
        var p = new AssetLocation(Normalize(pattern));
        return api.World.Items
            .Where(i => i?.Code != null && !i.IsMissing && WildcardUtil.Match(p, i.Code))
            .OrderBy(i => i.Code.ToString(), StringComparer.Ordinal).ToList();
    }

    public static TubData? Tub(ICoreServerAPI api)
    {
        var system = System(api, TubSystem);
        if (system == null || !On(system)) return null;
        var tub = api.World.GetBlock(new AssetLocation(TubBlock));
        if (tub == null || tub.IsMissing || tub.Code == null) return null;
        var config = Prop(system, "Config");
        var data = new TubData
        {
            Mod = Mod,
            Tub = tub,
            BatchSize = (int)Dbl(config, "BatchSize", 8),
            LitresPerBatch = Dbl(config, "LitresPerBatch", 1),
        };
        if (Prop(Prop(system, "Rules"), "Rules") is not IEnumerable rules) return data;
        foreach (var rule in rules)
        {
            string liquid = (string)Prop(rule, "Liquid")!, input = (string)Prop(rule, "Input")!, output = (string)Prop(rule, "Output")!;
            var failureCode = Prop(rule, "Failure") as string;
            var liquids = Matching(api, liquid);
            Item? i = ItemOf(api, Normalize(input)), o = ItemOf(api, Normalize(output));
            if (i == null || o == null || liquids.Count == 0)
            {
                api.Logger.Notification("[seraphexport] pickling tub rule {0} in {1} to {2}: not every item is registered; not exported", input, liquid, output);
                continue;
            }
            double grace = Dbl(rule, "GraceHours"), every = Dbl(rule, "LossEveryHours"), chance = Dbl(rule, "LossChance");
            bool loses = every > 0 || chance > 0;
            data.Rules.Add(new TubRule(
                Prop(rule, "Kind")?.ToString()?.ToLowerInvariant() ?? "pickle", Normalize(liquid), liquids, i, o, Dbl(rule, "Hours"),
                grace, every, chance,
                loses && !string.IsNullOrWhiteSpace(failureCode) ? ItemOf(api, Normalize(failureCode)) : null,
                (int)Dbl(rule, "FailureQuantity", 1)));
        }
        return data;
    }

    public static LotteryData? Lottery(ICoreServerAPI api)
    {
        var system = System(api, ReclamationSystem);
        if (system == null || !On(system)) return null;
        Item? oiled = ItemOf(api, Oiled), steel = ItemOf(api, Steel), bits = ItemOf(api, SteelBit);
        if (oiled == null || steel == null || bits == null) return null;
        var config = Prop(system, "Config");
        return new LotteryData
        {
            Mod = Mod,
            Item = oiled,
            Win = steel,
            Lose = bits,
            Chance = Dbl(config, "UsableGearChance", 0.1),
            LosePerItem = (int)Dbl(config, "BitsPerFailedGear", 1),
        };
    }

    public static CutterData? Cutter(ICoreServerAPI api)
    {
        if (System(api, MainSystem) == null || api.Assets.TryGet(RigAsset) is not { } asset) return null;
        var config = Config(api);
        // The switch, once the gameplay adds it; missing means on.
        if (Prop(config, "GearCutter") is false) return null;
        var settings = Prop(config, "GearCutterSettings");
        var oilSettings = Prop(System(api, OilSystem), "Config");
        var cutterOil = Prop(oilSettings, "GearCutter");

        JObject rig;
        try { rig = JObject.Parse(asset.ToText()); }
        catch (Exception e)
        {
            api.Logger.Warning("[seraphexport] cannot read {0}: {1}; the gear cutter is not exported", RigAsset, e.Message);
            return null;
        }
        var cut = rig["cut"] as JObject;
        int turns = (int)Dbl(settings, "TurnsPerTooth", (int?)cut?["turnsPerTooth"] ?? 12);
        int wear = (int)Dbl(settings, "CutterWearPerGear", DefaultWearPerGear);
        double drain = Dbl(cutterOil, "DrainPerJob", DefaultDrainPerGear);
        var data = new CutterData
        {
            Mod = Mod,
            TurnsPerTooth = turns,
            KitCode = KitCode,
            Kit = ItemOf(api, KitCode),
            FrameCode = FrameCode,
            Frame = api.World.GetBlock(new AssetLocation(FrameCode)) is { IsMissing: false, Code: not null } f ? f : null,
            Tank = Dbl(cutterOil, "Tank", DefaultTank),
        };
        int smallTeeth = 0;
        foreach (var (name, (blankCode, gearCode)) in ClassStock)
        {
            var master = (string?)cut?["masters"]?[name];
            var teeth = (int?)rig["work"]?["end"]?[name];
            if (master == null || teeth is not > 0) continue;
            Item? blank = ItemOf(api, blankCode), m = ItemOf(api, master), gear = ItemOf(api, gearCode);
            if (blank == null || m == null || gear == null) continue;
            if (smallTeeth == 0) smallTeeth = teeth.Value;
            // Wear and oil scale with the teeth cut, against the small gear's (#480: a large gear
            // wears the kit in proportion and drains double).
            double scale = (double)teeth.Value / smallTeeth;
            data.Classes.Add(new CutterClass(name, blank, m, gear, teeth.Value,
                (int)Math.Ceiling(wear * scale - 1e-9), name == "thin" ? drain : drain * 2));
        }
        if (data.Classes.Count == 0) return null;

        foreach (var pattern in Prop(oilSettings, "OilLiquids") as IEnumerable<string> ?? Array.Empty<string>())
        foreach (var item in Matching(api, pattern))
        {
            var perLitre = item.Attributes?["waterTightContainerProps"]?["itemsPerLitre"].AsFloat(0) ?? 0;
            if (perLitre > 0 && data.Oils.All(o => o.Item != item)) data.Oils.Add(new Oil(item, 1 / perLitre));
        }
        if (Prop(oilSettings, "OilLumps") is IDictionary lumps)
            foreach (DictionaryEntry e in lumps)
            foreach (var item in Matching(api, (string)e.Key))
                if (data.Oils.All(o => o.Item != item)) data.Oils.Add(new Oil(item, Convert.ToDouble(e.Value)));
        return data;
    }
}
