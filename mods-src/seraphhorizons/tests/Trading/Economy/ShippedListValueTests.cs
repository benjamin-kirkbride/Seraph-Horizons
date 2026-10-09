using System.Text.Json;
using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Economy.Core;
using SeraphHorizons.Mod.Trading.Values.Core;

namespace SeraphHorizons.Mod.Tests.Trading.Economy;

/// <summary>
/// The thirteen shipped lists priced from the shipped value table (2026-10-08): a listed good is
/// its value × a sell or a buy factor (<see cref="Pricing.ListBase"/>, <see cref="ListPriceRules"/>), so every entry must have a
/// value the mod's lookup finds (<see cref="ItemValues.Lookup"/>: direct, or its variant family's),
/// unless it overrides its price, and an override must say why. <c>tools/item-values check</c>
/// holds the same against a fresh export in CI.
/// </summary>
public class ShippedListValueTests
{
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

    private static IEnumerable<(string Type, string Side, TradeEntry Entry)> Entries() =>
        Lists.Value.SelectMany(kv => new[] { ("selling", kv.Value.Selling), ("buying", kv.Value.Buying) }.SelectMany(s =>
            s.Item2.Core.Concat(s.Item2.Rotating.List).Concat(s.Item2.Regional.Values.SelectMany(r => r.Core.Concat(r.Rotating)))
                .Select(e => (kv.Key, s.Item1, e))));

    [Fact]
    public void AllThirteenListsAreRead()
    {
        Assert.Equal(13, Lists.Value.Count);
        Assert.True(Entries().Count() > 1500, $"only {Entries().Count()} entries");
    }

    [Fact]
    public void EveryEntryWithoutAnOverrideHasAValue()
    {
        var missing = new List<string>();
        int valued = 0;
        foreach (var (type, side, e) in Entries())
        {
            if (e.Kind != null || e.Price != null) continue;
            valued++;
            var lookup = Table.Value.Lookup(BuyerIndex.FullCode(e.Code));
            if (lookup.Source == ValueSource.Missing || lookup.Value <= 0)
                missing.Add($"{type} {side} {e.Key}");
        }
        Assert.True(valued > 1100, $"only {valued} entries priced from values");
        Assert.True(missing.Count == 0, "trade list entries with no value (add a raw value or an override in tools/item-values and "
            + "rebuild the table, or give the entry a price with a priceReason):\n" + string.Join("\n", missing));
    }

    [Fact]
    public void EveryPriceOverrideSaysWhy()
    {
        var bare = Entries().Where(t => t.Entry.Price != null && string.IsNullOrWhiteSpace(t.Entry.PriceReason))
            .Select(t => $"{t.Type} {t.Side} {t.Entry.Key}").ToList();
        Assert.True(bare.Count == 0, "a price without a priceReason:\n" + string.Join("\n", bare));
        Assert.All(Entries().Where(t => t.Entry.Price != null), t => Assert.True(t.Entry.Price > 0, t.Entry.Key));
    }

    [Fact]
    public void MapsLeadsAndSchematicsAreTheOverridesTheyAlwaysWere()
    {
        // The special entries are priced by the maps system; schematics have no value by rule.
        Assert.All(Entries().Where(t => t.Entry.Kind != null), t => Assert.NotNull(t.Entry.Price));
        var schematics = Entries().Where(t => t.Entry.Code.Contains("schematic")).ToList();
        Assert.NotEmpty(schematics);
        Assert.All(schematics, t => Assert.True(t.Entry.Price != null, $"{t.Type} {t.Entry.Key} has no price override"));
    }

    [Fact]
    public void ListedGoodsArePricedFromTheirValueByTheShippedRules()
    {
        var rules = ListPriceRules.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "list-prices.json")));
        foreach (var code in new[] { "game:ingot-iron", "seraphhorizons:gear-steel" })
        {
            var entry = Entries().First(t => BuyerIndex.FullCode(t.Entry.Code) == code && t.Entry.Price is null).Entry;
            double value = Table.Value.ValueOf(code);
            Assert.True(value > 0, code);
            Assert.Equal(rules.Sell * value * entry.StackSize, Pricing.ListBase(entry, value, traderBuys: false, roll: 1, rules)!.Value, 6);
            Assert.Equal(rules.Buy * value * entry.StackSize, Pricing.ListBase(entry, value, traderBuys: true, roll: 1, rules)!.Value, 6);
        }
    }
}
