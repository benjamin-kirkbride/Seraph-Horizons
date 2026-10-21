namespace SeraphHorizons.PackTests;

/// <summary>Today's makespans (seconds of game time from the order to every order ended) of
/// <see cref="EidolonLookaheadScenarios"/>' cases, measured with <c>SH_MINE_BASELINE=1</c> (look-ahead routes
/// off: every job dispatched as before routes) on the virtual clock (<see cref="VirtualClock"/>), for the routes'
/// must-not-be-slower check. The clock plays each run alike (its seed, <see cref="VirtualClock.RandSeed"/>, the same),
/// so these are the one makespan each case has, on 2026-10-21; earlier ones (the mean of runs that spread by up to
/// 17 %, #1000) are in the history. The check's bounds are kept from then, set past the most seen.</summary>
internal static class MineBaselines
{
    public const double Pit2 = 166.1, Pit3 = 163.0;
    public const double Trench2 = 168.1, Trench3 = 134.2;
    public const double Tunnel2 = 91.4, Tunnel3 = 114.8;
    public const double Spiral2 = 139.0, Spiral3 = 149.3;
}
