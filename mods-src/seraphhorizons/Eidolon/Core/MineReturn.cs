namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>
/// A mining eidolon's tries at its walk back to where it was ordered (README "Eidolon", mining): stopped short of
/// its place (stuck, off an edge into the pit) it sets off again up to <see cref="Tries"/> times; stopped short of
/// another standing in its way (a third of a block before their boxes touch, or falling in behind one ahead in
/// single file) it waits a moment and sets off again up to <see cref="Bumps"/> times; a walk that gets no nearer
/// its place for <see cref="StallSeconds"/> (pressing against one in its way, which the game's walk takes for
/// walking) is stopped short. Past either count the order ends where it stands. The stops short of another were
/// once free, so one walking home into a member that never moved (waiting for the job it had just reported done)
/// set off again every 3 seconds for ever, its order never ending and nothing told.
/// </summary>
public sealed class MineReturn
{
    /// <summary>Times it sets off back again when its walk back stops short of where it was ordered.</summary>
    public const int Tries = 3;

    /// <summary>Times it sets off back again after stopping short of another in its way: enough for members still at
    /// work to cross its way home many times over, not for one that stays in it.</summary>
    public const int Bumps = 10;

    /// <summary>How long a walk back may go on getting no nearer its place (by <see cref="Nearer"/>) before it counts
    /// as stopped short: longer than a wait to give way.</summary>
    public const double StallSeconds = 20;

    /// <summary>How much nearer its place a walk must come to count as getting somewhere.</summary>
    public const double Nearer = 0.5;

    private int _tries, _bumps;
    private double _best = double.PositiveInfinity, _bestAt;

    /// <summary>It sets off back (again) at <paramref name="now"/>: the walk's progress is measured from here.</summary>
    public void SetOff(double now) => (_best, _bestAt) = (double.PositiveInfinity, now);

    /// <summary>Whether the walk, <paramref name="distance"/> from its place at <paramref name="now"/>, has got no
    /// nearer for <see cref="StallSeconds"/>.</summary>
    public bool Stalled(double distance, double now)
    {
        if (distance < _best - Nearer)
            (_best, _bestAt) = (distance, now);
        return now - _bestAt >= StallSeconds;
    }

    /// <summary>Whether it sets off again after its walk stopped short of its place; false once it is out of tries.</summary>
    public bool StoppedShort() => ++_tries <= Tries;

    /// <summary>Whether it sets off again after stopping short of another in its way; false once it is out of bumps.</summary>
    public bool Bumped() => ++_bumps <= Bumps;

    /// <summary>The tries and bumps it has spent, for the log.</summary>
    public override string ToString() => $"{_tries}/{Tries} tries, {_bumps}/{Bumps} bumps";
}
