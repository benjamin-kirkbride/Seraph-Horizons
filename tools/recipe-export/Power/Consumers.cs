using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using LoadFn = System.Func<SeraphHorizons.RecipeExport.Power.Live, SeraphHorizons.RecipeExport.Power.Load>;

namespace SeraphHorizons.RecipeExport.Power;

/// <summary>A load and the figures it is made of.</summary>
public sealed record Load(double Value, params Figure[] Sources)
{
    public static Load Of(Figure f) => new(f.Value, f);
}

/// <summary>
/// One kind of power consumer: the blocks whose block entity has a behavior of exactly
/// <see cref="Behavior"/>, and how its load is found. Without <see cref="ItemPrefix"/> the blocks are
/// grouped by domain and first code part, one entry each (every axle the game's axle behavior
/// drives); with it, one entry linked to that block.
/// </summary>
public sealed record Spec(string Behavior, string Category, LoadFn Work)
{
    /// <summary>Mod that must be loaded; the blocks' presence is checked as well.</summary>
    public string? Mod { get; init; }
    public string? Id { get; init; }
    public string? Name { get; init; }
    public string? ItemPrefix { get; init; }
    public LoadFn? Max { get; init; }
    public LoadFn? Idle { get; init; }
    /// <summary>The seraphhorizons MachineOil entry (MachineOilSettings.&lt;Oil&gt;) of a machine with a tank.</summary>
    public string? Oil { get; init; }
    /// <summary>True when a dry tank does not raise the load (the gear cutter: it wears the kit instead).</summary>
    public bool OilDryLoadFixed { get; init; }
    public string? Note { get; init; }
    /// <summary>True when the behavior is not declared on a block (smex's blower port is added to the
    /// blower's structure in code): the entry exists when <see cref="ItemPrefix"/> resolves.</summary>
    public bool PresenceByItem { get; init; }
}

/// <summary>
/// The power consumers, with their load while working and fully oiled. Loads from block JSON
/// (the game's MPConsumer behavior), from a mod's config (by reflection) or, where the figure is a
/// literal in the behavior's GetResistance, from the table below, cited as a code constant.
/// docs/recipe-browser/power.md says how to add one.
/// </summary>
public static class Consumers
{
    public const string Seraph = "seraphhorizons";
    private const string OilSystem = "SeraphHorizons.Mod.MachineOil.MachineOilSystem";

    private static LoadFn Const(string what, double value, string cls) => _ => Load.Of(Live.Constant(what, value, cls));

    private static LoadFn Cfg(string what, string type, string path, double fallback, string from) =>
        live => Load.Of(live.Number(what, type, path, fallback, from));

    private const string IwSystem = "ImmersiveWoodworking.ImmersiveWoodworkingModSystem";
    private const string IwFrom = "ModConfig/ImmersiveWoodworking/automation.json";
    private const string Smex = "SteelmakingExpanded.SmexValues";
    private const string SmexFrom = "ModConfig (smex)";
    private const string Ppex = "PipesAndPowerExpanded.PpexValues";
    private const string Gondola = "GondolaCableCar.GondolaCableCarModSystem";
    private const string GondolaFrom = "ModConfig (gondolacablecar)";
    private const string MwConfig = "Millwright.ModConfig.ModConfig";

    private static Load Times(Load a, Figure b) => new(a.Value * b.Value, a.Sources.Append(b).ToArray());

    public static readonly Spec[] Specs =
    {
        // The game: literals in GetResistance.
        new("BEBehaviorMPPulverizer", "machine", Const("load with an axle fitted", 0.085, "BEBehaviorMPPulverizer"))
        {
            Name = "Pulverizer",
            Idle = Const("load without an axle", 0.005, "BEBehaviorMPPulverizer"), Oil = "Pulverizer",
        },
        new("BEBehaviorMPToggle", "machine", Const("load with a hammer on the helve", 0.125, "BEBehaviorMPToggle"))
        {
            Name = "Helve hammer", Idle = Const("load without a hammer", 0.0005, "BEBehaviorMPToggle"), Oil = "HelveHammer",
            Note = "the load is on the toggle, the block on the axle that lifts the helve; without a hammer on the helve it is 0.0005",
        },
        new("BEBehaviorMPArchimedesScrew", "machine", live => Load.Of(new Figure(0.015, "resistance", "code default (BEBehaviorMPArchimedesScrew)")))
        {
            Name = "Archimedes screw", Note = "per block of screw",
        },
        new("BEBehaviorMPAxle", "transmission", Const("friction", 0.0005, "BEBehaviorMPAxle")),
        new("BEBehaviorMPAngledGears", "transmission", Const("friction", 0.0005, "BEBehaviorMPAngledGears")),
        new("BEBehaviorMPTransmission", "transmission", Const("friction", 0.0005, "BEBehaviorMPTransmission"))
        {
            Note = "the clutch that engages it adds no load of its own",
        },
        new("BEBehaviorMPSpurGear", "transmission", Const("friction", 0.0005, "BEBehaviorMPSpurGear")),
        new("BEBehaviorMPLargeGear3m", "transmission", Const("friction", 0.004, "BEBehaviorMPLargeGear3m")),
        new("BEBehaviorMPBrake", "brake", Const("most resistance", 3, "BEBehaviorMPBrake"))
        {
            Note = "engaged, it rises from 0 by 1 every 20 seconds to 3; released, it falls 1 every 10 seconds",
        },

        // Millwright
        new("BEBehaviorBrakeEnhanced", "brake", live => Times(Load.Of(Live.Constant("most resistance", 3, "BEBehaviorBrakeEnhanced")),
            live.Number("BrakeResistanceModifier", MwConfig, "Loaded.BrakeResistanceModifier", 2, "ModConfig/millwright.json")))
        {
            Mod = "millwright", Note = "the game's brake ramp (to 3), times BrakeResistanceModifier while engaged",
        },
        new("BEBehaviorAxlePassthrough", "transmission", Const("friction", 0.0005, "BEBehaviorAxlePassthrough"))
        {
            Mod = "millwright", Id = "millwright:woodenaxlepassthrough", ItemPrefix = "millwright:woodenaxlepassthroughfull",
        },
        new("BEBehaviorImprovedAxlePassthrough", "transmission", Const("friction", 0.0005, "BEBehaviorAxlePassthrough"))
        {
            Mod = "millwright", Id = "millwright:improvedaxlepassthrough", ItemPrefix = "millwright:improvedaxlepassthroughfull",
        },

        // Small mechanics mods
        new("BEBehaviorGearbox12", "transmission", Const("friction", 0.001, "BEBehaviorGearbox12")) { Mod = "mpegearbox" },
        new("BEBehaviorCenteredSpurGear", "transmission", Const("friction", 0.001, "BEBehaviorCenteredSpurGear")) { Mod = "mpecenteredspurgear" },
        new("BEBehaviorMMTransmission", "transmission", Const("friction", 0.0005, "BEBehaviorMMTransmission")) { Mod = "madmechanics" },

        // Immersive Woodworking
        new("BEBehaviorSawmillMP", "machine", Cfg("SawmillResistance", IwSystem, "Config.SawmillResistance", 0.085, IwFrom))
        {
            Mod = "immersivewoodworking", Id = "immersivewoodworking:sawmill", ItemPrefix = "immersivewoodworking:sawmill-frame", Name = "Sawmill", Idle = Const("load until assembled", 0.005, "BEBehaviorSawmillMP"),
            Oil = "Sawmill",
        },
        new("BEBehaviorSawmillMP", "machine", live => Times(
            Load.Of(live.Number("SawmillResistance", IwSystem, "Config.SawmillResistance", 0.085, IwFrom)),
            Live.Constant("× 0.6 with a flywheel", 0.6, "BEBehaviorSawmillMP")))
        {
            Mod = "immersivewoodworking", Id = "immersivewoodworking:sawmill@flywheel", ItemPrefix = "immersivewoodworking:sawmill-frame", Idle = Const("load until assembled", 0.005, "BEBehaviorSawmillMP"),
            Oil = "Sawmill", Name = "Sawmill, with a flywheel",
        },
        new("BEBehaviorChopperMP", "machine", Cfg("ChopperResistance", IwSystem, "Config.ChopperResistance", 0.085, IwFrom))
        {
            Mod = "immersivewoodworking", Id = "immersivewoodworking:chopper", ItemPrefix = "immersivewoodworking:chopper-frame", Name = "Chopper", Idle = Const("load until assembled", 0.005, "BEBehaviorChopperMP"),
            Oil = "Chopper",
        },

        // Panning Machine
        new("BEBehaviorPanningMachine", "machine", Cfg("MechanicalResistance", "PanningMachine.PanningMachineModSystem", "Config.MechanicalResistance", 0.085, "ModConfig (panningmachine)"))
        {
            Mod = "panningmachine",
        },

        // Steelmaking Expanded
        new("BEBehaviorMPConverterTransmission", "machine", Cfg("BessemerTransmissionResistance", Smex, "BessemerTransmissionResistance", 0.25, SmexFrom))
        {
            Mod = "smex",
        },
        new("BEBehaviorMpBlowerPort", "machine", live => Load.Of(live.Number("MpBlowerBaseLoad", Smex, "MpBlowerBaseLoad", 0.05, SmexFrom)))
        {
            Mod = "smex", Id = "smex:mpblower", ItemPrefix = "smex:mpblower", PresenceByItem = true,
            Max = live =>
            {
                var b = live.Number("MpBlowerBaseLoad", Smex, "MpBlowerBaseLoad", 0.05, SmexFrom);
                var per = live.Number("MpBlowerLoadPerAtm", Smex, "MpBlowerLoadPerAtm", 0.05, SmexFrom);
                var max = live.Number("MpBlowerMaxPressure", Smex, "MpBlowerMaxPressure", 2, SmexFrom);
                return new Load(b.Value + per.Value * max.Value, b, per, max);
            },
            Note = "MpBlowerBaseLoad + MpBlowerLoadPerAtm per atm of back-pressure, up to MpBlowerMaxPressure",
        },

        // Pipes and Power Expanded
        new("BEBehaviorMpPumpDrive", "machine", live =>
        {
            var b = live.Number("MpPumpBaseLoad", Ppex, "MpPumpBaseLoad", 0.05, "ModConfig (ppex)");
            var per = live.Number("MpPumpLoadPerAtm", Ppex, "MpPumpLoadPerAtm", 0.05, "ModConfig (ppex)");
            var p = live.Number("MpPumpDeliveryPressure", Ppex, "MpPumpDeliveryPressure", 1.5, "ModConfig (ppex)");
            return new Load(b.Value + per.Value * p.Value, b, per, p);
        })
        {
            Mod = "ppex", Note = "MpPumpBaseLoad + MpPumpLoadPerAtm × MpPumpDeliveryPressure",
        },
        new("BEBehaviorEngineMPGenerator", "transmission", Const("friction", 0.0005, "BEBehaviorEngineMPGenerator"))
        {
            Mod = "ppex", Note = "an engine's output; it drives the network, this is only its friction",
        },

        // Gondola Cable Car
        new("BEBehaviorStationPowerConsumer", "machine", Cfg("ActiveStationResistance", Gondola, "Config.ActiveStationResistance", 2.25, GondolaFrom))
        {
            Mod = "gondolacablecar", Id = "gondolacablecar:station", ItemPrefix = "gondolacablecar:kit-station", Name = "Cable car station, running",
            Idle = Cfg("IdleStationResistance", Gondola, "Config.IdleStationResistance", 0.02, GondolaFrom),
        },
        new("BEBehaviorStationPowerConsumer", "machine", live => Times(
            Load.Of(live.Number("ActiveStationResistance", Gondola, "Config.ActiveStationResistance", 2.25, GondolaFrom)),
            Live.Constant("× 1.6 with Viking cabins", 1.6, "GondolaPower")))
        {
            Mod = "gondolacablecar", Id = "gondolacablecar:station@viking", ItemPrefix = "gondolacablecar:kit-station", Name = "Cable car station, running Viking cabins",
            Idle = Cfg("IdleStationResistance", Gondola, "Config.IdleStationResistance", 0.02, GondolaFrom),
        },
        new("BEBehaviorSmallStationConsumer", "machine", live => Times(
            Load.Of(live.Number("ActiveStationResistance", Gondola, "Config.ActiveStationResistance", 2.25, GondolaFrom)),
            Live.Constant("× cabins / 5", 0.2, "SmallStationControl")))
        {
            Mod = "gondolacablecar", Id = "gondolacablecar:smallstation@1", ItemPrefix = "gondolacablecar:kit-small-station", Name = "Small cable car station, 1 cabin",
            Idle = Cfg("IdleStationResistance", Gondola, "Config.IdleStationResistance", 0.02, GondolaFrom),
            Note = "ActiveStationResistance × cabins / 5: each more cabin adds the same",
        },

        // The pack's own mod (mods-src/seraphhorizons)
        new("BEBehaviorMillMP", "machine", Cfg("Resistance", "SeraphHorizons.Mod.BuckingSawmill.BuckingSawmillSystem", "Config.Resistance", 0.17, "ModConfig/seraphhorizons.json (BuckingSawmillSettings)"))
        {
            Mod = Seraph, Id = "seraphhorizons:buckingmill", ItemPrefix = "seraphhorizons:buckingmill-frame", Name = "Bucking sawmill",
            Idle = SeraphIdle, Oil = "BuckingMill",
        },
        new("BEBehaviorRosserMP", "machine", Cfg("Resistance", "SeraphHorizons.Mod.Rosser.RosserSystem", "Config.Resistance", 0.2, "ModConfig/seraphhorizons.json (RosserSettings)"))
        {
            Mod = Seraph, Id = "seraphhorizons:rosser", ItemPrefix = "seraphhorizons:rosser-frame", Name = "Rosser",
            Idle = SeraphIdle, Oil = "Rosser",
        },
        new("BEBehaviorGearCutterMP", "machine", Cfg("Resistance", "SeraphHorizons.Mod.GearCutter.GearCutterSystem", "Config.Resistance", 0.2, "ModConfig/seraphhorizons.json (GearCutterSettings)"))
        {
            Mod = Seraph, Id = "seraphhorizons:gearcutter", ItemPrefix = "seraphhorizons:gearcutter-frame", Name = "Gear cutter",
            Idle = SeraphIdle, Oil = "GearCutter", OilDryLoadFixed = true,
            Note = "a dry tank does not raise its load: it wears the cutter kit faster instead",
        },
        new("BEBehaviorDrawBenchMP", "machine", Cfg("ResistanceLead", "SeraphHorizons.Mod.DrawBench.DrawBenchSystem", "Config.ResistanceLead", 0.2, "ModConfig/seraphhorizons.json (DrawBenchSettings)"))
        {
            Mod = Seraph, Id = "seraphhorizons:drawbench", ItemPrefix = "seraphhorizons:drawbench-frame", Name = "Draw bench",
            Max = Cfg("ResistanceCopper", "SeraphHorizons.Mod.DrawBench.DrawBenchSystem", "Config.ResistanceCopper", 0.35, "ModConfig/seraphhorizons.json (DrawBenchSettings)"),
            Idle = SeraphIdle, Oil = "DrawBench",
            Note = "ResistanceLead empty or drawing lead, ResistanceCopper drawing copper",
        },
    };

    /// <summary>MPE's brass gearboxes have the same name as the wooden ones.</summary>
    private static string Brass(string name, string part) =>
        part.EndsWith("brass", StringComparison.Ordinal) && !name.Contains("brass", StringComparison.OrdinalIgnoreCase) ? name + ", brass" : name;

    private static Load SeraphIdle(Live live) => Load.Of(live.Number("IncompleteResistance (until assembled)",
        "SeraphHorizons.Mod.BuckingSawmill.BEBehaviorMillMP", "IncompleteResistance", 0.005, "BEBehaviorMillMP.IncompleteResistance"));

    // Notes for the game's plain MPConsumer blocks, by id.
    private static readonly Dictionary<string, string> ConsumerNames = new()
    {
        ["game:quern"] = "Quern",
    };

    private static readonly Dictionary<string, string> ConsumerNotes = new()
    {
        ["game:axle-arm"] = "what attaches to the arm (a winch, a well) may raise it at runtime",
    };

    public static List<JObject> Build(Live live, BlockIndex blocks)
    {
        var mods = new Items.ModIndex(live.Api);
        var list = new List<JObject>();
        PlainConsumers(live, blocks, mods, list);
        foreach (var spec in Specs)
        {
            if (spec.Mod != null && !live.ModLoaded(spec.Mod)) continue;
            var found = blocks.WithBehavior(spec.Behavior);
            if (found.Count == 0 && spec.PresenceByItem && spec.ItemPrefix != null && blocks.Matching(spec.ItemPrefix) is { Count: > 0 } byItem)
                found = byItem;
            if (found.Count == 0)
            {
                if (spec.Mod != null)
                    live.Api.Logger.Warning("[seraphexport] power: {0} is loaded but no block has {1}; {2} left out",
                        spec.Mod, spec.Behavior, spec.Id ?? spec.Behavior);
                continue;
            }
            if (spec.ItemPrefix != null || spec.Id != null)
            {
                var item = spec.ItemPrefix != null ? blocks.ItemFor(spec.ItemPrefix) : blocks.ItemFor(found);
                var first = found[0];
                var id = spec.Id ?? $"{first.Code.Domain}:{first.FirstCodePart()}";
                list.Add(Entry(live, blocks, spec, id, item, mods.ModForCollectible(first), first.FirstCodePart()));
                continue;
            }
            // A group none of whose blocks the export holds (a legacy domain's aliases) is left out,
            // unless no group has one.
            var groups = found.GroupBy(b => (b.Code.Domain, Part: b.FirstCodePart()))
                .Select(g => (g.Key.Domain, g.Key.Part, Blocks: g.ToList(), Item: blocks.ItemFor(g))).ToList();
            if (groups.Any(g => g.Item != null)) groups.RemoveAll(g => g.Item == null);
            foreach (var g in groups)
                list.Add(Entry(live, blocks, spec, $"{g.Domain}:{g.Part}", g.Item, mods.ModForCollectible(g.Blocks[0]), g.Part));
        }
        var dupes = list.GroupBy(o => (string)o["id"]!).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupes.Count > 0) throw new InvalidOperationException("power consumer ids are not unique: " + string.Join(", ", dupes));
        return list;
    }

    private static JObject Entry(Live live, BlockIndex blocks, Spec spec, string id, string? item, string mod, string part)
    {
        var figures = new List<Figure>();
        var work = spec.Work(live);
        figures.AddRange(work.Sources);
        var o = new JObject
        {
            ["id"] = id,
            ["item"] = item,
            ["mod"] = mod,
            ["name"] = spec.Name ?? Brass(blocks.NameOf(item, part), part),
            ["category"] = spec.Category,
            ["load"] = PowerSection.Num(work.Value),
        };
        if (spec.Max?.Invoke(live) is { } max)
        {
            o["loadMax"] = PowerSection.Num(max.Value);
            figures.AddRange(max.Sources);
        }
        if (spec.Idle?.Invoke(live) is { } idle)
        {
            o["idleLoad"] = PowerSection.Num(idle.Value);
            figures.AddRange(idle.Sources);
        }
        if (spec.Oil != null && Oil(live, spec.Oil) is { } oil)
        {
            var (dry, tank) = oil;
            o["oil"] = new JObject
            {
                ["dryMultiplier"] = spec.OilDryLoadFixed ? 1 : PowerSection.Num(dry.Value),
                ["tank"] = PowerSection.Num(tank.Value / 100),
            };
            if (!spec.OilDryLoadFixed) figures.Add(dry);
            figures.Add(tank);
        }
        if (spec.Note != null) o["note"] = spec.Note;
        o["sources"] = PowerSection.Sources(figures);
        return o;
    }

    /// <summary>The seraphhorizons MachineOil multiplier and the machine's tank (points), or null
    /// when the mod or its MachineOil switch is off.</summary>
    private static (Figure Dry, Figure Tank)? Oil(Live live, string machine)
    {
        if (!live.ModLoaded(Seraph) || live.Get(OilSystem, "On") is not true) return null;
        const string from = "ModConfig/seraphhorizons.json (MachineOilSettings)";
        return (live.Number("DryResistanceMultiplier", OilSystem, "Config.DryResistanceMultiplier", 3, from),
            live.Number($"{machine}.Tank", OilSystem, $"Config.{machine}.Tank", 1000, from));
    }

    /// <summary>Blocks with the game's own MPConsumer behavior (the quern, the grinding wheel, a
    /// mod's winch or mixing bowl): load is its behavior property <c>resistance</c>, 0.1 when unset.</summary>
    private static void PlainConsumers(Live live, BlockIndex blocks, Items.ModIndex mods, List<JObject> list)
    {
        const string behavior = "BEBehaviorMPConsumer";
        foreach (var group in blocks.WithBehavior(behavior).GroupBy(b => (b.Code.Domain, Part: b.FirstCodePart())))
        {
            var block = group.First();
            var load = live.BlockNumber("resistance", block, b => blocks.Behavior(b, behavior)?.properties?["resistance"], 0.1,
                "behavior property resistance (MPConsumer)", behavior);
            string id = $"{group.Key.Domain}:{group.Key.Part}";
            var item = blocks.ItemFor(group);
            var o = new JObject
            {
                ["id"] = id,
                ["item"] = item,
                ["mod"] = mods.ModForCollectible(block),
                ["name"] = ConsumerNames.TryGetValue(id, out var name) ? name : blocks.NameOf(item, group.Key.Part),
                ["category"] = "machine",
                ["load"] = PowerSection.Num(load.Value),
            };
            if (ConsumerNotes.TryGetValue(id, out var note)) o["note"] = note;
            o["sources"] = PowerSection.Sources(new[] { load });
            list.Add(o);
        }
    }
}
