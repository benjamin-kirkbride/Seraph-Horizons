using System.Text.Json;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.StampBattery.Core;
using Xunit;

namespace SeraphHorizons.Tests.StampBattery;

/// <summary>The stamps' drops as the particles find them (<see cref="Stamping.Landed"/>): the rig's figures, and
/// each landing where the rig draws that stamp down on its die.</summary>
public class StampDropTests
{
    private static string Rig => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rigs/stampbattery-rig.json"));

    [Fact]
    public void The_drops_figures_are_the_rigs()
    {
        using var doc = JsonDocument.Parse(Rig);
        var stamps = doc.RootElement.GetProperty("stamps");
        Assert.Equal(Stamping.TurnsPerRev, stamps.GetProperty("turnsPerRev").GetDouble());
        Assert.Equal(Stamping.LandsAt, stamps.GetProperty("landsAt").GetDouble());
        Assert.Equal(Stamping.FiringOrder, stamps.GetProperty("firingOrder").EnumerateArray().Select(v => v.GetInt32()));
        var rig = OreMachineRig.Parse(Rig);
        for (int n = 1; n <= 5; n++)
            Assert.NotNull(rig.Point($"mortar{n}"));
        Assert.NotNull(rig.Point("screen"));
    }

    [Fact]
    public void Each_stamp_lands_where_the_rig_draws_it_on_its_die()
    {
        var parts = OreMachineRig.Parse(Rig).Parts;
        double Y(int stamp, double travel) => parts.Matrices(new RigInput(0, Travel: travel))[parts.IndexOf($"stamp{stamp}")][13];
        double step = 0.01, travel = 0;
        var landed = new List<int>();
        for (int i = 0; i < 2 * 2 * Math.PI / step; i++, travel += step)
        {
            int n = Stamping.Landed(travel, travel + step);
            if (n == 0)
                continue;
            landed.Add(n);
            double least = Enumerable.Range(0, 400).Min(k => Y(n, k * 2 * Math.PI / 100));
            Assert.True(Y(n, travel + step) - least < 0.1 / 16 + 1e-3, $"stamp {n} at travel {travel + step:F2} is {Y(n, travel + step) - least:F4} over its die");
        }
        // two axle turns, a camshaft turn: ten drops, each stamp twice, in the firing order
        Assert.Equal(10, landed.Count);
        int first = landed.IndexOf(1);
        Assert.Equal([1, 3, 5, 2, 4], landed.Skip(first).Concat(landed.Take(first)).Take(5));
    }

    [Fact]
    public void Standing_still_lands_nothing_and_stamp_one_lands_first()
    {
        Assert.Equal(0, Stamping.Landed(1.0, 1.0));
        double at = Stamping.TurnsPerRev * Stamping.LandsAt;
        Assert.Equal(1, Stamping.Landed(at - 0.01, at + 0.01));
    }
}
