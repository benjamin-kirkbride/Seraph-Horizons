using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Mod.Tests;

public class ClearSkyPlanTests
{
    // Exact in binary, so the sampled days land on the spells' edges.
    private const double Step = 0.125;

    [Theory]
    [InlineData(8, 0)]
    [InlineData(12, 0)]
    [InlineData(15.9, 0)]
    [InlineData(0, 12)]
    [InlineData(6.5, 5.5)]
    [InlineData(16, 20)]
    [InlineData(23, 13)]
    public void DaytimeIsLeftAloneAndOtherwiseTheClockGoesToTheNextNoon(double hour, double expected) =>
        Assert.Equal(expected, ClearSkyPlan.HoursToDay(hour, 24), 6);

    [Fact]
    public void DaytimeScalesWithTheLengthOfTheDay()
    {
        // A 48-hour day: daytime is 16 to 32, noon 24.
        Assert.Equal(0, ClearSkyPlan.HoursToDay(20, 48), 6);
        Assert.Equal(24, ClearSkyPlan.HoursToDay(0, 48), 6);
        Assert.Equal(40, ClearSkyPlan.HoursToDay(32, 48), 6);
    }

    [Fact]
    public void AlreadyDryNeedsNoForwarding()
    {
        var spell = ClearSkyPlan.FindDrySpell(_ => true, Step)!.Value;
        Assert.Equal(0, spell.StartDays);
        Assert.Equal(ClearSkyPlan.WantedDryDays, spell.LengthDays, 6);
    }

    [Fact]
    public void TheFirstLongEnoughDrySpellIsTaken()
    {
        // Rain for 2 days, dry for 6 hours, rain until day 5, then dry for good.
        bool Dry(double d) => d is >= 2 and < 2.25 || d >= 5;
        var spell = ClearSkyPlan.FindDrySpell(Dry, Step)!.Value;
        Assert.Equal(5, spell.StartDays, 6);
        Assert.True(spell.LengthDays >= ClearSkyPlan.WantedDryDays - 1e-9);
    }

    [Fact]
    public void FailingThatTheLongestShortSpellIsTaken()
    {
        bool Dry(double d) => d is >= 2 and < 2.25 || d is >= 7 and < 7.5 || d is >= 9 and < 9.1;
        var spell = ClearSkyPlan.FindDrySpell(Dry, Step)!.Value;
        Assert.Equal(7, spell.StartDays, 6);
        Assert.Equal(0.5, spell.LengthDays, 6);
    }

    [Fact]
    public void ADrySpellRunningToTheEndOfTheSearchCounts()
    {
        bool Dry(double d) => d >= ClearSkyPlan.SearchDays - 0.25;
        var spell = ClearSkyPlan.FindDrySpell(Dry, Step)!.Value;
        Assert.Equal(ClearSkyPlan.SearchDays - 0.25, spell.StartDays, 6);
    }

    [Fact]
    public void RainThroughoutFindsNothing() => Assert.Null(ClearSkyPlan.FindDrySpell(_ => false, Step));

    [Fact]
    public void TheLockRoundTrips()
    {
        var lk = new ClearLock { OverridePrecipitation = 0.3f, AutoChangePatterns = false, StormDaysAhead = 4.25 };
        byte[] bytes = lk.ToBytes();
        Assert.DoesNotContain("Baseline", System.Text.Encoding.UTF8.GetString(bytes));
        var back = ClearLock.FromBytes(bytes)!;
        Assert.False(back.StopsTime);
        Assert.Equal(0.3f, back.OverridePrecipitation);
        Assert.False(back.AutoChangePatterns);
        Assert.Equal(4.25, back.StormDaysAhead);

        var none = ClearLock.FromBytes(new ClearLock().ToBytes())!;
        Assert.Null(none.OverridePrecipitation);
        Assert.Null(none.StormDaysAhead);
        Assert.Null(ClearLock.FromBytes(null));
    }

    [Fact]
    public void ALockFromTheVersionThatStoppedTimeStillReadsAndSaysSo()
    {
        // As a world saved by that version holds it.
        var old = ClearLock.FromBytes(System.Text.Encoding.UTF8.GetBytes(
            """{"HadBaseline":true,"Baseline":60,"OverridePrecipitation":null,"AutoChangePatterns":true,"StormDaysAhead":5.5}"""))!;
        Assert.True(old.StopsTime);
        Assert.True(old.HadBaseline);
        Assert.Equal(60f, old.Baseline);
        Assert.Equal(5.5, old.StormDaysAhead);

        old.HadBaseline = null;
        old.Baseline = null;
        var migrated = ClearLock.FromBytes(old.ToBytes())!;
        Assert.False(migrated.StopsTime);
        Assert.Null(migrated.Baseline);
        Assert.Equal(5.5, migrated.StormDaysAhead);
    }

    // SurvivalCoreSystem.GetSolarSphericalCoords (1.22.7), as disassembled: the cosine of the
    // sun's zenith angle for a latitude (-1..1, positive north), a year fraction and a day fraction.
    private static double SurvivalSunHeight(double latitude, double yearRel, double dayRel)
    {
        const double tilt = 0.409105182;
        double lat = latitude * Math.PI / 2;
        double hourAngle = 2 * Math.PI * (dayRel - 0.5);
        double declination = -tilt * Math.Cos(2 * Math.PI * (yearRel + 0.02739726));
        return Math.Sin(lat) * Math.Sin(declination) + Math.Cos(lat) * Math.Cos(declination) * Math.Cos(hourAngle);
    }

    // Outside the tropics (the tilt is 0.409 rad, a latitude of 0.26) the summer solstice's noon
    // sun is the highest of the year. Inside them the sun passes overhead on two other days, so the
    // solstice's is a little lower: at most the tilt off overhead, at the equator (cos 23.4
    // degrees, 0.92).
    [Theory]
    [InlineData(0.9, 0)]
    [InlineData(0.6, 0)]
    [InlineData(0.3, 0)]
    [InlineData(-0.3, 0)]
    [InlineData(-0.6, 0)]
    [InlineData(0.1, 0.04)]
    [InlineData(0.0001, 0.09)]
    [InlineData(-0.1, 0.04)]
    public void TheHeldSunIsTheSummerNoonSun(double latitude, double tropicalSlack)
    {
        // The hemisphere as the survival mod has it: south unless the latitude is above 0.
        float held = ClearSkyPlan.MidsummerYearRel(southern: latitude <= 0);
        Assert.InRange(held, 0f, 1f);
        double heldHeight = SurvivalSunHeight(latitude, held, ClearSkyPlan.NoonDayRel), highest = double.MinValue;
        for (double year = 0; year < 1; year += 0.001)
            for (double day = 0; day < 1; day += 0.01)
                highest = Math.Max(highest, SurvivalSunHeight(latitude, year, day));
        Assert.True(heldHeight >= highest - tropicalSlack - 1e-9,
            $"latitude {latitude}: the held sun at {heldHeight:0.0000}, the year's highest at {highest:0.0000}");
        // The solstice itself, outside the tropics: a day either side the noon sun is lower.
        if (tropicalSlack == 0)
        {
            Assert.True(heldHeight >= SurvivalSunHeight(latitude, held + 1 / 365.0, 0.5) - 1e-9);
            Assert.True(heldHeight >= SurvivalSunHeight(latitude, held - 1 / 365.0, 0.5) - 1e-9);
        }
    }

    [Fact]
    public void MidsummerIsHalfAYearApartAcrossTheEquator()
    {
        float north = ClearSkyPlan.MidsummerYearRel(southern: false), south = ClearSkyPlan.MidsummerYearRel(southern: true);
        Assert.Equal(0.5 - 10.0 / 365, north, 5);
        Assert.Equal(0.5, (south - north + 1) % 1, 5);
    }

    [Fact]
    public void AWrapWrapsWhatIsInstalledOnceAndPutsItBack()
    {
        var wrap = new DelegateWrap<Func<int>>();
        Func<int> original = () => 1;
        Func<int> Make(Func<int> inner) => () => inner() + 100;

        var installed = wrap.Wrap(original, Make)!;
        Assert.True(wrap.Held);
        Assert.Equal(101, installed());
        Assert.True(wrap.IsLive(installed));
        // Already ours: no second wrap.
        Assert.Null(wrap.Wrap(installed, Make));

        Assert.Same(original, wrap.Unwrap(installed));
        Assert.False(wrap.Held);
        Assert.False(wrap.IsLive(installed));
        // Nothing held: nothing to put back.
        Assert.Null(wrap.Unwrap(original));
    }

    [Fact]
    public void AReplacedWrapIsWrappedAgainAndTheOldWrapperGoesQuiet()
    {
        var wrap = new DelegateWrap<Func<int>>();
        Func<int> first = () => 1, second = () => 2;
        var old = wrap.Wrap(first, inner => inner)!;
        // Something installs its own delegate over ours (as the survival mod does at load).
        var fresh = wrap.Wrap(second, inner => inner)!;
        Assert.NotSame(old, fresh);
        Assert.False(wrap.IsLive(old));
        Assert.True(wrap.IsLive(fresh));
        // Nothing installed yet: nothing to wrap.
        Assert.Null(wrap.Wrap(null, inner => inner));

        // Released while another delegate is installed: that one stays.
        Func<int> other = () => 3;
        Assert.Null(wrap.Unwrap(other));
        Assert.False(wrap.Held);
    }
}
