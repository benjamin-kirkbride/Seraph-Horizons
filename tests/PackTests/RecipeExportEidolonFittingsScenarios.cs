using Atlas.XUnit;
using Newtonsoft.Json.Linq;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The eidolon section (tools/recipe-export/EidolonSection.cs, docs/recipe-browser/schema.md, "The
/// eidolon's fittings"), what the site's fitting sheet shows: the five layers in build order with
/// the all-steel build as their defaults, every build's stats as the mod's EidolonStats has them
/// (all steel keeps 300 HP), every code an item of the export, and every material's texture carried.
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

        var textures = (JObject)Eidolon["textures"]!;
        foreach (var m in Eidolon["layers"]!.SelectMany(l => l["materials"]!))
        {
            var path = (string?)m["texture"];
            Assert.True(path != null && textures[path] != null, $"{m["id"]}'s texture {path} is not carried");
        }
        Assert.NotNull(textures["seraphhorizons:block/metal/armor-generic/chain-stainlesssteel"]);
        foreach (var code in Eidolon.SelectTokens("$..code").Select(t => (string)t!))
            Assert.True(Doc["items"]![code] != null, $"{code} is not an item of the export");
        Assert.True(Doc["items"]![(string)Eidolon["item"]!] != null);
    }
}
