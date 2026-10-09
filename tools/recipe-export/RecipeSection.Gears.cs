using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;
using Vintagestory.API.Common;

namespace SeraphHorizons.RecipeExport;

/// <summary>The gear chain (pickling tub, neutralized gear lottery, gear cutter) and tool mold casting.
/// See docs/recipe-browser/schema.md.</summary>
public static partial class RecipeSection
{
    public const string TubType = "picklingtub";
    public const string LotteryType = "lottery";
    public const string CutterType = "gearcutter";
    public const string CastingType = "casting";

    private static void AddType(SortedDictionary<string, JObject> types, string code, string name, int count, string shape, string registry, string mod)
    {
        if (types.ContainsKey(code))
            throw new RecipeExportException($"A recipe registry has the type code '{code}' that {name.ToLowerInvariant()} uses");
        types[code] = new JObject
        {
            ["name"] = name,
            ["count"] = count,
            ["shape"] = shape,
            ["registry"] = registry,
            ["mod"] = mod,
        };
    }

    private static void FillGearChain(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        if (GearChain.Tub(ctx.Api) is { } tub)
        {
            var mine = tub.Rules.Select(r => TubRecord(ctx, tub, r)).ToList();
            records.AddRange(mine);
            AddType(types, TubType, "Pickling tub", mine.Count, "tub", "PicklingTubSettings", tub.Mod);
        }
        if (GearChain.Lottery(ctx.Api) is { } lottery)
        {
            records.Add(LotteryRecord(ctx, lottery));
            AddType(types, LotteryType, "Decided on pickup", 1, "lottery", "GearReclamationSettings", lottery.Mod);
        }
        if (GearChain.Cutter(ctx.Api) is { } cutter)
        {
            if (cutter.Kit == null || cutter.Frame == null)
                ctx.Api.Logger.Warning("[seraphexport] the gear cutter's {0} or {1} is not registered; its process is exported without them",
                    cutter.KitCode, cutter.FrameCode);
            var mine = cutter.Classes.Select(c => CutterRecord(ctx, cutter, c)).ToList();
            records.AddRange(mine);
            AddType(types, CutterType, "Gear cutter", mine.Count, "machine", "GearCutterSettings", cutter.Mod);
        }
    }

    private static void FillCasting(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        var groups = Casting.Find(ctx.Api);
        var mods = new Items.ModIndex(ctx.Api);
        var mine = groups.Select(g => CastingRecord(ctx, mods, g)).ToList();
        records.AddRange(mine);
        AddType(types, CastingType, "Casting", mine.Count, "generic", nameof(Vintagestory.GameContent.BlockToolMold), "game");
    }

    /// <summary>A concrete stack, recorded as referenced.</summary>
    private static JObject Stack(Context ctx, CollectibleObject c, double quantity, double? litres = null)
    {
        var o = new JObject { ["code"] = c.Code.ToString(), ["kind"] = Kind(c.ItemClass), ["quantity"] = Num(quantity) };
        if (litres != null) o["litres"] = Num(litres.Value);
        ctx.Referenced.Add(c.Code.ToString());
        return o;
    }

    private static JObject Def(string code, string kind, double quantity, string? role = null, double? litres = null)
    {
        var o = new JObject { ["code"] = code, ["kind"] = kind, ["quantity"] = Num(quantity) };
        if (litres != null) o["litres"] = Num(litres.Value);
        if (role != null) o["role"] = role;
        return o;
    }

    /// <summary>
    /// One row of the tub's rules: a batch of gears (role batch), the liquid (role liquid, the
    /// litres a finished batch uses up) and the tub (role station); the gear it becomes and, when
    /// gears can be lost, what a lost one becomes (`tub.failure`).
    /// </summary>
    private static JObject TubRecord(Context ctx, TubData tub, TubRule r)
    {
        var input = r.Input.Code.ToString();
        var ingredients = new JArray(
            Def(input, "item", 1, "batch"),
            Def(r.LiquidPattern, "item", 1, "liquid", tub.LitresPerBatch),
            Def(tub.Tub.Code.ToString(), Kind(tub.Tub.ItemClass), 1, "station"));
        var outputs = new JArray(Def(r.Output.Code.ToString(), "item", 1));
        var produced = new JArray(Stack(ctx, r.Output, 1));
        var block = new JObject
        {
            ["kind"] = r.Kind,
            ["hours"] = Num(r.Hours),
            ["batchSize"] = tub.BatchSize,
            ["litresPerBatch"] = Num(tub.LitresPerBatch),
        };
        if (r.LossEveryHours > 0)
        {
            block["graceHours"] = Num(r.GraceHours);
            block["lossEveryHours"] = Num(r.LossEveryHours);
        }
        if (r.LossChance > 0) block["lossChance"] = Num(r.LossChance);
        if (r.Failure != null && r.FailureQuantity > 0)
        {
            outputs.Add(Def(r.Failure.Code.ToString(), "item", r.FailureQuantity));
            produced.Add(Stack(ctx, r.Failure, r.FailureQuantity));
            block["failure"] = 1;
        }
        return new JObject
        {
            ["id"] = $"{TubType}|{input}|{r.LiquidPattern}",
            ["type"] = TubType,
            ["mod"] = tub.Mod,
            ["ingredients"] = ingredients,
            ["outputs"] = outputs,
            ["variants"] = new JArray(new JObject
            {
                ["ingredients"] = new JArray
                {
                    new JArray(Stack(ctx, r.Input, 1)),
                    new JArray(r.Liquids.Select(l => Stack(ctx, l, 1, tub.LitresPerBatch))),
                    new JArray(Stack(ctx, tub.Tub, 1)),
                },
                ["outputs"] = produced,
            }),
            ["tub"] = block,
            ["extra"] = new JObject { ["blockEntity"] = "seraphhorizons.PicklingTub" },
        };
    }

    /// <summary>The neutralized gear: each one is decided when it lands in a player's inventory, a
    /// sound stainless gear with the chance, else stainless bits.</summary>
    private static JObject LotteryRecord(Context ctx, LotteryData l)
    {
        var item = l.Item.Code.ToString();
        var outputs = new JArray(Def(l.Win.Code.ToString(), "item", 1));
        var produced = new JArray(Stack(ctx, l.Win, 1));
        var lose = new JArray();
        if (l.LosePerItem > 0)
        {
            outputs.Add(Def(l.Lose.Code.ToString(), "item", l.LosePerItem));
            produced.Add(Stack(ctx, l.Lose, l.LosePerItem));
            lose.Add(1);
        }
        var outcomes = new JArray(new JObject { ["chance"] = Num(l.Chance), ["outputs"] = new JArray(0) });
        if (l.Chance < 1) outcomes.Add(new JObject { ["chance"] = Num(1 - l.Chance), ["outputs"] = lose });
        return new JObject
        {
            ["id"] = $"{LotteryType}|{item}|0",
            ["type"] = LotteryType,
            ["mod"] = l.Mod,
            ["ingredients"] = new JArray(Def(item, "item", 1)),
            ["outputs"] = outputs,
            ["variants"] = new JArray(new JObject
            {
                ["ingredients"] = new JArray { new JArray(Stack(ctx, l.Item, 1)) },
                ["outputs"] = produced,
            }),
            ["lottery"] = new JObject { ["trigger"] = "inventory", ["outcomes"] = outcomes },
        };
    }

    /// <summary>
    /// One blank size on the gear cutter: the blank (consumed), the master (kept: fitted, never
    /// consumed), the cutter kit (a tool that wears per gear, more as the oil runs down), the oil
    /// (drained from the machine's tank per gear) and the machine; the gear it cuts.
    /// </summary>
    private static JObject CutterRecord(Context ctx, CutterData c, CutterClass k)
    {
        double litres = k.Drain / GearChain.PointsPerLitre;
        var firstOil = c.Oils.FirstOrDefault();
        var kit = Def(c.KitCode, "item", 1, "tool");
        kit["isTool"] = true;
        kit["toolDurabilityCost"] = k.Wear;
        var ingredients = new JArray(
            Def(k.Blank.Code.ToString(), "item", 1),
            Def(k.Master.Code.ToString(), "item", 1, "kept"),
            kit,
            Def(firstOil?.Item.Code.ToString() ?? "game:oilportion-*", "item",
                firstOil != null ? litres / firstOil.LitresPerItem : litres * GearChain.PointsPerLitre, "oil", litres),
            Def(c.FrameCode, "block", 1, "station"));
        return new JObject
        {
            ["id"] = $"{CutterType}|{k.Blank.Code}|0",
            ["type"] = CutterType,
            ["mod"] = c.Mod,
            ["ingredients"] = ingredients,
            ["outputs"] = new JArray(Def(k.Gear.Code.ToString(), "item", 1)),
            ["variants"] = new JArray(new JObject
            {
                ["ingredients"] = new JArray
                {
                    new JArray(Stack(ctx, k.Blank, 1)),
                    new JArray(Stack(ctx, k.Master, 1)),
                    c.Kit != null ? new JArray(Stack(ctx, c.Kit, 1)) : new JArray(),
                    new JArray(c.Oils.Select(o => Stack(ctx, o.Item, litres / o.LitresPerItem, litres))),
                    c.Frame != null ? new JArray(Stack(ctx, c.Frame, 1)) : new JArray(),
                },
                ["outputs"] = new JArray(Stack(ctx, k.Gear, 1)),
            }),
            ["machine"] = new JObject
            {
                ["power"] = "mechanical",
                ["turns"] = k.Teeth * c.TurnsPerTooth,
                ["work"] = new JObject { ["amount"] = k.Teeth, ["unit"] = "teeth", ["turnsPerUnit"] = c.TurnsPerTooth },
                ["kept"] = new JArray(1),
                ["wear"] = new JObject { ["ingredient"] = 2, ["rule"] = "dividedByOilFill" },
                ["oil"] = new JObject { ["ingredient"] = 3, ["points"] = Num(k.Drain), ["tank"] = Num(c.Tank) },
            },
            ["extra"] = new JObject { ["rig"] = GearChain.RigAsset.ToString(), ["class"] = k.Name },
        };
    }

    /// <summary>One tool mold (all its colours): the mold (role station), the metal poured in as its
    /// ingot (wildcard metal; quantity in ingots, `extra.units` in units) and what it drops.</summary>
    private static JObject CastingRecord(Context ctx, Items.ModIndex mods, MoldGroup g)
    {
        var first = g.Molds[0];
        var moldCode = first.Variant.ContainsKey("color")
            ? first.Code.Domain + ":" + first.Code.Path.Replace("-" + first.Variant["color"] + "-", "-*-")
            : first.Code.ToString();
        double ingots = g.Units / 100.0;
        var metal = Def("game:ingot-*", "item", ingots, "metal");
        metal["wildcardName"] = "metal";
        metal["extra"] = new JObject { ["units"] = g.Units };
        var variants = g.Casts.Select(c => new JObject
        {
            ["bindings"] = new JObject { ["metal"] = c.Metal },
            ["ingredients"] = new JArray
            {
                new JArray(g.Molds.Select(m => Stack(ctx, m, 1))),
                new JArray(Stack(ctx, c.Ingot, ingots)),
            },
            ["outputs"] = new JArray(c.Drops.Select(s => Stack(ctx, s.Collectible, s.StackSize))),
        }).ToList();
        variants.Sort((a, b) => string.CompareOrdinal((string)a["bindings"]!["metal"]!, (string)b["bindings"]!["metal"]!));
        return new JObject
        {
            ["id"] = $"{CastingType}|{first.Code}|0",
            ["type"] = CastingType,
            ["mod"] = mods.ModForCollectible(first),
            ["ingredients"] = new JArray(Def(moldCode, "block", 1, "station"), metal),
            ["outputs"] = new JArray(g.Templates.Select(t => Def(t.Code, Kind(t.Type), t.Quantity))),
            ["variants"] = new JArray(variants),
            ["requirements"] = new JArray($"Pour {g.Units} units of molten metal into the mold from a crucible, and take the casting out once it has hardened"),
            ["extra"] = new JObject { ["blockClass"] = nameof(Vintagestory.GameContent.BlockToolMold) },
        };
    }
}
