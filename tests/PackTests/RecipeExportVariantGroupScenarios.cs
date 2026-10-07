using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.TidyVariants;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace SeraphHorizons.PackTests;

/// <summary>
/// Variant groups in the export (`variantGroups`, docs/recipe-browser/schema.md "Variant groups"):
/// what the server's own Tidy Variants resolution groups, then what the handbook's shipped `groupBy`
/// groups among the rest, as the site will fold them. Expected values come from the vanilla assets
/// (gravel is `gravel-{rock}`, its handbook groups it with `gravel-*`; juice is only in creative inside
/// a bucket, and `rawjuice.json` groups its pages with `juiceportion-*`) and from the mod's resolution,
/// read here directly, never from the exporter's reflection.
/// </summary>
public partial class RecipeExportScenarios
{
    private JObject VariantGroupSection =>
        Doc["variantGroups"] as JObject ?? throw new Xunit.Sdk.XunitException("no variantGroups");

    // survival/blocktypes/soil/gravel.json: gravel-{rock}, handbook groupBy gravel-*; a granite leads
    // the engine's preferred rock list.
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Gravel_is_one_group_led_by_granite_with_every_rock()
    {
        var (id, group) = VariantGroupSection.Properties()
            .Select(p => (p.Name, Group: (JObject)p.Value))
            .Single(g => g.Group["members"]!.Values<string>().Contains("game:gravel-granite"));
        Assert.Equal("Gravel", (string)group["title"]!);
        var members = group["members"]!.Values<string>().ToList();
        // gravel-granite and gravel-granite-land-1 (the land-{layer} family the shipped groupBy pulls in) tie
        // on their rock, and the engine breaks the tie by creative order, which follows block ids and so
        // varies between worlds: either may lead.
        Assert.StartsWith("game:gravel-granite", members[0]);
        foreach (var rock in new[] { "andesite", "basalt", "chalk", "limestone", "sandstone", "slate" })
            Assert.Contains($"game:gravel-{rock}", members);
        Assert.Equal(id, TidyVariantsModSystem.ForSide(EnumAppSide.Server)!.Resolution.GroupById(id)?.Id);
    }

    // survival/itemtypes/liquid/rawjuice.json: juiceportion-{fruit}, in creative only as a bucket's
    // contents, handbook groupBy juiceportion-*. The exporter's second pass groups the items the
    // handbook does; the first exported code, apple's, leads.
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Juices_are_one_group_from_the_handbooks_groupBy()
    {
        var group = (JObject)VariantGroupSection["handbook:game:juiceportion-*"]!;
        Assert.Equal("Juice", (string)group["title"]!);
        var members = group["members"]!.Values<string>().ToList();
        Assert.Equal("game:juiceportion-apple", members[0]);
        foreach (var fruit in new[] { "redcurrant", "cranberry", "blueberry", "peach" })
            Assert.Contains($"game:juiceportion-{fruit}", members);
        // Not a creative entry, so no Tidy Variants group has a juice.
        var bridge = TidyVariantsModSystem.ForSide(EnumAppSide.Server)!;
        Assert.DoesNotContain(bridge.Resolution.Groups, g => g.Members.Any(e => bridge.CollectibleOf(e).Code.Path.StartsWith("juiceportion-")));
    }

    /// <summary>Every exported Tidy Variants group is the engine's group of that id: its members are
    /// codes of the group's entries in rank order, items of the export, and in no other group. A
    /// handbook group's members are items of the export, in no other group, and match its pattern.</summary>
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Variant_groups_are_the_server_resolution_in_rank_order()
    {
        var bridge = TidyVariantsModSystem.ForSide(EnumAppSide.Server);
        Assert.NotNull(bridge);
        var r = bridge.Resolution;
        var items = (JObject)Doc["items"]!;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int handbook = 0;
        foreach (var p in VariantGroupSection.Properties())
        {
            if (p.Name.StartsWith("handbook:"))
            {
                handbook++;
                var parts = p.Name.Split(':', 3);
                var pattern = new AssetLocation(parts[1], parts[2]);
                foreach (var code in p.Value["members"]!.Values<string>().Select(c => c!))
                {
                    Assert.True(items.ContainsKey(code), $"{p.Name}: {code} is not an item");
                    Assert.True(seen.Add(code), $"{p.Name}: {code} is in two groups");
                    Assert.True(WildcardUtil.Match(pattern, new AssetLocation(code)) || p.Value["members"]![0]!.ToString() == code,
                        $"{p.Name}: {code} does not match the pattern");
                }
                continue;
            }
            var g = r.GroupById(p.Name);
            Assert.True(g != null, $"{p.Name} is not a group of the server's resolution");
            Assert.False(string.IsNullOrWhiteSpace((string?)p.Value["title"]), $"{p.Name} has no title");
            var members = p.Value["members"]!.Values<string>().Select(c => c!).ToList();
            Assert.True(members.Count >= 2, $"{p.Name} has {members.Count} members");
            // The best rank among each code's entries, as the group orders its members.
            var rank = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var e in g.Members)
            {
                var code = bridge.CollectibleOf(e).Code.ToString();
                int k = r.RepresentativeRank(e);
                rank[code] = rank.TryGetValue(code, out int had) ? Math.Min(had, k) : k;
            }
            foreach (var code in members)
            {
                Assert.True(rank.ContainsKey(code), $"{p.Name}: {code} is not a member of the group");
                Assert.True(items.ContainsKey(code), $"{p.Name}: {code} is not an item");
                Assert.True(seen.Add(code), $"{p.Name}: {code} is in two groups");
            }
            Assert.Equal(members.OrderBy(c => rank[c]).ToList(), members);
        }
        Assert.True(handbook > 0, "no handbook group");
    }
}
