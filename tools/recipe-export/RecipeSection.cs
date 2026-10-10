using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Items;
using SeraphHorizons.RecipeExport.Recipes;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport;

/// <summary>Writes `recipes` and `recipeTypes`. See docs/recipe-browser/exporter.md.</summary>
public static partial class RecipeSection
{
    /// <summary>English names of the base game's types; mod types get a name built from their code.</summary>
    private static readonly Dictionary<string, string> TypeNames = new()
    {
        ["grid"] = "Crafting grid",
        ["smithing"] = "Smithing",
        ["knapping"] = "Knapping",
        ["clayforming"] = "Clay forming",
        ["barrel"] = "Barrel",
        ["alloy"] = "Alloying",
        ["cooking"] = "Cooking",
    };

    /// <summary>Type code of blocks built in place (not a registry; see <see cref="InPlaceBuilds"/>).</summary>
    public const string InPlaceType = "construction";

    /// <summary>Type code of the Butchering mod's carcass processing (not a registry; see <see cref="Butchery"/>).</summary>
    public const string ButcheryType = "butchery";

    /// <summary>Type code of the Butchering mod's smoking rack (not a registry; see <see cref="Smoking"/>).</summary>
    public const string SmokingType = "smoking";

    /// <returns>Every item and block code the exported recipes reference.</returns>
    public static ISet<string> Fill(ICoreServerAPI api, JObject root) => Fill(api, root, Registries.Find(api));

    /// <summary>Exports the given registries; a registry that cannot be read throws <see cref="RecipeExportException"/>.</summary>
    public static ISet<string> Fill(ICoreServerAPI api, JObject root, IReadOnlyList<RegistryInfo> registries)
    {
        var ctx = new Context(api);
        var records = new List<JObject>();
        var types = new SortedDictionary<string, JObject>(StringComparer.Ordinal);
        var modNames = api.ModLoader.Mods.ToDictionary(m => m.Info.ModID, m => m.Info.Name);

        foreach (var reg in registries)
        {
            List<JObject> exported;
            string typeCode, shape;
            try
            {
                (typeCode, shape, exported) = ExportRegistry(ctx, reg, types.Keys);
            }
            catch (Exception e) when (e is not RecipeExportException)
            {
                throw new RecipeExportException($"Recipe registry '{reg.Code}' cannot be serialised: {e.Message}", e);
            }
            records.AddRange(exported);
            var name = TypeNames.GetValueOrDefault(typeCode) ??
                       $"{Capitalise(TypeName(reg.Code))} ({modNames.GetValueOrDefault(reg.Mod, reg.Mod)})";
            types[typeCode] = new JObject
            {
                ["name"] = name,
                ["count"] = exported.Count,
                ["shape"] = shape,
                ["registry"] = reg.Code,
                ["mod"] = reg.Mod,
            };
        }

        if (types.ContainsKey(InPlaceType))
            throw new RecipeExportException($"A recipe registry has the type code '{InPlaceType}' that built-in-place blocks use");
        var builds = InPlaceBuilds.Find(ctx.Api, ctx.Expander);
        var mods = new ModIndex(api);
        using (new EnglishLocale())
            records.AddRange(builds.Select(b => BuildRecord(ctx, mods, b)));
        types[InPlaceType] = new JObject
        {
            ["name"] = "Built in place",
            ["count"] = builds.Count,
            ["shape"] = "construction",
            ["registry"] = nameof(Vintagestory.GameContent.BEBehaviorRightClickConstructable),
            ["mod"] = "survival",
        };

        var butchery = Butchery.Find(ctx.Api);
        if (butchery != null)
        {
            if (types.ContainsKey(ButcheryType))
                throw new RecipeExportException($"A recipe registry has the type code '{ButcheryType}' that butchery uses");
            var chains = butchery.Chains.Select(c => ButcheryRecord(ctx, butchery, c)).ToList();
            records.AddRange(chains);
            types[ButcheryType] = new JObject
            {
                ["name"] = "Butchery",
                ["count"] = chains.Count,
                ["shape"] = "butchery",
                ["registry"] = butchery.BehaviorClass,
                ["mod"] = butchery.Mod,
            };
        }

        // One type per kind of transition, every kind even when no collectible has it, like
        // empty registries.
        var transitions = Transitions.Find(ctx.Api, out var skipped);
        if (skipped > 0)
            api.Logger.Warning("[seraphexport] {0} transition(s) whose transitioned stack did not resolve were skipped", skipped);
        var origins = new TransitionOrigins(api, mods);
        foreach (var (kind, (code, name)) in Transitions.Types)
        {
            if (types.ContainsKey(code))
                throw new RecipeExportException($"A recipe registry has the type code '{code}' that {name.ToLowerInvariant()} uses");
            var mine = transitions.Where(t => t.Props.Type == kind).ToList();
            records.AddRange(mine.Select(t => TransitionRecord(ctx, origins, code, t)));
            types[code] = new JObject
            {
                ["name"] = name,
                ["count"] = mine.Count,
                ["shape"] = "transition",
                ["registry"] = nameof(TransitionableProperties),
                ["mod"] = "game",
            };
        }

        var smoking = Smoking.Find(ctx.Api);
        if (smoking != null)
        {
            if (types.ContainsKey(SmokingType))
                throw new RecipeExportException($"A recipe registry has the type code '{SmokingType}' that the smoking rack uses");
            records.AddRange(smoking.Items.Select(s => SmokingRecord(ctx, smoking, s)));
            types[SmokingType] = new JObject
            {
                ["name"] = "Smoking rack",
                ["count"] = smoking.Items.Count,
                ["shape"] = "transition",
                ["registry"] = smoking.BlockEntityClass,
                ["mod"] = smoking.Mod,
            };
        }

        FillGearChain(ctx, records, types);
        FillDrawBench(ctx, records, types);
        FillPressBrake(ctx, records, types);
        FillSquaringShear(ctx, records, types);
        FillMandrelStation(ctx, records, types);
        FillSpalling(ctx, records, types);
        FillEidolon(ctx, records, types);
        FillOreRoasting(ctx, records, types);
        FillCasting(ctx, records, types);

        records.Sort((a, b) => string.CompareOrdinal((string)a["id"]!, (string)b["id"]!));
        for (int i = 1; i < records.Count; i++)
            if ((string)records[i]["id"]! == (string)records[i - 1]["id"]!)
                throw new RecipeExportException($"Duplicate recipe id {records[i]["id"]}");

        root["recipes"] = new JArray(records);
        Switches.AnnotateRecipes(api, (JArray)root["recipes"]!);
        root["recipeTypes"] = new JObject(types.Select(kv => new JProperty(kv.Key, kv.Value)));
        return ctx.Referenced;
    }

    /// <summary>The type part of a registry code: `smithingrecipes` is `smithing`.</summary>
    public static string TypeName(string registryCode)
    {
        foreach (var suffix in new[] { "recipes", "recipe" })
            if (registryCode.EndsWith(suffix) && registryCode.Length > suffix.Length)
                return registryCode[..^suffix.Length];
        return registryCode;
    }

    private static (string TypeCode, string Shape, List<JObject> Records) ExportRegistry(
        Context ctx, RegistryInfo reg, IEnumerable<string> taken)
    {
        var list = Registries.RecipeList(reg.Registry)
                   ?? throw new NotSupportedException($"{reg.Registry.GetType().FullName} keeps its recipes in no list");
        var element = Registries.ElementType(list)
                      ?? throw new NotSupportedException($"cannot tell the recipe class of {list.GetType().FullName}");
        var reader = Readers.For(element);

        var typeName = TypeName(reg.Code);
        var typeCode = Registries.BaseGameMods.Contains(reg.Mod) ? typeName : $"{reg.Mod}:{typeName}";
        if (taken.Contains(typeCode)) typeCode = $"{reg.Mod}:{reg.Code}";

        var read = new List<RecipeForm>();
        foreach (var recipe in list)
            if (recipe != null) read.Add(reader.Read(recipe));
        var registered = WaterClones.Fold(read, out var folded);
        if (folded > 0)
            ctx.Api.Logger.Notification("[seraphexport] {0}: folded {1} Hydrate or Diedrate water copies into their recipes", reg.Code, folded);

        // Definition files: the type's own folder, plus any other file a registered recipe's
        // Name points at (ACulinaryArtillery's simmerrecipes load from recipes/simmering).
        var files = new List<DefinitionFile>();
        var seen = new HashSet<string>();
        void AddFolder(string folder)
        {
            foreach (var f in ctx.Definitions.Folder(folder, element, reader))
                if (seen.Add(f.Location.ToString())) files.Add(f);
        }
        AddFolder(typeName);
        foreach (var name in registered.Select(r => r.Name).OfType<AssetLocation>().DistinctBy(n => n.ToString()))
        {
            var parts = name.Path.Split('/');
            if (parts.Length > 2 && parts[0] == "recipes" && name.Path.EndsWith(".json")) AddFolder(parts[1]);
            else if (parts[0] == "recipes" && !seen.Contains(name.ToString()) &&
                     ctx.Definitions.At(name, element, reader) is { } single && seen.Add(single.Location.ToString()))
                files.Add(single);
        }

        var records = new List<JObject>();
        foreach (var group in Grouper.Group(registered, files))
        {
            if (group.Definition != null && group.Definition.Form == null)
            {
                ctx.Api.Logger.Warning("[seraphexport] {0} entry {1} does not parse as {2}; not exported ({3})",
                    group.Definition.File.Location, group.Definition.Index, element.Name, group.Definition.ParseError);
                continue;
            }
            records.Add(Record(ctx, reg, typeCode, group));
        }
        return (typeCode, reader.Shape, records);
    }

    private static JObject Record(Context ctx, RegistryInfo reg, string typeCode, Group group)
    {
        var def = group.Definition;
        var first = group.Variants.FirstOrDefault()?.Form;
        var shape = group.Form ?? first!;
        var o = new JObject();
        if (def != null)
        {
            o["id"] = $"{typeCode}|{def.File.Location}|{def.Index}";
            o["type"] = typeCode;
            o["mod"] = def.File.Mod;
            o["source"] = def.File.Location.ToString();
            // Some mod loaders (ACulinaryArtillery) register definitions marked disabled.
            if (!def.Enabled && group.Variants.Count == 0) o["enabled"] = false;
        }
        else
        {
            // `r<n>` cannot clash with a definition's index when Name is a definition file.
            o["id"] = $"{typeCode}|{group.CodeSource ?? "code"}|r{group.CodeIndex}";
            o["type"] = typeCode;
            var domain = first!.Name?.Domain;
            o["mod"] = domain != null && ctx.ModIds.Contains(domain) ? domain : reg.Mod;
        }

        o["ingredients"] = new JArray(shape.Slots.Select(s => Ingredient(ctx, s)));
        o["outputs"] = new JArray(shape.Outputs.Select(Output));

        var variants = new List<JObject>();
        foreach (var v in group.Variants)
        {
            var slots = def != null ? Grouper.Align(shape.Slots, v.Form.Slots)! : v.Form.Slots.Select(s => (s, (SlotForm?)s)).ToList();
            var vo = new JObject();
            if (v.Bindings.Count > 0) vo["bindings"] = new JObject(v.Bindings.Select(kv => new JProperty(kv.Key, kv.Value)));
            vo["ingredients"] = new JArray(slots.Select(pair => new JArray(Accepted(ctx, pair.Item2))));
            vo["outputs"] = new JArray(v.Form.Outputs.Select(out_ => Produced(ctx, out_.Stack)).OfType<JObject>());
            variants.Add(vo);
        }
        // Registration order follows hash sets of variant values; sort for stable output.
        variants.Sort((a, b) => string.CompareOrdinal(a.ToString(Formatting.None), b.ToString(Formatting.None)));
        o["variants"] = new JArray(variants);

        var blocks = first ?? shape;
        if (blocks.BlockName != null && blocks.Block != null) o[blocks.BlockName] = blocks.Block.DeepClone();
        if (blocks.Voxels is { Count: > 0 } voxels && voxels.All(l => l.HasValues)) o["voxels"] = voxels.DeepClone();
        var requirements = (first ?? shape).Requirements;
        if (requirements.Count > 0) o["requirements"] = new JArray(requirements);

        var extra = (JObject)(first ?? shape).Extra.DeepClone();
        if ((first ?? shape).Attributes is JObject attributes && attributes.HasValues) extra["attributes"] = attributes.DeepClone();
        var water = group.Variants.SelectMany(v => v.Form.FoldedWater).ToList();
        if (water.Count > 0)
            extra["waterCopies"] = new JObject
            {
                ["mod"] = WaterClones.Mod,
                ["recipes"] = water.Count,
                ["water"] = new JArray(water.Distinct().OrderBy(w => w, StringComparer.Ordinal)),
            };
        if (def == null) extra["registeredByCode"] = true;
        else if (!def.Enabled && group.Variants.Count > 0) extra["disabledButRegistered"] = true;
        else if (def.Enabled && group.Variants.Count == 0) extra["resolved"] = false;
        if (def != null && group.Form?.Name != null) extra["name"] = group.Form.Name.ToString();
        if (extra.HasValues) o["extra"] = extra;
        return o;
    }

    private static JObject BuildRecord(Context ctx, ModIndex mods, InPlaceBuild build)
    {
        var code = build.Block.Code.ToString();
        var o = new JObject
        {
            ["id"] = $"{InPlaceType}|{code}|{build.BehaviorIndex}",
            ["type"] = InPlaceType,
            ["mod"] = mods.ModForCollectible(build.Block),
        };
        o["ingredients"] = new JArray(build.Slots.Select(s =>
        {
            if (s.NameLangCode != null) s.Form.Extra["name"] = Lang.Get(s.NameLangCode);
            if (s.StoreWildCard != null) s.Form.Extra["storeWildCard"] = s.StoreWildCard;
            return Ingredient(ctx, s.Form);
        }));
        var made = new StackSpec { Type = EnumItemClass.Block, Code = build.Block.Code, Resolved = new ItemStack(build.Block) };
        o["outputs"] = new JArray(new JObject { ["code"] = code, ["kind"] = "block", ["quantity"] = 1 });

        var variants = new List<JObject>();
        foreach (var v in build.Variants)
        {
            var vo = new JObject();
            if (v.Bindings.Count > 0) vo["bindings"] = new JObject(v.Bindings.Select(kv => new JProperty(kv.Key, kv.Value)));
            vo["ingredients"] = new JArray(v.Slots.Select(s => new JArray(Take(ctx,
                ctx.Expander.Accepted(s.Spec).Where(stack =>
                    s.Group == null || InPlaceBuilds.Lookup(ctx.Api.World, stack)?.Variant?[s.Group] == s.Value)))));
            vo["outputs"] = new JArray(new[] { Produced(ctx, made) }.OfType<JObject>());
            variants.Add(vo);
        }
        variants.Sort((a, b) => string.CompareOrdinal(a.ToString(Formatting.None), b.ToString(Formatting.None)));
        o["variants"] = new JArray(variants);

        o["construction"] = new JObject
        {
            ["stages"] = new JArray(build.Stages.Select(stage =>
            {
                var so = new JObject { ["ingredients"] = new JArray(stage.Slots) };
                if (stage.ActionLangCode != null) so["action"] = Lang.Get(stage.ActionLangCode);
                return so;
            })),
        };

        var extra = new JObject { ["behavior"] = build.Behavior };
        if (build.BrokenDropsRatio != null) extra["brokenDropsRatio"] = Num(build.BrokenDropsRatio.Value);
        if (build.Members.Count > 1) extra["members"] = new JArray(build.Members.Select(b => b.Code.ToString()));
        o["extra"] = extra;
        return o;
    }

    /// <summary>
    /// One creature's butchery: ingredients are the carcass in each state plus the stations
    /// and tools, outputs everything any stage gives, and `butchery` says which stage takes
    /// and gives what. Variants differ in what they give, so their yields are in
    /// `butchery.variants`, aligned with `outputs` (null where a variant gives nothing).
    /// </summary>
    private static JObject ButcheryRecord(Context ctx, ButcheryData data, ButcheryChain chain)
    {
        var ingredients = new List<(JObject Def, List<CollectibleObject> Accepts)>();
        int Slot(List<CollectibleObject> accepts, string role, int? toolCost = null, JObject? extra = null)
        {
            var c = accepts[0];
            var o = new JObject { ["code"] = c.Code.ToString(), ["kind"] = Kind(c.ItemClass), ["quantity"] = 1 };
            if (role == "tool") o["isTool"] = true;
            if (toolCost != null) o["toolDurabilityCost"] = toolCost;
            o["role"] = role;
            if (extra != null) o["extra"] = extra;
            ingredients.Add((o, accepts));
            return ingredients.Count - 1;
        }
        static List<CollectibleObject> Visible(IEnumerable<CollectibleObject> all)
        {
            var list = all.ToList();
            var shown = list.Where(c => HandbookRule.PagesFor(c).Any()).ToList();
            return shown.Count > 0 ? shown : list;
        }
        static JObject Efficiency(ButcheryStation station, List<CollectibleObject> blocks) => new()
        {
            ["efficiency"] = new JObject(blocks.Select(b => new JProperty(b.Code.ToString(), Num(station.Efficiency[b.Code.ToString()])))),
        };
        static List<CollectibleObject> All<T>(IEnumerable<T> list) where T : CollectibleObject => list.Cast<CollectibleObject>().ToList();

        // Outputs, each with a yield per variant (null when the variant does not give it).
        var variants = chain.Variants;
        var outputs = new List<(JObject Def, JObject?[] Yields, List<CollectibleObject> Alternatives)>();
        int Output(CollectibleObject c, System.Func<ButcheryVariant, JObject?> yield, JObject? extra = null, double? litres = null,
                   List<CollectibleObject>? alternatives = null)
        {
            var yields = variants.Select(yield).ToArray();
            var first = yields.First(y => y != null)!;
            var o = new JObject { ["code"] = c.Code.ToString(), ["kind"] = Kind(c.ItemClass), ["quantity"] = first["avg"]!.DeepClone() };
            if (litres != null) o["litres"] = Num(litres.Value);
            var e = extra ?? new JObject();
            if (alternatives is { Count: > 0 }) e["alternatives"] = new JArray(alternatives.Select(a => a.Code.ToString()));
            if (e.HasValues) o["extra"] = e;
            outputs.Add((o, yields, alternatives ?? new List<CollectibleObject>()));
            return outputs.Count - 1;
        }
        static JObject One(ButcheryVariant _) => new() { ["avg"] = 1 };
        // The drops of one stage: one output per item, in order of first appearance across
        // variants. A variant that drops an item twice (the mod's reward and the creature's own
        // drop) gets the sum.
        List<int> DropOutputs(System.Func<ButcheryVariant, List<BlockDropItemStack>> of, bool workstation, double scale)
        {
            var order = new List<CollectibleObject>();
            foreach (var v in variants)
            foreach (var d in of(v))
                if (!order.Contains(d.ResolvedItemstack!.Collectible)) order.Add(d.ResolvedItemstack!.Collectible);
            return order.Select(c =>
            {
                var drops = variants.SelectMany(of).Where(d => d.ResolvedItemstack!.Collectible == c).ToList();
                var scaledBy = new JArray();
                if (workstation) scaledBy.Add("efficiency");
                if (drops.Any(d => Butchery.ScaledByCondition(data, d, workstation))) scaledBy.Add("condition");
                return Output(c, v =>
                {
                    var mine = of(v).Where(d => d.ResolvedItemstack!.Collectible == c).ToList();
                    if (mine.Count == 0) return null;
                    var y = new JObject { ["avg"] = Num(mine.Sum(d => (double)(d.Quantity?.avg ?? 1)) * scale) };
                    var spread = mine.Sum(d => (double)(d.Quantity?.var ?? 0)) * scale;
                    if (spread != 0) y["var"] = Num(spread);
                    return y;
                }, scaledBy.Count > 0 ? new JObject { ["scaledBy"] = scaledBy } : null);
            }).ToList();
        }

        var stages = new JArray();
        var dead = All(chain.Dead);
        stages.Add(new JObject
        {
            ["step"] = "pickUp",
            ["ingredients"] = new JArray(),
            ["outputs"] = new JArray(Output(dead[0], One, alternatives: dead.Skip(1).ToList())),
        });

        var hooks = Visible(data.Hook.Blocks);
        var skin = new JObject
        {
            ["step"] = "skin",
            ["ingredients"] = new JArray(
                Slot(dead, "carcass"),
                Slot(hooks, "station", extra: Efficiency(data.Hook, hooks)),
                Slot(All(data.Knives), "tool", chain.KnifeCost)),
        };
        skin["outputs"] = new JArray(new[] { Output(chain.Skinned, One) }.Concat(DropOutputs(v => v.Skinning, true, 1)));
        stages.Add(skin);

        var bleed = new JObject { ["step"] = "bleed", ["ingredients"] = new JArray(Slot(new() { chain.Skinned }, "carcass")) };
        if (chain.BleedHours != null) bleed["hours"] = Num(chain.BleedHours.Value);
        var bleedOutputs = new JArray(Output(chain.BledOut, One));
        if (chain.Blood != null && chain.BloodAmount > 0 && data.Buckets.Count > 0)
        {
            var bucket = Slot(Visible(data.Buckets), "station");
            bleed["optional"] = new JArray(bucket);
            bleedOutputs.Add(Output(chain.Blood, _ => new JObject { ["avg"] = chain.BloodAmount },
                new JObject { ["needs"] = new JArray(bucket) }, chain.BloodLitres));
        }
        bleed["outputs"] = bleedOutputs;
        stages.Add(bleed);

        var tables = Visible(data.Table.Blocks);
        var butcher = new JObject
        {
            ["step"] = "butcher",
            ["ingredients"] = new JArray(Slot(new() { chain.BledOut }, "carcass"), Slot(tables, "station", extra: Efficiency(data.Table, tables))),
        };
        // new JArray(JArray) would copy the inner array's items, not nest it.
        var tools = new JArray { new JArray(Slot(All(data.Knives), "tool", chain.KnifeCost)) };
        if (data.Table.TakesCleaver && data.Cleavers.Count > 0) tools.Add(new JArray(Slot(All(data.Cleavers), "tool", chain.CleaverCost)));
        butcher["options"] = tools;
        var butcherOutputs = DropOutputs(v => v.Butchering, true, 1);
        foreach (var remains in variants.Select(v => v.Remains).OfType<Block>().Distinct())
            butcherOutputs.Add(Output(remains, v => v.Remains == remains ? new JObject { ["avg"] = 1 } : null));
        butcher["outputs"] = new JArray(butcherOutputs);
        stages.Add(butcher);

        if (variants.Any(v => v.FieldHarvest.Count > 0))
        {
            var harvest = new JObject { ["step"] = "harvest", ["ingredients"] = new JArray(Slot(All(data.Knives), "tool")) };
            if (data.FieldHarvestMultiplier is { } m) harvest["multiplier"] = Num(m);
            harvest["outputs"] = new JArray(DropOutputs(v => v.FieldHarvest, false, data.FieldHarvestMultiplier ?? 1));
            stages.Add(harvest);
        }

        var o = new JObject
        {
            ["id"] = $"{ButcheryType}|{chain.EntityType}|{dead[0].Code}",
            ["type"] = ButcheryType,
            ["mod"] = data.Mod,
            ["ingredients"] = new JArray(ingredients.Select(i => i.Def)),
            ["outputs"] = new JArray(outputs.Select(x => x.Def)),
        };
        var accepted = ingredients.Select(i => new JArray(Take(ctx, i.Accepts.Select(c => new JObject
        {
            ["code"] = c.Code.ToString(),
            ["kind"] = Kind(c.ItemClass),
            ["quantity"] = 1,
        })))).ToList();
        o["variants"] = new JArray(variants.Select((_, vi) =>
        {
            var given = new List<JObject>();
            foreach (var (def, yields, alternatives) in outputs)
            {
                if (yields[vi] is not { } y) continue;
                foreach (var code in new[] { (string)def["code"]! }.Concat(alternatives.Select(a => a.Code.ToString())))
                {
                    var stack = new JObject { ["code"] = code, ["kind"] = def["kind"]!.DeepClone(), ["quantity"] = y["avg"]!.DeepClone() };
                    if (def["litres"] != null) stack["litres"] = def["litres"]!.DeepClone();
                    ctx.Referenced.Add(code);
                    given.Add(stack);
                }
            }
            return new JObject
            {
                ["ingredients"] = new JArray(accepted.Select(a => a.DeepClone())),
                ["outputs"] = new JArray(given),
            };
        }));
        o["butchery"] = new JObject
        {
            ["entityType"] = chain.EntityType,
            ["workload"] = chain.Workload,
            ["stages"] = stages,
            ["variants"] = new JArray(variants.Select((v, vi) => new JObject
            {
                ["entities"] = new JArray(v.Entities.Select(e => new JObject { ["code"] = e.Code, ["name"] = e.Name })),
                ["yields"] = new JArray(outputs.Select(x => (JToken?)x.Yields[vi] ?? JValue.CreateNull())),
            })),
        };
        if (data.Condition is { } condition)
            o["butchery"]!["condition"] = new JObject { ["min"] = Num(condition.Min), ["max"] = Num(condition.Max) };
        o["extra"] = new JObject { ["behavior"] = data.BehaviorClass };
        return o;
    }

    /// <summary>
    /// One transition: the collectible is the one ingredient, the transitioned stack the one
    /// output, with the transition ratio (stacks out per stack in) as its quantity, since the
    /// engine sizes the new stack by the ratio and ignores the stack's own size.
    /// </summary>
    private static JObject TransitionRecord(Context ctx, TransitionOrigins origins, string type, Transition t)
    {
        var from = t.From.Code.ToString();
        var to = t.Output.Collectible.Code.ToString();
        var input = new JObject { ["code"] = from, ["kind"] = Kind(t.From.ItemClass), ["quantity"] = 1 };
        var output = new JObject { ["code"] = to, ["kind"] = Kind(t.Output.Class), ["quantity"] = Num(Json.Round(t.Props.TransitionRatio)) };
        if (t.Output.Attributes is { Count: > 0 } attrs) output["attributes"] = JObject.Parse(attrs.ToJsonToken());
        ctx.Referenced.Add(from);
        ctx.Referenced.Add(to);
        var (source, mod) = origins.Of(t);
        var o = new JObject
        {
            ["id"] = $"{type}|{from}|{t.Index}",
            ["type"] = type,
            ["mod"] = mod,
        };
        if (source != null) o["source"] = source.ToString();
        o.Merge(new JObject
        {
            ["ingredients"] = new JArray(input),
            ["outputs"] = new JArray(output),
            ["variants"] = new JArray(new JObject
            {
                // new JArray(JArray) would copy the inner array's items, not nest it.
                ["ingredients"] = new JArray { new JArray(input.DeepClone()) },
                ["outputs"] = new JArray(output.DeepClone()),
            }),
            ["transition"] = new JObject
            {
                ["type"] = Json.Lower(t.Props.Type),
                ["freshHours"] = Hours(t.Props.FreshHours),
                ["transitionHours"] = Hours(t.Props.TransitionHours),
            },
        });
        return o;
    }

    /// <summary>
    /// One item on the smoking rack, as a transition with a station: the item is the first
    /// ingredient, the racks the second (role station, not consumed), and the item it becomes
    /// the one output.
    /// </summary>
    private static JObject SmokingRecord(Context ctx, SmokingData data, Smoked s)
    {
        var from = s.From.Code.ToString();
        var to = s.Output.Code.ToString();
        var shown = data.Racks.Where(b => HandbookRule.PagesFor(b).Any()).ToList();
        var racks = shown.Count > 0 ? shown : data.Racks;
        var input = new JObject { ["code"] = from, ["kind"] = "item", ["quantity"] = 1 };
        var rack = new JObject { ["code"] = racks[0].Code.ToString(), ["kind"] = "block", ["quantity"] = 1, ["role"] = "station" };
        var output = new JObject { ["code"] = to, ["kind"] = "item", ["quantity"] = 1 };
        var accepted = racks.Select(b => new JObject { ["code"] = b.Code.ToString(), ["kind"] = "block", ["quantity"] = 1 }).ToList();
        ctx.Referenced.Add(from);
        ctx.Referenced.Add(to);
        foreach (var b in racks) ctx.Referenced.Add(b.Code.ToString());
        return new JObject
        {
            ["id"] = $"{SmokingType}|{from}|0",
            ["type"] = SmokingType,
            ["mod"] = data.Mod,
            ["ingredients"] = new JArray(input, rack),
            ["outputs"] = new JArray(output),
            ["variants"] = new JArray(new JObject
            {
                ["ingredients"] = new JArray { new JArray(input.DeepClone()), new JArray(accepted) },
                ["outputs"] = new JArray(output.DeepClone()),
            }),
            ["transition"] = new JObject
            {
                ["type"] = "smoke",
                ["freshHours"] = new JObject { ["avg"] = 0 },
                ["transitionHours"] = new JObject { ["avg"] = Num(data.Hours) },
            },
            ["requirements"] = new JArray("A burning firepit directly below the rack; the time starts over whenever the fire is out"),
            ["extra"] = new JObject { ["blockEntity"] = data.BlockEntityClass, ["attribute"] = Smoking.Attribute },
        };
    }

    private static JObject Hours(NatFloat? hours)
    {
        var o = new JObject { ["avg"] = Num(Json.Round(hours?.avg ?? 0)) };
        if (hours != null && hours.var != 0) o["var"] = Num(Json.Round(hours.var));
        return o;
    }

    private static JObject Ingredient(Context ctx, SlotForm slot)
    {
        var spec = slot.Primary;
        var o = new JObject();
        if (slot.Key != null) o["key"] = slot.Key;
        o["code"] = spec.Code?.ToString() ?? ctx.TagCode(spec.Tags);
        o["kind"] = Kind(spec.Type);
        o["quantity"] = Num(spec.Quantity);
        if (spec.Litres != null) o["litres"] = Num(spec.Litres.Value);
        if (spec.Attributes is JObject attrs && attrs.HasValues) o["attributes"] = attrs.DeepClone();
        if (spec.WildcardName != null && spec.Code != null && Readers.IsPattern(spec.Code)) o["wildcardName"] = spec.WildcardName;
        if (spec.AllowedVariants is { Length: > 0 }) o["allowedVariants"] = new JArray(spec.AllowedVariants);
        if (spec.SkipVariants is { Length: > 0 }) o["skipVariants"] = new JArray(spec.SkipVariants);
        if (slot.IsTool) o["isTool"] = true;
        if (slot.ToolDurabilityCost != null) o["toolDurabilityCost"] = slot.ToolDurabilityCost;
        if (slot.Returned != null)
        {
            var produced = Produced(ctx, slot.Returned);
            o["returned"] = produced ?? new JObject
            {
                ["code"] = slot.Returned.Code!.ToString(),
                ["kind"] = Kind(slot.Returned.Type),
                ["quantity"] = Num(slot.Returned.Quantity),
            };
        }
        if (slot.Role != null) o["role"] = slot.Role;
        if (slot.MinQuantity != null) o["minQuantity"] = Num(slot.MinQuantity.Value);
        if (slot.MaxQuantity != null) o["maxQuantity"] = Num(slot.MaxQuantity.Value);
        if (slot.MinRatio != null) o["minRatio"] = Num(slot.MinRatio.Value);
        if (slot.MaxRatio != null) o["maxRatio"] = Num(slot.MaxRatio.Value);
        var extra = (JObject)slot.Extra.DeepClone();
        if (!spec.Tags.IsEmpty) extra["tags"] = ctx.Tags(spec.Tags);
        if (extra.HasValues) o["extra"] = extra;
        return o;
    }

    private static JObject Output(OutputForm output)
    {
        var spec = output.Stack;
        var o = new JObject
        {
            ["code"] = spec.Code?.ToString() ?? "",
            ["kind"] = Kind(spec.Type),
            ["quantity"] = Num(spec.Quantity),
        };
        if (spec.Litres != null) o["litres"] = Num(spec.Litres.Value);
        if (spec.Attributes is JObject attrs && attrs.HasValues) o["attributes"] = attrs.DeepClone();
        if (output.Extra.HasValues) o["extra"] = output.Extra.DeepClone();
        return o;
    }

    /// <summary>Concrete stacks for a registered slot: the union over its alternatives, first wins.</summary>
    private static IEnumerable<JObject> Accepted(Context ctx, SlotForm? slot)
    {
        if (slot == null) yield break;
        foreach (var stack in Take(ctx, slot.Accepts.SelectMany(ctx.Expander.Accepted)))
            yield return stack;
    }

    /// <summary>Distinct stacks by code, first wins, recorded as referenced.</summary>
    private static IEnumerable<JObject> Take(Context ctx, IEnumerable<JObject> stacks)
    {
        var codes = new HashSet<string>();
        foreach (var stack in stacks)
        {
            var code = (string)stack["code"]!;
            if (!codes.Add(code)) continue;
            ctx.Referenced.Add(code);
            Round(stack);
            yield return stack;
        }
    }

    private static JObject? Produced(Context ctx, StackSpec spec)
    {
        var stack = ctx.Expander.Produced(spec);
        if (stack == null) return null;
        ctx.Referenced.Add((string)stack["code"]!);
        Round(stack);
        return stack;
    }

    private static void Round(JObject stack)
    {
        foreach (var key in new[] { "quantity", "litres" })
            if (stack[key] is JValue { Value: double d }) stack[key] = Num(d);
    }

    private static JToken Num(double d)
    {
        d = Math.Round(d, 6);
        return d == Math.Floor(d) && Math.Abs(d) < long.MaxValue ? new JValue((long)d) : new JValue(d);
    }

    private static string Kind(EnumItemClass type) => type == EnumItemClass.Block ? "block" : "item";

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private sealed class Context
    {
        public readonly ICoreServerAPI Api;
        public readonly StackExpander Expander;
        public readonly DefinitionIndex Definitions;
        public readonly HashSet<string> ModIds;
        public readonly HashSet<string> Referenced = new(StringComparer.Ordinal);

        public Context(ICoreServerAPI api)
        {
            Api = api;
            Expander = new StackExpander(api.World);
            Definitions = new DefinitionIndex(api);
            ModIds = api.ModLoader.Mods.Select(m => m.Info.ModID).ToHashSet();
        }

        public List<string> TagNames(TagSet set) => set.IsEmpty
            ? new()
            : Api.CollectibleTagRegistry.SlowEnumerateTagNames(set).OrderBy(t => t, StringComparer.Ordinal).ToList();

        /// <summary>The tag condition, as in the asset: conditions with required and forbidden tag names.</summary>
        public JObject Tags(ComplexTagCondition<TagSet> tags) => new()
        {
            ["disjunctive"] = tags.isDisjunctive,
            ["conditions"] = new JArray((tags.conditions ?? Array.Empty<ComplexTagCondition<TagSet>.Condition>())
                .Select(c => new JObject
                {
                    ["required"] = new JArray(TagNames(c.RequiredTags)),
                    ["forbidden"] = new JArray(TagNames(c.ForbiddenTags)),
                })),
        };

        /// <summary>
        /// A tags-only ingredient has no code, but the schema requires one: `tag:` plus its
        /// required tags. extra.tags has the full condition.
        /// </summary>
        public string TagCode(ComplexTagCondition<TagSet> tags)
        {
            var names = tags.conditions is { Length: > 0 } c ? TagNames(c[0].RequiredTags) : new();
            return "tag:" + (names.Count == 0 ? "any" : string.Join("+", names).Replace(':', '-'));
        }
    }
}

/// <summary>The export cannot represent the pack; names the registry at fault.</summary>
public sealed class RecipeExportException : Exception
{
    public RecipeExportException(string message, Exception? inner = null) : base(message, inner) { }
}
