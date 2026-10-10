namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>
/// Who may command an eidolon or open what it carries (Eidolon/README.md, "Ownership"): its owner,
/// and anyone in the owner's company (the trading standing's company, a vanilla group; when standing
/// is off, any group the two share). An eidolon with no owner (spawned by an admin with no player)
/// answers anyone.
/// </summary>
public static class EidolonOwnership
{
    /// <param name="ownerUid">The owner's player uid, or null/empty for none.</param>
    /// <param name="playerUid">Who asks.</param>
    /// <param name="ownerCompany">The owner's company now, or null for none (or standing off).</param>
    /// <param name="playerCompany">The asker's company now, or null.</param>
    /// <param name="shareGroup">With standing off: whether the two share a group; ignored when
    /// either company is known.</param>
    public static bool MayCommand(string? ownerUid, string playerUid, int? ownerCompany, int? playerCompany, bool shareGroup = false)
    {
        if (string.IsNullOrEmpty(ownerUid) || ownerUid == playerUid)
            return true;
        if (ownerCompany is { } a && playerCompany is { } b)
            return a == b;
        if (ownerCompany is null && playerCompany is null)
            return shareGroup;
        return false;
    }
}
