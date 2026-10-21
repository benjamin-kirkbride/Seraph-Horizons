using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SeraphHorizons.Mod.Trading.Schematics.Core;
using SeraphHorizons.Mod.Trading.Standing.Core;

namespace SeraphHorizons.Mod.Tests.Trading.Schematics;

/// <summary>
/// A schematic's handbook section (<see cref="SchematicHandbook"/>), from the shipped table, tiers
/// and English lang file: who sells it, for how much, from which standing tier, and that traders do
/// not buy schematics; nothing for anything else, which keeps its value line.
/// </summary>
public class SchematicHandbookTests
{
    private static string Shipped(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name));

    private static readonly Lazy<SchematicTable> Table = new(() => SchematicTable.Parse(Shipped("schematic-gates.json")));

    private static readonly Lazy<StandingRules> Rules = new(() => JsonSerializer.Deserialize<StandingRules>(Shipped("standing-tiers.json"),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!);

    private static readonly Lazy<JsonObject> En = new(() => JsonNode.Parse(Shipped("lang-en.json"),
        documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject());

    /// <summary>Lang.Get over the shipped English file; a missing key comes back as itself, as in the game.</summary>
    private static string Lang(string key, object[] args)
    {
        string bare = key.StartsWith(SchematicHandbook.Domain) ? key[SchematicHandbook.Domain.Length..] : key;
        return En.Value[bare] is { } v ? string.Format(CultureInfo.InvariantCulture, (string)v!, args) : key;
    }

    private static List<SchematicHandbookLine>? Lines(string code, params string[] off) =>
        SchematicHandbook.Lines(Table.Value, code, off.Contains, i => Rules.Value.Tier(i).Code, Lang);

    private static List<string> Texts(string code, params string[] off) => Lines(code, off)!.Select(l => l.Text).ToList();

    private const string Heading = "Sold by, from the trade window's Schematics tab:";
    private const string NoBuy = "Traders do not buy schematics.";

    [Fact]
    public void AMachineSchematicSaysWhoSellsItForHowMuchAndFromWhichTier() =>
        Assert.Equal([Heading, "Mechanic: 40 rusty gears, from standing Regular", NoBuy], Texts("seraphhorizons:schematic-rosser"));

    [Fact]
    public void OnlySellerLinesCarryTheGear() =>
        Assert.Equal([false, true, false], Lines("seraphhorizons:schematic-rosser")!.Select(l => l.Gear));

    [Fact]
    public void EverySellerGetsALineAtTheOneFlatPrice() =>
        Assert.Equal([Heading, "Curio dealer: 64 rusty gears, from standing Trusted", "Treasure hunter: 64 rusty gears, from standing Trusted", NoBuy],
            Texts("walkingstick:schematic-flintlock"));

    [Theory]
    [InlineData("game:schematic-glider", "Curio dealer: 75 rusty gears, from standing Trusted")]
    [InlineData("schematic-customtranslocator", "Curio dealer: 80 rusty gears, from standing Trusted")]
    [InlineData("betterruins:br-schematic-door", "Carpenter: 7 rusty gears, from standing Known")]
    [InlineData("cartwrightscaravan:cartschematics-carts", "Mechanic: 30 rusty gears, from standing Regular")]
    [InlineData("abyssaldepths:ad-schematic-divinggear", "Curio dealer: 90 rusty gears, from standing Trusted")]
    [InlineData("seraphhorizons:schematic-eidolon", "Curio dealer: 90 rusty gears, from standing Trusted")]
    public void OtherModsSchematicsAreCoveredToo(string code, string first) =>
        Assert.Equal(first, Texts(code)[1]);

    [Fact]
    public void EverySaleInTheTableHasASection()
    {
        foreach (var sale in Table.Value.Sales)
        {
            var lines = Lines(sale.Code)!;
            Assert.Equal(sale.Sellers.Count + 2, lines.Count);
            Assert.DoesNotContain(lines, l => l.Text.Contains("trading-") || l.Text.Contains('{'));
        }
    }

    [Fact]
    public void WithStandingOffNoTierIsNamed() =>
        Assert.Equal([Heading, "Mechanic: 40 rusty gears, with no standing needed", NoBuy],
            Texts("seraphhorizons:schematic-rosser", SchematicHandbook.StandingSwitch));

    [Theory]
    [InlineData("seraphhorizons:schematic-rosser", "Rosser")]
    [InlineData("seraphhorizons:schematic-eidolon", "Eidolon")]
    [InlineData("seraphhorizons:schematic-windmill", SchematicCatalog.MachineSwitch)]
    public void AMachineSwitchedOffIsSoldByNobody(string code, string off) =>
        Assert.Equal([NoBuy], Texts(code, off));

    [Fact]
    public void AnotherModsSchematicIgnoresTheMachineSwitch() =>
        Assert.Equal(3, Texts("betterruins:br-schematic-door", SchematicCatalog.MachineSwitch).Count);

    [Theory]
    [InlineData("scrolled:rolled-schematic-glider")]
    [InlineData("scrolled:br-rolled-schematic-door")]
    [InlineData("cartwrightscaravan:cartschematics-canopies")]
    public void ARolledCopyOrAnUnsoldSchematicOnlySaysTradersDoNotBuyIt(string code) =>
        Assert.Equal([NoBuy], Texts(code));

    [Theory]
    [InlineData("game:gear-rusty")]
    [InlineData("game:paper-parchment")]
    [InlineData("seraphhorizons:rosser-frame-oak")]
    public void AnythingElseKeepsItsValueLine(string code) => Assert.Null(Lines(code));
}
