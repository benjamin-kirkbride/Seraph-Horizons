using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Pipes.Core;

namespace SeraphHorizons.Tests.Pipes;

/// <summary>The chute section as the pipe section (<c>UnifiedPipes</c>): the guard on the game's
/// chute section and chute recipes, held to 1.22.7's files (trimmed fixtures) and to the shipped
/// patch, and the shipped recipes held to the ladder: the forged lead section, the closing recipe,
/// and every pipe shape from sections and a joint.</summary>
public class ChuteSectionsTests
{
    private static readonly JsonLoadSettings Lenient = new() { CommentHandling = CommentHandling.Ignore };

    private static string Text(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, file));

    private static JToken Json(string file) => JToken.Parse(Text(file), Lenient);

    private static string Item => Text("game-chutesection.json");
    private static string Recipes => Text("game-chute-recipes.json");

    private static string EditItem(Action<JObject> edit)
    {
        var root = JObject.Parse(Item, Lenient);
        edit(root);
        return root.ToString();
    }

    private static string EditRecipes(Action<JArray> edit)
    {
        var root = JArray.Parse(Recipes, Lenient);
        edit(root);
        return root.ToString();
    }

    [Fact]
    public void The_games_files_are_as_the_patch_expects()
    {
        Assert.Null(ChuteSections.CheckItem(Item));
        Assert.Null(ChuteSections.CheckRecipes(Recipes));
    }

    [Fact]
    public void A_changed_item_is_refused()
    {
        Assert.Contains("variants", ChuteSections.CheckItem(EditItem(r => ((JArray)r["variantgroups"]![0]!["states"]!).Add("lead"))));
        Assert.Contains("variants", ChuteSections.CheckItem(EditItem(r => r["variantgroups"]![0]!["code"] = "metal")));
        Assert.Contains("texture", ChuteSections.CheckItem(EditItem(r => r["textures"]!["metaltex"]!["base"] = "block/metal/sheet/copper1")));
        Assert.Contains("texture", ChuteSections.CheckItem(EditItem(r => r["texturesByType"] = new JObject())));
        Assert.Contains("creative", ChuteSections.CheckItem(EditItem(r => r.Remove("creativeinventory"))));
        Assert.Contains("attributes", ChuteSections.CheckItem(EditItem(r => r["attributes"]!["handbook"] = new JObject())));
        Assert.Contains("code", ChuteSections.CheckItem(EditItem(r => r["code"] = "chute")));
        Assert.Contains("does not parse", ChuteSections.CheckItem("{ code: "));
    }

    [Fact]
    public void Moved_or_changed_recipes_are_refused()
    {
        // a recipe inserted before the plate recipe moves it
        Assert.Contains("recipe 5", ChuteSections.CheckRecipes(EditRecipes(r => r.Insert(0, r[1]!.DeepClone()))));
        Assert.Contains("recipe 5", ChuteSections.CheckRecipes(EditRecipes(r => r[5]!["ingredients"]!["P"]!["code"] = "metalplate-tinbronze")));
        Assert.Contains("recipe 5", ChuteSections.CheckRecipes(EditRecipes(r => r[5]!["output"]!["quantity"] = 3)));
        // a second recipe making sections would survive the patch
        Assert.Contains("recipe 6", ChuteSections.CheckRecipes(EditRecipes(r => r.Add(r[5]!.DeepClone()))));
        Assert.Contains("fewer", ChuteSections.CheckRecipes("[]"));
        // a chute recipe that already takes something besides sections, or moved
        Assert.Contains("recipe 1", ChuteSections.CheckRecipes(EditRecipes(r => r[1]!["ingredients"]!["S"] = new JObject { ["code"] = "solderbar-*" })));
        Assert.Contains("recipe 3", ChuteSections.CheckRecipes(EditRecipes(r => r[3]!["ingredientPattern"] = "I_I,_I_")));
        Assert.Contains("recipe 0", ChuteSections.CheckRecipes(EditRecipes(r => r[0]!["output"]!["code"] = "chute-elbow-down-west")));
    }

    private static string BetterRuins => Text("betterruins-mechanical.json");

    [Fact]
    public void Better_ruins_blueprint_chutes_are_as_the_patch_expects_and_a_change_is_refused()
    {
        Assert.Null(ChuteSections.CheckBetterRuins(BetterRuins));
        string Edit(Action<JArray> edit)
        {
            var root = JArray.Parse(BetterRuins, Lenient);
            edit(root);
            return root.ToString();
        }
        // a recipe inserted before them moves them
        Assert.Contains("recipe 6", ChuteSections.CheckBetterRuins(Edit(r => r.Insert(0, r[0]!.DeepClone()))));
        Assert.Contains("recipe 7", ChuteSections.CheckBetterRuins(Edit(r => r[7]!["ingredients"]!["I"]!["code"] = "game:chutesection-*")));
        Assert.Contains("recipe 9", ChuteSections.CheckBetterRuins(Edit(r => ((JObject)r[9]!["ingredients"]!).Add("S", new JObject { ["code"] = "game:solderbar-*" }))));
        Assert.Contains("recipe 10", ChuteSections.CheckBetterRuins(Edit(r => r.RemoveAt(10))));
        Assert.Contains("does not parse", ChuteSections.CheckBetterRuins("[ {"));
    }

    [Fact]
    public void Shipped_better_ruins_patch_switches_off_exactly_its_five_chutes()
    {
        var patch = (JArray)Json("unifiedpipes-betterruins.json");
        Assert.All(patch, op =>
        {
            Assert.Equal("betterruins:" + ChuteSections.BetterRuinsFile, (string?)op["file"]);
            Assert.Equal("betterruins", (string?)op["dependsOn"]![0]!["modid"]);
            Assert.Equal("add", (string?)op["op"]);
            Assert.False((bool)op["value"]!);
        });
        Assert.Equal(ChuteSections.BetterRuinsChutes.Keys.Order().Select(i => $"/{i}/enabled"), patch.Select(op => (string)op["path"]!));
        // the blueprint's other recipes, the riveted block among them, are left alone
        var recipes = JArray.Parse(BetterRuins, Lenient);
        Assert.Equal("game:metalblock-new-riveted-{metal}", (string?)recipes[11]!["output"]!["code"]);
        Assert.DoesNotContain(patch, op => ((string)op["path"]!).StartsWith("/11/"));
    }

    // The patch, applied to the fixtures as the game's loader applies it: every op lands where it was
    // written for, and after it the guard sees the states and the switched-off recipe.
    [Fact]
    public void Shipped_patch_adds_the_three_metals_and_switches_off_the_plate_recipe()
    {
        var patch = (JArray)Json("unifiedpipes-chutesection.json");
        var item = JObject.Parse(Item, Lenient);
        var recipes = JArray.Parse(Recipes, Lenient);
        foreach (var op in patch)
        {
            Assert.Equal("server", (string?)op["side"]);
            var file = (string)op["file"]!;
            JToken root = file == "game:" + ChuteSections.ItemFile ? item
                : file == "game:" + ChuteSections.RecipeFile ? recipes
                : throw new Xunit.Sdk.XunitException($"the patch targets {file}");
            var path = ((string)op["path"]!).Split('/').Skip(1).ToArray();
            var parent = path[..^1].Aggregate(root, (node, key) => node is JArray a ? a[int.Parse(key)]! : node[key]!);
            switch ((string)op["op"]!, path[^1])
            {
                case ("add", "-"):
                    ((JArray)parent).Add(op["value"]!.DeepClone());
                    break;
                case ("add", var key):
                    Assert.Null(parent[key]); // an add on an existing key would replace it
                    parent[key] = op["value"]!.DeepClone();
                    break;
                case ("replace", var key):
                    Assert.NotNull(parent[key]);
                    parent[key] = op["value"]!.DeepClone();
                    break;
                default:
                    throw new Xunit.Sdk.XunitException($"unexpected op {op}");
            }
        }
        Assert.Equal(ChuteSections.GameMetals.Concat(ChuteSections.AddedMetals), item["variantgroups"]![0]!["states"]!.Select(s => (string)s!));
        Assert.Equal(ChuteSections.Metals.Order(), ChuteSections.GameMetals.Concat(ChuteSections.AddedMetals).Order());
        Assert.Equal(["*-copper"], item["creativeinventory"]!["mechanics"]!.Select(s => (string)s!));
        Assert.Equal("seraphhorizons:chutesection-handbook-text", (string?)item["attributes"]!["handbook"]!["extraSections"]![0]!["text"]);
        Assert.False((bool)recipes[ChuteSections.PlateRecipeIndex]!["enabled"]!);
        Assert.All(recipes.Take(ChuteSections.PlateRecipeIndex), r => Assert.Null(r["enabled"]));
        // every chute takes a solder bar per section and the soldering iron, in its own pattern, its yield the game's
        foreach (var chute in ChuteSections.ChuteRecipes)
        {
            var r = recipes[chute.Index]!;
            var pattern = ((string)r["ingredientPattern"]!).Split(',');
            Assert.Equal(chute.Sections, pattern.Sum(row => row.Count(c => c == 'I')));
            // the game reads a pattern without its commas, row by row of its width ("I,I" at width 2 is "II")
            var game = chute.Pattern.Replace(",", "");
            Assert.Equal(Trim(game.Chunk(chute.Width).Select(row => new string(row).Replace('I', 'P'))), Trim(pattern.Select(row => row.Replace('I', 'P'))));
            Assert.Equal((chute.SolderedWidth, chute.SolderedHeight), ((int)r["width"]!, (int)r["height"]!));
            Assert.Equal(chute.SolderedHeight, pattern.Length);
            Assert.All(pattern, row => Assert.Equal(chute.SolderedWidth, row.Length));
            Assert.Equal(chute.Sections, (int)r["ingredients"]![ChuteSections.SolderKey]!["quantity"]!);
            Assert.Equal(["tin", "silver"], r["ingredients"]![ChuteSections.SolderKey]!["allowedVariants"]!.Select(v => (string)v!));
            Assert.Equal("game:solderingiron", (string?)r["ingredients"]![ChuteSections.SolderingIronKey]!["code"]);
            Assert.True((bool)r["ingredients"]![ChuteSections.SolderingIronKey]!["isTool"]!);
            Assert.Equal(1, pattern.Sum(row => row.Count(c => c.ToString() == ChuteSections.SolderKey)));
            Assert.Equal(1, pattern.Sum(row => row.Count(c => c.ToString() == ChuteSections.SolderingIronKey)));
            Assert.Null(r["output"]!["quantity"]); // the game's yield: one
            // no hammer: that is a pipe
            Assert.DoesNotContain(r["ingredients"]!.Children<JProperty>(), p => ((string?)p.Value["code"])?.Contains("hammer") == true);
        }
        // the patched files no longer pass the guard: a second application would be refused
        Assert.NotNull(ChuteSections.CheckItem(item.ToString()));
    }

    [Fact]
    public void Lead_is_forged_as_the_game_forges_copper()
    {
        var recipe = Assert.Single((JArray)Json("chutesection-smithing.json"))!;
        Assert.Equal(["lead"], recipe["ingredient"]!["allowedVariants"]!.Select(s => (string)s!));
        Assert.Equal("game:ingot-*", (string?)recipe["ingredient"]!["code"]);
        Assert.Equal("game:chutesection-{metal}", (string?)recipe["output"]!["code"]);
        // the game's pattern: a square tube, 5 x 5 with a 3 x 3 bore, two layers, within one ingot's 42 voxels
        var layers = recipe["pattern"]!.Select(l => l.Select(r => (string)r!).ToList()).ToList();
        Assert.Equal(2, layers.Count);
        Assert.All(layers, l => Assert.Equal(["#####", "#___#", "#___#", "#___#", "#####"], l));
        Assert.Equal(32, layers.Sum(l => l.Sum(r => r.Count(c => c == '#'))));
    }

    [Fact]
    public void Two_open_sections_solder_shut_into_two_sections()
    {
        var recipe = Assert.Single((JArray)Json("chutesection-grid.json"))!;
        var ingredients = (JObject)recipe["ingredients"]!;
        Assert.Equal("seraphhorizons:chutesectionopen-*", (string?)ingredients["O"]!["code"]);
        Assert.Equal(ChuteSections.SolderedMetals, ingredients["O"]!["allowedVariants"]!.Select(s => (string)s!));
        Assert.Equal(2, ((string)recipe["ingredientPattern"]!).Count(c => c == 'O'));
        Assert.Equal(2, (int)ingredients["S"]!["quantity"]!);
        Assert.Equal(["tin", "silver"], ingredients["S"]!["allowedVariants"]!.Select(s => (string)s!));
        Assert.True((bool)ingredients["I"]!["isTool"]!);
        Assert.Equal(2, (int)ingredients["I"]!["toolDurabilityCost"]!); // as the game's soldering
        Assert.Equal("game:chutesection-{metal}", (string?)recipe["output"]!["code"]);
        Assert.Equal(2 * ChuteSections.ClosedPerOpen, (int)recipe["output"]!["quantity"]!);

        var open = (JObject)Json("chutesectionopen-itemtype.json");
        Assert.Equal("chutesectionopen", (string?)open["code"]);
        Assert.Equal(ChuteSections.SolderedMetals, open["variantgroups"]![0]!["states"]!.Select(s => (string)s!));
        Assert.Equal("game:block/metal/sheet/{metal}1", (string?)open["textures"]!["metaltex"]!["base"]);
    }

    /// <summary>A pattern's sections alone (<c>P</c>), every other cell blank, empty rows and columns
    /// cut: where the sections lie, whatever else fills the free cells.</summary>
    private static string[] Trim(IEnumerable<string> pattern)
    {
        var rows = pattern.Select(row => new string(row.Select(c => c == 'P' ? 'P' : '_').ToArray())).Where(row => row.Contains('P')).ToList();
        int first = rows.Min(row => row.IndexOf('P')), last = rows.Max(row => row.LastIndexOf('P'));
        return rows.Select(row => row.PadRight(last + 1, '_')[first..(last + 1)]).ToArray();
    }

    private static readonly Dictionary<string, string[]> ChutePatterns = new()
    {
        // the game's chute patterns (recipes/grid/chute.json), the cells a section fills
        ["straight-ns"] = ["II"],
        ["bend-nw"] = ["I_", "_I"],
        ["tjunction-uns"] = ["_I_", "I_I"],
        ["xjunction-nswe"] = ["_I_", "I_I", "_I_"],
    };

    [Fact]
    public void Every_pipe_shape_is_made_from_sections_and_a_joint_for_every_metal()
    {
        var recipes = (JArray)Json("unifiedpipes-grid.json");
        foreach (var (shape, sections, pipes) in ChuteSections.PipeShapes)
        {
            var made = recipes.Where(r => (string?)r["output"]!["code"] == $"ppex:pipe-{shape}-{{metal}}").ToList();
            Assert.Equal(2, made.Count);
            foreach (var (metals, joint) in new[] { (ChuteSections.SolderedMetals, "game:solderbar-*"), (ChuteSections.NailedMetals, "game:metalnailsandstrips-*") })
            {
                var r = Assert.Single(made, r => r["ingredients"]!["P"]!["allowedVariants"]!.Select(s => (string)s!).SequenceEqual(metals));
                Assert.Equal("game:chutesection-*", (string?)r["ingredients"]!["P"]!["code"]);
                Assert.Equal("metal", (string?)r["ingredients"]!["P"]!["name"]);
                Assert.Equal(pipes, (int)r["output"]!["quantity"]!);
                var pattern = ((string)r["ingredientPattern"]!).Split(',');
                Assert.Equal(sections, pattern.Sum(row => row.Count(c => c == 'P')));
                // the sections lie in the game's chute pattern (its other cells hold the joint and the tool)
                Assert.Equal(Trim(ChutePatterns[shape].Select(row => row.Replace('I', 'P'))), Trim(pattern));
                Assert.Equal(pattern.Length, (int)r["height"]!);
                Assert.All(pattern, row => Assert.Equal((int)r["width"]!, row.Length));
                var ingredients = (JObject)r["ingredients"]!;
                var jointKey = Assert.Single(ingredients.Properties(), p => (string?)p.Value["code"] == joint).Name;
                var tools = ingredients.Properties().Where(p => (bool?)p.Value["isTool"] == true).Select(p => p.Value).ToList();
                // the hammer is in every pipe recipe: it is what tells a pipe from a chute
                Assert.Single(tools, t => (string?)t["code"] == "game:hammer-*");
                if (joint == "game:solderbar-*")
                {
                    Assert.Equal(sections, (int)ingredients[jointKey]!["quantity"]!); // a bar per section, as a chute
                    Assert.Equal(ChuteSections.SolderBars(shape), sections);
                    var iron = Assert.Single(tools, t => (string?)t["code"] == "game:solderingiron");
                    Assert.Equal(2, (int)iron["toolDurabilityCost"]!);
                    Assert.Equal(2, tools.Count);
                }
                else
                {
                    Assert.Single(tools);
                    // nails and strips of the sections' own metal
                    Assert.Equal("metal", (string?)ingredients[jointKey]!["name"]);
                    Assert.Equal(metals, ingredients[jointKey]!["allowedVariants"]!.Select(s => (string)s!));
                    Assert.Equal(1, (int)ingredients[jointKey]!["quantity"]!);
                }
                Assert.Equal(1, pattern.Sum(row => row.Count(c => c.ToString() == jointKey)));
            }
        }
        // nothing else makes pipe from pipe any more, and the valves stay
        Assert.DoesNotContain(recipes, r => r["ingredients"]!.Children<JProperty>().Any(p => (string?)p.Value["code"] == "ppex:pipe-straight-ns-*")
                                            && !((string)r["output"]!["code"]!).Contains("valve"));
        Assert.Equal(2, recipes.Count(r => ((string)r["output"]!["code"]!).Contains("valve")));
        Assert.Equal(ChuteSections.PipeShapes.Length * 2 + 2, recipes.Count);
    }

    [Fact]
    public void Two_sections_make_two_straight_pipes_so_each_route_gives_its_ladder_figure()
    {
        Assert.Equal(1, ChuteSections.ForgedPerIngot);
        Assert.Equal(ChuteSections.BrakedPerPlate / (double)ChuteSections.IngotsPerPlate, 1.0);
        Assert.Equal(3, ChuteSections.DrawnPerIngot);
        Assert.Equal(2.0, CastPipeMold.PipesPerIngot());
        Assert.Equal(ChuteSections.CastPerIngot, CastPipeMold.SectionsPerFill);
        Assert.Equal(2, ChuteSections.SolderBars("bend-nw"));
        Assert.Equal(2, ChuteSections.SolderBars("straight-ns"));
        Assert.Equal(3, ChuteSections.SolderBars("tjunction-uns"));
        Assert.Equal(4, ChuteSections.SolderBars("xjunction-nswe"));
        Assert.Equal("game:chutesection-lead", ChuteSections.Section("lead"));
        Assert.Equal("seraphhorizons:chutesectionopen-copper", ChuteSections.OpenSection("copper"));
        Assert.Equal("ppex:pipe-bend-nw-steel", ChuteSections.Pipe("bend-nw", "steel"));
    }
}
