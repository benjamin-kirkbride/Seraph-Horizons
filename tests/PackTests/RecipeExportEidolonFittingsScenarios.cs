using Atlas.XUnit;
using Newtonsoft.Json.Linq;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The eidolon section (tools/recipe-export/EidolonSection.cs, docs/recipe-browser/schema.md, "The
/// eidolon's fittings"), what the site's fitting sheet shows: the five layers in build order with
/// the all-steel build as their defaults, every build's stats as the mod's EidolonStats has them
/// (all steel keeps 300 HP), every code an item of the export, and every material's texture path
/// carried and in the repository's texture store (textures/index.json, which the site build takes
/// the images from: CI's server has none).
/// </summary>
public partial class RecipeExportScenarios
{
    private JObject Eidolon => (JObject)(Doc["eidolon"] ?? throw new Xunit.Sdk.XunitException("the export has no eidolon section"));

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Eidolon_section_carries_the_layers_in_build_order()
    {
        var layers = Eidolon["layers"]!.Cast<JObject>().ToList();
        Assert.Equal(["bones", "joints", "plates", "sash", "skirt"], layers.Select(l => (string)l["id"]!));
        Assert.Equal(["oak", "steel", "steel", "leather-plain", "steel"], layers.Select(l => (string)l["default"]!));
        string[] Materials(string id) => layers.Single(l => (string)l["id"]! == id)["materials"]!.Select(m => (string)m["id"]!).ToArray();
        Assert.Equal(["iron", "steel", "stainlesssteel"], Materials("joints"));
        Assert.Equal(["iron", "steel", "stainlesssteel", "brass"], Materials("plates"));
        Assert.Equal(["iron", "steel", "stainlesssteel"], Materials("skirt"));
        Assert.DoesNotContain("rotten", Materials("bones"));
        Assert.Contains("veryaged", Materials("bones"));
        Assert.Equal(23, Materials("sash").Length);
        // The joints take their rods and nails and strips, then the moving mechanism's Jonas parts.
        var joints = layers.Single(l => (string)l["id"]! == "joints");
        var stainless = joints["materials"]!.Single(m => (string)m["id"]! == "stainlesssteel");
        Assert.Equal(["game:rod-stainlesssteel", "game:metalnailsandstrips-stainlesssteel"], stainless["items"]!.Select(i => (string)i["code"]!));
        Assert.Contains(joints["parts"]!, p => (string)p["code"]! == "game:jonasframes-joint01" && (int)p["count"]! == 2);
        Assert.Contains(layers[0]["parts"]!, p => (string)p["code"]! == "game:rustypart-eidolon2tr");
    }

    [AtlasScenario(TimeoutMs = Timeout)]
    public void Eidolon_section_carries_every_build_and_its_textures()
    {
        var builds = Eidolon["builds"]!.Cast<JObject>().ToList();
        Assert.Equal(3 * 4 * 3, builds.Count);
        JObject Build(string j, string p, string s) => builds.Single(b => (string)b["joints"]! == j && (string)b["plates"]! == p && (string)b["skirt"]! == s);
        Assert.Equal(300, (double)Build("steel", "steel", "steel")["maxHealth"]!, 4);
        Assert.Equal(4, (int)Build("steel", "steel", "steel")["armour"]!["protectionTier"]!);
        Assert.Equal(300 / (1.5 * 1.5 * 1.25), (double)Build("iron", "iron", "iron")["maxHealth"]!, 4);
        var brass = Build("steel", "brass", "steel");
        Assert.Equal(0.022 * 1.25, (double)brass["walkSpeed"]!, 5);
        Assert.Equal(8, (double)brass["defenceDamage"]!, 4);
        Assert.Equal(2, (int)brass["armour"]!["protectionTier"]!);

        Assert.Null(Eidolon["textures"]);   // paths only: the images are the site build's
        var stored = (JObject)JObject.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "textures", "index.json")))["textures"]!;
        var paths = new List<string>();
        foreach (var l in Eidolon["layers"]!)
            foreach (var m in l["materials"]!)
            {
                var path = (string?)m["texture"];
                Assert.True(path != null, $"{l["id"]}'s {m["id"]} carries no texture");
                paths.Add(path!);
            }
        paths.AddRange(((JObject)Eidolon["shapeTextures"]!).Properties().Select(p => (string)p.Value!));
        Assert.Contains("seraphhorizons:block/metal/armor-generic/chain-stainlesssteel", paths);
        var missing = paths.Distinct().Where(p => stored[p] == null).ToList();
        Assert.True(missing.Count == 0, "not in textures/index.json (tools/textures.py import, docs/recipe-browser/icons.md): " + string.Join(", ", missing));
        foreach (var code in Eidolon.SelectTokens("$..code").Select(t => (string)t!))
            Assert.True(Doc["items"]![code] != null, $"{code} is not an item of the export");
        Assert.True(Doc["items"]![(string)Eidolon["item"]!] != null);
    }

    private static string RepoRoot()
    {
        // Not AppContext.BaseDirectory: after boot it points into the game install.
        var dir = Path.GetDirectoryName(typeof(RecipeExportScenarios).Assembly.Location)!;
        for (var d = new DirectoryInfo(dir); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "pack", "pack.toml"))) return d.FullName;
        throw new Xunit.Sdk.XunitException("the repository root (pack/pack.toml) is not above " + dir);
    }
}
