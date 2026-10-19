using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.Tests.Machines;

/// <summary>The ore machines' outlets (<see cref="Outlet"/>): a product waits only for a chute or container
/// attached there; with nothing attached tailings go on the heap and the rest drops in front of the face.
/// And every ore machine's built name (<c>block-{machine}</c>, what its block info's title shows) is in
/// en.json, with every block type's name.</summary>
public class OutletTests
{
    [Theory]
    [InlineData(true, false, OutletFate.Held)]
    [InlineData(true, true, OutletFate.Held)]
    [InlineData(false, true, OutletFate.Discarded)]
    [InlineData(false, false, OutletFate.Dropped)]
    public void A_product_waits_only_for_what_is_attached(bool attached, bool tailings, OutletFate fate) =>
        Assert.Equal(fate, Outlet.Fate(attached, tailings));

    [Fact]
    public void A_dropped_product_appears_just_beyond_the_face()
    {
        // north (-z): the middle of the cell in front, low in it
        Assert.Equal((0.5, 0.25, -0.5), Outlet.DropOffset(0, 0, -1));
        Assert.Equal((1.5, 0.25, 0.5), Outlet.DropOffset(1, 0, 0));
        // down: the middle of the cell below; up: just over the top face
        Assert.Equal((0.5, -0.5, 0.5), Outlet.DropOffset(0, -1, 0));
        Assert.Equal((0.5, 1.1, 0.5), Outlet.DropOffset(0, 1, 0));
    }

    private static string ModDir([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));

    private static readonly Regex Code = new(@"^\s*""?code""?\s*:\s*""([^""]+)""", RegexOptions.Multiline);
    private static readonly Regex Class = new(@"^\s*""?class""?\s*:\s*""([^""]+)""", RegexOptions.Multiline);
    private static readonly Regex Machine = new(@"""?machine""?\s*:\s*""([^""]+)""");

    // Machines/Game/MachineBlocks.cs, BlockMachine.ClassName (the test project does not compile the game half)
    private const string BlockMachineClass = "seraphhorizons.Machine";

    [Fact]
    public void Every_block_type_and_built_machine_has_an_english_name()
    {
        string assets = Path.Combine(ModDir(), "assets", "seraphhorizons");
        var lang = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(assets, "lang", "en.json")))!;
        var missing = new List<string>();
        int machines = 0;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(assets, "blocktypes"), "*.json", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            if (Code.Match(text) is not { Success: true } code)
                continue;
            string c = code.Groups[1].Value;
            if (!lang.Keys.Any(k => k == $"block-{c}" || k.StartsWith($"block-{c}-", StringComparison.Ordinal)))
                missing.Add($"block-{c}* ({Path.GetFileName(file)})");
            // BlockMachine.GetPlacedBlockName: a built machine is named block-{machine}
            if (Class.Match(text) is { Success: true } cls && cls.Groups[1].Value == BlockMachineClass)
            {
                machines++;
                string machine = Machine.Match(text) is { Success: true } m ? m.Groups[1].Value : c.Split('-')[0];
                if (!lang.ContainsKey($"block-{machine}"))
                    missing.Add($"block-{machine} (built {machine})");
            }
        }
        Assert.True(machines >= 10, $"found only {machines} machine block types");
        Assert.True(missing.Count == 0, "en.json has no " + string.Join(", ", missing));
    }
}
