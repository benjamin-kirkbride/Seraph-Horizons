using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace SeraphHorizons.RecipeExport.Power;

/// <summary>
/// The power producers. Every network producer here is a rotor in the game's sense
/// (<c>BEBehaviorMPRotor.GetTorque</c>: torque(s) = max(0, capableSpeed − s) × TorqueFactor, and
/// capableSpeed settles at TargetSpeed), except ppex's engine generator, a constant-power source.
/// docs/recipe-browser/power.md lists where each figure comes from.
/// </summary>
public static class Producers
{
    public const string Millwright = "millwright";
    public const string HandCrank = "handcrank";
    public const string Ppex = "ppex";
    public const string Yang = "yangtransport";

    private static Items.ModIndex Mods = null!;

    public static List<JObject> Build(Live live, BlockIndex blocks)
    {
        Mods = new Items.ModIndex(live.Api);
        var list = new List<JObject>();
        VanillaWindmills(live, blocks, list);
        if (live.ModLoaded(Millwright)) MillwrightWindmills(live, blocks, list);
        WaterWheel(live, blocks, list);
        if (live.ModLoaded(HandCrank)) Crank(live, blocks, list);
        if (live.ModLoaded(Ppex)) PpexEngines(live, blocks, list);
        if (live.ModLoaded(Yang)) YangEngines(live, blocks, list);
        return list;
    }

    private static JObject Producer(string id, string? item, string mod, string name, string family, JObject model,
        double? shownKN, string conditions, string? cost, IEnumerable<Figure> figures)
    {
        var o = new JObject
        {
            ["id"] = id,
            ["item"] = item,
            ["mod"] = mod,
            ["name"] = name,
            ["family"] = family,
            ["model"] = model,
            ["shownKN"] = shownKN is { } kn ? PowerSection.Num(kn) : JValue.CreateNull(),
            ["conditions"] = conditions,
        };
        if (cost != null) o["cost"] = cost;
        o["sources"] = PowerSection.Sources(figures);
        return o;
    }

    private static JObject Rotor(double targetSpeed, double torqueFactor) => new()
    {
        ["kind"] = "rotor",
        ["targetSpeed"] = PowerSection.Num(targetSpeed),
        ["torqueFactor"] = PowerSection.Num(torqueFactor),
    };

    private static JObject Wind(double speedPerWind, double speedCap, double torqueFactor, bool turbulence, int sails, int max, double perSail) => new()
    {
        ["kind"] = "wind",
        ["speedPerWind"] = PowerSection.Num(speedPerWind),
        ["speedCap"] = PowerSection.Num(speedCap),
        ["torqueFactor"] = PowerSection.Num(torqueFactor),
        ["turbulencePenalty"] = turbulence,
        ["sails"] = new JObject { ["count"] = sails, ["max"] = max, ["torqueFactorPerSail"] = PowerSection.Num(perSail) },
    };

    private static string Lower(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];

    /// <summary>The game's windmill (BEBehaviorWindmillRotor): TargetSpeed min(0.6, wind),
    /// TorqueFactor sails / 4 × powerMul, halved by turbulence. One entry per tier at its most sails.</summary>
    private static void VanillaWindmills(Live live, BlockIndex blocks, List<JObject> list)
    {
        const string behavior = "BEBehaviorWindmillRotor";
        foreach (var tier in blocks.WithBehavior(behavior).GroupBy(b => b.Variant?["tier"] ?? b.Code.Path))
        {
            var block = tier.First();
            var powerMul = live.BlockNumber("powerMul", block, b => blocks.Behavior(b, behavior)?.properties?["powerMul"],
                1, "behavior property powerMulByType (MPWindmillRotor)", behavior);
            var max = live.BlockNumber("maxLength", block, b => b.Attributes?["sailedShapes"]?["maxLength"],
                5, "block attributes sailedShapesByType");
            var cap = Live.Constant("speedCap 0.6", 0.6, behavior);
            var quarter = Live.Constant("torque factor sails / 4", 0.25, behavior);
            int sails = (int)max.Value;
            double perSail = powerMul.Value / 4;
            var item = blocks.ItemFor(tier);
            var tierName = tier.Key;
            list.Add(Producer($"{block.Code.Domain}:windmillrotor-{tierName}@{sails}", item, Mods.ModForCollectible(block),
                $"Windmill rotor ({tierName}), {sails} sails", "wind",
                Wind(1, cap.Value, sails * perSail, true, sails, sails, perSail),
                (int)(sails / 5f * 100f * (float)powerMul.Value),
                "wind at the rotor; another windmill within 1.5 × sails blocks halves the torque",
                null, new[] { powerMul, max, cap, quarter }));
        }
    }

    // BEBehaviorWindmillRotorEnhanced: the blade type multiplies the sail modifier, and caps the sails.
    private static readonly Dictionary<string, (double Mult, int MaxSails, string Blades)> MillwrightTypes = new()
    {
        ["single"] = (1, 7, "4 blades"),
        ["double"] = (2, 5, "8 blades"),
        ["three"] = (0.75, 8, "3 blades"),
        ["six"] = (1.5, 6, "6 blades"),
    };

    private const string MwConfig = "Millwright.ModConfig.ModConfig";

    /// <summary>Millwright's rotors: TargetSpeed min(0.6 × bm, wind × bm), TorqueFactor sails × bm / 4,
    /// bm the fitted sail's modifier (config) times the blade type's; no turbulence check.</summary>
    private static void MillwrightWindmills(Live live, BlockIndex blocks, List<JObject> list)
    {
        const string enhanced = "BEBehaviorWindmillRotorEnhanced";
        var centered = live.Number("SailCenteredModifier", MwConfig, "Loaded.SailCenteredModifier", 2, "ModConfig/millwright.json");
        foreach (var group in blocks.WithBehavior(enhanced).GroupBy(b => b.Variant?["type"] ?? ""))
        {
            if (!MillwrightTypes.TryGetValue(group.Key, out var t))
            {
                live.Api.Logger.Warning("[seraphexport] power: Millwright rotor type {0} is not known; left out", group.Key);
                continue;
            }
            var mult = Live.Constant($"blade type {group.Key} × {t.Mult}", t.Mult, enhanced);
            var maxSails = Live.Constant($"most sails {t.MaxSails}", t.MaxSails, enhanced);
            AddMillwright(list, blocks, group, $"millwright:windmillrotor-{group.Key}", $"Windmill rotor ({group.Key}, {t.Blades})",
                centered.Value * t.Mult, t.MaxSails, "centered sails (angled and wide have their own modifier, 2 by default like this one)",
                new[] { centered, mult, maxSails }, enhanced);
        }

        const string ud = "BEBehaviorWindmillRotorUD";
        var wide = live.Number("SailWideModifier", MwConfig, "Loaded.SailWideModifier", 2, "ModConfig/millwright.json");
        foreach (var group in blocks.WithBehavior(ud).GroupBy(b => b.Variant?["type"] ?? ""))
        {
            double m = group.Key == "two" ? 0.667 : 1;
            var mult = Live.Constant($"blade type {group.Key} × {m}", m, ud);
            var maxSails = Live.Constant("most sails 8", 8, ud);
            AddMillwright(list, blocks, group, $"millwright:windmillrotorud-{group.Key}", $"Vertical windmill rotor ({group.Key})",
                wide.Value * m, 8, "wide sails", new[] { wide, mult, maxSails }, ud);
        }
    }

    private static void AddMillwright(List<JObject> list, BlockIndex blocks, IEnumerable<Block> group, string idBase, string name,
        double bm, int sails, string sailNote, Figure[] figures, string behavior)
    {
        var cap = Live.Constant("speed cap 0.6 × modifier", 0.6, behavior);
        list.Add(Producer($"{idBase}@{sails}", blocks.ItemFor(group), Millwright, $"{name}, {sails} sails", "wind",
            Wind(bm, 0.6 * bm, sails * bm / 4, false, sails, sails, bm / 4),
            (int)(sails * (float)bm / 5f * 100f),
            $"wind at the rotor, {sailNote}", null, figures.Append(cap)));
    }

    /// <summary>The water wheel (BEBehaviorMPWaterWheel): TargetSpeed min(0.3, flowRate), TorqueFactor
    /// flowRate. flowRate = |Σ over the wheel's ring cells of (radial × facing) · push| × radius × 750,
    /// counting only moving water faster than requiresMinFlowSpeed: rapid water.</summary>
    private static void WaterWheel(Live live, BlockIndex blocks, List<JObject> list)
    {
        const string behavior = "BEBehaviorMPWaterWheel";
        var wheels = blocks.WithBehavior(behavior);
        if (wheels.Count == 0) return;
        var wheel = wheels[0];
        var diameter = live.BlockNumber("diameter", wheel, b => blocks.Behavior(b, behavior)?.properties?["diameter"], 3,
            "behavior property diameter (MPWaterWheel)", behavior);
        var minFlow = live.BlockNumber("requiresMinFlowSpeed", wheel, b => blocks.Behavior(b, behavior)?.properties?["requiresMinFlowSpeed"], 1.5,
            "behavior property requiresMinFlowSpeed (MPWaterWheel)", behavior);
        var rapid = blocks.Matching("game:rapidwater-e").FirstOrDefault();
        var push = live.BlockNumber("rapid water push", rapid, b => b.Attributes?["pushVector"]?["x"], 0.004,
            "block attributes pushVectorByType (game:rapidwater)");
        var flowSpeed = live.BlockNumber("rapid water flowSpeed", rapid, b => b.Attributes?["flowSpeed"], 2,
            "block attributes flowSpeed (game:rapidwater)");
        var scale = Live.Constant("flow × 750", 750, behavior);
        var cap = Live.Constant("speed cap 0.3", 0.3, behavior);
        int radius = (int)diameter.Value / 2;
        if (flowSpeed.Value <= minFlow.Value)
        {
            live.Api.Logger.Warning("[seraphexport] power: rapid water flows at {0}, not above the wheel's {1}; no water wheel entries",
                flowSpeed.Value, minFlow.Value);
            return;
        }
        var item = blocks.ItemFor(wheels);
        var figures = new[] { diameter, minFlow, push, flowSpeed, scale, cap };
        foreach (var (cells, factor, label) in new[]
                 {
                     (1, 1.0, "1 rapid water cell under the wheel"),
                     (3, 1 + 2 * Math.Sqrt(0.5), "3 rapid water cells under the wheel (the bottom one and its two diagonal neighbours)"),
                 })
        {
            double flow = Math.Abs(push.Value) * factor * radius * scale.Value;
            list.Add(Producer($"{wheel.Code.Domain}:waterwheel@{cells}", item, Mods.ModForCollectible(wheel),
                $"Water wheel, {cells} rapid water cell{(cells == 1 ? "" : "s")}", "water",
                Rotor(Math.Min(cap.Value, flow), flow), null,
                $"{label}, flowing across the wheel; still or plain water gives nothing", null, figures));
        }
    }

    /// <summary>Manual Crank (BEBehaviorManualCrank): TargetSpeed from config, TorqueFactor
    /// TorquePerPlayer × players. One entry, one player.</summary>
    private static void Crank(Live live, BlockIndex blocks, List<JObject> list)
    {
        const string type = "ManualCrank.ManualCrankModSystem";
        const string from = "ModConfig/ManualCrank.json";
        var speed = live.Number("TargetSpeed", type, "Config.TargetSpeed", 0.5, from);
        var torque = live.Number("TorquePerPlayer", type, "Config.TorquePerPlayer", 0.5, from);
        var hunger = live.Number("HungerCostPerSecond", type, "Config.HungerCostPerSecond", 2.5, from);
        var item = blocks.ItemFor("handcrank:handcrank");
        list.Add(Producer("handcrank:handcrank@1", item, HandCrank, "Hand crank, 1 player", "muscle",
            Rotor(speed.Value, torque.Value), null,
            "one player holding right-click on it; each more player adds the same torque",
            $"{Fmt(hunger.Value)} satiety per second", new[] { speed, torque, hunger }));
    }

    private const string PpexValues = "PipesAndPowerExpanded.PpexValues";

    /// <summary>ppex's engines through their MP generator (BEBehaviorEngineMPGenerator): a power
    /// budget P × MpLoadPerEnginePower × MpRatedSpeed spread over the shaft speed, the shaft running
    /// at (0.5 + P) × π / 5.</summary>
    private static void PpexEngines(Live live, BlockIndex blocks, List<JObject> list)
    {
        const string from = "ModConfig (ppex)";
        var loadPerPower = live.Number("MpLoadPerEnginePower", PpexValues, "MpLoadPerEnginePower", 2, from);
        var rated = live.Number("MpRatedSpeed", PpexValues, "MpRatedSpeed", 1, from);
        var watt = blocks.ItemFor("ppex:enginewatt");
        var cornish = blocks.ItemFor("ppex:enginecornish");
        var engines = new List<(string Id, string? Item, string Name, string Power, double PDefault, string Steam, double SDefault, string Engage, double EDefault, string Break, double BDefault)>
        {
            ("ppex:enginewatt", watt, "Watt engine", "WattEngineMaxPower", 0.3, "WattEngineSteamRate", 30, "WattEngineEngagePressure", 2, "WattEngineBreakPressure", 4),
        };
        foreach (var (key, label, p, s, e, b) in new[]
                 {
                     ("low", "Low", 0.2, 8.0, 5.0, 8.0),
                     ("normal", "Normal", 0.4, 16.0, 6.0, 8.0),
                     ("high", "High", 0.8, 32.0, 7.0, 8.0),
                 })
            engines.Add(($"ppex:enginecornish-{key}", cornish, $"Cornish engine, {key} throttle", $"CornishEnginePower{label}", p,
                $"CornishEngineSteam{label}", s, $"CornishEngineEngagePressure{label}", e, $"CornishEngineBreakPressure{label}", b));

        foreach (var en in engines)
        {
            var power = live.Number(en.Power, PpexValues, en.Power, en.PDefault, from);
            var steam = live.Number(en.Steam, PpexValues, en.Steam, en.SDefault, from);
            var engage = live.Number(en.Engage, PpexValues, en.Engage, en.EDefault, from);
            var brk = live.Number(en.Break, PpexValues, en.Break, en.BDefault, from);
            var shaftConst = Live.Constant("shaft speed (0.5 + P) × π / 5", Math.PI / 5, "BlockEntityEngine");
            var minConst = Live.Constant("budget spread over at least 0.25 × MpRatedSpeed", 0.25, "BEBehaviorEngineMPGenerator");
            var taperConst = Live.Constant("torque tapers from 2/3 of the shaft speed", 2.0 / 3, "BEBehaviorEngineMPGenerator");
            double shaft = (0.5 + power.Value) * Math.PI / 5;
            var model = new JObject
            {
                ["kind"] = "constantPower",
                ["budget"] = PowerSection.Num(power.Value * loadPerPower.Value * rated.Value),
                ["shaftSpeed"] = PowerSection.Num(shaft),
                ["minSpeed"] = PowerSection.Num(0.25 * rated.Value),
                ["taperFrom"] = PowerSection.Num(shaft * 2 / 3),
            };
            double lo = Math.Min(engage.Value, brk.Value), hi = Math.Max(engage.Value, brk.Value);
            list.Add(Producer($"{en.Id}", en.Item, Ppex, en.Name, "steam", model, null,
                $"steam at the inlet between {Fmt(lo)} and {Fmt(hi)} atm, with an MP generator on the engine",
                $"{Fmt(steam.Value)} L/s of steam", new[] { power, loadPerPower, rated, steam, engage, brk, shaftConst, minConst, taperConst }));
        }
    }

    private const string YangSettings = "YangTransport.YangTransportSettings";

    /// <summary>Yang Transport's stationary steam engine (SteamMechanica): at temperature T the
    /// target speed is T / 100 × AccelerationNPer100C and the stall torque T / 100 × RawPowerNPer100C
    /// (times the server's multipliers); torque(s) = (target − s) × stall / target.</summary>
    private static void YangEngines(Live live, BlockIndex blocks, List<JObject> list)
    {
        const string from = "ModConfig/yangtransport/yangtransport.json";
        var speedMul = live.Number("StationarySteamEngineSpeedMultiplier", YangSettings, "StationarySteamEngineSpeedMultiplier", 1, from);
        var powerMul = live.Number("StationarySteamEnginePowerMultiplier", YangSettings, "StationarySteamEnginePowerMultiplier", 1, from);
        foreach (var tier in blocks.Matching("yangtransport:steamengine").GroupBy(b => b.Variant?["tier"] ?? ""))
        {
            if (tier.Key == "") continue;
            var block = tier.First();
            var temp = live.BlockNumber("MaxTemperatureC", block, b => b.Attributes?["SteamEngine"]?["MaxTemperatureC"], 800,
                "block attributes SteamEngine", "SteamEngineConfig");
            var accel = live.BlockNumber("AccelerationNPer100C", block, b => b.Attributes?["SteamEngine"]?["AccelerationNPer100C"], 1,
                "block attributes SteamEngine", "SteamEngineConfig");
            var raw = live.BlockNumber("RawPowerNPer100C", block, b => b.Attributes?["SteamEngine"]?["RawPowerNPer100C"], 1,
                "block attributes SteamEngine", "SteamEngineConfig");
            double t100 = temp.Value / 100;
            double target = t100 * accel.Value * speedMul.Value;
            double stall = t100 * raw.Value * powerMul.Value;
            list.Add(Producer($"yangtransport:steamengine-{tier.Key}", blocks.ItemFor(tier), Yang,
                $"Steam engine ({tier.Key})", "steam", Rotor(target, target > 0 ? stall / target : 0), null,
                $"at its safe maximum temperature, {Fmt(temp.Value)} °C", null,
                new[] { temp, accel, raw, speedMul, powerMul }));
        }
    }

    public static string Fmt(double v) => Math.Round(v, 3).ToString(System.Globalization.CultureInfo.InvariantCulture);
}
