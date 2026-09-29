using System.Text;
using SeraphHorizons.IconExport.Core;

namespace SeraphHorizons.IconExport.Tests;

public class ListFileTests
{
    private static ListResult Parse(string s) => ListFile.Parse(Encoding.UTF8.GetBytes(s));

    [Fact]
    public void Text_with_comments_blank_lines_and_crlf()
    {
        ListResult r = Parse(
            "# icons to redo\r\n"
            + "game:stick\r\n"
            + "\r\n"
            + "   game:clutter-art/bottle   # trailing comment\r\n"
            + "\t\n"
            + "tankardsandgoblets:t&g-winebottle-blue");
        Assert.Equal("text", r.Format);
        Assert.Equal(new[]
        {
            new ListEntry("game:stick", null),
            new ListEntry("game:clutter-art/bottle", null),
            new ListEntry("tankardsandgoblets:t&g-winebottle-blue", null),
        }, r.Entries);
        Assert.Empty(r.Rejected);
        Assert.Equal(0, r.Duplicates);
    }

    [Fact]
    public void Bad_codes_are_rejected_with_their_line()
    {
        ListResult r = Parse("stick\ngame:ok\nGame:upper\ngame:two words\ngame:a:b\n");
        Assert.Equal(new[] { new ListEntry("game:ok", null) }, r.Entries);
        Assert.Equal(new[] { 1, 3, 4, 5 }, r.Rejected.Select(x => x.Line));
        Assert.Equal("stick", r.Rejected[0].Text);
        Assert.Contains("no domain", r.Rejected[0].Reason);
    }

    [Fact]
    public void A_duplicate_code_is_kept_once_at_its_first_place()
    {
        ListResult r = Parse("game:b\ngame:a\ngame:b\ngame:a # again\n");
        Assert.Equal(new[] { "game:b", "game:a" }, r.Entries.Select(e => e.Code));
        Assert.Equal(2, r.Duplicates);
    }

    [Fact]
    public void Recipe_export_gives_the_keys_of_items_with_their_kind()
    {
        // Shaped like schema/examples/minimal.json: other top-level keys before and after, and a
        // nested "kind" inside attributes that must not be taken for the item's own.
        string json = """
            {
              "schemaVersion": 1,
              "recipes": { "grid": [ { "output": { "code": "game:not-an-item" } } ] },
              "items": {
                "game:stick": { "name": "Stick", "attributes": { "kind": "block" }, "kind": "item" },
                "game:crate": { "kind": "block", "sources": [ { "kind": "item" } ] },
                "game:mystery": { "name": "No kind" },
                "not a code": { "kind": "item" },
                "game:stick": { "kind": "item" }
              },
              "mods": { "game": { "name": "Vintage Story" } }
            }
            """;
        ListResult r = Parse(json);
        Assert.Equal("recipe export", r.Format);
        Assert.Equal(new[]
        {
            new ListEntry("game:stick", IconKind.Item),
            new ListEntry("game:crate", IconKind.Block),
            new ListEntry("game:mystery", null),
        }, r.Entries);
        Assert.Single(r.Rejected);
        Assert.Equal("not a code", r.Rejected[0].Text);
        Assert.Equal(1, r.Duplicates);
    }

    [Fact]
    public void Json_object_without_items_is_an_error()
    {
        Assert.Throws<FormatException>(() => Parse("{ \"recipes\": {} }"));
    }

    [Fact]
    public void Json_array_of_codes()
    {
        ListResult r = Parse("[\"game:a\", \"game:b\", 3, \"game:a\"]");
        Assert.Equal("json array", r.Format);
        Assert.Equal(new[] { "game:a", "game:b" }, r.Entries.Select(e => e.Code));
        Assert.Equal(3, r.Rejected.Single().Line);
        Assert.Equal(1, r.Duplicates);
    }

    [Fact]
    public void Byte_order_mark_and_leading_space_are_ignored()
    {
        byte[] bom = { 0xEF, 0xBB, 0xBF };
        ListResult r = ListFile.Parse(bom.Concat(Encoding.UTF8.GetBytes("\n  {\"items\": {\"game:a\": {}}}")).ToArray());
        Assert.Equal("recipe export", r.Format);
        Assert.Equal(new[] { new ListEntry("game:a", null) }, r.Entries);
    }
}
