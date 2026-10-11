namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>What makes eidolons one job (README "Eidolon", working together): the same owner (their uid,
/// "" for none), the same kind of work (the order's code: <c>mine</c>, <c>fell</c>) and the very same
/// area, corner for corner. An overlapping area, or another owner's eidolons, is another job.</summary>
public readonly record struct JobKey(string Owner, string Kind, MarkArea Area);

/// <summary>One eidolon's share of a job, for the report: its id, its name as the report gives it,
/// and how much it did (blocks mined, trees felled).</summary>
public sealed record JobShare(long Member, string Name, int Count);

/// <summary>
/// One shared job (<see cref="JobBoard{TUnit, TShared}"/>): its key, its members and what they did, the
/// level it works (mining: the layer), and what its members share (<typeparamref name="TShared"/>,
/// made with the job: mining's cells every member leaves). A member is <em>present</em> (working),
/// <em>away</em> (its order interrupted: dry, slumped, a fight) or has left. Only present members gate
/// the level and hold up the report; an away member is present again when it starts again.
/// </summary>
public sealed class SharedJob<TShared> where TShared : new()
{
    private sealed class Member(string name)
    {
        public string Name = name;
        public int Count;
        public bool Away;
        public bool Finished;
        public int? Level;
        public bool Left;
    }

    private readonly Dictionary<long, Member> _members = new();
    private readonly Dictionary<long, double> _expected = new();

    internal SharedJob(JobKey key) => Key = key;

    public JobKey Key { get; }

    /// <summary>What the members share, made with the job.</summary>
    public TShared Shared { get; } = new();

    /// <summary>Whether its report has been made (<see cref="JobBoard{TUnit, TShared}.TryClose"/>): no one
    /// joins it any more, and an order with its key makes a new job.</summary>
    public bool Closed { get; internal set; }

    /// <summary>Its members that have not left (present or away), in the order they joined.</summary>
    public IReadOnlyList<long> Members => _members.Where(m => !m.Value.Left).Select(m => m.Key).ToList();

    /// <summary>Its members working now.</summary>
    public IReadOnlyList<long> Present => _members.Where(m => !m.Value.Left && !m.Value.Away).Select(m => m.Key).ToList();

    public bool Has(long member) => _members.TryGetValue(member, out var m) && !m.Left;

    /// <summary>The level the job works (mining: the layer): the highest any present member has work
    /// on (<see cref="JobBoard{TUnit, TShared}.SetLevel"/>), or null for none. A member whose own is lower
    /// has nothing on the job's level and waits; so a pit goes down a layer at a time, all of it.</summary>
    public int? Level => _members.Values.Where(m => !m.Left && !m.Away).Select(m => m.Level).Max();

    /// <summary>What the members did in all, those that left included.</summary>
    public int Total => _members.Values.Sum(m => m.Count);

    /// <summary>Each member's share, those that left included, most first, then in the order they joined.</summary>
    public IReadOnlyList<JobShare> Shares =>
        _members.Select((m, i) => (Share: new JobShare(m.Key, m.Value.Name, m.Value.Count), i))
            .OrderByDescending(s => s.Share.Count).ThenBy(s => s.i).Select(s => s.Share).ToList();

    /// <summary>Counts <paramref name="count"/> more to a member's share.</summary>
    public void Tally(long member, int count)
    {
        if (_members.TryGetValue(member, out var m))
            m.Count += count;
    }

    /// <summary>The ids it waits for (<see cref="JobBoard{TUnit, TShared}.Join"/>'s <c>expect</c>) that
    /// have not joined, and until when.</summary>
    public IReadOnlyDictionary<long, double> Expected => _expected;

    /// <summary>Whether it is done at <paramref name="now"/>: every present member has nothing left
    /// (<see cref="JobBoard{TUnit, TShared}.SetFinished"/>), and none it still waits for to load.</summary>
    public bool Done(double now) =>
        !Closed && Present.Count > 0 && Present.All(id => _members[id].Finished) && !_expected.Values.Any(until => until > now);

    internal bool Add(long member, string name)
    {
        _expected.Remove(member);
        if (_members.TryGetValue(member, out var m))
        {
            bool back = m.Left;
            m.Left = false;
            m.Away = false;
            m.Name = name;
            return back;
        }
        _members[member] = new Member(name);
        return true;
    }

    internal void Expect(long member, double until)
    {
        if (!_members.ContainsKey(member))
            _expected[member] = until;
    }

    internal void Leave(long member)
    {
        _expected.Remove(member);
        if (_members.TryGetValue(member, out var m))
        {
            m.Left = true;
            m.Level = null;
            m.Finished = false;
        }
    }

    internal void SetAway(long member, bool away)
    {
        if (_members.TryGetValue(member, out var m))
            m.Away = away;
    }

    internal void SetLevel(long member, int? level)
    {
        if (_members.TryGetValue(member, out var m))
            m.Level = level;
    }

    internal void SetFinished(long member, bool finished)
    {
        if (_members.TryGetValue(member, out var m))
            m.Finished = finished;
    }

    /// <summary>Whether a member has said it has nothing left (<see cref="JobBoard{TUnit, TShared}.SetFinished"/>).</summary>
    public bool IsFinished(long member) => _members.TryGetValue(member, out var m) && m.Finished;
}

/// <summary>
/// The shared jobs of one kind of work and their reservations (README "Eidolon", working together),
/// game-independent; the server keeps one per kind (<c>EidolonJobSystem</c>). Members are eidolons by
/// entity id; a unit (<typeparamref name="TUnit"/>) is what one works at a time: for mining a block
/// column (x, z) of the 2 × 2 square it strikes in, for felling a tree's stump.
///
/// <para>How an order uses it: <see cref="Join"/> as it starts (again), <see cref="Away"/> when it is
/// interrupted, <see cref="Leave"/> when it ends or is replaced (the server's sweep does this for an
/// eidolon unloaded, gone or given another order). Before walking to a unit, <see cref="TryReserve"/>
/// it (choosing among those <see cref="Free"/>); <see cref="Struck"/> on each blow, which keeps the
/// reservation; <see cref="Release"/> once done with it. <see cref="SetLevel"/> and
/// <see cref="SetFinished"/> after each search; once finished, <see cref="TryClose"/>: true for the one
/// member that makes the report.</para>
///
/// <para>Reservations are of the board, not of a job, so separate jobs over one place never hold the
/// same unit. A member holds one set of units at a time (a reservation replaces the last); one not
/// struck within <see cref="EidolonTeamwork.ReserveSeconds"/> lapses.</para>
/// </summary>
public sealed class JobBoard<TUnit, TShared> where TUnit : notnull where TShared : new()
{
    private readonly List<SharedJob<TShared>> _jobs = new();
    private readonly Dictionary<long, SharedJob<TShared>> _of = new();
    private readonly Dictionary<TUnit, (long Member, double Since)> _held = new();
    private readonly Dictionary<long, List<TUnit>> _holding = new();

    /// <summary>The open jobs (not closed) and closed ones some member has not yet left.</summary>
    public IReadOnlyList<SharedJob<TShared>> Jobs => _jobs;

    /// <summary>The job a member is in, or null.</summary>
    public SharedJob<TShared>? JobOf(long member) => _of.GetValueOrDefault(member);

    /// <summary>
    /// Puts <paramref name="member"/> in the open job with <paramref name="key"/>, making it if there is
    /// none (a closed one is not joined: the member makes a new one), and marks it present; it leaves
    /// any other job first. <paramref name="expect"/>: the ids of members it worked with before (saved
    /// with its order), which the job waits for until <paramref name="expectUntil"/> before it may be
    /// done, so a job loaded again does not report while its other members have yet to load.
    /// </summary>
    public SharedJob<TShared> Join(long member, string name, JobKey key, IEnumerable<long>? expect = null, double expectUntil = 0)
    {
        var job = _of.GetValueOrDefault(member);
        if (job == null || job.Closed || job.Key != key)
        {
            if (job != null)
                Leave(member);
            job = _jobs.FirstOrDefault(j => !j.Closed && j.Key == key);
            if (job == null)
                _jobs.Add(job = new SharedJob<TShared>(key));
        }
        job.Add(member, name);
        _of[member] = job;
        foreach (long id in expect ?? [])
            if (id != member)
                job.Expect(id, expectUntil);
        return job;
    }

    /// <summary>The member stops working for now (interrupted): its units are freed at once, and it
    /// neither gates the job's level nor holds up its report until it <see cref="Join"/>s again.</summary>
    public void Away(long member)
    {
        Release(member);
        _of.GetValueOrDefault(member)?.SetAway(member, true);
    }

    /// <summary>The member leaves its job (its order ended or was replaced, or it is gone): its units
    /// are freed; its share stays in the job's report. A job no one is left in is dropped.</summary>
    public void Leave(long member)
    {
        Release(member);
        if (!_of.Remove(member, out var job))
            return;
        job.Leave(member);
        if (job.Members.Count == 0)
            _jobs.Remove(job);
    }

    /// <summary>Every member of every job (for the server's sweep).</summary>
    public IReadOnlyList<long> AllMembers => _of.Keys.ToList();

    // ---- reservations ----

    /// <summary>Whether <paramref name="unit"/> is free for <paramref name="member"/> at <paramref name="now"/>:
    /// held by no one else, or by a reservation that has lapsed.</summary>
    public bool Free(long member, TUnit unit, double now) =>
        !_held.TryGetValue(unit, out var h) || h.Member == member || Lapsed(h.Since, now);

    /// <summary>Reserves all of <paramref name="units"/> for <paramref name="member"/>, or none if any is
    /// held by another (<see cref="Free"/>); its earlier reservation is freed either way it succeeds.
    /// True when they are its.</summary>
    public bool TryReserve(long member, IEnumerable<TUnit> units, double now)
    {
        var list = units.Distinct().ToList();
        if (!list.All(u => Free(member, u, now)))
            return false;
        Release(member);
        foreach (var unit in list)
        {
            if (_held.TryGetValue(unit, out var h) && h.Member != member)
                _holding.GetValueOrDefault(h.Member)?.Remove(unit);
            _held[unit] = (member, now);
        }
        _holding[member] = list;
        return true;
    }

    /// <summary>The member struck its units: their reservation runs again from <paramref name="now"/>.</summary>
    public void Struck(long member, double now)
    {
        foreach (var unit in _holding.GetValueOrDefault(member) ?? [])
            _held[unit] = (member, now);
    }

    /// <summary>Frees the member's units.</summary>
    public void Release(long member)
    {
        if (!_holding.Remove(member, out var units))
            return;
        foreach (var unit in units)
            if (_held.TryGetValue(unit, out var h) && h.Member == member)
                _held.Remove(unit);
    }

    /// <summary>The units the member holds, lapsed or not.</summary>
    public IReadOnlyList<TUnit> Held(long member) => _holding.GetValueOrDefault(member) ?? [];

    /// <summary>Who holds <paramref name="unit"/> at <paramref name="now"/>, or null.</summary>
    public long? Holder(TUnit unit, double now) =>
        _held.TryGetValue(unit, out var h) && !Lapsed(h.Since, now) ? h.Member : null;

    /// <summary>Frees reservations that have lapsed (they are never counted after
    /// <see cref="EidolonTeamwork.ReserveSeconds"/> anyway; this only tidies).</summary>
    public void Expire(double now)
    {
        foreach (var (unit, h) in _held.Where(e => Lapsed(e.Value.Since, now)).ToList())
        {
            _held.Remove(unit);
            _holding.GetValueOrDefault(h.Member)?.Remove(unit);
        }
    }

    private static bool Lapsed(double since, double now) => now - since >= EidolonTeamwork.ReserveSeconds;

    // ---- the job's level and its end ----

    /// <summary>The highest level the member has work on (mining: the highest layer with a block left
    /// it may break and has not given up), or null for none; its job works the highest of its present
    /// members' (<see cref="SharedJob{TShared}.Level"/>).</summary>
    public void SetLevel(long member, int? level) => _of.GetValueOrDefault(member)?.SetLevel(member, level);

    /// <summary>Whether the member has nothing left it can do in the job (and nothing it waits on, such
    /// as a part of the area to load).</summary>
    public void SetFinished(long member, bool finished) => _of.GetValueOrDefault(member)?.SetFinished(member, finished);

    /// <summary>Closes the member's job if it is done (<see cref="SharedJob{TShared}.Done"/>): true once,
    /// for the member that makes the report; its members then go back each to its own place.</summary>
    public bool TryClose(long member, double now)
    {
        if (_of.GetValueOrDefault(member) is not { } job || !job.Done(now))
            return false;
        job.Closed = true;
        return true;
    }
}

/// <summary>
/// Working together (README "Eidolon", working together), game-independent: the reservation's and
/// the wait's lengths, where a spare eidolon waits (<see cref="Rim"/>), and the report's shares
/// (<see cref="ShareText"/>).
/// </summary>
public static class EidolonTeamwork
{
    /// <summary>A reservation not struck within this lapses, should its holder be stuck without
    /// letting it go.</summary>
    public const double ReserveSeconds = 30;

    /// <summary>How long a job loaded again waits for a member it had to load (the chunk holds'
    /// <c>EidolonChunkSystem.RestoreSeconds</c>): after it, it may be done without it, and the member
    /// loading later joins a new job.</summary>
    public const double ExpectSeconds = 120;

    /// <summary>How far outside the area, in blocks between the area and its box, a spare eidolon
    /// waits first: a miner reaching the area's edge stands with its box at most one column clear of it.</summary>
    public const int RimGap = 2;

    /// <summary>How far apart, at least, two eidolons wait (their boxes 1.7 wide).</summary>
    public const double RimSpacing = 2.5;

    /// <summary>
    /// Where a spare eidolon may wait for work, out of the area and out of the way: path nodes (block
    /// corners, its box over the two columns each side) on rings round <paramref name="area"/>, its box
    /// <see cref="RimGap"/> columns clear of it first, then nearer (1, then 0 between); on each ring
    /// nearest (<paramref name="fromX"/>, <paramref name="fromZ"/>), none within <see cref="RimSpacing"/>
    /// of where another eidolon is or waits (<paramref name="taken"/>). Only x and z: the ground's
    /// height is the world's.
    /// </summary>
    public static IReadOnlyList<(int X, int Z)> Rim(MarkArea area, double fromX, double fromZ, IEnumerable<(double X, double Z)> taken)
    {
        var others = taken.ToList();
        var stands = new List<(int X, int Z, int Gap, double From)>();
        for (int gap = RimGap; gap >= 0; gap--)
        {
            // Its box covers columns x - 1 and x: clear of the area by gap columns west at
            // x = Min.X - 1 - gap, east at x = Max.X + 2 + gap.
            int west = area.Min.X - 1 - gap, east = area.Max.X + 2 + gap, north = area.Min.Z - 1 - gap, south = area.Max.Z + 2 + gap;
            var ring = new HashSet<(int, int)>();
            for (int x = west; x <= east; x++)
            {
                ring.Add((x, north));
                ring.Add((x, south));
            }
            for (int z = north; z <= south; z++)
            {
                ring.Add((west, z));
                ring.Add((east, z));
            }
            foreach (var (x, z) in ring)
            {
                if (others.Any(o => (o.X - x) * (o.X - x) + (o.Z - z) * (o.Z - z) < RimSpacing * RimSpacing))
                    continue;
                stands.Add((x, z, gap, (x - fromX) * (x - fromX) + (z - fromZ) * (z - fromZ)));
            }
        }
        return stands.OrderByDescending(s => s.Gap).ThenBy(s => s.From).ThenBy(s => s.X).ThenBy(s => s.Z)
            .Select(s => (s.X, s.Z)).ToList();
    }

    /// <summary>The report's shares as text: each with <paramref name="format"/> (<c>{0}</c> the name,
    /// <c>{1}</c> the count), joined by <paramref name="separator"/>, in <see cref="SharedJob{TShared}.Shares"/>'
    /// order.</summary>
    public static string ShareText(IEnumerable<JobShare> shares, string format, string separator = ", ") =>
        string.Join(separator, shares.Select(s => string.Format(System.Globalization.CultureInfo.InvariantCulture, format, s.Name, s.Count)));
}
