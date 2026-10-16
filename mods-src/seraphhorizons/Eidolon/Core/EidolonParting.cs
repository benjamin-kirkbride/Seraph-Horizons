namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>
/// Two of a crew by routes whose boxes touch part (README "Eidolon", working together, *Routes*;
/// <c>MineOrder.Untangle</c>): one steps away and the other waits for it. Game-independent: who moves, and
/// how long a step away is left to go on before they look again (#971: two walking home in a spiral pit, the
/// one stepping away climbing out beside the other, its climb stopped and begun again every
/// <see cref="TouchSeconds"/> for ever, its box never clear of the other's before the climb's top).
/// </summary>
public static class EidolonParting
{
    /// <summary>How long its box may stay in another's before they part.</summary>
    public const double TouchSeconds = 0.6;

    /// <summary>How long a step away it set off on to part is left to go on while their boxes still touch
    /// before it looks again: a climb (<see cref="EidolonClimb.Seconds"/>, its box over the foot until the
    /// climb's last third) and a few steps on.</summary>
    public const double StepSeconds = 4;

    /// <summary>How long two may stay touching, unparted, before the one that steps away and the one that
    /// waits change over (and again each time after): where the first finds no way apart, or gets nowhere
    /// on it, the other tries.</summary>
    public const double SwapSeconds = 10;

    /// <summary>Whether it steps away now (else it waits for the other): <paramref name="givesWay"/> by the
    /// rule of who gives way, turned round for each whole <see cref="SwapSeconds"/> the two have touched
    /// unparted (<paramref name="touching"/>).</summary>
    public static bool Moves(bool givesWay, double touching) =>
        givesWay != (touching >= 0 && (long)Math.Floor(touching / SwapSeconds) % 2 == 1);

    /// <summary>Whether a step away it set off on at <paramref name="since"/> (null: none) is left to go on
    /// at <paramref name="now"/>: it is still walking or climbing it (<paramref name="walking"/>), and has
    /// been for less than <see cref="StepSeconds"/>.</summary>
    public static bool KeepsStepping(double? since, bool walking, double now) =>
        since is { } from && walking && now - from < StepSeconds;
}
