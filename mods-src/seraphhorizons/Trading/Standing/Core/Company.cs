namespace SeraphHorizons.Mod.Trading.Standing.Core;

/// <summary>A company's standing (#463), keyed by the vanilla group's uid: a record per trader,
/// and the players whose personal standing has been merged in (members while they stay).</summary>
public sealed class CompanyRecord
{
    public int GroupUid { get; set; }
    public Dictionary<string, StandingRecord> Traders { get; set; } = new();
    public List<string> Merged { get; set; } = [];
}

/// <summary>
/// Companies (#463): a player's company is one of their vanilla groups, the one they chose
/// (<c>/sh company</c>) or else the first they are in. The rules:
/// <list type="number">
/// <item>Becoming a member (forming, joining, or choosing the group as company): for each trader,
/// company = max(company, the player's personal), once per membership (<see cref="Sync"/>).</item>
/// <item>Leaving (left, kicked, switched company, group gone): the player keeps only their
/// personal record; the company keeps everything.</item>
/// <item>Gains and penalties go to the player and their company (<see cref="StandingLedger"/>).</item>
/// </list>
/// Groups live in the server's player data and fire no events, so membership is read live from the
/// game and passed in; <see cref="Sync"/> then catches up whatever changed (the game side also calls it
/// from patches on join, leave and disband, so it happens at once). An unknown group uid is no company.
/// </summary>
public sealed class CompanyBook
{
    private readonly StandingState _state;
    private readonly StandingLedger _ledger;

    internal CompanyBook(StandingState state, StandingLedger ledger)
    {
        _state = state;
        _ledger = ledger;
    }

    public CompanyRecord? Record(int group) => _state.Companies.TryGetValue(group, out var c) ? c : null;

    public StandingRecord? Get(int group, string trader) =>
        Record(group) is { } c && c.Traders.TryGetValue(trader, out var r) ? r : null;

    internal StandingRecord GetOrAdd(int group, string trader)
    {
        if (!_state.Companies.TryGetValue(group, out var c)) _state.Companies[group] = c = new CompanyRecord { GroupUid = group };
        if (!c.Traders.TryGetValue(trader, out var r)) c.Traders[trader] = r = new StandingRecord();
        return r;
    }

    public int? Designated(string player) => _state.Designated.TryGetValue(player, out int g) ? g : null;

    public void Designate(string player, int group) => _state.Designated[player] = group;

    /// <summary>The player's company: the group they chose if they are still in it and it exists,
    /// else the first of their groups that exists (<paramref name="memberships"/> in the order they
    /// joined), else none.</summary>
    public int? Resolve(string player, IReadOnlyList<int> memberships, Func<int, bool> exists)
    {
        if (Designated(player) is int chosen && memberships.Contains(chosen) && exists(chosen)) return chosen;
        foreach (int g in memberships)
            if (exists(g)) return g;
        return null;
    }

    /// <summary>
    /// Brings the records up to date with the player's current company: a company they are no
    /// longer merged with (left, kicked, switched, disbanded) forgets them, and keeps its standing;
    /// a company they are new to takes max(company, personal) per trader. Idempotent.
    /// </summary>
    /// <returns>Whether the player was merged into <paramref name="company"/> now.</returns>
    public bool Sync(string player, int? company, double day)
    {
        foreach (var c in _state.Companies.Values)
            if (c.GroupUid != company) c.Merged.Remove(player);
        if (company is not int g) return false;
        if (Record(g) is { } existing && existing.Merged.Contains(player)) return false;
        foreach (var (trader, personal) in _ledger.PersonalRecords(player))
        {
            var rec = GetOrAdd(g, trader);
            if (personal.Points > rec.Points) rec.Add(personal.Points - rec.Points, StandingKinds.Merge, day, _ledger.Rules.EventsKept);
        }
        if (!_state.Companies.TryGetValue(g, out var record)) _state.Companies[g] = record = new CompanyRecord { GroupUid = g };
        record.Merged.Add(player);
        return true;
    }

    /// <summary>A player joined a group (<paramref name="memberships"/> includes it): with no valid
    /// choice, their first group becomes their company for good, which is this one for a player in
    /// no other group.</summary>
    public void Joined(string player, int group, IReadOnlyList<int> memberships, Func<int, bool> exists)
    {
        if (Designated(player) is int chosen && memberships.Contains(chosen) && exists(chosen)) return;
        Designate(player, Resolve(player, memberships, exists) ?? group);
    }

    /// <summary>The group is gone: its record and every choice of it.</summary>
    public void Disbanded(int group)
    {
        _state.Companies.Remove(group);
        foreach (string player in _state.Designated.Where(kv => kv.Value == group).Select(kv => kv.Key).ToList())
            _state.Designated.Remove(player);
    }
}
