using Atlas.XUnit;
using Newtonsoft.Json;
using SeraphHorizons.Mod.Trading.Values;
using SeraphHorizons.Mod.Trading.Values.Core;
using Vintagestory.API.Common;

namespace SeraphHorizons.PackTests;

/// <summary>
/// Item base values (#449): the server loads the shipped table (mods-src/seraphhorizons
/// Trading/Values), the table matches the pack, and <c>/sh trade value</c> answers. Also writes the
/// pack's recipe export to <c>$ITEM_VALUES_EXPORT</c> when that is set, the input of
/// <c>tools/item-values</c> (a full export takes a minute or two, so only on request).
/// </summary>
public partial class TradingScenarios
{
    private async Task<string> Run(string command)
    {
        var result = await World.ExecuteCommand(command);
        output.WriteLine($"{command}: {(result.Ok ? "ok" : "FAILED")}: {result.Message}");
        Assert.True(result.Ok, $"{command}: {result.Message}");
        return result.Message ?? "";
    }

    [AtlasScenario]
    public void Table_is_loaded_on_the_server()
    {
        var values = ItemValuesSystem.For(World.Api);
        Assert.True(values.Count > 10_000, $"only {values.Count} values loaded");
        Assert.Equal(1.0, values.ValueOf("game:gear-rusty"));
        Assert.InRange(values.ValueOf("game:ingot-copper"), 1, 4);
        Assert.False(values.IsWorthless("game:ingot-copper"));
    }

    // Liquids are priced per litre in the table (its perLitre) and per item to trading.
    [AtlasScenario]
    public void Liquids_are_priced_per_litre()
    {
        var values = ItemValuesSystem.For(World.Api);
        const string cider = "game:ciderportion-apple";
        var l = values.Lookup(cider);
        Assert.Equal(ValueSource.Direct, l.Source);
        Assert.Equal(100, values.PerLitre(cider));
        Assert.True(l.Display > 0, $"{cider} has no value");
        Assert.Equal(l.Display / 100, values.ValueOf(cider), 9);
        Assert.Contains("gears per litre", ItemValuesSystem.Describe(l));
        // The handbook line, through the lang key (a stand-in for Lang.Get, which is the client's).
        Assert.Equal($"{ValueHandbook.Format(l.Display)} per litre",
            ValueHandbook.Text(l, (key, args) => key == "seraphhorizons:itemvalues-handbook-perlitre" ? string.Format("{0} per litre", args) : key));
        var copper = values.Lookup("game:ingot-copper");
        Assert.Equal(ValueHandbook.Format(copper.Value), ValueHandbook.Text(copper, (key, _) => key));
        Assert.Null(values.PerLitre("game:ingot-copper"));
    }

    // A table built from an older export lists codes the pack no longer registers. A few stale
    // codes are harmless (they fall back to nothing); many mean the table needs rebuilding.
    [AtlasScenario]
    public void Table_codes_are_registered_in_the_pack()
    {
        var world = World.Api.World;
        var values = ItemValuesSystem.For(World.Api);
        var stale = values.Codes
            .Where(c => world.GetItem(new AssetLocation(c)) == null && world.GetBlock(new AssetLocation(c)) == null)
            .ToList();
        output.WriteLine($"{stale.Count} of {values.Count} codes not registered: {string.Join(", ", stale.Take(30))}");
        Assert.True(stale.Count <= values.Count / 50, $"{stale.Count} table codes are not registered; rebuild with tools/item-values");
    }

    [AtlasScenario]
    public async Task Value_command_reports_value_and_source()
    {
        Assert.Contains("(direct)", await Run("/sh trade value game:ingot-copper"));
        Assert.Contains("family fallback", await Run("/sh trade value game:plank-notawood"));
        Assert.Contains("no value", await Run("/sh trade value game:notanitem"));
    }

    // The handbook's value line (client side, ValueHandbook): the game method it follows is there,
    // and the server has told clients which switches are off (none, in this default-config world).
    [AtlasScenario]
    public void Handbook_line_has_its_hook_and_the_server_publishes_the_off_switches()
    {
        Assert.NotNull(ValueHandbook.Target());
        Assert.Equal("", World.Api.World.Config.GetString(ValueHandbook.OffKey, "missing"));
        Assert.Equal("1", ValueHandbook.Format(1.0));
        Assert.Equal("0.125", ValueHandbook.Format(0.125));
    }

    [AtlasScenario(TimeoutMs = 900_000)]
    public void Export_is_written_when_asked()
    {
        var path = Environment.GetEnvironmentVariable("ITEM_VALUES_EXPORT");
        if (string.IsNullOrEmpty(path)) return;
        var doc = ExportUnderTest.Get(World.Api);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, doc.ToString(Formatting.None));
        output.WriteLine($"export written to {path}");
    }
}
