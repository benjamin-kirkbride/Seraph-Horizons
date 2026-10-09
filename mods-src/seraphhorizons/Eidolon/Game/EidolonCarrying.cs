using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// The container an eidolon carries as a pack mule (#676), as the jobs that take from it see it: an
/// entity behaviour that carries one implements this. The fell order (#677) replants from it
/// (<see cref="EidolonFeller.Replant"/>). Until carrying exists nothing implements it, so an eidolon
/// carries nothing and does not replant.
/// </summary>
public interface IEidolonCarrier
{
    /// <summary>The carried container's slots, or null while it carries none.</summary>
    IInventory? CarriedInventory { get; }
}

public static class EidolonCarrying
{
    /// <summary>What <paramref name="eidolon"/> carries: the first of its behaviours implementing
    /// <see cref="IEidolonCarrier"/> that carries something, or null. Server side.</summary>
    public static IInventory? CarriedInventory(this EntityLaborEidolon eidolon) =>
        eidolon.SidedProperties?.Behaviors.OfType<IEidolonCarrier>().Select(c => c.CarriedInventory).FirstOrDefault(i => i != null);
}
