using Newtonsoft.Json;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using SeraphHorizons.Mod.DrawBench.Core;

namespace SeraphHorizons.Tests;

/// <summary>
/// ModConfig/seraphhorizons.json is read with <see cref="ModConfigJson"/> and written back with the
/// game's StoreModConfig (<c>JsonConvert.SerializeObject(config, Formatting.Indented)</c>) on every
/// start. The game's LoadModConfig appended the file's lists to the defaults, so each start added a
/// copy (a tester's ChimneyShaftBlocks held 120 entries, 4 distinct).
/// </summary>
public class ModConfigJsonTests
{
    /// <summary>The shape of SeraphHorizonsConfig (game code, not compiled here): settings objects
    /// in properties initialised with <c>new()</c>.</summary>
    private sealed class Root
    {
        public CrucibleFurnaceConfig CrucibleFurnaceSettings { get; set; } = new();
        public DrawBenchConfig DrawBenchSettings { get; set; } = new();
        public string[] Entities { get; set; } = ["cartwrightscaravan:*"];
        public bool Switch { get; set; } = true;
    }

    private static string Store(object config) => JsonConvert.SerializeObject(config, Formatting.Indented);

    private static Root Start(string? file)
    {
        var config = file == null ? new Root() : ModConfigJson.Deserialize<Root>(file)!;
        config.CrucibleFurnaceSettings.RemoveDuplicateCodes();
        return config;
    }

    [Fact]
    public void Round_trips_keep_every_list_its_length()
    {
        var defaults = new CrucibleFurnaceConfig();
        string? file = null;
        for (var start = 0; start < 5; start++)
            file = Store(Start(file));
        var c = ModConfigJson.Deserialize<Root>(file!)!.CrucibleFurnaceSettings;
        Assert.Equal(defaults.ChimneyShaftBlocks, c.ChimneyShaftBlocks);
        Assert.Equal(defaults.ChimneyTopBlocks, c.ChimneyTopBlocks);
        Assert.Equal(defaults.LargeCastingMetals, c.LargeCastingMetals);
        Assert.Equal(defaults.LargeCastingMolds, c.LargeCastingMolds);
        Assert.Equal(Store(new Root()), file);
    }

    [Fact]
    public void The_games_plain_deserialize_appends_which_is_why_this_exists()
    {
        var file = Store(new Root());
        var plain = JsonConvert.DeserializeObject<Root>(file)!;
        Assert.Equal(2 * new CrucibleFurnaceConfig().ChimneyShaftBlocks.Count, plain.CrucibleFurnaceSettings.ChimneyShaftBlocks.Count);
    }

    [Fact]
    public void A_files_list_replaces_the_default_even_when_shorter_or_empty()
    {
        var c = ModConfigJson.Deserialize<Root>(
            """{ "CrucibleFurnaceSettings": { "ChimneyTopBlocks": ["mymod:chimney"], "LargeCastingMetals": [] } }""")!
            .CrucibleFurnaceSettings;
        Assert.Equal(["mymod:chimney"], c.ChimneyTopBlocks);
        Assert.Empty(c.LargeCastingMetals);
        // A setting the file lacks keeps its default.
        Assert.Equal(new CrucibleFurnaceConfig().ChimneyShaftBlocks, c.ChimneyShaftBlocks);
    }

    [Fact]
    public void Arrays_are_replaced()
    {
        var root = ModConfigJson.Deserialize<Root>("""{ "Entities": ["a:*", "b:*"] }""")!;
        Assert.Equal(["a:*", "b:*"], root.Entities);
    }

    [Fact]
    public void Dictionaries_still_merge_so_a_new_default_key_reaches_an_old_file()
    {
        // A file written before the steel die existed: it gets the steel die, its iron entry wins.
        var bench = ModConfigJson.Deserialize<Root>("""{ "DrawBenchSettings": { "DieMetals": { "iron": ["copper"] } } }""")!
            .DrawBenchSettings;
        Assert.Equal(["copper"], bench.DieMetals["iron"]);
        Assert.Equal(DrawBenchConfig.DefaultDieMetals()["steel"], bench.DieMetals["steel"]);
        Assert.Equal(2, bench.DieMetals.Count);
        // And round trips do not grow the arrays inside it.
        var file = Store(new Root());
        for (var start = 0; start < 3; start++)
            file = Store(Start(file));
        Assert.Equal(Store(new Root()), file);
    }

    [Fact]
    public void A_bloated_file_collapses_to_its_distinct_entries_in_order()
    {
        var defaults = new CrucibleFurnaceConfig();
        var bloated = new Root();
        var shaft = bloated.CrucibleFurnaceSettings.ChimneyShaftBlocks;
        bloated.CrucibleFurnaceSettings.ChimneyShaftBlocks = [.. Enumerable.Repeat(shaft, 30).SelectMany(x => x)];
        bloated.CrucibleFurnaceSettings.LargeCastingMolds = [.. Enumerable.Repeat(defaults.LargeCastingMolds, 54).SelectMany(x => x)];
        var file = Store(bloated);

        var config = ModConfigJson.Deserialize<Root>(file)!.CrucibleFurnaceSettings;
        Assert.Equal(120, config.ChimneyShaftBlocks.Count);
        var fixes = config.RemoveDuplicateCodes();
        Assert.Equal(2, fixes.Count);
        Assert.Contains(fixes, f => f.StartsWith("ChimneyShaftBlocks had 116 repeated", StringComparison.Ordinal));
        Assert.Equal(defaults.ChimneyShaftBlocks, config.ChimneyShaftBlocks);
        Assert.Equal(defaults.LargeCastingMolds, config.LargeCastingMolds);
        Assert.Empty(config.RemoveDuplicateCodes());
    }

    [Fact]
    public void RemoveDuplicates_keeps_the_first_of_each_and_is_case_sensitive()
    {
        List<string> list = ["b", "a", "b", "A", "a"];
        Assert.Equal(2, ModConfigJson.RemoveDuplicates(list));
        Assert.Equal(["b", "a", "A"], list);
        Assert.Equal(0, ModConfigJson.RemoveDuplicates(null));
    }

    [Fact]
    public void A_missing_file_loads_as_null_like_the_games()
    {
        Assert.Null(ModConfigJson.Load<Root>(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")));
    }
}
