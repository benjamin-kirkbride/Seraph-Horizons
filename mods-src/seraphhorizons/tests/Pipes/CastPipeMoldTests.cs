using System.Text.Json;
using System.Text.Json.Nodes;
using SeraphHorizons.Mod.Pipes.Core;

namespace SeraphHorizons.Tests.Pipes;

/// <summary>Cast pipes (<c>CastPipes</c>): the guard on smex's two mold blocktypes, held to smex
/// 0.10.1's files (trimmed fixtures) and to the shipped patch, and the ladder's figure, held to the
/// shipped patch and recipe.</summary>
public class CastPipeMoldTests
{
    private static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private static string Text(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, file));

    private static JsonNode Json(string file) => JsonNode.Parse(Text(file), documentOptions: Lenient)!;

    private static string Fired => Text("smex-toolmoldfired.json");
    private static string Raw => Text("smex-toolmoldraw.json");

    private static string Edit(string json, Action<JsonObject> edit)
    {
        var root = JsonNode.Parse(json, documentOptions: Lenient)!.AsObject();
        edit(root);
        return root.ToJsonString();
    }

    private static JsonArray Groups(JsonObject root) => root["variantgroups"]!.AsArray();

    [Fact]
    public void Smex_molds_are_as_the_patch_expects()
    {
        Assert.Null(CastPipeMold.CheckFired(Fired));
        Assert.Null(CastPipeMold.CheckRaw(Raw));
    }

    [Fact]
    public void A_tooltype_group_that_already_has_pipe_or_moved_is_refused()
    {
        Assert.Contains("already has pipe", CastPipeMold.CheckFired(Edit(Fired, r => Groups(r)[2]!["states"]!.AsArray().Add("pipe"))));
        Assert.Contains("second is not materialtype", CastPipeMold.CheckRaw(Edit(Raw, r =>
        {
            var groups = Groups(r);
            var tool = groups[2]!.DeepClone();
            groups[2] = groups[1]!.DeepClone();
            groups[1] = tool;
        })));
        Assert.Contains("third is not tooltype", CastPipeMold.CheckFired(Edit(Fired, r => Groups(r)[2]!["code"] = "shape")));
        Assert.Contains("fewer than three", CastPipeMold.CheckFired(Edit(Fired, r => Groups(r).RemoveAt(2))));
        Assert.Contains("materialtype with raw", CastPipeMold.CheckRaw(Fired));
        Assert.Contains("first is not color", CastPipeMold.CheckFired(Edit(Fired, r => Groups(r)[0]!["code"] = "clay")));
    }

    [Fact]
    public void A_fired_mold_of_another_class_or_with_a_catch_all_key_is_refused()
    {
        Assert.Contains("class", CastPipeMold.CheckFired(Edit(Fired, r => r["class"] = "smex.BlockFancyMold")));
        // A key that answers for every mold would shadow the patch's (the game takes the first match).
        Assert.Contains("already answers", CastPipeMold.CheckFired(Edit(Fired, r => r["attributesByType"]!.AsObject().Insert(0, "toolmold-*", new JsonObject()))));
        Assert.Contains("attributes by type", CastPipeMold.CheckFired(Edit(Fired, r => r.Remove("attributesByType"))));
        Assert.Contains("shape by type already", CastPipeMold.CheckFired(Edit(Fired, r => r["shapebytype"]!["*"] = new JsonObject())));
        Assert.Contains("plain shape", CastPipeMold.CheckRaw(Edit(Raw, r => r["shape"] = new JsonObject { ["base"] = "smex:molten/molds/plate" })));
        Assert.Contains("no shape by type", CastPipeMold.CheckRaw(Edit(Raw, r => r.Remove("shapebytype"))));
    }

    [Fact]
    public void A_raw_mold_whose_firing_is_not_by_tool_type_is_refused()
    {
        Assert.Contains("fired stack is not by tool type", CastPipeMold.CheckRaw(Edit(Raw, r =>
            r["combustiblePropsByType"]!["toolmold-blue-raw-*"]!["smeltedStack"]!["code"] = "smex:toolmold-blue-fired-plate")));
        Assert.Contains("red clay's fired stack", CastPipeMold.CheckRaw(Edit(Raw, r => r["combustiblePropsByType"]!.AsObject().Remove("toolmold-red-raw-*"))));
        Assert.Contains("beehive kiln", CastPipeMold.CheckRaw(Edit(Raw, r =>
            r["attributesByType"]!["toolmold-red-raw-*"]!["beehivekiln"]!["2"]!["code"] = "smex:toolmold-red-fired-plate")));
        Assert.Contains("combustible", CastPipeMold.CheckRaw(Edit(Raw, r => r.Remove("combustiblePropsByType"))));
    }

    /// <summary>The shipped patch, applied to smex's files as the game's patch loader applies an
    /// <c>add</c>, leaves both with the pipe tool type (so the guard then says it is already there)
    /// and adds only keys of its own.</summary>
    [Fact]
    public void Shipped_patch_adds_the_pipe_tool_type_to_both_molds()
    {
        var patch = Json("castpipes-smexmold.json").AsArray();
        var files = new Dictionary<string, JsonObject>
        {
            ["smex:blocktypes/molds/toolmoldfired.json"] = JsonNode.Parse(Fired)!.AsObject(),
            ["smex:blocktypes/molds/toolmoldraw.json"] = JsonNode.Parse(Raw)!.AsObject(),
        };
        foreach (var op in patch.Select(o => o!.AsObject()))
        {
            Assert.Equal("add", (string?)op["op"]);
            Assert.Equal("server", (string?)op["side"]);
            Assert.Equal(CastPipeMold.SmexId, (string?)op["dependsOn"]![0]!["modid"]);
            var target = files[(string)op["file"]!];
            var path = ((string)op["path"]!).Split('/')[1..];
            JsonNode parent = target;
            foreach (var part in path[..^1])
                parent = parent is JsonArray array ? array[int.Parse(part)]! : parent[part]!;
            var value = op["value"]!.DeepClone();
            if (path[^1] == "-")
                parent.AsArray().Add(value);
            else
            {
                Assert.False(parent.AsObject().ContainsKey(path[^1]), $"the patch replaces smex's {op["path"]}");
                parent[path[^1]] = value;
            }
        }
        foreach (var (file, root) in files)
        {
            var states = Groups(root)[CastPipeMold.ToolTypeGroupIndex]!["states"]!.AsArray().Select(s => (string)s!).ToList();
            Assert.Equal(CastPipeMold.ToolType, states[^1]);
            Assert.Single(states, CastPipeMold.ToolType);
            var shape = root["shapebytype"]!["*-" + CastPipeMold.ToolType]!;
            Assert.Equal("seraphhorizons:block/clay/mold/pipe", (string?)shape["base"]);
        }
        Assert.Contains("already has pipe", CastPipeMold.CheckFired(files["smex:blocktypes/molds/toolmoldfired.json"].ToJsonString()));
        Assert.Contains("already has pipe", CastPipeMold.CheckRaw(files["smex:blocktypes/molds/toolmoldraw.json"].ToJsonString()));
    }

    [Fact]
    public void Shipped_patch_carries_the_ladder_figures()
    {
        var fired = Json("castpipes-smexmold.json").AsArray()
            .Single(o => (string?)o!["path"] == "/attributesByType/toolmold-*-fired-pipe")!["value"]!;
        Assert.Equal(CastPipeMold.RequiredUnits, (int)fired["requiredUnits"]!);
        Assert.Equal(CastPipeMold.SectionCode + "-{metal}", (string?)fired["drop"]!["code"]);
        Assert.Equal("item", (string?)fired["drop"]!["type"]);
        Assert.Equal(CastPipeMold.SectionsPerFill, (int)fired["drop"]!["quantity"]!);
        Assert.Equal(2, CastPipeMold.SectionsPerFill);
        // The pedestal and the game's mold read the same keys; the surface stays in the cavity
        // either way the shape turns (x 4..12, z 3..13 before its 90 degree turn).
        var quad = fired["fillQuadsByLevel"]![0]!;
        Assert.True((double)quad["x1"]! >= 4 && (double)quad["x2"]! <= 12 && (double)quad["z1"]! >= 4 && (double)quad["z2"]! <= 12);
        Assert.True(CastPipeMold.Matches("toolmold-*-fired-pipe", CastPipeMold.Mold("tan", "fired")[(CastPipeMold.SmexId.Length + 1)..]));
    }

    [Fact]
    public void One_ingot_casts_two_sections_and_two_pipes()
    {
        Assert.Equal(2.0, CastPipeMold.SectionsPerIngot());
        Assert.Equal(1.0, CastPipeMold.SectionsPerIngot(requiredUnits: 200));
        Assert.Equal(0.0, CastPipeMold.SectionsPerIngot(requiredUnits: 0));
        Assert.Equal(2.0, CastPipeMold.PipesPerIngot());
        Assert.Equal("game:chutesection-iron", CastPipeMold.Section("iron"));
        Assert.Equal("ppex:pipe-straight-ns-steel", CastPipeMold.StraightPipe("steel"));
        // the sections it casts are states UnifiedPipes adds, and the metals the grid bands with nails
        Assert.All(CastPipeMold.Metals, m => Assert.Contains(m, ChuteSections.AddedMetals));
        Assert.Equal(ChuteSections.NailedMetals, CastPipeMold.Metals);
    }

    [Fact]
    public void The_raw_mold_is_clay_formed_one_layer_deep_round_a_core()
    {
        var recipe = Assert.Single(Json("castpipes-pipemold.json").AsArray())!;
        Assert.Equal("smex:toolmold-{color}-raw-pipe", (string?)recipe["output"]!["code"]);
        Assert.Equal(["blue", "fire", "red"], recipe["ingredient"]!["allowedVariants"]!.AsArray().Select(s => (string)s!));
        var layers = recipe["pattern"]!.AsArray().Select(l => l!.AsArray().Select(r => (string)r!).ToList()).ToList();
        Assert.Equal(2, layers.Count);
        Assert.All(layers, l => Assert.All(l, row => Assert.Equal(14, row.Length)));
        Assert.All(layers[0], row => Assert.DoesNotContain('_', row));
        // Two troughs either side of the core, closed at both ends.
        Assert.Equal(60, layers[1].Sum(row => row.Count(c => c == '_')));
        Assert.DoesNotContain('_', layers[1][0] + layers[1][1] + layers[1][12] + layers[1][13]);
        Assert.All(layers[1].Skip(2).Take(10), row => Assert.Equal("###___##___###", row));
    }

    [Theory]
    [InlineData("toolmold-*-fired-pipe", "toolmold-blue-fired-pipe", true)]
    [InlineData("*-pipe", "toolmold-blue-raw-pipe", true)]
    [InlineData("smex:toolmold-red-raw-*", "toolmold-red-raw-pipe", true)]
    [InlineData("toolmold-red-raw-*", "toolmold-blue-raw-pipe", false)]
    [InlineData("toolmold-*-fired-plate", "toolmold-blue-fired-pipe", false)]
    [InlineData("TOOLMOLD-*", "toolmold-blue-fired-pipe", true)]
    public void By_type_keys_match_as_the_game_matches(string pattern, string code, bool expected) =>
        Assert.Equal(expected, CastPipeMold.Matches(pattern, code));
}
