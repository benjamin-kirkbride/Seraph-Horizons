using System.Text.Json;
using System.Text.Json.Nodes;
using SeraphHorizons.Mod.Pipes.Core;

namespace SeraphHorizons.Tests.Pipes;

/// <summary>Soldered joints (<c>UnifiedPipes</c>): the guard on ppex's four pipe shape tables, held to
/// ppex 0.7.1's blocktypes (a fixture trimmed to their <c>shapebytype</c>) and to the shipped patch,
/// which puts ppex's copper and lead pipes on this mod's soldered models.</summary>
public class SolderedJointsTests
{
    private static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private static string Text(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, file));

    private static JsonObject Ppex() => JsonNode.Parse(Text("ppex-pipe-shapes.json"), documentOptions: Lenient)!.AsObject();

    private static string Patch => Text("unifiedpipes-solderedjoints.json");

    private static Func<string, string?> Files(JsonObject ppex) => file => ppex[file]?.ToJsonString();

    private static string? Check(Action<JsonObject> edit)
    {
        var ppex = Ppex();
        edit(ppex);
        return SolderedJoints.Check(Patch, Files(ppex));
    }

    private static JsonObject Table(JsonObject ppex, string kind) => ppex[$"blocktypes/pipes/{kind}.json"]!["shapebytype"]!.AsObject();

    [Fact]
    public void The_shipped_patch_fits_ppex_as_it_ships() =>
        Assert.Null(SolderedJoints.Check(Patch, Files(Ppex())));

    [Fact]
    public void Each_orientation_gets_a_copper_and_a_lead_entry_first_with_its_rotations_and_our_shape()
    {
        var ops = JsonNode.Parse(Patch, documentOptions: Lenient)!.AsArray();
        Assert.Equal(PipeAssetGuard.PipeBlocktypes.Select(f => "ppex:" + f), ops.Select(op => (string?)op!["file"]));
        foreach (var op in ops)
        {
            var file = ((string)op!["file"]!)["ppex:".Length..];
            var kind = SolderedJoints.Kind(file);
            var theirs = Table(Ppex(), kind);
            var ours = op["value"]!.AsObject().ToList();
            Assert.Equal(theirs.Count * 3, ours.Count);
            for (var i = 0; i < theirs.Count; i++)
            {
                var (key, value) = theirs.ElementAt(i);
                var stem = key[..^1];
                Assert.Equal(new[] { stem + "copper", stem + "lead", key }, ours.Skip(3 * i).Take(3).Select(p => p.Key));
                foreach (var entry in ours.Skip(3 * i).Take(2).Select(p => p.Value!.AsObject()))
                {
                    Assert.Equal(SolderedJoints.ShapePrefix + kind, (string?)entry["base"]);
                    foreach (var axis in new[] { "rotateX", "rotateY", "rotateZ" })
                        Assert.Equal((double?)value![axis], (double?)entry[axis]);
                }
                Assert.True(JsonNode.DeepEquals(value, ours[3 * i + 2].Value));
            }
        }
    }

    [Fact]
    public void A_changed_ppex_table_is_refused()
    {
        // a rotation changed
        Assert.Contains("is not as the patch has it", Check(p => Table(p, "bend")["*-bend-en-*"]!["rotateY"] = 90));
        // an orientation added, or one gone
        Assert.Contains("orientations", Check(p => Table(p, "straight")["*-straight-du-*"] = new JsonObject { ["base"] = "ppex:pipes/straight" }));
        Assert.Contains("orientations", Check(p => Table(p, "xjunction").Remove("*-xjunction-weud-*")));
        // a key no longer ending in the material wildcard
        Assert.Contains("material wildcard", Check(p =>
        {
            var t = Table(p, "tjunction");
            var v = t["*-tjunction-wne-*"]!.DeepClone();
            t.Remove("*-tjunction-wne-*");
            t["*-tjunction-wne"] = v;
        }));
        // the shape moved
        Assert.Contains("is not as the patch has it", Check(p => Table(p, "straight")["*-straight-ns-*"]!["base"] = "ppex:pipes/straight2"));
        // no table, or no file
        Assert.Contains("no shapebytype", Check(p => p["blocktypes/pipes/bend.json"]!.AsObject().Remove("shapebytype")));
        Assert.Contains("missing", Check(p => p.Remove("blocktypes/pipes/xjunction.json")));
    }

    [Fact]
    public void A_patch_that_is_not_one_replace_per_pipe_is_refused()
    {
        Assert.Contains("one operation per pipe", SolderedJoints.Check("[]", Files(Ppex())));
        var ops = JsonNode.Parse(Patch, documentOptions: Lenient)!.AsArray();
        ops[0]!["op"] = "add";
        Assert.Contains("replaced shapebytype", SolderedJoints.Check(ops.ToJsonString(), Files(Ppex())));
        Assert.Contains("does not parse", SolderedJoints.Check("[", Files(Ppex())));
    }
}
