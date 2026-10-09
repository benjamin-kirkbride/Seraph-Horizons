namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>
/// Why an eidolon cannot work now: one is reported by each upkeep (damage, charge; oil later), and
/// the one with the lowest <see cref="Rank"/> is shown. <see cref="Slumps"/>: it drops into
/// <c>slump</c> and holds it (0 HP, out of charge); otherwise it only stands and waits (out of oil).
/// <see cref="Code"/> is a lang key's last part (<c>seraphhorizons:eidolon-stop-{code}</c>).
/// </summary>
public sealed record EidolonStop(string Code, bool Slumps, int Rank)
{
    /// <summary>At 0 HP: slumped, disabled, never dead, until repaired.</summary>
    public static readonly EidolonStop Damaged = new("damaged", true, 0);

    /// <summary>Out of charge: slumped until a temporal gear recharges it.</summary>
    public static readonly EidolonStop NoCharge = new("nocharge", true, 10);

    /// <summary>The stop that counts among <paramref name="stops"/> (nulls are upkeeps that are
    /// fine): a slumping one before one that only waits, then the lowest rank; null when none.</summary>
    public static EidolonStop? First(IEnumerable<EidolonStop?> stops) =>
        stops.OfType<EidolonStop>().OrderByDescending(s => s.Slumps).ThenBy(s => s.Rank).FirstOrDefault();
}
