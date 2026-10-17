using System.Text.Json;
using SeraphHorizons.Mod.BallMill.Core;
using SeraphHorizons.Mod.Ore.Core;

namespace SeraphHorizons.Tests.BallMill;

/// <summary>The ball mill's grind (#915) on the shipped <c>config/ore-processing.json</c>: crushed ore and the
/// table's middlings in, ground and screened ore out, nothing lost, every locked share it lifts freed.</summary>
public class BallMillGrindTests
{
    private static readonly OreRecovery R = new(JsonSerializer.Deserialize<OreProcessingConfig>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ore-processing.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!);

    private static readonly OreMachine[] All = [OreMachine.Sluice, OreMachine.Jig, OreMachine.Table, OreMachine.Vanner];

    private static double Line(OreStream s)
    {
        double caught = 0;
        foreach (var m in All)
        {
            var pass = R.Concentrate(m, s);
            caught += pass.Concentrate;
            s = pass.Tailings;
        }
        return caught;
    }

    [Fact]
    public void CrushedOreBecomesGroundAndScreenedOreOfItsGrade()
    {
        var crushed = OreStream.FromRock("hematite", "poor", OreForm.Crushed, rock: "andesite");
        var product = BallMillGrind.Grind(R, OreForm.Crushed, crushed)!;
        Assert.Equal("seraphhorizons:groundore-hematite-poor", product.Code);
        Assert.Equal(OreState.Ground | OreState.Classified, product.Stream.State);
        // No metal is lost, and the rock is carried for the tailings.
        Assert.Equal(crushed.Units, product.Stream.Units, 12);
        Assert.Equal("andesite", product.Stream.Rock);
        Assert.Null(product.Stream.Locked);
    }

    [Fact]
    public void PoorOreGroundIsOnTheWholeRouteInsteadOfAThirdOfIt()
    {
        var crushed = OreStream.FromRock("hematite", "poor", OreForm.Crushed, units: 1);
        // Unground and unclassified: 0.4 × 0.85 of the full line.
        Assert.Equal(0.4 * 0.85 * 0.9892, Line(crushed), 9);
        // Ground and screened by the mill: nothing locked, so on the whole ideal route, where the vanner (an
        // oxide's final step) takes all the rest.
        Assert.Equal(1, Line(BallMillGrind.Grind(R, OreForm.Crushed, crushed)!.Stream), 9);
    }

    [Fact]
    public void TheTablesMiddlingsAreFreedAndGoBackToTheHead()
    {
        var s = new OreStream("hematite", "poor", 1, State: OreState.Classified);
        OreStream? middlings = null;
        foreach (var m in All)
        {
            var pass = R.Concentrate(m, s);
            middlings ??= pass.Middlings;
            s = pass.Tailings;
        }
        Assert.NotNull(middlings);
        Assert.True(middlings!.Locked > 0);
        // The middlings come back through the same infeed as the table's feed was: crushed, classified, not ground.
        var milled = BallMillGrind.Grind(R, OreForm.Crushed, middlings)!;
        Assert.Equal("seraphhorizons:groundore-hematite-poor", milled.Code);
        Assert.Equal(0, milled.Stream.Locked!.Value, 12);
        Assert.Equal(WorkedBy.None, milled.Stream.Worked);
        Assert.Equal(middlings.Units, milled.Stream.Units, 12);
        Assert.Equal(middlings.Units, Line(milled.Stream), 9);
    }

    [Fact]
    public void MediumOreIsOnlyScreened()
    {
        var crushed = OreStream.FromRock("cassiterite", "medium", OreForm.Crushed, units: 1);
        var product = BallMillGrind.Grind(R, OreForm.Crushed, crushed)!;
        Assert.Equal(1, R.LockFactor(R.Ore("cassiterite"), "medium", product.Stream.State), 12);
    }

    [Theory]
    [InlineData(OreForm.Raw)]
    [InlineData(OreForm.RoastedRock)]
    [InlineData(OreForm.Concentrate)]
    [InlineData(null)]
    public void ItTakesOnlyCrushedAndGroundOre(OreForm? form)
    {
        var s = OreStream.FromRock("hematite", "rich", OreForm.Crushed);
        Assert.False(BallMillGrind.Takes(form, s));
        Assert.Null(BallMillGrind.Grind(R, form, s));
        Assert.False(BallMillGrind.Takes(OreForm.Crushed, null));
        Assert.False(BallMillGrind.Takes(OreForm.Crushed, s with { Grade = "-" }));
    }

    [Fact]
    public void ItsFiguresAreTheIssues()
    {
        var spec = R.Machine(OreMachine.BallMill);
        Assert.Equal(1.1, spec.Load, 9);
        Assert.Equal(0.22, spec.EmptyLoad, 9);
        Assert.False(spec.LoadFollowsJob);
        Assert.Equal(25, spec.WaterPerItem, 9);
        Assert.Equal(6, spec.ItemsPerHour, 9);
        Assert.False(spec.Batch);
        // Load is steady: the full figure working or idle.
        Assert.Equal(1.1, R.LoadAt(OreMachine.BallMill, 0), 9);
        // 57 turns an hour at 0.6: 9.55 turns a rock.
        Assert.Equal(95.5 * 0.6 / 6, spec.TurnsPerItem, 9);
    }
}
