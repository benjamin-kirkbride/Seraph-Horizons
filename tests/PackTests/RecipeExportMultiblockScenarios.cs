using Atlas.XUnit;
using Newtonsoft.Json.Linq;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The export's multiblocks (MultiblockSection): the structures players build block by block,
/// from the layouts in smex's blastfurnace/door.json and smokestack/intake.json, ppex's
/// boiler/cornish.json and the game's clay/door-kiln.json, counted by hand and written down here.
/// </summary>
public partial class RecipeExportScenarios
{
    private JObject Multiblock(string id) =>
        Doc["multiblocks"]!["structures"]!.Cast<JObject>().SingleOrDefault(s => (string)s["id"]! == id)
        ?? throw new Xunit.Sdk.XunitException($"no multiblock {id}");

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Hand_built_multiblocks_are_exported_with_their_layouts()
    {
        var ids = Doc["multiblocks"]!["structures"]!.Select(s => (string)s["id"]!).ToList();
        foreach (var id in new[] { "smex:blastfurnacedoor", "smex:smokestack", "smex:cowperstove", "smex:convertercontrol",
                     "ppex:boilercornish", "ppex:boilerlancashire", "game:doorkiln", "game:stonecoffinsection" })
            Assert.Contains(id, ids);

        // door.json: 147 offsets, the door itself (number 3) at the origin.
        var furnace = Multiblock("smex:blastfurnacedoor");
        Assert.Equal("smex", (string)furnace["mod"]!);
        var cells = (JArray)furnace["sizes"]![0]!["cells"]!;
        Assert.Equal(147, cells.Count);
        var parts = (JArray)furnace["parts"]!;
        var origin = cells.Single(c => (int)c[0]! == 0 && (int)c[1]! == 0 && (int)c[2]! == 0);
        Assert.Equal("smex:blastfurnacedoor*", (string)parts[(int)origin[3]!]!["pattern"]!);
        // Number 11, game:air, only air; number 10, air or a coal pile, drawn as a coal pile.
        var air = parts.Cast<JObject>().Single(p => (string)p["pattern"]! == "game:air");
        Assert.True((bool)air["air"]!);
        Assert.Null(air["block"]);
        var charge = parts.Cast<JObject>().Single(p => (string)p["pattern"]! == "game:@(air|coalpile)");
        Assert.True((bool)charge["air"]!);
        Assert.Equal("game:coalpile", (string)charge["block"]!);
        var bricks = parts.Cast<JObject>().Single(p => (string)p["pattern"]! == "game:refractorybricks-good-tier*");
        Assert.Equal("game:refractorybricks-good-tier1", (string)bricks["block"]!);
        Assert.Equal(new[] { "game:refractorybricks-good-tier1", "game:refractorybricks-good-tier2", "game:refractorybricks-good-tier3" },
            bricks["accepts"]!.Select(c => (string)c!).Order());

        Assert.Equal(72, Multiblock("smex:smokestack")["sizes"]![0]!["cells"]!.Count());
        Assert.Equal(64, Multiblock("ppex:boilercornish")["sizes"]![0]!["cells"]!.Count());
        // The kiln door's variants (tier1..3, fire) carry one layout.
        Assert.Equal(4, Multiblock("game:doorkiln")["codes"]!.Count());
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Multiblock_blocks_are_drawn_with_their_shapes()
    {
        var shapes = (JObject)Doc["multiblocks"]!["shapes"]!;
        // Every block a part is drawn with has a shape entry.
        foreach (var s in Doc["multiblocks"]!["structures"]!)
            foreach (var p in s["parts"]!)
                if (p["block"] is { } b) Assert.True(shapes.ContainsKey((string)b!), $"{s["id"]}: no shape for {b}");
        // Refractory bricks are a cube; the door's upper half draws nothing (the door's shape is
        // two blocks tall); the hopper is a JSON shape with elements.
        Assert.Equal("cube", (string)shapes["game:refractorybricks-good-tier1"]!["draw"]!);
        Assert.Equal("none", (string)shapes["game:multiblock-monolithic-0-p1-0"]!["draw"]!);
        var hopper = shapes["smex:hopperbell"]!;
        Assert.Equal("shape", (string)hopper["draw"]!);
        Assert.NotEmpty(hopper["shapes"]![0]!["elements"]!);
    }
}
