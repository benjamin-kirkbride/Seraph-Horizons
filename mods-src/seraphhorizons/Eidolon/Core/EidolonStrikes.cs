namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>
/// One blow (README "Eidolon", self-defence): the animation code (the entity type's), its length and
/// the frame it lands on (30 a second), how far it reaches as the gap between the two hitboxes, and
/// whether it is struck on the move (the upper body only, over the walk or run, so it keeps after the
/// creature) or standing.
/// </summary>
public sealed record Strike(string Animation, int Frames, int HitFrame, double Reach, bool Moving)
{
    public double Seconds => Frames / EidolonStrikes.Fps;

    /// <summary>When in the animation it lands, in seconds.</summary>
    public double HitAt => HitFrame / EidolonStrikes.Fps;
}

/// <summary>
/// Which blow it strikes and whether it lands (README "Eidolon", self-defence). A creature standing
/// within reach gets the standing blows, a punch, a kick and a slam in turn; one on the move gets the
/// blows struck on the move (a jab, a jab and a two-fisted hammer, or only hammers for a creature too
/// low for a jab), while the eidolon keeps closing on it. A blow is started only when, its own way
/// (standing or closing as it moves now) and the creature's motion carried on to the moment it lands,
/// the gap between them will then be within its reach; it lands only if, at that moment, the creature
/// really is within its reach (a little slack for the swing's sweep), in front, and level with it. Every
/// third blow is the heavy one (<see cref="EidolonConfig.SlamDamage"/>).
/// </summary>
public static class EidolonStrikes
{
    public const double Fps = 30;

    // Frames and reaches from the shape (Eidolon/tools/make_shape.py: the moving strikes' EVENTS "hit"
    // frames; tools/tests/test_eidolon_model.py holds these to the shape's fists). The reach is how far
    // past its own 1.7-wide box the fists get at the hit frame (0.85 from the centre to the box's side).

    /// <summary>Vanilla's <c>stand-punch</c>: the right fist comes over and out at frame 24, 1.5 blocks in front.</summary>
    public static readonly Strike Punch = new("punch", Frames: 45, HitFrame: 24, Reach: 0.8, Moving: false);

    /// <summary>Vanilla's <c>stand-kick</c>: the right foot out at frame 20, 1.8 blocks in front.</summary>
    public static readonly Strike Kick = new("kick", Frames: 35, HitFrame: 20, Reach: 1.0, Moving: false);

    /// <summary>Vanilla's <c>stand-slam</c>: both fists reach the ground at frame 35, 1.55 blocks in front.</summary>
    public static readonly Strike Slam = new("slam", Frames: 45, HitFrame: 35, Reach: 0.8, Moving: false);

    /// <summary>The authored <c>strike-jab</c>: a straight right at frame 9, 1.9 blocks in front.</summary>
    public static readonly Strike Jab = new("strike-jab", Frames: 24, HitFrame: 9, Reach: 1.1, Moving: true);

    /// <summary>The authored <c>strike-hammer</c>: both fists down at frame 14, 1.4 blocks in front, a block up.</summary>
    public static readonly Strike Hammer = new("strike-hammer", Frames: 30, HitFrame: 14, Reach: 0.6, Moving: true);

    /// <summary>How much further than its reach a blow still lands at its hit frame, in blocks: the fist's
    /// own size and the sweep of the swing, which the hitboxes' gap does not see.</summary>
    public const double HitSlack = 0.5;

    /// <summary>How far to either side of where it faces a blow lands, in degrees.</summary>
    public const double HitArcDegrees = 75;

    /// <summary>A creature whose top is lower than this, in blocks above its feet, is struck on the
    /// move with the hammer only (the jab would go over it).</summary>
    public const double LowTarget = 1.5;

    /// <summary>A creature slower than this, in blocks a second, counts as standing.</summary>
    public const double StillSpeed = 0.5;

    /// <summary>Within this gap, in blocks, it stops closing and stands (every blow reaches it).</summary>
    public const double StandOff = 0.4;

    /// <summary>The furthest ahead it aims at a creature on the move, in seconds of its motion.</summary>
    public const double MaxLeadSeconds = 1.0;

    public static bool Heavy(int n) => n % 3 == 2;

    public static Strike Standing(int n) => (n % 3) switch
    {
        0 => Punch,
        1 => Kick,
        _ => Slam,
    };

    public static Strike OnTheMove(int n, bool lowTarget) => lowTarget || Heavy(n) ? Hammer : Jab;

    /// <summary>The gap between the boxes after <paramref name="seconds"/>, the creature at
    /// (<paramref name="dx"/>, <paramref name="dz"/>) from the eidolon (centres, across) moving at
    /// (<paramref name="vx"/>, <paramref name="vz"/>) blocks a second relative to it;
    /// <paramref name="radii"/> is the two boxes' half widths together.</summary>
    public static double GapAfter(double dx, double dz, double vx, double vz, double seconds, double radii)
    {
        double x = dx + vx * seconds, z = dz + vz * seconds;
        return Math.Sqrt(x * x + z * z) - radii;
    }

    /// <summary>
    /// The <paramref name="n"/>th blow to strike now, or null to keep closing. Standing, when the
    /// creature is (nearly) still and stays within the standing blow's reach until it lands; else on the
    /// move, when, the eidolon moving on as it moves now (<paramref name="ownVx"/>, <paramref name="ownVz"/>),
    /// the creature is within the moving blow's reach when it lands.
    /// </summary>
    public static Strike? Choose(int n, double dx, double dz, double radii, double targetVx, double targetVz, double ownVx, double ownVz,
        bool lowTarget)
    {
        var standing = Standing(n);
        double targetSpeed = Math.Sqrt(targetVx * targetVx + targetVz * targetVz);
        if (targetSpeed < StillSpeed && GapAfter(dx, dz, targetVx, targetVz, standing.HitAt, radii) <= standing.Reach)
            return standing;
        var moving = OnTheMove(n, lowTarget);
        return GapAfter(dx, dz, targetVx - ownVx, targetVz - ownVz, moving.HitAt, radii) <= moving.Reach ? moving : null;
    }

    /// <summary>Whether a blow lands, at its hit frame: the gap between the boxes then, how far (in
    /// degrees, either way) the creature is from where the eidolon faces, and whether they are level
    /// (neither above the other's box).</summary>
    public static bool Lands(Strike strike, double gap, double offFacingDegrees, bool level) =>
        level && gap <= strike.Reach + HitSlack && Math.Abs(offFacingDegrees) <= HitArcDegrees;

    /// <summary>How far ahead, in seconds of its motion, it aims at a creature <paramref name="gap"/>
    /// blocks off when it closes at <paramref name="closingSpeed"/> blocks a second: the time to get
    /// there, at most <see cref="MaxLeadSeconds"/>.</summary>
    public static double LeadSeconds(double gap, double closingSpeed) =>
        gap <= 0 ? 0 : Math.Min(MaxLeadSeconds, gap / Math.Max(closingSpeed, 1));
}
