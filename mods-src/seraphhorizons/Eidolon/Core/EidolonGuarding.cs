namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>One AI task of a creature's entity type, as its JSON gives it (the <c>taskai</c>
/// behaviour's <c>aitasks</c>): what the guard order reads to tell a hostile creature.</summary>
/// <param name="Code">The task's code (<c>meleeattack</c>, <c>throwatentity</c>, ...).</param>
/// <param name="EntityCodes">Its target codes (<c>entityCodes</c>; the game's default is <c>player</c>).</param>
/// <param name="WhenIn">Emotion states it runs only in (<c>whenInEmotionState</c>, any of them), or null.</param>
/// <param name="WhenNotIn">Emotion states it never runs in, or null.</param>
public sealed record CreatureTask(string Code, IReadOnlyList<string> EntityCodes, IReadOnlyList<string>? WhenIn = null,
    IReadOnlyList<string>? WhenNotIn = null, int? MinGeneration = null, int? MaxGeneration = null);

/// <summary>The world's <c>creatureHostility</c> setting, as the game's targeting reads it.</summary>
public enum CreatureHostility
{
    Aggressive,
    Passive,
    Off,
}

/// <summary>
/// Guarding a point (#680; README "Eidolon", guarding): which creatures are hostile, so that the
/// eidolon goes for them, decided from the game's own data for the creature: it is hostile when one of
/// its attack tasks would attack a player now. Never a creature with an owner (a mount, a hacked
/// locust, a guard animal), one of a tamed type, or one bred by players (generation 1 and on: the game's
/// domestication), however its tasks read. An attack the game drops as the species is bred (a task
/// with a <c>maxGeneration</c>: the wild sheep's and boar's charge at a player who comes too close,
/// "to prevent easy fencing in") is livestock keeping its space, not a hunter: it counts only while the
/// creature is angry (<see cref="AngryStates"/>), so a wild animal just penned is safe.
/// </summary>
public static class EidolonHostility
{
    /// <summary>The emotion states in which a creature attacks even on a world whose
    /// <c>creatureHostility</c> is <c>passive</c> (the game's <c>CanSensePlayer</c>).</summary>
    public static readonly IReadOnlyList<string> AngryStates = ["aggressiveondamage", "aggressivearoundentities"];

    /// <summary>Attack tasks whose code does not say "attack": the ranged and the bowtorn's.</summary>
    private static readonly HashSet<string> RangedAttacks = new(StringComparer.Ordinal)
    {
        "throwatentity", "shootatentity", "turretmode", "eidolonslam",
    };

    /// <summary>Attack tasks that strike only what their owner fights (the mech helper's and the hacked
    /// locust's), never on their own.</summary>
    private static readonly HashSet<string> Directed = new(StringComparer.Ordinal)
    {
        "meleeattacktargetingentity",
    };

    /// <summary>Whether a task of this code attacks (melee or ranged) rather than seeks, flees or idles.</summary>
    public static bool IsAttack(string code) =>
        !Directed.Contains(code) && (code.Contains("attack", StringComparison.Ordinal) || RangedAttacks.Contains(code));

    /// <summary>Whether target codes take in players: <c>player</c> itself, or a wildcard
    /// (<c>*</c>, <c>pla*</c>) it matches, as the game's targeting reads them.</summary>
    public static bool TargetsPlayers(IEnumerable<string> entityCodes) =>
        entityCodes.Any(c => c == "player" || (c.EndsWith('*') && "player".StartsWith(c[..^1], StringComparison.Ordinal)));

    /// <summary>
    /// Whether a creature is hostile now: not owned, of a tamed type or bred by players, and one of its
    /// attack tasks targets players and may run (its emotion-state and generation conditions hold), on
    /// a world whose hostility lets it (<see cref="CreatureHostility.Passive"/>: only while angry,
    /// <see cref="CreatureHostility.Off"/>: never).
    /// </summary>
    public static bool IsHostile(IEnumerable<CreatureTask> tasks, Func<string, bool> inState, int generation, bool owned, bool tamedType,
        CreatureHostility hostility)
    {
        if (owned || tamedType || generation >= 1 || hostility == CreatureHostility.Off)
            return false;
        bool angry = AngryStates.Any(inState);
        if (hostility == CreatureHostility.Passive && !angry)
            return false;
        return tasks.Any(t => IsAttack(t.Code) && TargetsPlayers(t.EntityCodes)
                              && (t.MaxGeneration == null || angry)
                              && (t.WhenIn == null || t.WhenIn.Any(inState))
                              && (t.WhenNotIn == null || !t.WhenNotIn.Any(inState))
                              && (t.MinGeneration is not { } min || generation >= min)
                              && (t.MaxGeneration is not { } max || generation <= max));
    }
}

/// <summary>Where a guard's feet stand: its point, or a post about it.</summary>
public readonly record struct GuardPost(double X, double Y, double Z);

/// <summary>
/// The guard order's geometry (#680): what it watches (creatures within the radius of its point), where
/// about the point it stands (its post, <see cref="Post"/>: a standable place within
/// <see cref="PostRange"/> that keeps clear of every other guard, so that several guarding one point do
/// not stand in each other; taken again every <see cref="WanderMinSeconds"/> to
/// <see cref="WanderMaxSeconds"/>, so they wander about it) and when it is home (standing within
/// <see cref="HomeTolerance"/> of its post).
/// </summary>
public static class EidolonGuard
{
    /// <summary>How near its post, across and up or down, it counts as standing there.</summary>
    public const double HomeTolerance = 1.5;

    /// <summary>How near its post, across, it stops walking there.</summary>
    public const double PostTolerance = 0.75;

    /// <summary>How far from its point, across, its post may be, in blocks.</summary>
    public const int PostRange = 4;

    /// <summary>How far apart, across, a post keeps from another guard's post and from where another
    /// guard stands, in blocks (the eidolon is 1.7 wide).</summary>
    public const double PostSpacing = 3;

    /// <summary>How far apart up or down two places are that never clash (the eidolon is 3.75 tall).</summary>
    public const double PostClearHeight = 4;

    /// <summary>The heights about its point's floor, in order, a post's feet may stand at.</summary>
    public static readonly int[] PostHeights = [0, 1, -1, 2, -2];

    /// <summary>How long it keeps a post before taking another, at random between these, in seconds.</summary>
    public const double WanderMinSeconds = 15;

    public const double WanderMaxSeconds = 45;

    /// <summary>How often it looks about for hostile creatures, in seconds.</summary>
    public const double ScanSeconds = 1;

    /// <summary>How far past its radius it keeps after a creature it went for before letting it go, in blocks.</summary>
    public const double ChaseSlack = 4;

    public static bool Watches(double distanceToPoint, double radius) => distanceToPoint <= radius;

    public static bool AtHome(double dx, double dy, double dz) =>
        dx * dx + dz * dz < HomeTolerance * HomeTolerance && Math.Abs(dy) < HomeTolerance;

    /// <summary>Whether it stands near enough its post to stop walking there.</summary>
    public static bool AtPost(double dx, double dy, double dz) =>
        dx * dx + dz * dz < PostTolerance * PostTolerance && Math.Abs(dy) < HomeTolerance;

    /// <summary>Whether a post at <paramref name="at"/> keeps clear of every place in
    /// <paramref name="taken"/> (other guards' posts and where they stand).</summary>
    public static bool Clear(GuardPost at, IEnumerable<GuardPost> taken) =>
        taken.All(t => (t.X - at.X) * (t.X - at.X) + (t.Z - at.Z) * (t.Z - at.Z) >= PostSpacing * PostSpacing
                       || Math.Abs(t.Y - at.Y) >= PostClearHeight);

    /// <summary>
    /// The places a guard of <paramref name="point"/> may take as its post: each block within
    /// <see cref="PostRange"/> of it across (nearest first, the point's own first of all), its feet where
    /// its whole box is free with a floor under it (<see cref="WidePath.Feet"/>, at one of
    /// <see cref="PostHeights"/>), never where it must not stand (lava, fire), and clear of
    /// <paramref name="taken"/>.
    /// </summary>
    public static List<GuardPost> Posts(IWideSpace space, GuardPost point, IReadOnlyCollection<GuardPost> taken)
    {
        var posts = new List<GuardPost>();
        int floor = (int)Math.Floor(point.Y + 0.01);
        for (int r = 0; r <= PostRange; r++)
        for (int dx = -r; dx <= r; dx++)
        for (int dz = -r; dz <= r; dz++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r || dx * dx + dz * dz > PostRange * PostRange)
                continue;
            double x = point.X + dx, z = point.Z + dz;
            if (WidePath.Feet(space, x, floor, z, PostHeights) is not { } feet
                || space.Cost(WidePath.NodeAt(x, feet, z)) > WidePath.Impassable)
                continue;
            var post = new GuardPost(x, feet, z);
            if (Clear(post, taken))
                posts.Add(post);
        }
        return posts;
    }

    /// <summary>A post for a guard of <paramref name="point"/>, at random among <see cref="Posts"/>, or
    /// null when there is none.</summary>
    public static GuardPost? Post(IWideSpace space, GuardPost point, IReadOnlyCollection<GuardPost> taken, Random random)
    {
        var posts = Posts(space, point, taken);
        return posts.Count == 0 ? null : posts[random.Next(posts.Count)];
    }

    /// <summary>How long it keeps a post it has reached, in seconds, at random.</summary>
    public static double WanderSeconds(Random random) =>
        WanderMinSeconds + random.NextDouble() * (WanderMaxSeconds - WanderMinSeconds);

    /// <summary>Of the creatures it watches, the one to go for: the nearest to the eidolon (index into
    /// <paramref name="distancesToEidolon"/>), or -1 for none.</summary>
    public static int Choose(IReadOnlyList<double> distancesToEidolon)
    {
        int best = -1;
        for (int i = 0; i < distancesToEidolon.Count; i++)
            if (best < 0 || distancesToEidolon[i] < distancesToEidolon[best])
                best = i;
        return best;
    }
}
