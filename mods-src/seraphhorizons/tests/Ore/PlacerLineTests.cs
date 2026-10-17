using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Tests.Ore;

/// <summary>
/// Placer on the line (#701) and the concentrators' wash (#734) on the shipped config: rich gravel and bog
/// iron chain through the concentrators like any ore, the gravel scaled from the pan's yield by what each
/// pass caught (the sluice alone 1.2×, sluice and jig 1.8×), its by-products once; and the shaftless
/// machines' clock (<see cref="WorkHours"/>).
/// </summary>
public class PlacerLineTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly OreRecovery R = new(JsonSerializer.Deserialize<OreProcessingConfig>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ore-processing.json")), Options)!);

    // A pan table: gold at 0.1 a pan, copper 0.05, and a stone at 0.3.
    private static readonly (string, double, bool)[] Table =
    [
        ("game:nugget-nativegold", 0.1, true),
        ("game:nugget-nativecopper", 0.05, true),
        ("game:stone-granite", 0.3, false),
    ];

    private static double Of(WashResult r, string code) => r.Products.Where(p => p.Code == code).Sum(p => p.Units);

    [Theory]
    [InlineData("game:richgravel-granite", PlacerKind.Gravel, "granite")]
    [InlineData("game:stone-bogiron", PlacerKind.BogIron, null)]
    public void Placer_feed_is_read_from_its_code(string code, PlacerKind kind, string? rock)
    {
        var feed = PlacerFeed.Parse(code);
        Assert.NotNull(feed);
        Assert.Equal(kind, feed!.Kind);
        Assert.Equal(rock, feed.Rock);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("game:gravel-granite")]
    [InlineData("game:richgravel-")]
    [InlineData("game:richgravel-granite-land-3")]
    [InlineData("seraphhorizons:crushedore-hematite-rich")]
    public void Other_codes_are_no_placer(string? code) => Assert.Null(PlacerFeed.Parse(code));

    [Fact]
    public void Placer_has_nothing_locked_and_every_lift()
    {
        var gravel = PlacerLine.Gravel("granite");
        Assert.Equal(1, R.LockFactor(R.Ore(gravel.Ore), gravel.Grade, gravel.State));
        Assert.Equal(0, R.LockedOf(gravel));
        Assert.True(PlacerLine.IsPlacer(gravel));
        Assert.Equal(PlacerLine.BogIronUnits, PlacerLine.BogIron().Units);
    }

    [Fact]
    public void The_sluice_alone_gives_the_pans_metal_at_its_figure_over_the_pans()
    {
        var r = ConcentratorWash.Wash(R, OreMachine.Sluice, PlacerLine.Gravel("granite"), () => Table);
        double scale = 0.55 / 0.45;
        Assert.Equal(8 * 0.1 * scale, Of(r, "game:nugget-nativegold"), 9);
        Assert.Equal(8 * 0.05 * scale, Of(r, "game:nugget-nativecopper"), 9);
        // by-products at the pan's own rate, from the first concentrator
        Assert.Equal(8 * 0.3, Of(r, "game:stone-granite"), 9);
        Assert.All(r.Products, p => Assert.Equal(1, p.UnitsPerItem));
        // the tailings carry the rest, worked by the sluice
        Assert.Equal(0.45, r.Tailings.Units, 9);
        Assert.Equal(WorkedBy.Sluice, r.Tailings.Worked);
        Assert.Equal("granite", r.Tailings.Rock);
    }

    [Fact]
    public void The_jig_on_the_sluices_tailings_brings_the_line_to_82_percent_and_no_more_stones()
    {
        var sluice = ConcentratorWash.Wash(R, OreMachine.Sluice, PlacerLine.Gravel("andesite"), () => Table);
        var jig = ConcentratorWash.Wash(R, OreMachine.Jig, sluice.Tailings, () => Table);
        double gold = Of(sluice, "game:nugget-nativegold") + Of(jig, "game:nugget-nativegold");
        Assert.Equal(8 * 0.1 * (1 - 0.45 * 0.40) / 0.45, gold, 9);
        Assert.Equal(1.8, gold / (8 * 0.1), 1);
        Assert.Equal(0, Of(jig, "game:stone-granite"));
        // a second sluice on the sluice's tailings catches nothing (#765: not from its own kind's)
        var again = ConcentratorWash.Wash(R, OreMachine.Sluice, sluice.Tailings, () => Table);
        Assert.Empty(again.Products);
        Assert.Equal(sluice.Tailings.Units, again.Tailings.Units, 9);
    }

    [Fact]
    public void Bog_iron_washes_to_limonite_concentrate()
    {
        var r = ConcentratorWash.Wash(R, OreMachine.Sluice, PlacerLine.BogIron());
        var p = Assert.Single(r.Products);
        Assert.Equal("seraphhorizons:concentrate-limonite", p.Code);
        Assert.Equal(14 * 0.55, p.Units, 9);
        Assert.Equal(R.ConcentrateUnits, p.UnitsPerItem);
        var jig = ConcentratorWash.Wash(R, OreMachine.Jig, r.Tailings);
        Assert.Equal(14 * (1 - 0.45 * 0.40), p.Units + jig.Products.Sum(x => x.Units), 9);
    }

    [Fact]
    public void Crushed_ore_washes_to_its_concentrate_with_its_locked_share()
    {
        // unclassified crushed hematite: × 0.85 catchable, of which the sluice catches 55 %
        var feed = OreStream.FromRock("hematite", "rich", OreForm.Crushed);
        var r = ConcentratorWash.Wash(R, OreMachine.Sluice, feed);
        var p = Assert.Single(r.Products);
        Assert.Equal("seraphhorizons:concentrate-hematite", p.Code);
        Assert.Equal(feed.Units * 0.85 * 0.55, p.Units, 9);
        Assert.Equal(feed.Units - p.Units, r.Tailings.Units, 9);
        Assert.NotNull(r.Tailings.Locked);
    }

    [Fact]
    public void Gravel_with_no_table_gives_nothing_but_still_goes_on()
    {
        var r = ConcentratorWash.Wash(R, OreMachine.Sluice, PlacerLine.Gravel("granite"));
        Assert.Empty(r.Products);
        Assert.Equal(0.45, r.Tailings.Units, 9);
    }

    [Fact]
    public void Chance_per_pan_of_a_single_drop_is_its_chance()
    {
        Assert.Equal(0.3, PlacerLine.ChancePerPan([0.3], 0), 12);
        // two drops at 1: whichever is rolled first wins, half each
        Assert.Equal(0.5, PlacerLine.ChancePerPan([1, 1], 0), 12);
    }

    [Fact]
    public void Work_hours_count_items_at_the_rate_and_carry_the_rest()
    {
        var (next, items) = WorkHours.None.Add(0.1, 16);
        Assert.Equal(1, items);
        Assert.Equal(0.6, next.Items, 9);
        (next, items) = next.Add(0.05, 16);
        Assert.Equal(1, items);
        Assert.Equal(0.4, next.Items, 9);
        Assert.Equal(0, next.Add(-1, 16).Items);
        Assert.Equal(0, next.Add(1, 0).Items);
        Assert.Equal(1, WorkHours.Restore(5).Items);
        Assert.Equal(0, WorkHours.Restore(double.NaN).Items);
    }
}
