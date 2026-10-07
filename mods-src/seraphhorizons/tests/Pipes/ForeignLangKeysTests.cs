using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Pipes.Core;
using Xunit;

namespace SeraphHorizons.Tests.Pipes;

public class ForeignLangKeysTests
{
    static string LangPath([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "assets", "seraphhorizons", "lang", "en.json"));

    private const string Own = """
        { "block-own": "Own", "smex:block-toolmold-*-raw-pipe": "Raw pipe ceramic mold",
          "smex:blockdesc-toolmold-*-raw-pipe": "Fire it.", "ppex:block-pipe-x": "Not this one" }
        """;

    [Fact]
    public void Takes_only_the_domains_keys_without_their_prefix()
    {
        var keys = ForeignLangKeys.For(Own, "smex");
        Assert.Equal(2, keys.Count);
        Assert.Equal("Raw pipe ceramic mold", keys["block-toolmold-*-raw-pipe"]);
        Assert.Equal("Fire it.", keys["blockdesc-toolmold-*-raw-pipe"]);
    }

    [Fact]
    public void Appends_what_the_other_file_lacks_and_keeps_what_it_has()
    {
        var theirs = """{ "block-toolmold-*-raw-plate": "Raw plate ceramic mold", "blockdesc-toolmold-*-raw-pipe": "Theirs" }""";
        var merged = ForeignLangKeys.Merge(theirs, ForeignLangKeys.For(Own, "smex"));
        Assert.NotNull(merged);
        var json = JObject.Parse(merged!);
        Assert.Equal("Raw plate ceramic mold", (string?)json["block-toolmold-*-raw-plate"]);
        Assert.Equal("Raw pipe ceramic mold", (string?)json["block-toolmold-*-raw-pipe"]);
        Assert.Equal("Theirs", (string?)json["blockdesc-toolmold-*-raw-pipe"]);
        Assert.Equal(3, json.Count);
    }

    [Fact]
    public void Nothing_to_add_is_null()
    {
        var theirs = """{ "block-toolmold-*-raw-pipe": "x", "blockdesc-toolmold-*-raw-pipe": "y" }""";
        Assert.Null(ForeignLangKeys.Merge(theirs, ForeignLangKeys.For(Own, "smex")));
    }

    [Fact]
    public void The_shipped_lang_file_names_the_pipe_mold_for_smex()
    {
        var keys = ForeignLangKeys.For(File.ReadAllText(LangPath()), "smex");
        foreach (var key in new[] { "block-toolmold-*-raw-pipe", "block-toolmold-*-fired-pipe", "blockdesc-toolmold-*-raw-pipe", "blockdesc-toolmold-*-fired-pipe" })
            Assert.True(keys.ContainsKey(key), key);
    }
}
