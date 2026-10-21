namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>
/// A mining eidolon's tries at its walk back to where it was ordered (README "Eidolon", mining): stopped short of
/// its place (stuck, off an edge into the pit) it sets off again up to <see cref="Tries"/> times; stopped short of
/// another standing in its way (a crew by routes stops a third of a block before their boxes touch) it waits a
/// moment and sets off again up to <see cref="Bumps"/> times. Past either the order ends where it stands. The stops
/// short of another were once free, so one walking home into a member that never moved (waiting for the job it had
/// just reported done) set off again every 3 seconds for ever, its order never ending and nothing told.
/// </summary>
public sealed class MineReturn
{
    /// <summary>Times it sets off back again when its walk back stops short of where it was ordered.</summary>
    public const int Tries = 3;

    /// <summary>Times it sets off back again after stopping short of another in its way: enough for members still at
    /// work to cross its way home many times over, not for one that stays in it.</summary>
    public const int Bumps = 10;

    private int _tries, _bumps;

    /// <summary>Whether it sets off again after its walk stopped short of its place; false once it is out of tries.</summary>
    public bool StoppedShort() => ++_tries <= Tries;

    /// <summary>Whether it sets off again after stopping short of another in its way; false once it is out of bumps.</summary>
    public bool Bumped() => ++_bumps <= Bumps;

    /// <summary>The tries and bumps it has spent, for the log.</summary>
    public override string ToString() => $"{_tries}/{Tries} tries, {_bumps}/{Bumps} bumps";
}
