namespace SeraphHorizons.Mod.Rosser.Core;

/// <summary>
/// The renderer's inputs beyond θ and ψ (design §2.1, §4.8, §4.10), all client-side:
/// <list type="bullet">
/// <item>T: the client's travel estimate, advanced with the shaft between syncs
/// (<see cref="EstimateTravel"/>), reset to the server's when they part (<see cref="Reconcile"/>),
/// and interpolated per frame between the last two ticks.</item>
/// <item>k: the shown class, held while the presence eases out (<see cref="ShownClass"/>).</item>
/// <item>p: presence, eased toward 1 while a trunk is in and 0 when not (<see cref="EasePresence"/>).</item>
/// <item>φ: feed travel. The feed is geared and never slips, so φ follows T exactly:
/// it grows by ΔT / <c>feed.blocksPerRadian</c> (<see cref="FeedAdvance"/>) and never decreases,
/// so a new trunk (T back to 0) does not turn the rolls back.</item>
/// </list>
/// </summary>
public static class RosserVisuals
{
    /// <summary>The rate, per second, at which presence closes on its target (as the mill's saw depth eases).</summary>
    public const float EaseRate = 12f;

    /// <summary>A sync this far (blocks) from the client's estimate replaces it.</summary>
    public const double ResyncBlocks = 1.0 / 16;

    /// <summary>p after <paramref name="dt"/> seconds: toward 1 while <paramref name="loaded"/>,
    /// else toward 0, landing on it once within a hair.</summary>
    public static float EasePresence(float presence, bool loaded, float dt)
    {
        float target = loaded ? 1 : 0;
        float next = presence + (target - presence) * (1 - MathF.Exp(-EaseRate * Math.Max(dt, 0)));
        return Math.Abs(target - next) < 0.002f ? target : Math.Clamp(next, 0, 1);
    }

    /// <summary>The class the rig is posed with: the loaded trunk's, or with none the last shown one
    /// until the presence has eased to 0, so the parts ease back rather than jump.</summary>
    public static int ShownClass(int loadedClass, int lastShown, float presence) =>
        loadedClass is 1 or 2 ? loadedClass : presence > 0 ? lastShown : 0;

    /// <summary>The client's T after a tick of <paramref name="radians"/> while running: the
    /// synced rate times the turning, never past <paramref name="end"/>.</summary>
    public static double EstimateTravel(double travel, double rate, double radians, double end) =>
        Math.Min(end, travel + Math.Max(0, rate) * Math.Max(0, radians));

    /// <summary>The client's T after a sync: the server's when they differ by more than
    /// <see cref="ResyncBlocks"/>, else its own estimate (so the trunk does not jitter).</summary>
    public static double Reconcile(double estimate, double server) =>
        Math.Abs(estimate - server) > ResyncBlocks ? server : estimate;

    /// <summary>T shown in a frame <paramref name="fraction"/> (0..1) of the way from the last tick to this one.</summary>
    public static double Interpolate(double lastTick, double thisTick, float fraction) =>
        lastTick + (thisTick - lastTick) * Math.Clamp(fraction, 0, 1);

    /// <summary>How much φ grows when the shown T goes from <paramref name="lastTravel"/> to
    /// <paramref name="travel"/>: ΔT / <paramref name="blocksPerFeedRadian"/>, 0 when T went back
    /// (a new trunk, or a sync behind the estimate).</summary>
    public static double FeedAdvance(double lastTravel, double travel, double blocksPerFeedRadian) =>
        blocksPerFeedRadian > 0 && travel > lastTravel ? (travel - lastTravel) / blocksPerFeedRadian : 0;
}
