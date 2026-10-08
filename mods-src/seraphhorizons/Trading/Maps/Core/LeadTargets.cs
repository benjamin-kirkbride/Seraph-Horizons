using SeraphHorizons.Mod.Trading.Core;

namespace SeraphHorizons.Mod.Trading.Maps.Core;

/// <summary>Where a lead goes: a trader camp (<see cref="CampLeads"/>), or the nearest reserved
/// settlement ground (behind the <c>mapsToTraders</c> unlock). <see cref="Prospector"/> and
/// <see cref="Far"/> were kinds of camp lead before the camp leads were reworked; leads of those
/// kinds bought then still read.</summary>
public enum LeadKind { Camp, Prospector, Far, Settlement }

/// <summary>A camp cell as the lead picker sees it: its type and site (null: the cell has none).</summary>
public readonly record struct CampSite(CellKey Cell, string Type, int X, int Z);

/// <summary>Lead kinds by code, and the settlement ground a trader's shelf leads to.</summary>
public static class LeadTargets
{
    public static string Code(LeadKind kind) => kind.ToString().ToLowerInvariant();

    public static bool TryParse(string? code, out LeadKind kind) =>
        Enum.TryParse(code, ignoreCase: true, out kind) && Enum.IsDefined(kind);

    /// <summary>The settlement ground nearest (x, z): the centre of its 8 km cell.</summary>
    public static (int X, int Z) Settlement(int x, int z) => TraderGrid.SettlementCentre(x, z);
}
