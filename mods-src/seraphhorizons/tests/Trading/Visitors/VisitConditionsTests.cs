using System.Text.Json;
using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Visitors.Core;

namespace SeraphHorizons.Mod.Tests.Trading.Visitors;

public class VisitConditionsTests
{
    private static readonly CampSite[] Camps =
    [
        new("camp:0,0", 1000, 1000),
        new("camp:1,0", 3000, 0),
        new("camp:5,5", 10000, 10000),
    ];

    [Fact]
    public void Standing_is_the_best_tier_among_the_camps_within_the_radius()
    {
        var tiers = new Dictionary<string, int> { ["camp:0,0"] = 1, ["camp:1,0"] = 2, ["camp:5,5"] = 4 };
        var check = VisitConditions.Standing(Camps, 0, 0, id => tiers[id]);
        Assert.True(check.Passed);
        Assert.Equal(2, check.BestTier);
        Assert.Equal("camp:1,0", check.CampId);
        Assert.Equal(2, check.Camps); // the far camp's partner tier does not count

        tiers["camp:1,0"] = 1;
        Assert.False(VisitConditions.Standing(Camps, 0, 0, id => tiers[id]).Passed);
        Assert.True(VisitConditions.Standing(Camps, 0, 0, id => tiers[id], new ConditionSettings { MinTier = 1 }).Passed);
    }

    [Fact]
    public void Standing_with_no_camp_near_fails()
    {
        var check = VisitConditions.Standing(Camps, -20000, 0, _ => 4);
        Assert.False(check.Passed);
        Assert.Null(check.CampId);
        Assert.Equal(0, check.Camps);
    }

    [Fact]
    public void Supply_sums_each_bought_item_once_against_the_threshold()
    {
        var levels = new Dictionary<string, double> { ["game:ingot-iron"] = 1.5, ["game:ingot-copper"] = 0.4 };
        double Level(string c) => levels.GetValueOrDefault(c);
        var check = VisitConditions.Supply(["game:ingot-iron", "game:ingot-copper", "game:ingot-iron", "game:flaxtwine"], Level);
        Assert.Equal(1.9, check.Level, 6);
        Assert.False(check.Passed);
        levels["game:flaxtwine"] = 0.1;
        Assert.True(VisitConditions.Supply(["game:ingot-iron", "game:ingot-copper", "game:flaxtwine"], Level).Passed);
        Assert.True(VisitConditions.Supply([], Level, new ConditionSettings { MinSupply = 0 }).Passed);
    }

    [Fact]
    public void An_evaluation_passes_the_kinds_whose_supply_passes_when_the_inn_and_standing_do()
    {
        var inn = InnRules.Evaluate(Grid.Inn(), new InnPos(0, 1, 0), []);
        var standing = new StandingCheck(true, 2, "camp:0,0", 1);
        var supply = new Dictionary<string, SupplyCheck>
        {
            [VisitorKinds.General] = new(true, 3),
            [VisitorKinds.Curio] = new(false, 0.5),
        };
        var eval = new InnEvaluation(inn, standing, supply);
        Assert.Equal([VisitorKinds.General], eval.PassingKinds);
        Assert.Empty(eval.Missing);

        var low = eval with { Standing = new StandingCheck(false, 1, "camp:0,0", 1) };
        Assert.Empty(low.PassingKinds);
        Assert.Equal(["standing"], low.Missing);

        var dark = eval with { Report = InnRules.Evaluate(Grid.Inn().Clear(0, 2, -2).Clear(2, 1, 2), new InnPos(0, 1, 0), []) };
        Assert.Empty(dark.PassingKinds);
        Assert.Equal(["bed", "light"], dark.Missing);

        var dry = eval with { Supply = supply.ToDictionary(kv => kv.Key, _ => new SupplyCheck(false, 0)) };
        Assert.Equal(["supply"], dry.Missing);
    }
}

/// <summary>The two shipped visitor lists: they load like the camp lists, sell 10–15 specials no
/// camp sells, some of them rare (standing tier 3), and buy something (the supply condition reads it).</summary>
public class ShippedVisitorListsTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static TradeListDef Load(string type) =>
        JsonSerializer.Deserialize<TradeListDef>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "tradelists", $"trader-{type}.json")), Options)!;

    private static IEnumerable<TradeEntry> Entries(TradeSide s) =>
        s.Core.Concat(s.Rotating.List).Concat(s.Regional.Values.SelectMany(r => r.Core.Concat(r.Rotating)));

    public static TheoryData<string> Types => new(TraderTypes.Visitors);

    [Theory]
    [MemberData(nameof(Types))]
    public void A_visitor_list_has_no_problems_and_sells_specials_no_camp_sells(string type)
    {
        var def = Load(type);
        Assert.Equal(type, def.Type);
        Assert.Empty(TradeListResolver.Problems(def));
        var selling = Entries(def.Selling).ToList();
        Assert.InRange(selling.Count, 10, 15);
        Assert.InRange(selling.Count(e => e.StandingTier == 3), 2, 5);
        Assert.NotEmpty(Entries(def.Buying));

        var campSells = TraderTypes.All.SelectMany(t => Entries(Load(t).Selling)).Select(e => e.Key).ToHashSet();
        var sold = selling.Where(e => campSells.Contains(e.Key)).Select(e => e.Key).ToList();
        Assert.True(sold.Count == 0, "Also sold at camps: " + string.Join(", ", sold));

        // Rare goods are on the shelf only for a trusted inn owner.
        var stranger = TradeListResolver.Resolve(def, new Region(Region.Temperate, Region.Sedimentary));
        var trusted = TradeListResolver.Resolve(def, new Region(Region.Temperate, Region.Sedimentary), 3);
        Assert.DoesNotContain(stranger.Selling.Rotating, e => e.StandingTier > 0);
        Assert.Contains(trusted.Selling.Rotating, e => e.StandingTier == 3);
    }

    [Fact]
    public void The_two_visitors_sell_different_goods()
    {
        var a = Entries(Load(TraderTypes.TravellingMerchant).Selling).Select(e => e.Key);
        var b = Entries(Load(TraderTypes.TravellingCurio).Selling).Select(e => e.Key);
        Assert.Empty(a.Intersect(b));
    }
}
