using System.Text.Json;
using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Mod.Tests;

public class PileTextTests
{
    // The shipped English entries, so the lines read as the game shows them.
    private static readonly Dictionary<string, string> En = Load();

    private static Dictionary<string, string> Load()
    {
        string dir = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(dir, "SeraphHorizons.csproj")))
            dir = Path.GetDirectoryName(dir) ?? throw new InvalidOperationException("no mod folder above the tests");
        var path = Path.Combine(dir, "assets", "seraphhorizons", "lang", "en.json");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
    }

    private static string Lang(string key, object[] args) =>
        string.Format(En[key.Replace("seraphhorizons:", "")], args);

    [Fact]
    public void A_firewood_pile()
    {
        var line = PileText.Line(new PileText.Facts(PileText.Kind.Pile, 2048, 2, 32, false, true), Lang);
        Assert.Equal("Pileable: up to 2,048 per block, 2 per click, 32 per Ctrl click. A full pile can be built on, more piles of it "
                     + "included, so it grows past one block. Put it down with Shift + right click.", line);
    }

    [Fact]
    public void A_pile_that_needs_ctrl_and_stays_one_block_high()
    {
        var line = PileText.Line(new PileText.Facts(PileText.Kind.Pile, 4096, 2, 64, true, false), Lang);
        Assert.Equal("Pileable: up to 4,096 per block, 2 per click, 64 per Ctrl click. A pile is one block high at most. "
                     + "Put it down with Ctrl + Shift + right click.", line);
    }

    [Fact]
    public void A_coal_pile_moves_the_same_either_way_and_a_limit_is_said()
    {
        Assert.Equal("Pileable: up to 16 per block, 2 per click. A full pile can be built on, more piles of it included, so it grows "
                     + "past one block. Put it down with Shift + right click.",
            PileText.Line(new PileText.Facts(PileText.Kind.Pile, 16, 2, 2, false, true), Lang));
        Assert.Contains("up to 3 piles high", PileText.Line(new PileText.Facts(PileText.Kind.Pile, 64, 1, 4, false, true, 3), Lang));
    }

    [Fact]
    public void Placed_and_loose_items_are_not_pileable()
    {
        Assert.Equal("Not pileable: it is set down on the ground, up to 4 per block.",
            PileText.Line(new PileText.Facts(PileText.Kind.Placed, 4, 0, 0, false, false), Lang));
        Assert.Equal("Not pileable: it lies in a loose heap of up to 12 per block, one block high.",
            PileText.Line(new PileText.Facts(PileText.Kind.Loose, 12, 0, 0, false, false), Lang));
    }
}
