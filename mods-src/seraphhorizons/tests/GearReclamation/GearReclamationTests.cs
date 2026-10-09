using System.Text.Json.Nodes;
using SeraphHorizons.Mod.GearReclamation.Core;

namespace SeraphHorizons.Mod.Tests.GearReclamation;

public class GearLotteryTests
{
    /// <summary>A fixed sequence of draws, repeated.</summary>
    private static Func<double> Draws(params double[] values)
    {
        int i = 0;
        return () => values[i++ % values.Length];
    }

    [Fact]
    public void Each_gear_is_one_draw_below_the_chance_sound_the_rest_bits()
    {
        var r = GearLottery.Roll(5, 0.1, 1, Draws(0.05, 0.5, 0.0999, 0.1, 0.99));
        Assert.Equal(new LotteryResult(5, 2, 3), r);
        Assert.Equal(3, r.Failed);
    }

    [Fact]
    public void Bits_scale_with_the_failures()
    {
        Assert.Equal(new LotteryResult(4, 0, 12), GearLottery.Roll(4, 0.1, 3, Draws(0.5)));
        Assert.Equal(new LotteryResult(4, 0, 0), GearLottery.Roll(4, 0.1, 0, Draws(0.5)));
        // negative settings never take anything away
        Assert.Equal(new LotteryResult(4, 0, 0), GearLottery.Roll(4, 0.1, -2, Draws(0.5)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 10)]
    [InlineData(-1, 0)]
    [InlineData(2, 10)]
    public void The_chance_is_clamped(double chance, int expectedSound) =>
        Assert.Equal(expectedSound, GearLottery.Roll(10, chance, 1, Draws(0.0, 0.5, 0.999)).Sound);

    [Fact]
    public void Nothing_in_nothing_out()
    {
        int calls = 0;
        Assert.Equal(new LotteryResult(0, 0, 0), GearLottery.Roll(0, 0.1, 1, () => { calls++; return 0; }));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void The_same_draws_give_the_same_result()
    {
        var a = new Random(1234);
        var b = new Random(1234);
        Assert.Equal(GearLottery.Roll(500, 0.1, 1, a.NextDouble), GearLottery.Roll(500, 0.1, 1, b.NextDouble));
    }

    [Fact]
    public void One_in_ten_over_a_large_sample()
    {
        const int gears = 100_000;
        var r = GearLottery.Roll(gears, 0.1, 1, new Random(42).NextDouble);
        var (sound, bits) = GearLottery.Expected(gears, 0.1, 1);
        Assert.Equal(10_000, sound, 6);
        Assert.Equal(90_000, bits, 6);
        double sd = GearLottery.SoundDeviation(gears, 0.1);
        Assert.Equal(Math.Sqrt(9000), sd, 6);
        Assert.InRange(r.Sound, sound - 4 * sd, sound + 4 * sd);
        Assert.Equal(gears - r.Sound, r.Bits);
    }

    [Fact]
    public void Expected_is_clamped_like_the_roll()
    {
        Assert.Equal((0d, 0d), GearLottery.Expected(0, 0.1, 1));
        Assert.Equal((10d, 0d), GearLottery.Expected(10, 3, 1));
        Assert.Equal((0d, 20d), GearLottery.Expected(10, -1, 2));
    }

    [Theory]
    [InlineData(0, 64, new int[0])]
    [InlineData(64, 64, new[] { 64 })]
    [InlineData(130, 64, new[] { 64, 64, 2 })]
    [InlineData(3, 0, new[] { 1, 1, 1 })]
    public void Stacks_split_at_the_max(int count, int max, int[] expected) =>
        Assert.Equal(expected, GearLottery.Stacks(count, max));
}

public class GearReclamationConfigTests
{
    [Fact]
    public void Defaults_are_in_range()
    {
        var c = new GearReclamationConfig();
        Assert.Empty(c.Sanitise());
        Assert.Equal(0.1, c.UsableGearChance);
        Assert.Equal(1, c.BitsPerFailedGear);
    }

    [Fact]
    public void Out_of_range_values_fall_back_with_a_line_each()
    {
        var c = new GearReclamationConfig { UsableGearChance = 1.5, BitsPerFailedGear = -1 };
        Assert.Equal(2, c.Sanitise().Count);
        Assert.Equal(0.1, c.UsableGearChance);
        Assert.Equal(1, c.BitsPerFailedGear);
        c = new GearReclamationConfig { UsableGearChance = double.NaN, BitsPerFailedGear = 20 };
        Assert.Contains("UsableGearChance", Assert.Single(c.Sanitise()));
        Assert.Equal(20, c.BitsPerFailedGear);
    }

    // Flash rust is gone with the stainless rework: no setting is left for it.
    [Fact]
    public void There_is_no_flash_rust_setting()
    {
        Assert.Equal(["BitsPerFailedGear", "UsableGearChance"],
            typeof(GearReclamationConfig).GetProperties().Select(p => p.Name).Order());
    }
}

public class OptionalIngredientsTests
{
    private static bool Loaded(string domain) => domain is "game" or "oils" or "seraphhorizons";

    [Theory]
    [InlineData("gear-rusty", "game")]
    [InlineData("game:gear-rusty", "game")]
    [InlineData("em:washingsodaportion", "em")]
    [InlineData("Oils:lyeportion", "oils")]
    public void Domain_of_a_code(string code, string domain) => Assert.Equal(domain, OptionalIngredients.Domain(code));

    [Fact]
    public void Alternatives_from_missing_mods_are_dropped()
    {
        Assert.Equal(["oils:lyeportion"],
            OptionalIngredients.Keep(["oils:lyeportion", "em:washingsodaportion", "em:causticsodaportion"], Loaded));
        Assert.Empty(OptionalIngredients.Keep(["em:washingsodaportion"], Loaded));
        Assert.True(OptionalIngredients.Available("game:limewaterportion", Loaded));
        Assert.False(OptionalIngredients.Available("expandedfoods:lard", Loaded));
    }
}

/// <summary>The shipped item types, recipes and lang entries agree with the Core codes and the
/// default settings.</summary>
public class GearReclamationAssetTests
{
    private static string Shipped(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name));

    private static JsonObject Json(string name) => JsonNode.Parse(Shipped(name),
        documentOptions: new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject();

    private static JsonArray JsonArr(string name) => JsonNode.Parse(Shipped(name),
        documentOptions: new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsArray();

    private static readonly string[] Types = ["stainless", "degreased", "pickled", "passivated", "neutralized"];

    [Fact]
    public void The_gear_item_type_makes_the_contract_codes()
    {
        var gear = Json("gear-itemtype.json");
        Assert.Equal("gear", (string?)gear["code"]);
        var states = gear["variantgroups"]![0]!["states"]!.AsArray().Select(s => (string)s!).ToArray();
        Assert.Equal(Types, states);
        Assert.Equal(
            new[] { GearCodes.Stainless, GearCodes.Degreased, GearCodes.Pickled, GearCodes.Passivated, GearCodes.Neutralized },
            states.Select(s => $"{GearCodes.Domain}:gear-{s}"));
        Assert.Equal("largegear", (string?)Json("largegear-itemtype.json")["code"]);
        Assert.Equal(GearCodes.LargeStainless, $"{GearCodes.Domain}:largegear-" + (string?)Json("largegear-itemtype.json")["variantgroups"]![0]!["states"]![0]);
    }

    [Fact]
    public void No_gear_rusts()
    {
        var gear = Json("gear-itemtype.json");
        Assert.Null(gear["transitionablePropsByType"]);
        Assert.Null(gear["transitionableProps"]);
        Assert.Null(gear["attributesByType"]);
        Assert.Equal("game:block/metal/ingot/stainlesssteel", (string?)gear["texturesByType"]!["*-stainless"]!["rusty-iron"]!["base"]);
    }

    [Fact]
    public void The_neutralizing_barrel_takes_passivated_gears()
    {
        var r = Assert.Single(JsonArr("gear-neutralize-recipes.json"));
        Assert.Equal(GearCodes.Passivated, (string?)r!["ingredients"]![1]!["code"]);
        Assert.Equal(GearCodes.Neutralized, (string?)r["output"]!["code"]);
    }

    [Fact]
    public void Every_gear_has_a_name_and_a_description()
    {
        var lang = Json("lang-en.json");
        foreach (var code in Types.Select(t => "gear-" + t).Append("largegear-stainless"))
        {
            Assert.False(string.IsNullOrWhiteSpace((string?)lang["item-" + code]), $"item-{code}");
            Assert.False(string.IsNullOrWhiteSpace((string?)lang["itemdesc-" + code]), $"itemdesc-{code}");
        }
        Assert.Contains("One in ten comes out sound.", (string?)lang["gearreclamation-rusty-text"]);
        Assert.Contains("stainless steel", (string?)lang["game:itemdesc-gear-rusty"]);
        foreach (var key in new[] { "item-gear-oiled", "item-gear-steel-bare", "picklingtub-info-rusted" })
            Assert.Null(lang[key]);
    }

    [Fact]
    public void Degreasing_takes_a_quarter_litre_a_gear_in_one_to_three_slots()
    {
        var recipes = JsonArr("gear-degrease-recipes.json");
        Assert.Equal(3, recipes.Count);
        for (int i = 0; i < 3; i++)
        {
            var r = recipes[i]!;
            int slots = i + 1;
            Assert.Equal($"seraphhorizons-gear-degrease-{slots}", (string?)r["code"]);
            var gears = r["ingredients"]![0]!;
            Assert.Equal(GearCodes.Rusty, (string?)gears["validStacks"]![0]!["code"]);
            Assert.Equal(slots, (int)gears["minQuantity"]!);
            Assert.Equal(slots, (int)gears["maxQuantity"]!);
            var alkali = r["ingredients"]![1]!;
            Assert.Equal(0.25 * slots, (double)alkali["portionSizeLitres"]!, 6);
            Assert.Equal(GearCodes.Degreased, (string?)r["cooksInto"]!["code"]);
            Assert.Equal(slots, (int)r["cooksInto"]!["quantity"]!);
        }
    }
}
