using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Pipes.Core;

namespace SeraphHorizons.Tests.Pipes;

/// <summary>The pipe chain's own pieces (<c>UnifiedPipes</c>): the angle (item and shape; the press
/// brake is its only maker, so it has no recipe), the pipe section (item, shape), every pipe shape from pipe sections and a joint, and the
/// chain's figures.</summary>
public class PipeSectionsTests
{
    private static readonly JsonLoadSettings Lenient = new() { CommentHandling = CommentHandling.Ignore };

    private static JToken Json(string file) => JToken.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, file)), Lenient);

    /// <summary>Every voxel a shape's elements fill (whole voxels; elements must not overlap).</summary>
    private static HashSet<(int, int, int)> Voxels(JObject shape)
    {
        var voxels = new HashSet<(int, int, int)>();
        foreach (var e in shape["elements"]!)
        {
            var from = e["from"]!.Select(v => (double)v!).ToArray();
            var to = e["to"]!.Select(v => (double)v!).ToArray();
            Assert.All(from.Concat(to), v => Assert.Equal(Math.Floor(v), v));
            for (int x = (int)from[0]; x < to[0]; x++)
            for (int y = (int)from[1]; y < to[1]; y++)
            for (int z = (int)from[2]; z < to[2]; z++)
                Assert.True(voxels.Add((x, y, z)), $"{e["name"]} overlaps at {x},{y},{z}");
        }
        return voxels;
    }

    [Fact]
    public void The_angle_is_a_half_plate_folded_into_a_4_by_4_L()
    {
        var item = (JObject)Json("angle-itemtype.json");
        Assert.Equal("angle", (string?)item["code"]);
        Assert.Equal(PipeSections.AngleMetals, item["variantgroups"]![0]!["states"]!.Select(s => (string)s!));
        Assert.Equal("metal", (string?)item["variantgroups"]![0]!["code"]);
        Assert.Equal("game:block/metal/sheet/{metal}1", (string?)item["textures"]!["metaltex"]!["base"]);
        Assert.Equal("seraphhorizons:angle-handbook-text", (string?)item["attributes"]!["handbook"]!["extraSections"]![0]!["text"]);

        // the press brake contract's L: the 8 x 4 half plate folded across its middle, two legs 4 across
        // the outside, 1 thick, 4 long, sharing a corner, centred in the block
        var voxels = Voxels((JObject)Json("angle-shape.json"));
        Assert.Equal(4 * 4 + 4 * 3, voxels.Count);
        Assert.Equal((6, 9), (voxels.Min(v => v.Item1), voxels.Max(v => v.Item1)));
        Assert.Equal((6, 9), (voxels.Min(v => v.Item2), voxels.Max(v => v.Item2)));
        Assert.Equal((6, 9), (voxels.Min(v => v.Item3), voxels.Max(v => v.Item3)));
        Assert.All(voxels, v => Assert.True(v.Item2 == 6 || v.Item3 == 6, $"{v} is off the L"));
        // two of them are much less than the chute section's 8 x 8 x 8 box they are soldered into: that
        // is the grid recipe's accepted abstraction (README "Unified pipes")
        Assert.True(2 * voxels.Count < 8 * 8 * 8 - 6 * 6 * 8);
        // a plate (two ingots) cuts into two half plates, each folded into one angle: an angle an ingot
        Assert.Equal(1, PipeSections.AnglesPerIngot);
    }

    [Fact]
    public void The_pipe_section_is_a_pipe_sized_tube_half_a_block_long()
    {
        var item = (JObject)Json("pipesection-itemtype.json");
        Assert.Equal("pipesection", (string?)item["code"]);
        Assert.Equal("metal", (string?)item["variantgroups"]![0]!["code"]);
        Assert.Equal(PipeSections.Metals, item["variantgroups"]![0]!["states"]!.Select(s => (string)s!));
        Assert.Equal(PipeRules.PipeMaterials.Order(), PipeSections.Metals.Order());
        // as ppex's pipes are textured (patches/unifiedpipes-ppex.json: sheet-plain/<metal>4)
        Assert.Equal("game:block/metal/sheet-plain/{metal}4", (string?)item["textures"]!["metal"]!["base"]);
        Assert.Equal("seraphhorizons:pipesection-handbook-text", (string?)item["attributes"]!["handbook"]!["extraSections"]![0]!["text"]);

        // the draw bench's drawn piece (its contract, pass 3): 6 across (ppex's pipe is 6/16 wide),
        // walls 0.3 round a 5.4 x 5.4 bore, 8 long, centred; four walls that do not overlap
        var boxes = ((JObject)Json("pipesection-shape.json"))["elements"]!
            .Select(e => (From: e["from"]!.Select(v => (double)v!).ToArray(), To: e["to"]!.Select(v => (double)v!).ToArray())).ToList();
        Assert.Equal(4, boxes.Count);
        for (int axis = 0; axis < 3; axis++)
        {
            Assert.Equal(axis == 2 ? 4 : 5, boxes.Min(b => b.From[axis]), 6);
            Assert.Equal(axis == 2 ? 12 : 11, boxes.Max(b => b.To[axis]), 6);
        }
        Assert.All(boxes, b => Assert.Equal(0.3, Enumerable.Range(0, 2).Min(i => b.To[i] - b.From[i]), 6));
        Assert.Equal(8 * (6 * 6 - 5.4 * 5.4), boxes.Sum(b => Enumerable.Range(0, 3).Aggregate(1.0, (v, i) => v * (b.To[i] - b.From[i]))), 6);
        Assert.Equal(PipeSections.PipeSection("iron"), CastPipeMold.Section("iron"));
        Assert.Equal("seraphhorizons:pipesection-copper", PipeSections.PipeSection("copper"));
        Assert.Equal("seraphhorizons:angle-lead", PipeSections.Angle("lead"));
    }

    private static readonly Dictionary<string, string[]> ChutePatterns = new()
    {
        // the game's chute patterns (recipes/grid/chute.json), the cells a section fills; a straight pipe is one section
        ["straight-ns"] = ["I"],
        ["bend-nw"] = ["I_", "_I"],
        ["tjunction-uns"] = ["_I_", "I_I"],
        ["xjunction-nswe"] = ["_I_", "I_I", "_I_"],
    };

    [Fact]
    public void Every_pipe_shape_is_made_from_pipe_sections_and_a_joint_for_every_metal()
    {
        var recipes = (JArray)Json("unifiedpipes-grid.json");
        foreach (var (shape, sections, pipes) in PipeSections.PipeShapes)
        {
            var made = recipes.Where(r => (string?)r["output"]!["code"] == $"ppex:pipe-{shape}-{{metal}}").ToList();
            Assert.Equal(2, made.Count);
            foreach (var (metals, joint) in new[] { (PipeSections.SolderedMetals, "game:solderbar-*"), (PipeSections.NailedMetals, "game:metalnailsandstrips-*") })
            {
                var r = Assert.Single(made, r => r["ingredients"]!["P"]!["allowedVariants"]!.Select(s => (string)s!).SequenceEqual(metals));
                Assert.Equal("seraphhorizons:pipesection-*", (string?)r["ingredients"]!["P"]!["code"]);
                Assert.Equal("metal", (string?)r["ingredients"]!["P"]!["name"]);
                Assert.Equal(1, (int)r["ingredients"]!["P"]!["quantity"]!);
                Assert.Equal(pipes, (int)r["output"]!["quantity"]!);
                Assert.Equal(1, pipes);
                var pattern = ((string)r["ingredientPattern"]!).Split(',');
                Assert.Equal(sections, pattern.Sum(row => row.Count(c => c == 'P')));
                // the sections lie in the game's chute pattern (its other cells hold the joint and the tool)
                Assert.Equal(ChuteSectionsTests.Trim(ChutePatterns[shape].Select(row => row.Replace('I', 'P'))), ChuteSectionsTests.Trim(pattern));
                Assert.Equal(pattern.Length, (int)r["height"]!);
                Assert.All(pattern, row => Assert.Equal((int)r["width"]!, row.Length));
                var ingredients = (JObject)r["ingredients"]!;
                var jointKey = Assert.Single(ingredients.Properties(), p => (string?)p.Value["code"] == joint).Name;
                var tools = ingredients.Properties().Where(p => (bool?)p.Value["isTool"] == true).Select(p => p.Value).ToList();
                var tool = Assert.Single(tools);
                if (joint == "game:solderbar-*")
                {
                    Assert.Equal(sections, (int)ingredients[jointKey]!["quantity"]!); // a bar per section, as a chute
                    Assert.Equal(PipeSections.SolderBars(shape), sections);
                    Assert.Equal(["tin", "silver"], ingredients[jointKey]!["allowedVariants"]!.Select(s => (string)s!));
                    Assert.Equal("game:solderingiron", (string?)tool["code"]);
                    Assert.Equal(2, (int)tool["toolDurabilityCost"]!);
                }
                else
                {
                    Assert.Equal("game:hammer-*", (string?)tool["code"]);
                    // nails and strips of the sections' own metal
                    Assert.Equal("metal", (string?)ingredients[jointKey]!["name"]);
                    Assert.Equal(metals, ingredients[jointKey]!["allowedVariants"]!.Select(s => (string)s!));
                    Assert.Equal(PipeSections.NailsAndStrips, (int)ingredients[jointKey]!["quantity"]!);
                }
                Assert.Equal(1, pattern.Sum(row => row.Count(c => c.ToString() == jointKey)));
                Assert.Equal(ingredients.Count, pattern.SelectMany(row => row).Where(c => c != '_').Distinct().Count());
            }
        }
        // no pipe from chute sections or from pipe; the valves stay
        Assert.DoesNotContain(recipes, r => r["ingredients"]!.Children<JProperty>().Any(p => ((string?)p.Value["code"])?.StartsWith("game:chutesection") == true));
        Assert.DoesNotContain(recipes, r => r["ingredients"]!.Children<JProperty>().Any(p => (string?)p.Value["code"] == "ppex:pipe-straight-ns-*")
                                            && !((string)r["output"]!["code"]!).Contains("valve"));
        Assert.Equal(2, recipes.Count(r => ((string)r["output"]!["code"]!).Contains("valve")));
        Assert.Equal(PipeSections.PipeShapes.Length * 2 + 2, recipes.Count);
    }

    [Fact]
    public void Each_route_gives_its_figure_per_ingot()
    {
        Assert.Equal(0.5, PipeSections.HollowsPerIngot);
        Assert.Equal(1.0, PipeSections.MandrelPipesPerIngot);
        Assert.Equal(2.0, PipeSections.DrawnPipesPerIngot);
        Assert.Equal(2.0, CastPipeMold.PipesPerIngot());
        Assert.Equal(1, PipeSections.SolderBars("straight-ns"));
        Assert.Equal(2, PipeSections.SolderBars("bend-nw"));
        Assert.Equal(3, PipeSections.SolderBars("tjunction-uns"));
        Assert.Equal(4, PipeSections.SolderBars("xjunction-nswe"));
        Assert.Equal("ppex:pipe-bend-nw-steel", PipeSections.Pipe("bend-nw", "steel"));
    }
}
