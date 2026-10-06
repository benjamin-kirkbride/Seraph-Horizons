using System.Text.Json.Nodes;
using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Tests.Admin;

public class AdminOutputTests
{
    [Fact]
    public void TextSplitsIntoSummaryAndLines()
    {
        var o = AdminOutput.FromText("ore list", "3 deposits:\ncopper:1,2 a\n\niron:0,0 b\r\n");
        Assert.Equal("3 deposits:", o.Summary);
        Assert.Equal(["copper:1,2 a", "iron:0,0 b"], o.Lines);
        Assert.Equal("3 deposits:\ncopper:1,2 a\niron:0,0 b", o.ToText());
    }

    [Fact]
    public void JsonHasTheGenericFieldsAndTheData()
    {
        var o = AdminOutput.FromText("trade camps", "2 camps:\n1,2 smith", ok: true);
        o.Data["radius"] = 4096;
        o.Data["camps"] = new JsonArray(new JsonObject { ["id"] = "1,2" });
        var json = JsonNode.Parse(o.ToJson())!.AsObject();
        Assert.Equal("trade camps", (string)json["command"]!);
        Assert.True((bool)json["ok"]!);
        Assert.Equal("2 camps:", (string)json["summary"]!);
        Assert.Equal("1,2 smith", (string)json["lines"]![0]!);
        Assert.Equal(4096, (int)json["radius"]!);
        Assert.Equal("1,2", (string)json["camps"]![0]!["id"]!);
    }

    [Fact]
    public void DataCannotOverrideCommandOrOk()
    {
        var o = new AdminOutput("ore cells", ok: false) { Summary = "x" };
        o.Data["ok"] = true;
        o.Data["command"] = "other";
        o.Data["summary"] = "replaced";
        var json = JsonNode.Parse(o.ToJson())!.AsObject();
        Assert.False((bool)json["ok"]!);
        Assert.Equal("ore cells", (string)json["command"]!);
        Assert.Equal("replaced", (string)json["summary"]!);
    }

    [Theory]
    [InlineData("{\"ok\":true}", "{\"ok\":true}\n")]
    [InlineData("game:ore-{rock}", "game:ore-{rock}\n")]
    [InlineData("a {\nb", "a {\nb")]
    [InlineData("plain", "plain")]
    [InlineData("", "")]
    public void ChatSafeAnswersSkipTheTranslationFormatter(string text, string expected) =>
        Assert.Equal(expected, AdminOutput.ChatSafe(text));

    [Fact]
    public void JsonKeepsCodesReadable()
    {
        var o = new AdminOutput("trade value") { Summary = "game:ingot-iron <b>4</b> & more" };
        Assert.Contains("game:ingot-iron <b>4</b> & more", o.ToJson());
    }

    [Theory]
    [InlineData(new[] { "list", "--json", "copper" }, true, new[] { "list", "copper" })]
    [InlineData(new[] { "--JSON" }, true, new string[0])]
    [InlineData(new[] { "list", "copper" }, false, new[] { "list", "copper" })]
    [InlineData(new[] { "--json", "x", "--json" }, true, new[] { "x" })]
    public void StripsTheFlag(string[] words, bool had, string[] left)
    {
        var list = words.ToList();
        Assert.Equal(had, AdminOutput.StripFlag(list));
        Assert.Equal(left, list);
    }

    [Theory]
    [InlineData("state", "state.json")]
    [InlineData("survey.csv", "survey.csv")]
    [InlineData(" export.json ", "export.json")]
    public void ResolvesPlainFileNames(string name, string file)
    {
        Assert.Equal(Path.Combine("/srv/data", file), AdminFiles.Resolve("/srv/data", name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("../state")]
    [InlineData("/etc/passwd")]
    [InlineData("a\\b")]
    [InlineData("c:x")]
    public void RefusesAnythingButAFileName(string? name)
    {
        Assert.Null(AdminFiles.Resolve("/srv/data", name));
    }
}

public class AdminStateBookTests
{
    /// <summary>A section holding a dictionary, as a system's state would.</summary>
    private sealed class FakeSection(string name) : IAdminState
    {
        public Dictionary<string, double> Levels { get; set; } = new();
        public string Section => name;
        public JsonNode Export() => new JsonObject(Levels.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value)));

        public void Import(JsonNode data)
        {
            if (data is not JsonObject o) throw new FormatException("not an object");
            Levels = o.ToDictionary(kv => kv.Key, kv => (double)kv.Value!);
        }
    }

    [Fact]
    public void ExportThenImportRoundTrips()
    {
        var supply = new FakeSection("supply") { Levels = { ["game:ingot-iron"] = 3.5, ["game:plank-oak"] = 0.25 } };
        var standing = new FakeSection("standing") { Levels = { ["camp:1,2"] = 120 } };
        var book = new AdminStateBook();
        book.Register(supply);
        book.Register(standing);
        var (file, errors) = book.Export(stamp: () => "2026-10-06");
        Assert.Empty(errors);
        Assert.Equal(AdminStateBook.Format, (string)file["format"]!);
        Assert.Equal("2026-10-06", (string)file["exported"]!);
        // Through text, as the file goes to disk.
        var read = JsonNode.Parse(file.ToJsonString());

        var other = new AdminStateBook();
        var supply2 = new FakeSection("supply");
        var standing2 = new FakeSection("standing") { Levels = { ["old"] = 1 } };
        other.Register(supply2);
        other.Register(standing2);
        var result = other.Import(read);
        Assert.Equal(ImportOutcome.Imported, result["supply"].Outcome);
        Assert.Equal(ImportOutcome.Imported, result["standing"].Outcome);
        Assert.Equal(supply.Levels, supply2.Levels);
        Assert.Equal(standing.Levels, standing2.Levels);
    }

    [Fact]
    public void ImportReportsUnknownMissingAndFailedSections()
    {
        var book = new AdminStateBook();
        book.Register(new FakeSection("supply"));
        book.Register(new FakeSection("orders"));
        var file = JsonNode.Parse("""
            {"format": "seraphhorizons-admin-state", "version": 1,
             "sections": {"supply": [1, 2], "visitors": {}}}
            """);
        var result = book.Import(file);
        Assert.Equal(ImportOutcome.Failed, result["supply"].Outcome);
        Assert.Equal("not an object", result["supply"].Error);
        Assert.Equal(ImportOutcome.UnknownSection, result["visitors"].Outcome);
        Assert.Equal(ImportOutcome.NotInFile, result["orders"].Outcome);
    }

    [Fact]
    public void ImportRefusesOtherFiles()
    {
        var book = new AdminStateBook();
        Assert.Throws<FormatException>(() => book.Import(JsonNode.Parse("""{"sections": {}}""")));
        Assert.Throws<FormatException>(() => book.Import(JsonNode.Parse("[]")));
        Assert.Throws<FormatException>(() => book.Import(JsonNode.Parse("""{"format": "seraphhorizons-admin-state"}""")));
    }

    [Fact]
    public void AFailingExportIsReportedAndTheRestKept()
    {
        var book = new AdminStateBook();
        book.Register(new DelegateAdminState("broken", () => throw new InvalidOperationException("boom"), _ => { }));
        book.Register(new FakeSection("supply") { Levels = { ["a"] = 1 } });
        var (file, errors) = book.Export();
        Assert.Equal("boom", errors["broken"]);
        Assert.NotNull(file["sections"]!["supply"]);
        Assert.Null(file["sections"]!["broken"]);
    }

    [Fact]
    public void ExportAndImportCanBeLimitedToSections()
    {
        var a = new FakeSection("a") { Levels = { ["x"] = 1 } };
        var b = new FakeSection("b") { Levels = { ["y"] = 2 } };
        var book = new AdminStateBook();
        book.Register(a);
        book.Register(b);
        var (file, _) = book.Export(only: ["b"]);
        Assert.Null(file["sections"]!["a"]);
        b.Levels.Clear();
        var result = book.Import(file, only: ["b"]);
        Assert.Single(result);
        Assert.Equal(2, b.Levels["y"]);
    }
}

public class ChannelLogTests
{
    [Fact]
    public void WritesOnlyChannelsThatAreOn()
    {
        string dir = Path.Combine(Path.GetTempPath(), "sh-admin-log-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(dir, "seraphhorizons-trade.log");
        var time = new DateTime(2026, 10, 6, 14, 3, 11);
        using (var log = new ChannelLog(path, ["supply", "standing"], () => time))
        {
            log.Write("supply", "nothing yet");
            Assert.False(File.Exists(path));
            Assert.True(log.Set("supply", true));
            Assert.False(log.Set("visitors", true));
            log.Write("supply", "iron sold\nx16");
            log.Write("standing", "not logged");
            log.Set(null, true);
            Assert.Equal(["standing", "supply"], log.On);
            log.Write("standing", "now logged");
            log.Set(null, false);
            log.Write("supply", "off again");
        }
        Assert.Equal(["2026-10-06 14:03:11 [supply] iron sold x16", "2026-10-06 14:03:11 [standing] now logged"], File.ReadAllLines(path));
        Directory.Delete(dir, true);
    }
}

public class MapOverlayTests
{
    [Fact]
    public void RoundTripsThroughJson()
    {
        var o = new MapOverlay { Key = "ore", Legend = "legend" };
        o.Rects.Add(new OverlayRect(0, 0, 5000, 5000, MapOverlay.Argb(1, 2, 3, 4), "cell 0,0"));
        o.Marks.Add(new OverlayMark(10, -20, MapOverlay.ColorFor("copper"), "copper:0,0", 10));
        o.Rings.Add(new OverlayRing(100, 200, 1500, -1));
        o.Lines.Add(new OverlayLine(1, 2, 3, 4, 5));
        var back = MapOverlay.FromJson(o.ToJson());
        Assert.Equal("ore", back.Key);
        Assert.Equal(o.Rects, back.Rects);
        Assert.Equal(o.Marks, back.Marks);
        Assert.Equal(o.Rings, back.Rings);
        Assert.Equal(o.Lines, back.Lines);
        Assert.Equal(4, back.Count);
    }

    [Fact]
    public void PacksColoursAsTheGameDoes()
    {
        // ColorUtil.ColorFromRgba: r | g << 8 | b << 16 | a << 24.
        Assert.Equal(0x04030201, MapOverlay.Argb(1, 2, 3, 4));
        Assert.Equal(MapOverlay.ColorFor("iron"), MapOverlay.ColorFor("iron"));
        Assert.NotEqual(MapOverlay.ColorFor("iron"), MapOverlay.ColorFor("copper"));
        Assert.Equal(255, (MapOverlay.ColorFor("tin") >> 24) & 0xff);
    }
}
