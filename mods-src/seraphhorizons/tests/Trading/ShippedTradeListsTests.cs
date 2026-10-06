using System.Text.Json;
using System.Text.Json.Nodes;
using SeraphHorizons.Mod.Trading.Core;

namespace SeraphHorizons.Mod.Tests.Trading;

/// <summary>
/// The eleven shipped lists (assets/seraphhorizons/config/tradelists): they parse into the format,
/// have no problems, stock something in every region on both sides, keep their wallets in range, and
/// keep every curated entry (tests/Trading/fixtures/curation-sources.json: each vanilla and other-mod
/// entry, and the types that took it in) in the types that took it in. Whether the game resolves every
/// entry is checked in the pack (tests/PackTests/TradingCoreScenarios.cs).
/// </summary>
public class ShippedTradeListsTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly Lazy<Dictionary<string, TradeListDef>> Lists = new(() =>
        TraderTypes.All.ToDictionary(t => t, t =>
        {
            string path = Path.Combine(AppContext.BaseDirectory, "tradelists", $"trader-{t}.json");
            var def = JsonSerializer.Deserialize<TradeListDef>(File.ReadAllText(path), Options)!;
            foreach (var e in All(def))
                e.AttributesKey = e.Attributes is JsonElement { ValueKind: JsonValueKind.Object } a ? Canonical(a) : "";
            return def;
        }));

    private static string Canonical(JsonElement a) => JsonNode.Parse(a.GetRawText())!.ToJsonString();

    private static IEnumerable<TradeEntry> All(TradeListDef d) =>
        new[] { d.Selling, d.Buying }.SelectMany(s => s.Core.Concat(s.Rotating.List).Concat(s.Regional.Values.SelectMany(r => r.Core.Concat(r.Rotating))));

    private static IEnumerable<TradeEntry> Side(TradeListDef d, string side) =>
        (side == "sell" ? new[] { d.Selling } : new[] { d.Buying })
        .SelectMany(s => s.Core.Concat(s.Rotating.List).Concat(s.Regional.Values.SelectMany(r => r.Core.Concat(r.Rotating))));

    [Fact]
    public void EveryListParsesAndHasNoProblems()
    {
        foreach (var (type, def) in Lists.Value)
        {
            Assert.Equal(type, def.Type);
            Assert.Empty(TradeListResolver.Problems(def));
        }
    }

    [Fact]
    public void EveryTypeStocksSomethingEverywhereWithoutPlayerSupply()
    {
        foreach (var (type, def) in Lists.Value)
            foreach (var region in Region.All)
            {
                var list = TradeListResolver.Resolve(def, region);
                // A fresh world's trader: player-supplied goods are not on the shelf.
                var shelf = RestockPlanner.Plan(list.Selling, Enumerable.Repeat(new SlotState(null, false), 16).ToList(), 1.1, new Random(1), _ => 0);
                Assert.True(shelf.Count(p => !p.IsEmpty) >= 4, $"{type} sells only {shelf.Count(p => !p.IsEmpty)} things in {region}");
                Assert.True(list.Buying.Core.Count > 0, $"{type} has no buying core in {region}");
                Assert.True(list.Buying.Core.Count + list.Buying.MaxRotating >= 6, $"{type} buys few things in {region}");
            }
    }

    [Fact]
    public void WalletsAreBiggerThanVanillasAndGrowWithStanding()
    {
        foreach (var (type, def) in Lists.Value)
        {
            Assert.InRange(def.Wallet[0].Avg, 60, 150);
            Assert.True(def.Wallet.Count >= 2, type);
            for (int i = 1; i < def.Wallet.Count; i++)
                Assert.True(def.Wallet[i].Avg > def.Wallet[i - 1].Avg, $"{type} tier {i}");
            Assert.True(def.CampWeight > 0, type);
        }
    }

    [Fact]
    public void MetalGlassFiredGoodsLeatherAndMachinePartsAreNeverSoldFromStock()
    {
        string[] mustBeSupplied = ["game:ingot-", "game:metalplate-", "game:glass-", "game:leather-", "game:storagevessel-", "game:anvil-", "game:lantern-", "ppex:", "mpegearbox:"];
        foreach (var (type, def) in Lists.Value)
            foreach (var e in Side(def, "sell"))
            {
                string code = e.Key.Split(':', 2)[1];
                if (mustBeSupplied.Any(code.StartsWith))
                    Assert.True(e.PlayerSupplied, $"{type} sells {e.Key} without player supply");
            }
    }

    [Fact]
    public void EveryCuratedEntryIsInTheTypesThatTookItIn()
    {
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "curation-sources.json")))!;
        var missing = new List<string>();
        int homed = 0;
        foreach (var row in fixture["entries"]!.AsArray())
        {
            string code = (string)row!["code"]!;
            string? attrs = row["attributes"] is JsonObject a ? a.ToJsonString() : null;
            foreach (var home in row["homes"]!.AsArray().Select(h => (string)h!))
            {
                var parts = home.Split(' ');
                if (parts.Length != 2 || !Lists.Value.TryGetValue(parts[0], out var def)) continue;
                homed++;
                bool found = Side(def, parts[1]).Any(e =>
                    e.Key.Split(':', 2)[1].StartsWith(code) && (attrs is null || e.AttributesKey == attrs || e.AttributesKey.Length == 0));
                if (!found) missing.Add($"{row["source"]} {row["list"]} {code} -> {home}");
            }
        }
        Assert.True(homed > 1000, $"only {homed} homes in the fixture");
        Assert.True(missing.Count == 0, "not in the list that took it in:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void EveryOtherModEntryOfThePackHasAHome()
    {
        var fixture = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "curation-sources.json")))!;
        var rows = fixture["entries"]!.AsArray();
        var sources = rows.Select(r => (string)r!["source"]!).Distinct().ToList();
        foreach (string mod in new[] { "vanilla", "ACulinaryArtillery", "oils-resoaped", "BetterRuins", "domesticanimaltrader", "craftablelocusts",
                     "tailors_delight", "wool", "signals", "butchering", "adventurers-walking-stick", "abyssaldepths", "translocatormap" })
            Assert.Contains(mod, sources);
        Assert.All(rows, r => Assert.NotEmpty(r!["homes"]!.AsArray()));
    }
}
