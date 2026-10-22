namespace SeraphHorizons.Mod.Arrastra.Core;

/// <summary>What the arrastra's pit holds (#732): nothing, a charge of crushed rock being ground, or the
/// ground charge (the paste) waiting to be drawn.</summary>
public enum PitState { Empty, Charged, Ground }

/// <summary>Why a held stack does or does not go into the pit.</summary>
public enum LoadVerdict
{
    Loads,
    /// <summary>Not crushed ore: the arrastra grinds only crushed rock.</summary>
    NotCrushed,
    /// <summary>The charge is full (<see cref="ArrastraCharge.Capacity"/> rock).</summary>
    Full,
    /// <summary>The ground charge is still in the pit: draw it first.</summary>
    Ground,
    /// <summary>Another ore or grade is in the pit: a charge is one ore and grade.</summary>
    Mixed,
}

/// <summary>Why a bucket does or does not water the pit.</summary>
public enum WaterVerdict
{
    Pours,
    /// <summary>The reservoir (the pit's water) is full.</summary>
    Full,
    /// <summary>The ground charge is in the pit: it needs no more water.</summary>
    Ground,
}

/// <summary>
/// The arrastra's batch (#732, epic #684): game-independent, so its rules are tested without the game.
/// <para>The player tips crushed rock into the pit, up to the charge (16 rock, a stack) of one ore and grade
/// (it lies in the pit as one stack), and pours water after it, 1.25 L a rock, two buckets (20 L) a full
/// charge (the owner's ruling: watered by bucket, no pipe). The drag stones grind the
/// charge while the shaft turns (rate in proportion to speed, #711) and the pit holds the charge's water:
/// with the charge dry it does not grind. A full charge takes <c>chargeHours</c> (7.5) at the reference
/// speed, <see cref="TurnsFor"/> turns; a part charge its share of that, so a rock always takes the same
/// work. When the turns are in, the whole charge is ground at once, its water spent, and it waits in the
/// pit as paste until the player draws it out by the gate. Nothing is lost (#765): each crushed rock
/// becomes one ground rock of the same ore and grade, its stream lifted by <c>OreState.Ground</c>.</para>
/// </summary>
public sealed class ArrastraCharge
{
    private readonly List<string> _rocks = [];

    public ArrastraCharge(int capacity) => Capacity = Math.Max(1, capacity);

    /// <summary>Rock a charge holds (the config's <c>charge</c>).</summary>
    public int Capacity { get; }

    /// <summary>The codes of the rock in the pit, in the order tipped in (crushed while charged, ground once
    /// ground).</summary>
    public IReadOnlyList<string> Rocks => _rocks;

    public int Count => _rocks.Count;

    public bool IsGround { get; private set; }

    /// <summary>Shaft turns of grinding done on this charge.</summary>
    public double Turns { get; private set; }

    public PitState State => _rocks.Count == 0 ? PitState.Empty : IsGround ? PitState.Ground : PitState.Charged;

    /// <summary>The turns a charge of <paramref name="rocks"/> takes: <paramref name="turnsPerCharge"/>
    /// (a full charge's) in proportion to the rock in it.</summary>
    public static double TurnsFor(int rocks, int capacity, double turnsPerCharge) =>
        capacity > 0 && turnsPerCharge > 0 ? turnsPerCharge * Math.Max(0, rocks) / capacity : 0;

    /// <summary>Litres the charge needs in the pit to grind: <paramref name="perRock"/> a rock.</summary>
    public double WaterNeeded(double perRock) => Math.Max(0, perRock) * (IsGround ? 0 : _rocks.Count);

    /// <summary>The share of the grind done, 0 to 1.</summary>
    public double Progress(double turnsPerCharge)
    {
        double need = TurnsFor(_rocks.Count, Capacity, turnsPerCharge);
        return IsGround ? 1 : need > 0 ? Math.Clamp(Turns / need, 0, 1) : 0;
    }

    /// <summary>Whether <paramref name="code"/> is crushed ore (<c>seraphhorizons:crushedore-{ore}-{grade}</c>).</summary>
    public static bool IsCrushed(string? code) =>
        code != null && code.StartsWith("seraphhorizons:crushedore-", StringComparison.Ordinal) && GroundCode(code) != null;

    /// <summary>The ground ore a crushed rock grinds to: <c>crushedore-{ore}-{grade}</c> to
    /// <c>groundore-{ore}-{grade}</c>; null for anything else.</summary>
    public static string? GroundCode(string? crushed)
    {
        const string from = "seraphhorizons:crushedore-";
        if (crushed == null || !crushed.StartsWith(from, StringComparison.Ordinal) || crushed.Length == from.Length)
            return null;
        string rest = crushed[from.Length..];
        return rest.Contains('-') ? "seraphhorizons:groundore-" + rest : null;
    }

    /// <summary>What tipping <paramref name="code"/> in would do.</summary>
    public LoadVerdict CanLoad(string? code)
    {
        if (!IsCrushed(code))
            return LoadVerdict.NotCrushed;
        if (IsGround)
            return LoadVerdict.Ground;
        if (_rocks.Count > 0 && _rocks[0] != code)
            return LoadVerdict.Mixed;
        return _rocks.Count >= Capacity ? LoadVerdict.Full : LoadVerdict.Loads;
    }

    /// <summary>Tips one rock in, if <see cref="CanLoad"/> allows. The turns already done stay: a rock
    /// added part way through takes its own share of the work on top of them.</summary>
    public LoadVerdict Load(string? code)
    {
        var verdict = CanLoad(code);
        if (verdict == LoadVerdict.Loads)
            _rocks.Add(code!);
        return verdict;
    }

    /// <summary>What a bucket would do, the pit holding <paramref name="litres"/> of <paramref name="capacity"/>.</summary>
    public WaterVerdict CanWater(double litres, double capacity)
    {
        if (IsGround)
            return WaterVerdict.Ground;
        return litres + 1e-9 >= capacity ? WaterVerdict.Full : WaterVerdict.Pours;
    }

    /// <summary>Whether the charge grinds now: charged (not yet ground) and the pit holding its water.</summary>
    public bool CanGrind(double litres, double perRock) =>
        State == PitState.Charged && litres + 1e-9 >= WaterNeeded(perRock);

    /// <summary>
    /// <paramref name="radians"/> of shaft turning while the machine works (<c>ShaftClock.AngleAdvance</c>).
    /// Returns true when that finished the grind: every rock is then its ground ore, and the caller spends
    /// the charge's water (<see cref="WaterNeeded"/> before the call) from the pit.
    /// </summary>
    public bool Work(double radians, double turnsPerCharge)
    {
        if (State != PitState.Charged || !double.IsFinite(radians) || radians <= 0)
            return false;
        Turns += radians / (2 * Math.PI);
        if (Turns + 1e-9 < TurnsFor(_rocks.Count, Capacity, turnsPerCharge))
            return false;
        for (int i = 0; i < _rocks.Count; i++)
            _rocks[i] = GroundCode(_rocks[i]) ?? _rocks[i];
        IsGround = true;
        Turns = 0;
        return true;
    }

    /// <summary>Draws the pit out by the gate: whatever is in it (the paste, or a charge not yet ground,
    /// which comes back crushed and loses its turns), leaving it empty. The water stays in the pit.</summary>
    public IReadOnlyList<string> Draw()
    {
        var drawn = _rocks.ToList();
        _rocks.Clear();
        IsGround = false;
        Turns = 0;
        return drawn;
    }

    /// <summary>Restored from a save: rock past the charge, or not of the right form, is returned in
    /// <paramref name="dropped"/> rather than lost.</summary>
    public static ArrastraCharge Restore(int capacity, IEnumerable<string> rocks, bool ground, double turns,
        out IReadOnlyList<string> dropped)
    {
        var charge = new ArrastraCharge(capacity);
        var lost = new List<string>();
        foreach (var code in rocks)
        {
            bool fits = (ground ? code.StartsWith("seraphhorizons:groundore-", StringComparison.Ordinal) : IsCrushed(code))
                && (charge._rocks.Count == 0 || charge._rocks[0] == code);
            if (fits && charge._rocks.Count < charge.Capacity)
                charge._rocks.Add(code);
            else
                lost.Add(code);
        }
        charge.IsGround = ground && charge._rocks.Count > 0;
        charge.Turns = charge.IsGround || !double.IsFinite(turns) ? 0 : Math.Max(0, turns);
        dropped = lost;
        return charge;
    }
}
