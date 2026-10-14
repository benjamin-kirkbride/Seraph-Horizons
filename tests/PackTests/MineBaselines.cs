namespace SeraphHorizons.PackTests;

/// <summary>Today's makespans (seconds of game time from the order to every order ended) of
/// <see cref="EidolonLookaheadScenarios"/>' cases, measured with <c>SH_MINE_BASELINE=1</c> (look-ahead routes
/// off: every job dispatched as before routes), one local run each on 2026-10-14, for the routes'
/// must-not-be-slower check. (Today's three in the trench brushed for 5 s once, over the contact rule the
/// routes are held to.)</summary>
internal static class MineBaselines
{
    public const double Pit2 = 165.2, Pit3 = 158.2;
    public const double Trench2 = 139.6, Trench3 = 152.7;
    public const double Tunnel2 = 95.0, Tunnel3 = 126.7;
    public const double Spiral2 = 144.4, Spiral3 = 153.1;
}
