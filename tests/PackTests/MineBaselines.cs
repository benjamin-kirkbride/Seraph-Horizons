namespace SeraphHorizons.PackTests;

/// <summary>Today's makespans (seconds of game time from the order to every order ended) of
/// <see cref="EidolonLookaheadScenarios"/>' cases, measured with <c>SH_MINE_BASELINE=1</c> (look-ahead routes
/// off: every job dispatched as before routes) on the virtual clock (<see cref="VirtualClock"/>), the mean of
/// three local runs on 2026-10-15, for the routes' must-not-be-slower check. Today's own runs still spread
/// (lowest to highest): tunnel of three 105.9-115.6, trench of two 146.5-157.1, pit of three 153.4-169.3,
/// the rest within 5 % (the engine's physics and chunk threads are not on the virtual clock; these runs
/// predate the planner's main-thread slices being counted in blocks read, <c>EidolonMinePlans.BudgetReads</c>).
/// Twelve more runs on 2026-10-16 (two servers at once) kept the means within 4 % of these and spread
/// further, up to 17 % over them (the tunnel of three, 103.4-129.8; pit of three 147.3-179.1; trench of
/// three 138.4-163.0; pit of two 164.0-182.6), which is why the check's bounds are set past the most seen,
/// not from these means (#1000).</summary>
internal static class MineBaselines
{
    public const double Pit2 = 166.9, Pit3 = 160.7;
    public const double Trench2 = 152.4, Trench3 = 147.5;
    public const double Tunnel2 = 95.0, Tunnel3 = 110.8;
    public const double Spiral2 = 141.5, Spiral3 = 152.3;
}
