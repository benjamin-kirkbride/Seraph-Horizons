using System.Text.Json;
using SeraphHorizons.Mod.Arrastra.Core;
using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Tests.Arrastra;

/// <summary>The arrastra's batch (#732): loading, watering by bucket, grinding by shaft turns, drawing.</summary>
public class ArrastraChargeTests
{
    private const string Poor = "seraphhorizons:crushedore-hematite-poor";
    private const string Medium = "seraphhorizons:crushedore-galena-medium";
    private static readonly double Turn = 2 * Math.PI;

    private static readonly OreRecovery R = new(JsonSerializer.Deserialize<OreProcessingConfig>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ore-processing.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!);

    [Fact]
    public void ShippedFiguresAreACharge_Of2In7_5Hours()
    {
        var spec = R.Machine(OreMachine.Arrastra);
        Assert.Equal(2, spec.ChargeItems);
        Assert.Equal(10, spec.WaterPerItem);
        Assert.Equal(0.1, spec.Load);
        // 429.75 shaft turns a full charge at any speed; at 0.6 that is 7.5 in-game hours
        Assert.Equal(95.5 * 0.6 * 7.5, spec.TurnsPerCharge, 9);
        Assert.Equal(spec.TurnsPerCharge / 2, ArrastraCharge.TurnsFor(1, 2, spec.TurnsPerCharge), 9);
    }

    [Theory]
    [InlineData(Poor, "seraphhorizons:groundore-hematite-poor")]
    [InlineData("seraphhorizons:crushedore-nativecopper-bountiful", "seraphhorizons:groundore-nativecopper-bountiful")]
    [InlineData("seraphhorizons:groundore-hematite-poor", null)]
    [InlineData("game:ore-poor-hematite-granite", null)]
    [InlineData("seraphhorizons:crushedore-", null)]
    [InlineData(null, null)]
    public void GroundCode_IsTheSameOreAndGrade(string? crushed, string? ground)
    {
        Assert.Equal(ground, ArrastraCharge.GroundCode(crushed));
        Assert.Equal(ground != null, ArrastraCharge.IsCrushed(crushed));
    }

    [Fact]
    public void Loads_CrushedRockUpToTheCharge()
    {
        var c = new ArrastraCharge(2);
        Assert.Equal(PitState.Empty, c.State);
        Assert.Equal(LoadVerdict.NotCrushed, c.Load("game:ore-poor-hematite-granite"));
        Assert.Equal(LoadVerdict.NotCrushed, c.Load("seraphhorizons:groundore-hematite-poor"));
        Assert.Equal(LoadVerdict.Loads, c.Load(Poor));
        Assert.Equal(LoadVerdict.Mixed, c.Load(Medium));
        Assert.Equal(LoadVerdict.Loads, c.Load(Poor));
        Assert.Equal(LoadVerdict.Full, c.Load(Poor));
        Assert.Equal(PitState.Charged, c.State);
        Assert.Equal(new[] { Poor, Poor }, c.Rocks);
    }

    [Fact]
    public void Dry_DoesNotGrind_ABucketARockDoes()
    {
        var c = new ArrastraCharge(2);
        c.Load(Poor);
        c.Load(Poor);
        Assert.Equal(20, c.WaterNeeded(10));
        Assert.False(c.CanGrind(0, 10));
        Assert.False(c.CanGrind(10, 10));
        Assert.True(c.CanGrind(20, 10));
        Assert.False(new ArrastraCharge(2).CanGrind(20, 10)); // empty: nothing to grind
    }

    [Fact]
    public void Water_PoursUntilFull_NotOntoThePaste()
    {
        var c = new ArrastraCharge(2);
        Assert.Equal(WaterVerdict.Pours, c.CanWater(0, 20));
        Assert.Equal(WaterVerdict.Pours, c.CanWater(10, 20));
        Assert.Equal(WaterVerdict.Full, c.CanWater(20, 20));
        c.Load(Poor);
        c.Work(1e6, 100);
        Assert.Equal(WaterVerdict.Ground, c.CanWater(0, 20));
    }

    [Fact]
    public void Grinds_TheWholeChargeAtOnce_WhenItsTurnsAreIn()
    {
        const double perCharge = 429.75;
        var c = new ArrastraCharge(2);
        c.Load(Medium);
        c.Load(Medium);
        Assert.False(c.Work(Turn * 200, perCharge));
        Assert.Equal(200 / perCharge, c.Progress(perCharge), 9);
        Assert.False(c.Work(Turn * 229, perCharge));
        Assert.Equal(PitState.Charged, c.State);
        Assert.True(c.Work(Turn * 0.75, perCharge));
        Assert.Equal(PitState.Ground, c.State);
        Assert.Equal(new[] { "seraphhorizons:groundore-galena-medium", "seraphhorizons:groundore-galena-medium" }, c.Rocks);
        Assert.Equal(0, c.WaterNeeded(10));
        Assert.Equal(1, c.Progress(perCharge));
        // ground: more turns do nothing, and no rock goes in until it is drawn
        Assert.False(c.Work(Turn * 1000, perCharge));
        Assert.Equal(LoadVerdict.Ground, c.Load(Poor));
    }

    [Fact]
    public void APartCharge_TakesItsShareOfTheWork()
    {
        var c = new ArrastraCharge(2);
        c.Load(Poor);
        Assert.False(c.Work(Turn * 99.9, 200));
        Assert.True(c.Work(Turn * 0.1, 200));
    }

    [Fact]
    public void ARockAddedPartWay_AddsItsShare()
    {
        var c = new ArrastraCharge(2);
        c.Load(Poor);
        c.Work(Turn * 50, 200);
        c.Load(Poor);
        Assert.False(c.Work(Turn * 100, 200)); // 150 of 200
        Assert.True(c.Work(Turn * 50, 200));
    }

    [Fact]
    public void Draw_EmptiesThePit_AGrindNotFinishedComesBackCrushed()
    {
        var c = new ArrastraCharge(2);
        c.Load(Poor);
        c.Work(Turn * 10, 200);
        Assert.Equal(new[] { Poor }, c.Draw());
        Assert.Equal(PitState.Empty, c.State);
        Assert.Equal(0, c.Turns);
        c.Load(Poor);
        c.Work(Turn * 100, 200);
        Assert.Equal(new[] { "seraphhorizons:groundore-hematite-poor" }, c.Draw());
        Assert.Equal(PitState.Empty, c.State);
        Assert.Equal(LoadVerdict.Loads, c.Load(Poor));
    }

    [Fact]
    public void Restore_KeepsWhatFits_ReturnsTheRest()
    {
        var c = ArrastraCharge.Restore(2, [Poor, "game:stone-granite", Medium, Poor, Poor], false, 12.5, out var dropped);
        Assert.Equal(new[] { Poor, Poor }, c.Rocks);
        Assert.Equal(12.5, c.Turns);
        Assert.Equal(new[] { "game:stone-granite", Medium, Poor }, dropped);

        var g = ArrastraCharge.Restore(2, ["seraphhorizons:groundore-hematite-poor"], true, 5, out dropped);
        Assert.Equal(PitState.Ground, g.State);
        Assert.Equal(0, g.Turns);
        Assert.Empty(dropped);

        var e = ArrastraCharge.Restore(2, [], true, double.NaN, out _);
        Assert.Equal(PitState.Empty, e.State);
    }

    [Fact]
    public void Grinding_LiftsPoorOresLock_AndLosesNothing()
    {
        // #765: poor ore not ground keeps 60 % locked; ground through the arrastra it concentrates whole
        var r = R;
        var crushed = OreStream.FromRock("hematite", "poor", OreForm.Crushed);
        var ground = r.Lift(crushed, OreState.Ground);
        Assert.Equal(crushed.Units, ground.Units);
        Assert.True(ground.State.HasFlag(OreState.Ground));
        var hematite = r.Ore("hematite");
        Assert.Equal(0.4 * 0.85, r.LockFactor(hematite, "poor", crushed.State), 9);
        Assert.Equal(0.85, r.LockFactor(hematite, "poor", ground.State), 9);
    }
}
