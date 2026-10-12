namespace SeraphHorizons.Mod.HandbookPlace.Core;

/// <summary>
/// Puts the handbook list's scroll back to <see cref="Target"/> (README, "The handbook keeps your
/// place"). The list may not be all there when it opens: Dovidarium fills the unsearched list 160
/// entries at a time as it is scrolled near its end, and runs a search it has not cached over a few
/// frames. So a pass sets the bar and reads where it ended up and how long the list now is, again
/// while the list grows; and if it still fell short, later ticks try again, until the target is
/// reached, the player moves the bar, or <see cref="MaxTicks"/> run out. Game-independent: the game
/// side passes what sets the bar and what reads it.
/// </summary>
public sealed class ListScroll
{
    /// <summary>How close counts as there, in GUI pixels.</summary>
    public const float Tolerance = 1f;

    /// <summary>Passes in one go while the list keeps growing (Dovidarium adds 160 entries a time).</summary>
    public const int MaxPassesPerTry = 64;

    public enum Outcome
    {
        /// <summary>At the target (or the list is gone): nothing more to do.</summary>
        Done,
        /// <summary>Short of the target, maybe because the list is still filling: try again next tick.</summary>
        Pending,
        /// <summary>Stopped: the player moved the bar, or the ticks ran out.</summary>
        Abandoned,
    }

    public float Target { get; }
    public int MaxTicks { get; }
    int ticks;
    float? left;

    public ListScroll(float target, int maxTicks)
    {
        Target = target;
        MaxTicks = maxTicks;
    }

    /// <summary>
    /// Sets the bar to the target and reads it back. <paramref name="apply"/> sets the bar to the value it
    /// is given and returns where the bar is and the list's total height afterwards (setting it can grow
    /// the list, and growing moves the bar).
    /// </summary>
    public Outcome Try(Func<float, (float Position, float Total)> apply)
    {
        float lastTotal = float.NegativeInfinity;
        (float Position, float Total) at = default;
        for (int i = 0; i < MaxPassesPerTry; i++)
        {
            at = apply(Target);
            if (Reached(at.Position)) break;
            // Short, or past it because the list grew under the bar: set it again on the longer list.
            // A list that did not grow is as long as it gets for now. (The first pass always gets a
            // second: what it started from is not known.)
            bool grew = at.Total > lastTotal;
            lastTotal = at.Total;
            if (!grew) break;
        }
        left = at.Position;
        return Reached(at.Position) ? Outcome.Done : Outcome.Pending;
    }

    /// <summary>
    /// A later tick: <paramref name="current"/> is where the bar is now. If it is not where the last try
    /// left it, the player (or a new search) moved it, and the place is theirs.
    /// </summary>
    public Outcome Tick(float current, Func<float, (float Position, float Total)> apply)
    {
        if (left is { } l && Math.Abs(current - l) > Tolerance) return Outcome.Abandoned;
        if (++ticks > MaxTicks) return Outcome.Abandoned;
        return Try(apply);
    }

    bool Reached(float position) => Math.Abs(position - Target) <= Tolerance;
}
