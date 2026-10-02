using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using SeraphHorizons.SeraphTweaks.TidyVariants.Core;
using static SeraphHorizons.SeraphTweaks.TidyVariants.Tests.Fx;

namespace SeraphHorizons.SeraphTweaks.TidyVariants.Tests;

/// <summary>
/// The pack's shipped override file (<c>assets/seraphtweaks/config/tidyvariants-overrides.json</c>) and its group titles
/// (<c>assets/seraphtweaks/lang/en.json</c>): they parse, agree with each other, and group small real-shaped
/// fixtures as intended. Whether every rule matches something in the loaded pack is the Atlas scenario's job.
/// </summary>
public class ShippedOverridesTests
{
    static string ModDir([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, ".."));

    static readonly string OverridesPath = Path.Combine(ModDir(), "assets", "seraphtweaks", "config", "tidyvariants-overrides.json");
    static readonly string LangPath = Path.Combine(ModDir(), "assets", "seraphtweaks", "lang", "en.json");

    static readonly Lazy<OverrideFile> Shipped = new(() => OverrideFile.Parse(File.ReadAllText(OverridesPath)));

    static readonly Lazy<Dictionary<string, string>> Lang = new(() =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(LangPath))!);

    // Group titles are seraphtweaks lang keys; the feature's keys all start with its own prefix.
    const string Domain = "seraphtweaks:";
    const string GroupPrefix = "tidyvariants-group-";

    static TidyResolution Resolve(IEnumerable<CreativeEntry> entries)
    {
        var r = TidyEngine.Resolve(entries.ToList(), WorldProperties, Shipped.Value);
        var bad = r.Issues.Where(i => i.Kind is not ("rule-unused" or "property-missing")).ToList();
        Assert.True(bad.Count == 0, string.Join("\n", bad));
        return r;
    }

    /// <summary>The group's title resolved to an en.json key (asserting it exists), for readable asserts.</summary>
    static string TitleText(TidyGroup g)
    {
        Assert.NotNull(g.Title);
        Assert.StartsWith(Domain, g.Title);
        string key = g.Title![Domain.Length..];
        Assert.True(Lang.Value.TryGetValue(key, out var text), $"en.json has no '{key}' (group {g.Id})");
        return text!;
    }

    static Regex TemplateRegex(string titleKey) =>
        new("^" + string.Join(".+", Regex.Split(titleKey, @"\{[^{}]+\}").Select(Regex.Escape)) + "$");

    [Fact]
    public void Parses_without_errors()
    {
        bool ok = OverrideFile.TryParse(File.ReadAllText(OverridesPath), out var file, out var errors);
        Assert.True(ok, string.Join("\n", errors));
        Assert.NotEmpty(file!.Rules);
    }

    [Fact]
    public void Nothing_is_hidden_outright()
    {
        // The epic's principle: hiding is only for orientation and open/closed variants (the automatic rule).
        Assert.DoesNotContain(Shipped.Value.Rules, r => r.Action == RuleAction.Hide);
    }

    [Fact]
    public void Every_title_key_exists_and_every_group_key_is_used()
    {
        var titles = Shipped.Value.Rules.Where(r => r.Group?.Title is not null).Select(r => r.Group!.Title!).ToList();
        var groupKeys = Lang.Value.Keys.Where(k => k.StartsWith(GroupPrefix, StringComparison.Ordinal)).ToList();

        foreach (var t in titles)
        {
            Assert.StartsWith(Domain, t);
            string key = t[Domain.Length..];
            Assert.StartsWith(GroupPrefix, key);
            if (key.Contains('{'))
            {
                var re = TemplateRegex(key);
                Assert.True(groupKeys.Any(re.IsMatch), $"no en.json key matches the title template '{key}'");
            }
            else Assert.True(Lang.Value.ContainsKey(key), $"en.json has no '{key}'");
        }

        var templates = titles.Select(t => t[Domain.Length..]).Select(k => k.Contains('{') ? TemplateRegex(k) : new Regex("^" + Regex.Escape(k) + "$")).ToList();
        var unused = groupKeys.Where(k => !templates.Any(re => re.IsMatch(k))).ToList();
        Assert.True(unused.Count == 0, "en.json group keys no rule title produces: " + string.Join(", ", unused));
    }

    [Fact]
    public void Ore_blocks_and_ore_items_with_the_same_code_group_apart_with_their_own_titles()
    {
        string[] rocks = ["granite", "andesite"];
        var entries = new List<CreativeEntry>();
        entries.AddRange(Product("game:ore", EntryKind.Block, ("grade", Grades), ("type", ["hematite", "limonite"]), ("rock", rocks)));
        entries.AddRange(Product("game:ore", EntryKind.Block, ("potential", ["low", "medium", "high"]), ("type", ["corundumruby"]), ("rock", rocks)));
        entries.AddRange(Product("game:ore", EntryKind.Block, ("type", ["bituminouscoal"]), ("rock", rocks)));
        entries.AddRange(Product("game:ore", EntryKind.Item, ("grade", Grades), ("ore", ["hematite"]), ("rock", rocks)));
        entries.AddRange(Product("game:ore", EntryKind.Item, ("ore", ["bituminouscoal", "sulfur"])));
        var r = Resolve(entries);

        int Block(string code) => Enumerable.Range(0, r.Entries.Count).First(i => r.Entries[i].Code == code && r.Entries[i].Kind == EntryKind.Block);
        int Item(string code) => Enumerable.Range(0, r.Entries.Count).First(i => r.Entries[i].Code == code && r.Entries[i].Kind == EntryKind.Item);

        var hematiteBlock = r.Groups[r.GroupOf(Block("game:ore-poor-hematite-granite"))];
        var hematiteItem = r.Groups[r.GroupOf(Item("game:ore-poor-hematite-granite"))];
        Assert.NotSame(hematiteBlock, hematiteItem);
        Assert.Equal("game-ore-hematite", hematiteBlock.Id);
        Assert.Equal(8, hematiteBlock.Members.Count);
        Assert.Equal("Iron ore (hematite)", TitleText(hematiteBlock));
        Assert.Equal("game:ore-medium-hematite-granite", r.Entries[hematiteBlock.Representative].Code);
        Assert.Equal("game-orechunk-hematite", hematiteItem.Id);
        Assert.Equal("Chunk of Hematite", TitleText(hematiteItem));

        Assert.Equal("Iron ore (limonite)", TitleText(r.Groups[r.GroupOf(Block("game:ore-rich-limonite-andesite"))]));

        // Gem potential (low/medium/high) is not a grade word, but the rule still collapses it.
        var ruby = r.Groups[r.GroupOf(Block("game:ore-high-corundumruby-granite"))];
        Assert.Equal(6, ruby.Members.Count);
        Assert.Equal("Ruby ore", TitleText(ruby));

        Assert.Equal("Black coal", TitleText(r.Groups[r.GroupOf(Block("game:ore-bituminouscoal-granite"))]));
        // Ungraded ore items have no rock: single entries.
        Assert.Equal(-1, r.GroupOf(Item("game:ore-bituminouscoal")));
        Assert.Equal(-1, r.GroupOf(Item("game:ore-sulfur")));
    }

    [Fact]
    public void Loose_and_crystallized_ores_get_mineral_titles()
    {
        var entries = Product("game:looseores", EntryKind.Block, ("ore", ["magnetite", "quartz_nativesilver"]), ("rock", ["granite", "basalt"]), ("cover", ["free"]))
            .Concat(Product("game:crystalizedore", EntryKind.Item, ("grade", Grades), ("ore", ["galena_nativesilver"]), ("rock", ["granite"])));
        var r = Resolve(entries);
        Assert.Equal("Iron ore bits (magnetite)", TitleText(r.GroupOf("game:looseores-magnetite-basalt-free")));
        Assert.Equal("Native silver bits (in quartz)", TitleText(r.GroupOf("game:looseores-quartz_nativesilver-granite-free")));
        Assert.Equal("Crystallized chunk of Native silver in galena", TitleText(r.GroupOf("game:crystalizedore-rich-galena_nativesilver-granite")));
    }

    [Fact]
    public void DoorVariants_doors_group_per_style()
    {
        string[] woods = ["aged", "veryaged", "oak", "birch"];
        var entries = Product("doorvariants:cobbledoor", EntryKind.Block, ("style", ["solid3x1cobblestone"]), ("material", ["granite", "basalt", "obsidian"]), ("wood", woods))
            .Concat(Product("doorvariants:rounddoor", EntryKind.Block, ("style", ["round2x1cobblestone", "round2x2cobblestone"]), ("material", ["granite", "basalt"]), ("wood", woods)))
            .Concat(Product("doorvariants:rounddoorbrick", EntryKind.Block, ("style", ["round2x1brick"]), ("material", ["black", "clinker", "fire", "red"]), ("wood", woods)))
            .Concat(Product("doorvariants:bamboodoor", EntryKind.Block, ("style", ["solid2x1bamboo"]), ("wood", ["bamboo"]), ("cloth", ["blue", "red", "plain"])));
        var r = Resolve(entries);

        var solid = r.GroupOf("doorvariants:cobbledoor-solid3x1cobblestone-basalt-birch");
        Assert.Equal(12, solid.Members.Count);
        Assert.Equal("Solid cobblestone door", TitleText(solid));
        Assert.Equal("doorvariants:cobbledoor-solid3x1cobblestone-granite-oak", r.Entries[solid.Representative].Code);

        var round = r.GroupOf("doorvariants:rounddoor-round2x1cobblestone-granite-oak");
        var gate = r.GroupOf("doorvariants:rounddoor-round2x2cobblestone-granite-oak");
        Assert.NotSame(round, gate);
        Assert.Equal("Round cobblestone gate (2x2)", TitleText(gate));

        // The brick colours are clay colours (no game worldproperties list); they collapse all the same.
        var brick = r.GroupOf("doorvariants:rounddoorbrick-round2x1brick-red-aged");
        Assert.Equal(16, brick.Members.Count);
        Assert.Equal("Round brick door", TitleText(brick));

        Assert.Equal("Linen bamboo door", TitleText(r.GroupOf("doorvariants:bamboodoor-solid2x1bamboo-bamboo-blue")));
        Assert.Equal(0, r.Entries.Count(e => r.IsHidden(r.Index(e.Code))));
    }

    [Fact]
    public void Clutter_attribute_stacks_collapse_per_code()
    {
        CreativeEntry Clutter(string code, string type) =>
            E("game:" + code, EntryKind.Block, [], Stack($"{{type:\"{type}\"}}", ("type", type)));
        var entries = new List<CreativeEntry>
        {
            Clutter("clutter", "book-big-closed"), Clutter("clutter", "bookshelves/large-book-closed"), Clutter("clutter", "pottery/jar1"),
            Clutter("clutter-devastation", "devastation/pipe1"), Clutter("clutter-devastation", "devastation/pipe2"),
            Clutter("clutteredbookshelf", "full1"), Clutter("clutteredbookshelfwithlore", "lore1"),
            E("game:jonas", EntryKind.Block, [("type", "lamp")], Stack("{type:\"lamp1\"}", ("type", "lamp1"))),
            E("game:jonas", EntryKind.Block, [("type", "lamp")], Stack("{type:\"lamp2\"}", ("type", "lamp2"))),
        };
        var r = Resolve(entries);
        var groups = r.Entries.Select((e, i) => r.GroupOf(i)).ToArray();
        Assert.All(groups, g => Assert.True(g >= 0));
        Assert.Equal(["game-clutter", "game-clutter-devastation", "game-clutteredbookshelf", "game-jonas"],
            r.Groups.Select(g => g.Id).ToArray());
        Assert.Equal("Clutter", TitleText(r.Groups[0]));
        Assert.Equal("Cluttered bookshelf", TitleText(r.Groups[2]));
        Assert.Equal("Jonas lamp", TitleText(r.Groups[3]));
    }

    [Fact]
    public void Food_Shelves_and_Purposeful_Storage_need_no_rules()
    {
        // Their nested wood/rock stacks arrive as attr:FSAttributes.wood etc. and collapse by value; each mod's
        // own handbook groupBy (shipped, honoured by default) then joins a type's variants (normal + short).
        string[] woods = ["oak", "birch", "acacia"];
        IEnumerable<CreativeEntry> Typed(string domainBase, string type, string attr, string[] values, string groupBy) =>
            values.Select(v => WithGroupBy(E(domainBase, EntryKind.Block, [("type", type), ("side", "east")],
                Stack($"{{{attr}:\"{v}\"}}", (attr, v))), groupBy));
        var entries = Typed("foodshelves:breadshelf", "normal", "FSAttributes.wood", woods, "breadshelf-*")
            .Concat(Typed("foodshelves:breadshelf", "short", "FSAttributes.wood", woods, "breadshelf-*"))
            .Concat(Typed("foodshelves:jar", "normal", "FSAttributes.wood", woods, "jar-*"))
            .Concat(Typed("purposefulstorage:swordpedestal", "normal", "PSAttributes.wood", woods, "swordpedestal-normal-*"))
            .Concat(Typed("purposefulstorage:swordpedestal", "stone", "PSAttributes.rock", ["granite", "basalt"], "swordpedestal-stone-*"));
        var r = Resolve(entries);
        Assert.DoesNotContain(Shipped.Value.Rules, x => x.Match.Domain is "foodshelves" or "purposefulstorage");
        Assert.Equal([6, 3, 3, 2], r.Groups.Select(g => g.Members.Count).ToArray());
        Assert.Same(r.GroupOf("foodshelves:breadshelf-normal-east"), r.GroupOf("foodshelves:breadshelf-short-east"));
        Assert.Contains("FSAttributes.wood:\"oak\"", r.Entries[r.Groups[0].Representative].Stack!.Key);
        Assert.Contains("PSAttributes.rock:\"granite\"", r.Entries[r.Groups[3].Representative].Stack!.Key);
    }

    static CreativeEntry WithGroupBy(CreativeEntry e, string pattern) =>
        new(e.Code, e.Kind, e.Variant, e.Tabs, e.Stack, shippedGroupBy: [pattern]);

    [Fact]
    public void Cross_type_and_per_type_groups()
    {
        var entries = new List<CreativeEntry>
        {
            E("game:amethyst", EntryKind.Item), E("game:clearquartz", EntryKind.Item), E("game:rosequartz", EntryKind.Item),
            E("game:smokyquartz", EntryKind.Item), E("game:crushed", EntryKind.Item, ("type", "quartz")),
        };
        entries.AddRange(Product("game:slantedroofing", EntryKind.Block, ("material", ["oak", "slate", "redclay"]), ("side", Horizontal), ("cover", ["free"])));
        entries.AddRange(Product("game:slantedroofingtip", EntryKind.Block, ("material", ["oak", "slate", "redclay"]), ("cover", ["free"])));
        entries.AddRange(Product("alchemy:herbrackmold", EntryKind.Block, ("color", ["blue", "fire", "red", "tan"]), ("materialtype", ["raw", "fired"])));
        var r = Resolve(entries);

        var quartz = r.GroupOf("game:amethyst");
        Assert.Equal(4, quartz.Members.Count);
        Assert.Equal("Quartz crystals", TitleText(quartz));
        Assert.Equal(-1, r.GroupOf(r.Index("game:crushed-quartz")));

        var roof = r.GroupOf("game:slantedroofing-slate-north-free");
        Assert.Equal(3, roof.Members.Count);
        Assert.Equal("Slanted roof", TitleText(roof));
        Assert.Equal("game:slantedroofing-oak-north-free", r.Entries[roof.Representative].Code);
        Assert.Equal("Roof tip", TitleText(r.GroupOf("game:slantedroofingtip-slate-free")));

        var mold = r.GroupOf("alchemy:herbrackmold-tan-raw");
        Assert.Equal(8, mold.Members.Count);
        Assert.Equal("Herb rack mold", TitleText(mold));
    }

    [Fact]
    public void Wooden_tankards_group_although_their_wood_values_end_in_a_dot()
    {
        var entries = Product("tankardsandgoblets:tankard-woodtype", EntryKind.Block,
            ("wood", ["acacia.", "oak.", "birch."]), ("binding", ["iron", "copper", "black", "plain"]));
        var r = Resolve(entries);
        var g = r.GroupOf("tankardsandgoblets:tankard-woodtype-acacia.-iron");
        Assert.Equal(12, g.Members.Count);
        Assert.Single(r.Groups);
        Assert.Equal("Wooden tankard", TitleText(g));
    }

    [Fact]
    public void Shutters_hide_side_and_status_but_keep_their_shape()
    {
        var entries = Product("slidingwoodenshutters:1x1woodenshutters", EntryKind.Block,
            ("side", Horizontal), ("status", ["opened", "closed"]), ("state", ["left", "right", "half", "fulltop"]), ("wood", ["oak", "birch", "aged"]));
        var r = Resolve(entries);
        var visible = Enumerable.Range(0, r.Entries.Count).Where(i => !r.IsHidden(i)).Select(i => r.Entries[i].Code).ToList();
        Assert.Equal(12, visible.Count);
        Assert.All(visible, c => Assert.Contains("-north-closed-", c));
        Assert.Equal(4, r.Groups.Count);
        var left = r.GroupOf("slidingwoodenshutters:1x1woodenshutters-north-closed-left-birch");
        Assert.Equal(3, left.Members.Count);
        Assert.Equal("slidingwoodenshutters:1x1woodenshutters-north-closed-left-oak", r.Entries[left.Representative].Code);
    }

    [Fact]
    public void Shingles_keep_raw_and_fired_apart()
    {
        var entries = Product("game:shingle", EntryKind.Item, ("state", ["raw", "burned"]), ("type", ["black", "blue", "fire", "red"]));
        var r = Resolve(entries);
        Assert.Equal("Raw clay shingle", TitleText(r.GroupOf("game:shingle-raw-fire")));
        Assert.Equal("Fired ceramic shingle", TitleText(r.GroupOf("game:shingle-burned-fire")));
        Assert.Equal(2, r.Groups.Count);
    }

    [Fact]
    public void Seed_amulets_group_and_the_other_amulets_stay_single()
    {
        CreativeEntry Neck(string value) => E("game:clothes", EntryKind.Item, ("category", "neck"), ("neck", value));
        CreativeEntry Nadiya(string value) => E("game:clothes", EntryKind.Item, ("type", "nadiya"), ("category", "neck"), ("neck", value));
        var r = Resolve([Neck("acorn-amulet"), Neck("jade-amulet"), Neck("walnut-amulet"), Neck("larch-seed-amulet"),
            Neck("bronzeamulet"), Nadiya("birch-amulet"), Nadiya("feather-amulet")]);
        var seed = r.GroupOf("game:clothes-neck-acorn-amulet");
        Assert.Equal("Seed and root amulet", TitleText(seed));
        Assert.Equal(["game:clothes-neck-acorn-amulet", "game:clothes-neck-walnut-amulet", "game:clothes-neck-larch-seed-amulet", "game:clothes-nadiya-neck-birch-amulet"],
            r.Codes(seed.Members));
        Assert.Equal(-1, r.GroupOf(r.Index("game:clothes-neck-jade-amulet")));
        Assert.Equal(-1, r.GroupOf(r.Index("game:clothes-nadiya-neck-feather-amulet")));
        Assert.Equal(-1, r.GroupOf(r.Index("game:clothes-neck-bronzeamulet")));
    }

    [Fact]
    public void ExpandedFoods_types_are_one_tile_each()
    {
        var entries = Product("expandedfoods:breadedvegetable", EntryKind.Item, ("type", ["spelt", "rye"]), ("veggie", ["carrot", "onion"]), ("state", ["raw", "partbaked", "cooked"]))
            .Concat(Product("expandedfoods:pasta", EntryKind.Item, ("type", ["spelt", "rye", "rice"])).Select(e => WithGroupBy(e, "pasta-*")));
        var r = Resolve(entries);
        var fried = r.GroupOf("expandedfoods:breadedvegetable-rye-onion-raw");
        Assert.Equal(12, fried.Members.Count);
        Assert.Equal("Fried vegetables", TitleText(fried));
        Assert.Equal("cooked", r.Entries[fried.Representative].Code.Split('-')[^1]);
        // No rule: EF's own groupBy ("pasta-*") makes the generic "type" one tile.
        var pasta = r.GroupOf("expandedfoods:pasta-rice");
        Assert.Equal(-1, pasta.RuleIndex);
        Assert.Equal(3, pasta.Members.Count);
    }
}
