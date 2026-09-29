using System.Text;
using SeraphHorizons.IconExport.Core;

namespace SeraphHorizons.IconExport.Tests;

public class ManifestTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "seraphicons-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void Written_form()
    {
        var m = new Manifest { Generator = "test" };
        m.AddIcon("tankardsandgoblets/item/t%26g-winebottle-blue.png",
            new IconEntry("tankardsandgoblets:t&g-winebottle-blue", IconKind.Item, 128, ImageCheck.Ok));
        m.AddIcon("game/block/crate.png", new IconEntry("game:crate", IconKind.Block, 64, ImageCheck.Untextured, CustomRenderer: true));
        m.AddFailure(new FailedEntry("game:missing", null, "no item or block"));
        string expected = """
            {
              "schemaVersion": 1,
              "generator": "test",
              "layout": "<domain>/<item|block>/<path>.png; bytes outside [a-z0-9_-] are %XX (UTF-8)",
              "icons": {
                "game/block/crate.png": {
                  "code": "game:crate",
                  "kind": "block",
                  "size": 64,
                  "check": "untextured",
                  "customRenderer": true
                },
                "tankardsandgoblets/item/t%26g-winebottle-blue.png": {
                  "code": "tankardsandgoblets:t&g-winebottle-blue",
                  "kind": "item",
                  "size": 128,
                  "check": "ok"
                }
              },
              "failed": [
                {
                  "code": "game:missing",
                  "kind": null,
                  "reason": "no item or block"
                }
              ]
            }

            """;
        Assert.Equal(expected.Replace("\r\n", "\n"), Encoding.UTF8.GetString(m.ToJson()).Replace("\r\n", "\n"));
    }

    [Fact]
    public void Round_trip()
    {
        var m = new Manifest { Generator = "g" };
        m.AddIcon("game/item/a.png", new IconEntry("game:a", IconKind.Item, 100, ImageCheck.Transparent));
        m.AddFailure(new FailedEntry("game:b", IconKind.Block, "boom"));
        Manifest back = Manifest.FromJson(m.ToJson());
        Assert.Equal("g", back.Generator);
        Assert.Equal(new IconEntry("game:a", IconKind.Item, 100, ImageCheck.Transparent), back.Icons["game/item/a.png"]);
        Assert.Equal(new[] { new FailedEntry("game:b", IconKind.Block, "boom") }, back.Failed);
    }

    [Fact]
    public void A_later_success_clears_the_failure()
    {
        var m = new Manifest();
        m.AddFailure(new FailedEntry("game:a", IconKind.Item, "threw"));
        m.AddFailure(new FailedEntry("game:a", null, "not registered"));
        m.AddFailure(new FailedEntry("game:b", IconKind.Item, "threw"));
        m.AddIcon("game/item/a.png", new IconEntry("game:a", IconKind.Item, 64, ImageCheck.Ok));
        Assert.Equal(new[] { "game:b" }, m.Failed.Select(f => f.Code));
    }

    [Fact]
    public void Wrong_schema_version_is_refused()
    {
        Assert.Throws<FormatException>(() => Manifest.FromJson(Encoding.UTF8.GetBytes("{\"schemaVersion\": 2}")));
    }

    [Fact]
    public void Save_and_load_from_a_directory()
    {
        var m = new Manifest();
        m.AddIcon("game/item/a.png", new IconEntry("game:a", IconKind.Item, 64, ImageCheck.Ok));
        m.Save(_dir);
        Assert.False(File.Exists(Path.Combine(_dir, "manifest.json.tmp")));
        Assert.Equal(new[] { "game/item/a.png" }, Manifest.LoadOrNew(_dir).Icons.Keys);
        Assert.Empty(Manifest.LoadOrNew(Path.Combine(_dir, "nothing-here")).Icons);
    }

    [Fact]
    public void Files_missing_from_the_manifest_are_adopted_by_their_path()
    {
        Touch("game/block/clutter-art/bottle.png");
        Touch("tankardsandgoblets/item/t%26g-winebottle-blue.png");
        Touch("game/item/known.png");
        Touch("game/thing/x.png");          // not item or block
        Touch("game/item/Upper.png");       // not a path the writer makes
        Touch("game/item/half.png.tmp");    // an interrupted write
        var m = new Manifest();
        m.AddIcon("game/item/known.png", new IconEntry("game:known", IconKind.Item, 128, ImageCheck.Ok));
        Assert.Equal(2, m.AdoptFiles(_dir, 0));
        Assert.Equal(new IconEntry("game:clutter-art/bottle", IconKind.Block, 0, ImageCheck.Unchecked), m.Icons["game/block/clutter-art/bottle.png"]);
        Assert.Equal("tankardsandgoblets:t&g-winebottle-blue", m.Icons["tankardsandgoblets/item/t%26g-winebottle-blue.png"].Code);
        Assert.Equal(128, m.Icons["game/item/known.png"].Size);
        Assert.Equal(3, m.Icons.Count);
    }

    private void Touch(string rel)
    {
        string path = Path.Combine(_dir, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[] { 1 });
    }
}
