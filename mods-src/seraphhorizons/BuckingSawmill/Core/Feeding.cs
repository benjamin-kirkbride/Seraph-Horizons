namespace SeraphHorizons.Mod.BuckingSawmill.Core;

/// <summary>What the Trunk Storage Rack at the mill's infeed end offers, as the server last found
/// it (synced, so the block info can say why no trunk is coming).</summary>
public enum RackState
{
    /// <summary>Not looked yet (a client before the first sync).</summary>
    Unknown,
    /// <summary><c>AutoPullFromRack</c> is off.</summary>
    Off,
    /// <summary>Logging Expanded is not as the mill expects.</summary>
    NoLogging,
    /// <summary>No rack has a cell in any of the ground cells just outside the infeed end.</summary>
    None,
    /// <summary>A rack is there with no trunk on it.</summary>
    Empty,
    /// <summary>The rack's top trunk still has its branches, which Logging Expanded wants off first.</summary>
    Branched,
    /// <summary>The rack's top trunk holds no logs.</summary>
    NoLogs,
    /// <summary>The rack's top trunk goes on at the next top of the cycle.</summary>
    Ready,
}

/// <summary>
/// Feeding the mill by hand. A trunk goes on only while the saws are at the top of their cycle;
/// a click at any other time waits for it while the button is held (the mill takes the trunk on the
/// tick its saws pass the top, as it takes a rack's), and a stopped mill's saws can be wound up by
/// hand with an empty hand.
/// </summary>
public static class Feeding
{
    /// <summary>Seconds a player holding right-click takes to wind the saws up the whole travel,
    /// from the bed to the top, while the mill is not turning.</summary>
    public const float HandWindSeconds = 2f;

    /// <summary>A hold to load that never got its stop (the player left) lapses after this.</summary>
    public const float HoldLapseSeconds = 120f;

    /// <summary>The depth after <paramref name="seconds"/> of winding by hand: the whole travel in
    /// <see cref="HandWindSeconds"/>, never past the top.</summary>
    public static float Wind(float depth, float seconds, float windSeconds = HandWindSeconds)
    {
        if (seconds <= 0)
            return depth;
        if (windSeconds <= 0)
            return 0;
        return Math.Clamp(depth - seconds / windSeconds, 0, 1);
    }

    /// <summary>Whether an empty-handed right click winds the saws up: the mill is not running (not
    /// assembled, or not turning fast enough) and the saws are not at the top. Otherwise it loads a
    /// trunk from the player's inventory.</summary>
    public static bool WindsUp(bool running, float depth) => !running && !SawDepth.AtTop(depth);

    /// <summary>Whether a winding already begun goes on: the mill is still not running and the saws
    /// are not all the way up (winding starts only outside the load window, but finishes at the top).</summary>
    public static bool KeepsWinding(bool running, float depth) => !running && depth > 0;

    /// <summary>Whether a trunk can go on this tick: at the top of the cycle, or the cycle passed it
    /// during the tick.</summary>
    public static bool CanTakeTrunk(float depth, bool passedTop) => passedTop || SawDepth.AtTop(depth);
}
