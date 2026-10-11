using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>A job an eidolon finishes, each costing oil (<see cref="EidolonOil.Cost"/>). Idling,
/// following, staying and guarding cost nothing. Later jobs add theirs here, with a figure in
/// <see cref="EidolonConfig"/>.</summary>
public enum EidolonJob
{
    /// <summary>A tree felled (#677).</summary>
    TreeFelled,
    /// <summary>A trunk delivered where it was told to haul it (#678).</summary>
    TrunkDelivered,
    /// <summary>A load (a container, a Carry On block) carried somewhere and set down (#676).</summary>
    LoadCarried,
}

/// <summary>
/// The eidolon's oil (Eidolon/README.md, "Oil"): a reservoir like a machine's tank
/// (<see cref="OilTank"/>, 100 points to the litre), filled the same way, drained per job done. Dry,
/// it stops and waits (<see cref="Dry"/>, a stop that does not slump), until oiled.
/// </summary>
public static class EidolonOil
{
    /// <summary>Out of oil: it stands and waits until oiled; it does not slump.</summary>
    public static readonly EidolonStop Dry = new("dry", false, 20);

    /// <summary>What <paramref name="job"/> costs by <paramref name="config"/>.</summary>
    public static double Cost(EidolonConfig config, EidolonJob job) => job switch
    {
        EidolonJob.TreeFelled => config.OilPerTreeFelled,
        EidolonJob.TrunkDelivered => config.OilPerTrunkDelivered,
        EidolonJob.LoadCarried => config.OilPerLoadCarried,
        _ => throw new ArgumentOutOfRangeException(nameof(job)),
    };

    /// <summary>The reservoir a new eidolon wakes with.</summary>
    public static OilTank Initial(EidolonConfig config) => OilTank.Empty(config.OilTank).Fill(config.OilTank * config.InitialOilShare);
}

/// <summary>The eidolon's repair (Eidolon/README.md, "Repair"): one item of metal parts or one iron
/// plate restores a share of its most health, more inside a gantry.</summary>
public static class EidolonRepair
{
    /// <summary>The health one item restores to an eidolon of <paramref name="maxHealth"/>, never
    /// more than it lacks.</summary>
    public static float Heal(EidolonConfig config, float health, float maxHealth, bool inGantry)
    {
        double heal = maxHealth * config.RepairShare * (inGantry ? config.GantryRepairMultiplier : 1);
        return (float)Math.Clamp(heal, 0, Math.Max(0, maxHealth - health));
    }

    /// <summary>Items it takes to stand up again from 0 HP (at <see cref="EidolonConfig.StandUpHealthShare"/>):
    /// for the README and the tests.</summary>
    public static int ItemsToStand(EidolonConfig config, float maxHealth, bool inGantry)
    {
        double per = maxHealth * config.RepairShare * (inGantry ? config.GantryRepairMultiplier : 1);
        return per <= 0 ? int.MaxValue : (int)Math.Ceiling(maxHealth * config.StandUpHealthShare / per - 1e-9);
    }
}
