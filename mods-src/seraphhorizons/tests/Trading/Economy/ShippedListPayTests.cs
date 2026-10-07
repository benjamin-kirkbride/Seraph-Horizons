using System.Text.Json;
using System.Text.Json.Nodes;
using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Economy.Core;
using SeraphHorizons.Mod.Trading.Values.Core;

namespace SeraphHorizons.Mod.Tests.Trading.Economy;

/// <summary>
/// The lists' buying prices hold the buy spread (2026-10-06): a list entry is the final pay, a fifth
/// of value, and the runtime spreads only off-list goods (<see cref="Pricing"/>). These hold the
/// thirteen shipped lists to the shipped value table so the two can't drift apart, and stand in for
/// the old runtime cap (a trader paid at most 0.6 × its own price): no list pays as much for an item
/// as some list asks for it.
/// </summary>
public class ShippedListPayTests
{
    /// <summary>"About a fifth": an entry may pay up to this share of the table's value.</summary>
    private const double MaxShare = 0.3;
    /// <summary>A known outlier may drift this far over its recorded share (table rebuilds move values).</summary>
    private const double OutlierSlack = 1.25;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly Lazy<Dictionary<string, TradeListDef>> Lists = new(() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "tradelists"), "trader-*.json").ToDictionary(
            f => Path.GetFileNameWithoutExtension(f)["trader-".Length..],
            f => JsonSerializer.Deserialize<TradeListDef>(File.ReadAllText(f), Options)!));

    private static readonly Lazy<ItemValues> Table = new(() =>
        ItemValues.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "item-values.json"))));

    private static IEnumerable<TradeEntry> Entries(TradeSide s) =>
        s.Core.Concat(s.Rotating.List).Concat(s.Regional.Values.SelectMany(r => r.Core.Concat(r.Rotating)));

    private static string Attrs(TradeEntry e) =>
        e.Attributes is JsonElement { ValueKind: JsonValueKind.Object } a ? JsonNode.Parse(a.GetRawText())!.ToJsonString() : "";

    /// <summary>(type, full code, list pay per item / table value) for every buying entry the table values.</summary>
    private static List<(string Type, string Code, double Share)> Shares()
    {
        var shares = new List<(string, string, double)>();
        foreach (var (type, def) in Lists.Value)
            foreach (var e in Entries(def.Buying))
            {
                string code = BuyerIndex.FullCode(e.Code);
                double value = Table.Value.ValueOf(code);
                if (value <= 0 || e.Price is not { Avg: > 0 } price) continue;
                shares.Add((type, code, price.Avg / Math.Max(1, e.StackSize) / value));
            }
        return shares;
    }

    [Fact]
    public void ListsPayAboutAFifthOfValueAtTheMedian()
    {
        var shares = Shares().Select(s => s.Share).OrderBy(x => x).ToList();
        Assert.True(shares.Count > 500, $"only {shares.Count} buying entries with a value");
        double median = shares[shares.Count / 2];
        Assert.InRange(median, 0.15, 0.25);
        // Three in four at about a fifth or under.
        Assert.True(shares[shares.Count * 3 / 4] <= MaxShare, $"75th percentile {shares[shares.Count * 3 / 4]:0.###}");
    }

    [Fact]
    public void EveryListBuyingPriceIsAtMostAboutAFifthOfValue()
    {
        var outliers = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "list-pay-outliers.json")))!["entries"]!
            .AsObject().ToDictionary(p => p.Key, p => (double)p.Value!);
        var over = new List<string>();
        foreach (var (type, code, share) in Shares())
        {
            double limit = outliers.TryGetValue($"{type} {code}", out double known) ? known * OutlierSlack : MaxShare;
            if (share > limit * (1 + 1e-6))
                over.Add($"{type} {code}: pays {share:0.###} × value (at most {limit:0.###})");
        }
        Assert.True(over.Count == 0, "a list buying price above about a fifth of the value table (write it at 0.2 × value, "
            + "or fix the table):\n" + string.Join("\n", over));
    }

    [Fact]
    public void NoListPaysForAnItemWhatAListAsksForIt()
    {
        // The lowest ask per item over every list, as the shelf rounds it (whole gears, at least one).
        var asks = new Dictionary<(string, string), double>();
        foreach (var def in Lists.Value.Values)
            foreach (var e in Entries(def.Selling))
            {
                if (e.Price is not { Avg: > 0 } price) continue;
                var key = (BuyerIndex.FullCode(e.Code), Attrs(e));
                double ask = Pricing.Listed(price.Avg, e.StackSize, 1, 1, traderBuys: false).UnitPrice / (double)Math.Max(1, e.StackSize);
                asks[key] = asks.TryGetValue(key, out double a) ? Math.Min(a, ask) : ask;
            }
        var loops = new List<string>();
        int compared = 0;
        foreach (var (type, def) in Lists.Value)
            foreach (var e in Entries(def.Buying))
            {
                if (e.Price is not { Avg: > 0 } price || !asks.TryGetValue((BuyerIndex.FullCode(e.Code), Attrs(e)), out double ask)) continue;
                compared++;
                // The pay per item as the list has it: a cheap stack is bought by a bigger unit (Pricing.Listed).
                double pay = price.Avg / Math.Max(1, e.StackSize);
                if (pay > 0.6 * ask * (1 + 1e-6)) loops.Add($"{type} {e.Code}: pays {pay:0.###} an item, asked {ask:0.###}");
            }
        Assert.True(compared > 200, $"only {compared} items both bought and sold");
        Assert.True(loops.Count == 0, "a list pays more than 0.6 × the lowest ask:\n" + string.Join("\n", loops));
    }

    [Fact]
    public void TheMechanicTradesTheReclaimedSteelGearsAtAFifthOfWhatItAsks()
    {
        var mechanic = Lists.Value["mechanic"];
        foreach (string code in new[] { "seraphhorizons:gear-steel", "seraphhorizons:largegear-steel" })
        {
            var sell = Assert.Single(Entries(mechanic.Selling), e => e.Code == code);
            var buy = Assert.Single(Entries(mechanic.Buying), e => e.Code == code);
            Assert.True(sell.PlayerSupplied, code);
            Assert.Equal(0.2 * sell.Price!.Avg, buy.Price!.Avg, 6);
        }
    }
}
