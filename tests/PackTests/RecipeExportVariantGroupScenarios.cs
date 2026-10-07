using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.TidyVariants;
using Vintagestory.API.Common;

namespace SeraphHorizons.PackTests;

/// <summary>
/// Tidy Variants groups in the export (`variantGroups`, docs/recipe-browser/schema.md "Variant
/// groups"): what the server's own resolution groups, as the site will fold it. Expected values come
/// from the vanilla assets (gravel is `gravel-{rock}`, its handbook groups it with `gravel-*`) and from
/// the mod's resolution, read here directly, never from the exporter's reflection.
/// </summary>
public partial class RecipeExportScenarios
{
    private JObject VariantGroupSection =>
        Doc["variantGroups"] as JObject ?? throw new Xunit.Sdk.XunitException("no variantGroups");

    // survival/blocktypes/soil/gravel.json: gravel-{rock}, handbook groupBy gravel-*; granite leads
    // the engine's preferred rock list.
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Gravel_is_one_group_led_by_granite_with_every_rock()
    {
        var (id, group) = VariantGroupSection.Properties()
            .Select(p => (p.Name, Group: (JObject)p.Value))
            .Single(g => g.Group["members"]!.Values<string>().Contains("game:gravel-granite"));
        Assert.Equal("Gravel", (string)group["title"]!);
        var members = group["members"]!.Values<string>().ToList();
        Assert.Equal("game:gravel-granite", members[0]);
        foreach (var rock in new[] { "andesite", "basalt", "chalk", "limestone", "sandstone", "slate" })
            Assert.Contains($"game:gravel-{rock}", members);
        Assert.Equal(id, TidyVariantsModSystem.ForSide(EnumAppSide.Server)!.Resolution.GroupById(id)?.Id);
    }

    /// <summary>Every exported group is the engine's group of that id: its members are codes of the
    /// group's entries in rank order, items of the export, and in no other group.</summary>
    [AtlasScenario(TimeoutMs = Timeout)]
    public void Variant_groups_are_the_server_resolution_in_rank_order()
    {
        var bridge = TidyVariantsModSystem.ForSide(EnumAppSide.Server);
        Assert.NotNull(bridge);
        var r = bridge.Resolution;
        var items = (JObject)Doc["items"]!;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in VariantGroupSection.Properties())
        {
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
    }
}
