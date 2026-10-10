namespace SeraphHorizons.Mod.Eidolon.Core;

public enum BindResult
{
    Bound,
    AlreadyBound,
    Full,
}

/// <summary>
/// The eidolons a command tool is bound to (README "Eidolon", the command tool): a list of entity
/// ids on the tool. Binding is explicit (right-click each eidolon), so a tool never orders an eidolon
/// its holder did not bind, though the holder could command it, and a company's tools each command
/// the eidolons bound to them.
/// </summary>
public static class EidolonBindings
{
    /// <summary>The most eidolons one tool is bound to.</summary>
    public const int Max = 16;

    public static BindResult Bind(IList<long> bound, long id)
    {
        if (bound.Contains(id))
            return BindResult.AlreadyBound;
        if (bound.Count >= Max)
            return BindResult.Full;
        bound.Add(id);
        return BindResult.Bound;
    }

    public static bool Unbind(IList<long> bound, long id) => bound.Remove(id);
}

public enum FollowGait
{
    Stand,
    Walk,
    Run,
}

/// <summary>
/// Following (README "Eidolon", following): how it moves for the distance to the one it follows. It
/// stands within <c>near</c> blocks and sets off only once they are <see cref="StartSlack"/> further,
/// so it does not shuffle after every step; it runs beyond <c>runFrom</c> and walks again once back
/// within <see cref="RunSlack"/> of it.
/// </summary>
public static class EidolonFollow
{
    public const double StartSlack = 1.5;
    public const double RunSlack = 3;

    public static FollowGait Gait(double distance, double near, double runFrom, FollowGait now)
    {
        if (distance <= near || (now == FollowGait.Stand && distance <= near + StartSlack))
            return FollowGait.Stand;
        if (distance > runFrom || (now == FollowGait.Run && distance > runFrom - RunSlack))
            return FollowGait.Run;
        return FollowGait.Walk;
    }

    /// <summary>Whether the one followed has moved far enough from where its path leads
    /// (<paramref name="moved"/> blocks) to search again: a quarter of the distance left, at least two
    /// blocks, so a far path is not searched again for every step.</summary>
    public static bool Repath(double moved, double distance) => moved > Math.Max(2, distance / 4);
}

/// <summary>
/// Self-defence (README "Eidolon", self-defence): it strikes back at a creature that hurt it, never
/// a player or another eidolon, while the creature lives, is within <c>range</c> and hurt it no longer
/// than <c>memory</c> seconds ago (each blow it lands keeps the fight going). Blows go a punch, a
/// kick and a slam in turn.
/// </summary>
public static class EidolonDefence
{
    public static bool Engages(bool attackerIsPlayer, bool attackerIsEidolon, bool attackerAlive, double secondsSinceHurt, double distance,
        double memory, double range) =>
        !attackerIsPlayer && !attackerIsEidolon && attackerAlive && secondsSinceHurt >= 0 && secondsSinceHurt <= memory && distance <= range;

    /// <summary>The <paramref name="n"/>th blow: its animation code (the entity type's), its length and
    /// when in it the blow lands, in seconds, and whether it is the heavy one. A punch, a kick and a
    /// slam in turn (the shape's <c>stand-punch</c>, 45 frames, and <c>stand-kick</c>, 35, both landing
    /// on frame 20; <c>stand-slam</c>, 45 frames, both fists coming down on frame 40, where the
    /// archives' eidolon's own slam lets go), at 30 frames a second.</summary>
    public static (string Animation, double Seconds, double HitAt, bool Slam) Blow(int n) =>
        (n % 3) switch
        {
            0 => ("punch", 45 / 30.0, 20 / 30.0, false),
            1 => ("kick", 35 / 30.0, 20 / 30.0, false),
            _ => ("slam", 45 / 30.0, 40 / 30.0, true),
        };
}
