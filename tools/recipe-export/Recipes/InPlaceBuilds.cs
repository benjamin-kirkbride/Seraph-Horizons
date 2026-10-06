using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>
/// A block built in place: placed, then completed stage by stage with right clicks that each
/// consume stacks from the hotbar (BEBehaviorRightClickConstructable or a subclass, or exlib's
/// ExRightClickConstructable, which since exlib 0.8 is its own behavior with stages of the same
/// shape). The water wheel and ppex's machines work this way.
/// Blocks of one type whose stages are equal (the four sides of a pump) are one build.
/// </summary>
public sealed class InPlaceBuild
{
    /// <summary>The block the record shows: the first one with a handbook page, else the first.</summary>
    public required Block Block;
    public required List<Block> Members;
    public required string Behavior;
    public required int BehaviorIndex;
    public float? BrokenDropsRatio;

    /// <summary>exlib's behavior (0.8 on) refuses a stage paid with one stored wildcard in two
    /// variants, so the slots of a stage that store the same group take the same value.</summary>
    public bool OneVariantPerStage;

    /// <summary>Every stage, the placed block (stage 0) included.</summary>
    public List<BuildStage> Stages = new();

    /// <summary>Every consumed stack, in stage order; stages refer to these by index.</summary>
    public List<BuildSlot> Slots = new();

    /// <summary>One per combination of the stored wildcards that later stages use.</summary>
    public List<BuildVariant> Variants = new();
}

public sealed class BuildStage
{
    public List<int> Slots = new();

    /// <summary>The lang code of the right-click action; null when it is the default ("Construct").</summary>
    public string? ActionLangCode;
}

public sealed class BuildSlot
{
    public required int Stage;
    public required SlotForm Form;

    /// <summary>Variant group whose value the stack used here is remembered under.</summary>
    public string? StoreWildCard;

    /// <summary>Lang code of the slot's name, shown in game for a wildcard ingredient.</summary>
    public string? NameLangCode;
}

public sealed class BuildVariant
{
    public Dictionary<string, string> Bindings = new(StringComparer.Ordinal);

    /// <summary>Per slot: the spec with placeholders filled, and the variant value it must have, if any.</summary>
    public List<(StackSpec Spec, string? Group, string? Value)> Slots = new();
}

public static class InPlaceBuilds
{
    public const string DefaultActionLangCode = "rollers-construct";

    /// <summary>exlib's construction behavior: a subclass of the engine's up to exlib 0.7, its own
    /// class (ExRightClickConstruction, same JSON stages) from 0.8. Matched by name, as exlib is not
    /// referenced.</summary>
    public const string ExlibBehaviorType = "ExpandedLib.Blocks.ExRightClickConstructable";

    /// <summary>Whether a block-entity behavior class builds its block in place: the engine's
    /// behavior or a subclass, or exlib's (or a subclass of it).</summary>
    public static bool IsConstructable(Type? type) =>
        type != null && (typeof(BEBehaviorRightClickConstructable).IsAssignableFrom(type) || IsExlibOwn(type));

    /// <summary>Whether the class is exlib's own behavior (0.8 on) or a subclass of it, as opposed
    /// to a subclass of the engine's.</summary>
    private static bool IsExlibOwn(Type type)
    {
        if (typeof(BEBehaviorRightClickConstructable).IsAssignableFrom(type)) return false;
        for (var t = type; t != null; t = t.BaseType)
            if (t.FullName == ExlibBehaviorType) return true;
        return false;
    }

    public static List<InPlaceBuild> Find(ICoreServerAPI api, StackExpander expander)
    {
        var groups = new Dictionary<string, InPlaceBuild>(StringComparer.Ordinal);
        var ordered = new List<InPlaceBuild>();
        foreach (var block in api.World.Blocks.Where(b => b?.Code != null && !b.IsMissing)
                     .OrderBy(b => b.Code.ToString(), StringComparer.Ordinal))
        {
            var behaviors = block.BlockEntityBehaviors ?? [];
            for (int i = 0; i < behaviors.Length; i++)
            {
                var type = api.ClassRegistry.GetBlockEntityBehaviorClass(behaviors[i].Name);
                if (!IsConstructable(type)) continue;
                var props = behaviors[i].properties;
                var stagesJson = props?["stages"];
                if (stagesJson == null || !stagesJson.Exists) continue;

                var key = string.Join("|", block.Code.Domain, block.FirstCodePart(), behaviors[i].Name, stagesJson.ToString());
                if (groups.TryGetValue(key, out var existing))
                {
                    existing.Members.Add(block);
                    continue;
                }
                // Parsed as the behavior parses it (JsonObject.AsObject, domain "game"); exlib's
                // ExConstructionStage has the engine's fields, so it parses the same.
                var stages = stagesJson.AsObject<ConstructionStage[]>() ?? [];
                var build = new InPlaceBuild
                {
                    Block = block,
                    Members = new() { block },
                    Behavior = behaviors[i].Name,
                    BehaviorIndex = i,
                    BrokenDropsRatio = props!["brokenDropsRatio"].Exists ? props["brokenDropsRatio"].AsFloat() : null,
                    OneVariantPerStage = IsExlibOwn(type!),
                };
                ReadStages(build, stages);
                groups[key] = build;
                ordered.Add(build);
            }
        }

        foreach (var build in ordered)
        {
            build.Block = build.Members.FirstOrDefault(b => Items.HandbookRule.PagesFor(b).Any()) ?? build.Members[0];
            build.Variants = Resolve(api.World, expander, build.Slots, build.OneVariantPerStage);
        }
        return ordered;
    }

    private static void ReadStages(InPlaceBuild build, ConstructionStage[] stages)
    {
        for (int s = 0; s < stages.Length; s++)
        {
            var stage = new BuildStage
            {
                ActionLangCode = stages[s].ActionLangCode is { } a && a != DefaultActionLangCode ? a : null,
            };
            // Stage 0 is the block as placed: the engine never asks for its stacks.
            if (s > 0)
            {
                foreach (var ing in stages[s].RequireStacks ?? [])
                {
                    if (ing?.Code == null) continue;
                    var form = Readers.Slot(ing);
                    // `name` here is a lang code for display, not a wildcard variable.
                    form.Primary.WildcardName = null;
                    stage.Slots.Add(build.Slots.Count);
                    build.Slots.Add(new BuildSlot
                    {
                        Stage = s,
                        Form = form,
                        StoreWildCard = ing.StoreWildCard,
                        NameLangCode = ing.Name,
                    });
                }
            }
            build.Stages.Add(stage);
        }
    }

    /// <summary>
    /// A stage fills `{group}` placeholders with the value remembered from the last slot that
    /// stored that group in an earlier stage (RightClickConstruction.StoredWildCards). Each
    /// placeholder group used becomes a binding, and the slot that stored it may only take
    /// stacks with the bound value. Groups stored but never used bind nothing: each slot takes
    /// any stack its own wildcard allows, as the engine does. With
    /// <paramref name="oneVariantPerStage"/> (exlib's behavior), the other slots of that slot's
    /// stage that store the same group must match it, so they take only the bound value too.
    /// </summary>
    internal static List<BuildVariant> Resolve(IWorldAccessor world, StackExpander expander, List<BuildSlot> slots,
        bool oneVariantPerStage = false)
    {
        // For each slot using `{group}`: the slot whose stored value it gets.
        var feeders = new Dictionary<string, SortedSet<int>>(StringComparer.Ordinal);
        for (int u = 0; u < slots.Count; u++)
        {
            foreach (var group in Placeholders(slots[u].Form.Primary.Code))
            {
                int feeder = -1;
                for (int j = 0; j < slots.Count && slots[j].Stage < slots[u].Stage; j++)
                    if (slots[j].StoreWildCard == group) feeder = j;
                if (feeder < 0) continue;
                if (!feeders.TryGetValue(group, out var set)) feeders[group] = set = new();
                set.Add(feeder);
            }
        }

        // The values a group can take: what its first feeder slot accepts.
        var values = new List<(string Group, List<string> Values)>();
        foreach (var (group, set) in feeders.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var spec = slots[set.Min].Form.Primary;
            var found = expander.Accepted(spec)
                .Select(s => Lookup(world, s))
                .Select(c => c?.Variant?[group])
                .OfType<string>()
                .Distinct()
                .OrderBy(v => v, StringComparer.Ordinal)
                .ToList();
            if (found.Count > 0) values.Add((group, found));
        }

        var combinations = new List<Dictionary<string, string>> { new(StringComparer.Ordinal) };
        foreach (var (group, vals) in values)
            combinations = combinations
                .SelectMany(c => vals.Select(v => new Dictionary<string, string>(c, StringComparer.Ordinal) { [group] = v }))
                .ToList();

        var variants = new List<BuildVariant>();
        foreach (var bindings in combinations)
        {
            var variant = new BuildVariant { Bindings = bindings };
            for (int i = 0; i < slots.Count; i++)
            {
                var spec = Fill(slots[i].Form.Primary, bindings);
                var group = slots[i].StoreWildCard;
                var restricted = group != null && bindings.ContainsKey(group)
                                 && (feeders[group].Contains(i)
                                     || oneVariantPerStage && feeders[group].Any(f => slots[f].Stage == slots[i].Stage));
                variant.Slots.Add((spec, restricted ? group : null, restricted ? bindings[group!] : null));
            }
            variants.Add(variant);
        }
        return variants;
    }

    internal static IEnumerable<string> Placeholders(AssetLocation? code)
    {
        if (code == null) yield break;
        var path = code.Path;
        for (int start = path.IndexOf('{'); start >= 0; start = path.IndexOf('{', start + 1))
        {
            var end = path.IndexOf('}', start);
            if (end > start + 1) yield return path[(start + 1)..end];
        }
    }

    private static StackSpec Fill(StackSpec spec, Dictionary<string, string> bindings)
    {
        if (spec.Code == null || !spec.Code.Path.Contains('{')) return spec;
        var path = spec.Code.Path;
        foreach (var (group, value) in bindings) path = path.Replace("{" + group + "}", value);
        return new StackSpec
        {
            Type = spec.Type,
            Code = new AssetLocation(spec.Code.Domain, path),
            AllowedVariants = spec.AllowedVariants,
            SkipVariants = spec.SkipVariants,
            Tags = spec.Tags,
            Quantity = spec.Quantity,
            Litres = spec.Litres,
            Attributes = spec.Attributes,
        };
    }

    internal static CollectibleObject? Lookup(IWorldAccessor world, Newtonsoft.Json.Linq.JObject stack)
    {
        var code = new AssetLocation((string)stack["code"]!);
        CollectibleObject? c = (string?)stack["kind"] == "block" ? world.GetBlock(code) : world.GetItem(code);
        return c == null || c.IsMissing ? null : c;
    }
}
