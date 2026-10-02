using SeraphHorizons.SeraphTweaks.TidyVariants.Core;
using static SeraphHorizons.SeraphTweaks.TidyVariants.Tests.Fx;

namespace SeraphHorizons.SeraphTweaks.TidyVariants.Tests;

/// <summary>
/// The general causes behind the first in-game report of under-grouping (termite mounds as 74 tiles, soil and
/// paintings as one tile per variant), each with real-shaped data from vanilla 1.22.7 and the pack's mods.
/// </summary>
public class UnderGroupingTests
{
    static readonly string[] MoreRocks = [.. Rock, "jade", "jasper", "komatiite", "arenite", "arkose", "marl", "tufa", "wacke"];

    [Fact]
    public void Termite_mounds_and_their_harvested_form_are_one_tile()
    {
        // termitemound-{rock}-{size} and termitemound-harvested-{rock}-{size}: two block types, 38 rocks (GeologyAdditions'
        // jade and jasper not in vanilla's list), sizes medium/large. "harvested" in the base is a process word.
        // The game lists the harvested ones first.
        var entries = Product("game:termitemound-harvested", EntryKind.Block, ("rock", MoreRocks), ("size", ["medium", "large"]))
            .Concat(Product("game:termitemound", EntryKind.Block, ("rock", MoreRocks), ("size", ["medium", "large"]))).ToList();
        var r = Resolve(entries);
        Assert.Equal(2, r.Families.Count);
        Assert.Equal(DimensionClass.Material, r.Families[0].Dimensions[0].Class);
        Assert.Equal(DimensionClass.GradeSizeQuality, r.Families[0].Dimensions[1].Class);
        var g = Assert.Single(r.Groups);
        Assert.Equal(entries.Count, g.Members.Count);
        Assert.Equal("auto:game:termitemound", g.Id);
        Assert.Equal("game:termitemound-harvested-granite-medium", r.Entries[g.Representative].Code);
        Assert.Empty(Handbook.Build(r).Issues);
    }

    [Fact]
    public void Process_words_in_the_base_merge_only_matching_families()
    {
        Assert.Equal("termitemound", TidyEngine.GroupBase("termitemound-harvested"));
        Assert.Equal("log", TidyEngine.GroupBase("log-placed"));
        Assert.Equal("raw", TidyEngine.GroupBase("raw"));          // the first token always stays
        Assert.Equal("cheese-wheel", TidyEngine.GroupBase("cheese-wheel"));
        // Same base but a different meaningful value: still apart.
        var r = Resolve([
            E("game:thing", EntryKind.Block, ("type", "a"), ("rock", "granite")),
            E("game:thing", EntryKind.Block, ("type", "a"), ("rock", "basalt")),
            E("game:thing-cooked", EntryKind.Block, ("type", "b"), ("rock", "granite")),
            E("game:thing-cooked", EntryKind.Block, ("type", "b"), ("rock", "basalt")),
        ]);
        Assert.Equal(2, r.Groups.Count);
    }

    [Fact]
    public void Soil_fertility_and_grass_coverage_are_levels()
    {
        // game:soil-{fertility}-{grasscoverage}, 20 blocks named "Barren soil (Sparse grass)", "Terra preta (Grassy)", ...
        var r = Resolve(Product("game:soil", EntryKind.Block,
            ("fertility", ["verylow", "low", "medium", "compost", "high"]), ("grasscoverage", ["none", "verysparse", "sparse", "normal"])));
        Assert.All(r.Families[0].Dimensions, d => Assert.Equal(DimensionClass.GradeSizeQuality, d.Class));
        Assert.Equal(20, Assert.Single(r.Groups).Members.Count);
        // DesirePaths: soilpath-{fertility}-{grasscoverage} with "forest" and numbered coverage.
        Assert.Equal(DimensionClass.GradeSizeQuality, Classify("fertility", "compost", "forest", "high", "low", "medium", "verylow").Class);
        Assert.Equal(DimensionClass.GradeSizeQuality, Classify("type", "fish", "large", "medium", "small", "tiny").Class); // carcass
        // Not enough level words: a shape stays meaningful.
        Assert.Equal(DimensionClass.Meaningful, Classify("state", "left", "half", "right", "halftop", "fulltop").Class);
    }

    [Fact]
    public void Numbers_are_filler_and_numbered_values_share_a_group()
    {
        Assert.Equal(DimensionClass.Filler, Classify("grass", "0", "1", "2", "3", "4", "5", "6", "7").Class);   // forestfloor
        Assert.Equal(DimensionClass.Filler, Classify("texture", "1", "2", "3", "10").Class);                 // butchering
        Assert.Equal(DimensionClass.Orientation, Classify("rotation", "0", "90", "180", "270").Class);       // names first
        Assert.Equal(DimensionClass.ProcessState, Classify("stage", "1", "2", "3").Class);

        Assert.Equal("collapsed", Vocabulary.NumberStem("collapsed3"));
        Assert.Equal("ruined-barred", Vocabulary.NumberStem("ruined-barred2"));
        Assert.Equal("base-short", Vocabulary.NumberStem("base2-short"));
        Assert.Equal("bookshelves/bookshelf-alchemy", Vocabulary.NumberStem("bookshelves/bookshelf-alchemy01"));
        Assert.Equal("round2x1", Vocabulary.NumberStem("round2x1"));   // a size keeps its numbers
        Assert.Equal("2x2gate", Vocabulary.NumberStem("2x2gate"));
        Assert.Equal("12", Vocabulary.NumberStem("12"));
        string same = "plain";
        Assert.Same(same, Vocabulary.NumberStem(same));

        // game:door-{style}: crude plus ruined-barred1..3 and ruined-rough1..3 give three tiles.
        var r = Resolve(Product("game:door", EntryKind.Block,
            ("style", ["crude", "ruined-barred1", "ruined-barred2", "ruined-barred3", "ruined-rough1", "ruined-rough2", "ruined-rough3"])));
        var style = r.Families[0].Dimensions[0];
        Assert.Equal(DimensionClass.Meaningful, style.Class);
        Assert.True(style.ByStem);
        Assert.Equal(["auto:game:door/style=ruined-barred", "auto:game:door/style=ruined-rough"], r.Groups.Select(g => g.Id));
        Assert.Equal(-1, r.GroupOf(r.Index("game:door-crude")));
        Assert.Empty(Handbook.Build(r).Issues);
    }

    [Fact]
    public void Mixed_stone_and_metal_materials_collapse()
    {
        // game:axehead-{material}: knapped stone and cast metal heads, 22 of 24 values rock or metal.
        var info = Classify("material", "andesite", "basalt", "chert", "flint", "granite", "obsidian", "copper", "tinbronze", "iron", "steel", "bismuthbronze");
        Assert.Equal(DimensionClass.Material, info.Class);
        Assert.Equal(MaterialKind.Rock, info.Material);
        Assert.Contains("rock, wood or metal", info.Reason);
        // game:beam-plane: woods, clays and metals together.
        Assert.Equal(MaterialKind.Wood, Classify("material", "oak", "pine", "birch", "copper", "blackclay", "iron").Material);
        Assert.Equal(DimensionClass.Meaningful, Classify("type", "rusty", "temporal").Class);
        // Generic material words: yangtransport's widerails_*-{dir}-{wood|metal}, alchemy's mortarpestle-{wood|stone}.
        Assert.Equal(DimensionClass.Material, Classify("material", "wood", "metal").Class);
        Assert.Equal(DimensionClass.Material, Classify("type", "wood", "stone").Class);
        Assert.Equal(DimensionClass.Meaningful, Classify("type", "bone", "horn", "feather", "fish").Class);
    }

    [Fact]
    public void One_collectibles_attribute_stacks_are_one_tile()
    {
        // game:bookshelf: one code, stacks type (1row1col..2row2col) x material (woods). The handbook can only group
        // whole codes, so splitting the stacks by type would be a groupby-conflict.
        var stacks = new List<CreativeEntry>();
        foreach (var type in new[] { "1row1col", "1row2col", "2row1col", "2row2col" })
            foreach (var wood in new[] { "oak", "birch", "aged" })
                stacks.Add(E("game:bookshelf", EntryKind.Block, [], Stack($"type={type},material={wood}", ("type", type), ("material", wood))));
        var r = Resolve(stacks);
        Assert.Equal(12, Assert.Single(r.Groups).Members.Count);
        Assert.Empty(Handbook.Build(r).Issues);

        // Bucket contents (game:woodbucket attr:ucontents JSON) likewise; an override can still split them.
        var buckets = new[] { "waterportion", "honeyportion", "milkportion" }
            .Select(c => E("game:woodbucket", EntryKind.Block, [], Stack(c, ("ucontents", $"[{{ \"type\": \"item\", \"code\": \"{c}\", \"makefull\": true }}]")))).ToList();
        Assert.Single(Resolve(buckets).Groups);
        Assert.Empty(Resolve(buckets, """{ "rules": [ { "match": { "domain": "game", "code": "woodbucket" }, "split": ["ucontents"] } ] }""").Groups);
    }

    [Fact]
    public void Stacks_without_exposed_attributes_follow_their_variants()
    {
        // stonequarry:stoneslab-{size}-{side}, one stack per code whose only attribute (a rock preset) isn't exposed.
        var slabs = new[] { "small", "medium", "large", "huge", "giant" }
            .Select(size => E($"game:stoneslab", EntryKind.Block, [("size", size), ("side", "north")], Stack("preset-" + size), "decorative")).ToList();
        var g = Assert.Single(Resolve(slabs).Groups);
        Assert.Equal(5, g.Members.Count);
    }

    [Fact]
    public void An_empty_material_list_is_reported()
    {
        // Vanilla's rock.json and wood.json write "Code"; a reader that only knows "code" got empty lists, and no rock
        // or wood dimension in the pack was recognised (#252 follow-up).
        var issues = new List<TidyIssue>();
        var props = new Dictionary<string, IReadOnlyList<string>>(WorldProperties) { ["game:block/rock"] = [] };
        _ = new TidySettings(props, null, issues);
        Assert.Contains(issues, i => i.Kind == "property-empty" && i.Message.Contains("game:block/rock"));
    }
}
