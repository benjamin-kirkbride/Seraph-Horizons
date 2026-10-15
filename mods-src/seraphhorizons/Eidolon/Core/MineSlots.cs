namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>A member's time in a narrow way (README "Eidolon", working together, *Narrow ways*): from
/// <paramref name="From"/> to <paramref name="To"/> (+∞: it holds it past the horizon), on the leg to (or
/// at the stand of) its stop <paramref name="Stop"/> (<see cref="MineStop.Seq"/>).</summary>
public sealed record MineSlot(int Zone, long Member, double From, double To, int Stop);

/// <summary>Who may be in each narrow way when (README "Eidolon", working together, *Narrow ways*): a
/// member's intervals in each zone, never two members' at once, <see cref="Gap"/> between. Intervals are
/// placed in the order the plan decides and never moved by a later placement, so the planned timeline has
/// no cycle (none holds one way waiting for another held by one waiting for the first), and a member waits
/// for its turn where it is, outside them (<see cref="Earliest"/>: every zone of a leg must fit at once).</summary>
public sealed class MineSlots
{
    /// <summary>Seconds kept between two members in one zone.</summary>
    public const double Gap = 1.5;

    private const double Eps = 1e-9;

    private readonly List<MineSlot> _slots = new();

    public MineSlots Clone()
    {
        var copy = new MineSlots();
        copy._slots.AddRange(_slots);
        return copy;
    }

    public IReadOnlyList<MineSlot> All => _slots;

    public IReadOnlyList<MineSlot> Of(int zone) => _slots.Where(s => s.Zone == zone).OrderBy(s => s.From).ToList();

    public IReadOnlyList<MineSlot> Of(long member) => _slots.Where(s => s.Member == member).OrderBy(s => s.From).ToList();

    /// <summary>The earliest t ≥ <paramref name="from"/> at which a leg whose zone runs start at offsets
    /// Enter and end at Leave (seconds after departing; Leave +∞ for a stand in the zone, held on) fits
    /// every zone it crosses, each interval <see cref="Gap"/> clear of the other members'; +∞ for never.</summary>
    public double Earliest(long member, double from, IReadOnlyList<(int Zone, double Enter, double Leave)> runs)
    {
        double t = from;
        if (runs.Count == 0)
            return t;
        // each conflict moves t to the least time past it; no time between is free of that one
        for (int round = 0; round < 10_000; round++)
        {
            bool moved = false;
            foreach (var (zone, enter, leave) in runs)
            foreach (var s in _slots)
            {
                if (s.Zone != zone || s.Member == member)
                    continue;
                double a0 = t + enter, a1 = t + leave;
                if (a0 < s.To + Gap - Eps && s.From < a1 + Gap - Eps)
                {
                    if (double.IsPositiveInfinity(s.To))
                        return double.PositiveInfinity;
                    t = s.To + Gap - enter;
                    moved = true;
                }
            }
            if (!moved)
                return t;
        }
        return double.PositiveInfinity;
    }

    /// <summary>Takes the member's intervals for a leg departing at <paramref name="depart"/>: one a run, or
    /// its interval in that zone stretched when it holds it from before (it set off from a stand in it).</summary>
    public void Reserve(long member, int stop, double depart, IReadOnlyList<(int Zone, double Enter, double Leave)> runs)
    {
        foreach (var (zone, enter, leave) in runs)
        {
            double from = depart + enter, to = depart + leave;
            int held = Holding(member, zone, from);
            if (held >= 0)
            {
                var h = _slots[held];
                // held on at a stand (+∞): cut to when it leaves; else run on to the later end
                _slots[held] = h with { To = double.IsPositiveInfinity(h.To) ? Math.Max(to, h.From) : Math.Max(h.To, to), Stop = stop };
            }
            else
                _slots.Add(new MineSlot(zone, member, from, to, stop));
        }
    }

    // the member's interval in a zone that runs on to (or past) a time: the stand it sets off from is in it
    private int Holding(long member, int zone, double at)
    {
        int found = -1;
        for (int k = 0; k < _slots.Count; k++)
        {
            var s = _slots[k];
            if (s.Member == member && s.Zone == zone && s.To >= at - Gap && s.From <= at + Eps && (found < 0 || s.From > _slots[found].From))
                found = k;
        }
        return found;
    }

    /// <summary>Working at a stand in the zone: its last interval there held on to <paramref name="to"/>
    /// (+∞ until its next departure is placed).</summary>
    public void Extend(long member, int zone, double to)
    {
        int last = -1;
        for (int k = 0; k < _slots.Count; k++)
            if (_slots[k].Member == member && _slots[k].Zone == zone && (last < 0 || _slots[k].From > _slots[last].From))
                last = k;
        if (last >= 0)
            _slots[last] = _slots[last] with { To = Math.Max(to, _slots[last].From) };
    }

    /// <summary>Holds a zone from <paramref name="from"/> to <paramref name="to"/> for a member already in it
    /// (a pinned stop's stand, a wait).</summary>
    public void Hold(long member, int stop, int zone, double from, double to) =>
        _slots.Add(new MineSlot(zone, member, from, to, stop));

    /// <summary>Drops the member's intervals from stop <paramref name="fromStop"/> on (repair).</summary>
    public void Release(long member, int fromStop) => _slots.RemoveAll(s => s.Member == member && s.Stop >= fromStop);

    /// <summary>Drops every interval of the member.</summary>
    public void Release(long member) => _slots.RemoveAll(s => s.Member == member);

    /// <summary>Whether any two members' intervals in one zone come within <see cref="Gap"/> (tests).</summary>
    public bool Overlaps()
    {
        foreach (var zone in _slots.GroupBy(s => s.Zone))
        {
            var list = zone.OrderBy(s => s.From).ToList();
            for (int i = 0; i < list.Count; i++)
            for (int j = i + 1; j < list.Count && list[j].From < list[i].To + Gap - 1e-6; j++)
                if (list[i].Member != list[j].Member)
                    return true;
        }
        return false;
    }
}
