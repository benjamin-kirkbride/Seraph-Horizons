using SeraphHorizons.Mod.Eidolon.Core;
using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.Tests.Eidolon;

/// <summary>Oil per job and repair (#674).</summary>
public class EidolonOilRepairTests
{
    private const float MaxHealth = 300;

    [Fact]
    public void ANewEidolonWakesWithAQuarterTank()
    {
        var tank = EidolonOil.Initial(new EidolonConfig());
        Assert.Equal(1000, tank.Capacity);
        Assert.Equal(250, tank.Points);
        Assert.False(tank.Dry);
    }

    [Fact]
    public void EachJobCostsItsFigureAndAFullTankLastsMany()
    {
        var c = new EidolonConfig();
        Assert.Equal(5, EidolonOil.Cost(c, EidolonJob.TreeFelled));
        Assert.Equal(3, EidolonOil.Cost(c, EidolonJob.TrunkDelivered));
        Assert.Equal(2, EidolonOil.Cost(c, EidolonJob.LoadCarried));
        Assert.Equal(200, OilDrain.JobsPerTank(c.OilTank, EidolonOil.Cost(c, EidolonJob.TreeFelled)));
        foreach (var job in Enum.GetValues<EidolonJob>())
            Assert.True(EidolonOil.Cost(c, job) > 0, job.ToString());
    }

    [Fact]
    public void JobsDrainItDryAndDryWaitsWithoutSlumping()
    {
        var c = new EidolonConfig();
        var tank = OilTank.Empty(c.OilTank).Fill(12);
        int felled = 0;
        while (!tank.Dry)
        {
            tank = tank.Drain(EidolonOil.Cost(c, EidolonJob.TreeFelled));
            felled++;
        }
        Assert.Equal(3, felled);
        Assert.False(EidolonOil.Dry.Slumps);
        // Damage and charge slump, so they count before dry.
        Assert.Equal(EidolonStop.NoCharge, EidolonStop.First([EidolonOil.Dry, EidolonStop.NoCharge]));
        Assert.Equal(EidolonOil.Dry, EidolonStop.First([null, EidolonOil.Dry]));
    }

    [Fact]
    public void AnItemRestoresATenthAndDoubleInTheGantry()
    {
        var c = new EidolonConfig();
        Assert.Equal(30, EidolonRepair.Heal(c, 0, MaxHealth, inGantry: false), 3);
        Assert.Equal(60, EidolonRepair.Heal(c, 0, MaxHealth, inGantry: true), 3);
        Assert.Equal(3, EidolonRepair.ItemsToStand(c, MaxHealth, inGantry: false));
        Assert.Equal(2, EidolonRepair.ItemsToStand(c, MaxHealth, inGantry: true));
    }

    [Fact]
    public void ARepairNeverGoesPastWholeAndAWholeOneTakesNone()
    {
        var c = new EidolonConfig();
        Assert.Equal(10, EidolonRepair.Heal(c, 290, MaxHealth, inGantry: true), 3);
        Assert.Equal(0, EidolonRepair.Heal(c, MaxHealth, MaxHealth, inGantry: false));
    }

    [Fact]
    public void OilAndRepairSettingsOutOfRangeFallBack()
    {
        var c = new EidolonConfig { OilTank = 0, InitialOilShare = 2, OilPerTreeFelled = -1, RepairShare = 0, GantryRepairMultiplier = 0.5 };
        Assert.Equal(5, c.Sanitise().Count);
        Assert.Equal(EidolonConfig.Defaults.OilTank, c.OilTank);
        Assert.Equal(EidolonConfig.Defaults.InitialOilShare, c.InitialOilShare);
        Assert.Equal(EidolonConfig.Defaults.OilPerTreeFelled, c.OilPerTreeFelled);
        Assert.Equal(EidolonConfig.Defaults.RepairShare, c.RepairShare);
        Assert.Equal(EidolonConfig.Defaults.GantryRepairMultiplier, c.GantryRepairMultiplier);
    }
}
