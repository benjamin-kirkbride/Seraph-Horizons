using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SeraphHorizons.Mod.Springs.Core;

namespace SeraphHorizons.Tests.Springs;

/// <summary>Springs (Springs/Core): the block codes, the host rock, how a spring pours and counts as
/// a source, the rate cap and the settings; and the shipped block type, held to the codes.</summary>
public class SpringsTests
{
    // ---- codes ----

    [Theory]
    [InlineData(SpringKind.Normal, "granite", "waterspring-normal-granite")]
    [InlineData(SpringKind.Rapid, "dolostone", "waterspring-rapid-dolostone")]
    public void Codes_round_trip(SpringKind kind, string rock, string path)
    {
        Assert.Equal(path, SpringCodes.BlockPath(kind, rock));
        Assert.Equal((kind, rock), SpringCodes.Parse(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("waterspring")]
    [InlineData("waterspring-normal")]
    [InlineData("waterspring-normal-")]
    [InlineData("waterspring-slow-granite")]
    [InlineData("rock-granite")]
    [InlineData("spring-steel")]
    public void Anything_else_is_no_spring(string? path) => Assert.Null(SpringCodes.Parse(path));

    [Fact]
    public void A_spring_pours_and_reverts_to_the_water_it_replaced()
    {
        Assert.Equal("water-still-7", SpringCodes.SourcePath(SpringKind.Normal));
        Assert.Equal("rapidwater-still-7", SpringCodes.SourcePath(SpringKind.Rapid));
        Assert.Equal("water-d-6", SpringCodes.FallingPath(SpringKind.Normal));
        Assert.Equal("rapidwater-d-6", SpringCodes.FallingPath(SpringKind.Rapid));
        Assert.True(SpringCodes.IsLiquidOf(SpringKind.Normal, "water-n-6"));
        Assert.False(SpringCodes.IsLiquidOf(SpringKind.Normal, "rapidwater-n-6"));
        Assert.True(SpringCodes.IsLiquidOf(SpringKind.Rapid, "rapidwater-still-7"));
        Assert.False(SpringCodes.IsLiquidOf(SpringKind.Rapid, "water-still-7"));
        Assert.False(SpringCodes.IsLiquidOf(SpringKind.Normal, "saltwater-still-7"));
    }

    [Theory]
    [InlineData("game", "water-still-7", SpringKind.Normal)]
    [InlineData("game", "rapidwater-still-7", SpringKind.Rapid)]
    [InlineData("game", "lava-still-7", null)]
    [InlineData("game", "saltwater-still-7", null)]
    [InlineData("hydrateordiedrate", "wellwater-fresh-clean-natural-still-7", null)]
    [InlineData("other", "water-still-7", null)]
    public void Only_water_and_rapids_rivulets_become_springs(string domain, string path, SpringKind? kind) =>
        Assert.Equal(kind, SpringCodes.KindOfFluid(domain, path));

    [Fact]
    public void A_broken_spring_drops_as_its_rock() =>
        Assert.Equal("game:rock-chalk", SpringCodes.HostRockCode("chalk"));

    // ---- host rock ----

    [Theory]
    [InlineData("game", "rock-granite", null, "granite")]
    [InlineData("game", "ore-poor-nativecopper-limestone", "limestone", "limestone")]
    [InlineData("game", "crackedrock-slate", "slate", "slate")]
    [InlineData("game", "soil-low-none", null, null)]
    [InlineData("game", "air", null, null)]
    [InlineData("othermod", "rock-granite", null, null)]
    [InlineData("game", "rock-", null, null)]
    public void A_neighbour_names_its_rock(string domain, string path, string? variant, string? rock) =>
        Assert.Equal(rock, HostRock.RockOf(domain, path, variant));

    private static readonly HashSet<string> Known = ["granite", "basalt", "limestone", "chalk"];

    [Fact]
    public void The_most_common_known_rock_round_the_cell_wins()
    {
        Assert.Equal("basalt", HostRock.Choose(["granite", "basalt", "basalt", null, "limestone", "basalt"], Known.Contains));
        // A tie: the first seen.
        Assert.Equal("limestone", HostRock.Choose(["limestone", "chalk", "chalk", "limestone", null, null], Known.Contains));
        // Rocks no spring exists for are skipped.
        Assert.Equal("chalk", HostRock.Choose(["marsrock", "marsrock", "marsrock", "chalk", null, null], Known.Contains));
    }

    [Fact]
    public void With_no_known_rock_it_falls_back()
    {
        Assert.Equal(HostRock.Fallback, HostRock.Choose([null, null, "marsrock", null, null, null], Known.Contains));
        Assert.Equal("andesite", HostRock.Choose([], Known.Contains, "andesite"));
    }

    // ---- pouring ----

    [Fact]
    public void It_pours_down_when_it_can_and_then_nowhere_else()
    {
        var faces = SpringPour.Faces(belowOpen: true, canSpreadDown: true, belowIsSource: false, _ => true);
        Assert.Equal([SpringPour.Down], faces);
    }

    [Fact]
    public void Over_open_water_it_cannot_pour_into_it_pours_nothing_unless_that_is_a_source()
    {
        Assert.Empty(SpringPour.Faces(true, false, false, _ => true));
        Assert.Equal(SpringPour.Horizontals, SpringPour.Faces(true, false, true, _ => true));
    }

    [Fact]
    public void On_solid_ground_it_pours_out_of_every_open_side()
    {
        var faces = SpringPour.Faces(false, false, false, f => f is SpringPour.East or SpringPour.West);
        Assert.Equal([SpringPour.East, SpringPour.West], faces);
        Assert.Empty(SpringPour.Faces(false, false, false, _ => false));
    }

    [Theory]
    // Above: feeds outright, as the same liquid above does in the game.
    [InlineData(0, false, 6, true, false, 7, true)]
    [InlineData(3, false, 2, true, false, 7, true)]
    // Beside: level 7, feeding a level-6 cell only.
    [InlineData(0, false, 6, false, true, 7, true)]
    [InlineData(0, false, 5, false, true, 7, false)]
    [InlineData(5, true, 4, false, true, 7, true)]
    // No spring: the game's answer stands.
    [InlineData(4, true, 3, false, false, 4, true)]
    [InlineData(0, false, 6, false, false, 0, false)]
    public void A_pouring_spring_counts_as_a_source(int level, bool feeds, int ourLevel, bool above, bool beside, int expectLevel, bool expectFeeds) =>
        Assert.Equal((expectLevel, expectFeeds), SpringPour.Feed(level, feeds, ourLevel, above, beside));

    // ---- the rate cap ----

    [Fact]
    public void The_budget_is_the_rate_over_the_seconds_gone_by_capped()
    {
        Assert.Equal(4f, SpringFlow.Budget(4f, 1f), 3);
        Assert.Equal(5f, SpringFlow.Budget(10f, 0.5f), 3);
        Assert.Equal(4f * SpringFlow.MaxCatchUpSeconds, SpringFlow.Budget(4f, 60f), 3);
        Assert.Equal(0f, SpringFlow.Budget(4f, 0f));
        Assert.Equal(0f, SpringFlow.Budget(4f, -1f));
        Assert.Equal(0f, SpringFlow.Budget(0f, 1f));
        Assert.Equal(0f, SpringFlow.Budget(float.NaN, 1f));
    }

    [Fact]
    public void One_network_takes_up_to_the_budget()
    {
        float given = 0;
        Assert.Equal(4f, SpringFlow.Share(4f, 1, (_, l) => given += l), 3);
        Assert.Equal(4f, given, 3);
        // Nearly full: it takes what it can, the rest is lost.
        Assert.Equal(1.5f, SpringFlow.Share(4f, 1, (_, l) => Math.Min(l, 1.5f)), 3);
    }

    [Fact]
    public void Networks_share_evenly_and_a_full_one_leaves_its_part_to_the_others()
    {
        var got = new float[3];
        float[] room = [0.5f, 100f, 100f];
        float taken = SpringFlow.Share(9f, 3, (i, l) =>
        {
            float take = Math.Min(l, room[i]);
            room[i] -= take;
            got[i] += take;
            return take;
        });
        Assert.Equal(9f, taken, 3);
        Assert.Equal(0.5f, got[0], 3);
        Assert.Equal(4.25f, got[1], 3);
        Assert.Equal(4.25f, got[2], 3);
    }

    [Fact]
    public void Nothing_is_given_with_no_network_or_no_budget()
    {
        Assert.Equal(0f, SpringFlow.Share(4f, 0, (_, l) => l));
        Assert.Equal(0f, SpringFlow.Share(0f, 2, (_, l) => l));
        // A network that takes more than offered is held to the offer.
        Assert.Equal(4f, SpringFlow.Share(4f, 1, (_, l) => l * 10), 3);
    }

    [Theory]
    [InlineData(false, false, TapChange.None)]
    [InlineData(true, true, TapChange.None)]
    [InlineData(false, true, TapChange.Tapped)]
    [InlineData(true, false, TapChange.Untapped)]
    public void Tapping_and_untapping(bool was, bool now, TapChange change) =>
        Assert.Equal(change, SpringFlow.Change(was, now));

    // ---- settings ----

    [Fact]
    public void A_rapids_spring_gives_two_to_three_times_a_spring()
    {
        var s = SpringsConfig.Defaults;
        Assert.Equal(s.NormalLitresPerSecond, s.LitresPerSecond(SpringKind.Normal));
        Assert.Equal(s.RapidLitresPerSecond, s.LitresPerSecond(SpringKind.Rapid));
        float ratio = s.RapidLitresPerSecond / s.NormalLitresPerSecond;
        Assert.InRange(ratio, 2f, 3f);
        // About a fluid intake under a waterwheel pump (ppex: 20 L per unit of speed, ~0.18 on a wheel).
        Assert.InRange(s.NormalLitresPerSecond, 2f, 6f);
    }

    [Fact]
    public void Settings_out_of_range_fall_back_to_the_defaults()
    {
        var s = new SpringsConfig { NormalLitresPerSecond = -1, RapidLitresPerSecond = float.PositiveInfinity };
        var fixes = s.Sanitise();
        Assert.Equal(2, fixes.Count);
        Assert.Equal(SpringsConfig.Defaults.NormalLitresPerSecond, s.NormalLitresPerSecond);
        Assert.Equal(SpringsConfig.Defaults.RapidLitresPerSecond, s.RapidLitresPerSecond);
        var ok = new SpringsConfig { NormalLitresPerSecond = 1.5f, RapidLitresPerSecond = 1000f };
        Assert.Empty(ok.Sanitise());
        Assert.Equal("1.5", SpringsConfig.Format(ok.NormalLitresPerSecond));
    }

    // ---- the shipped block type ----

    private static JsonObject BlockType()
    {
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "waterspring.json"));
        // The game's lenient JSON: comments, unquoted keys.
        text = Regex.Replace(text, @"^\s*//.*$", "", RegexOptions.Multiline);
        text = Regex.Replace(text, @"(?<=[{,]\s*)([A-Za-z_][A-Za-z0-9_]*)(?=\s*:)", "\"$1\"");
        return JsonNode.Parse(text, documentOptions: new() { AllowTrailingCommas = true })!.AsObject();
    }

    [Fact]
    public void The_block_type_has_the_codes_the_code_reads()
    {
        var type = BlockType();
        Assert.Equal(SpringCodes.BlockBase, (string?)type["code"]);
        var groups = type["variantgroups"]!.AsArray();
        Assert.Equal("kind", (string?)groups[0]!["code"]);
        Assert.Equal([SpringCodes.NormalName, SpringCodes.RapidName], groups[0]!["states"]!.AsArray().Select(s => (string?)s));
        Assert.Equal("rock", (string?)groups[1]!["code"]);
        // The game's rock's own list, GeologyAdditions' rocks included.
        Assert.Equal("block/rockwithdeposit", (string?)groups[1]!["loadFromProperties"]);
        Assert.Equal("seraphhorizons.WaterSpring", (string?)type["class"]);
        Assert.Equal("seraphhorizons.WaterSpring", (string?)type["entityClass"]);
        var fill = type["attributes"]!["waterTightContainerProps"]!;
        Assert.True((bool)fill["containable"]!);
        Assert.Equal("hydrateordiedrate:waterportion-fresh-well-clean", (string?)fill["whenFilled"]!["stack"]!["code"]);
        Assert.Empty(type["drops"]!.AsArray());
    }
}
