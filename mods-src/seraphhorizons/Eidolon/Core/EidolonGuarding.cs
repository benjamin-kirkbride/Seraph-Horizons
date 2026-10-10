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

/// <summary>
/// The guard order's geometry (#680): what it watches (creatures within the radius of its point) and
/// when it is home (standing within <see cref="HomeTolerance"/> of the point).
/// </summary>
public static class EidolonGuard
{
    /// <summary>How near the point, across and up or down, it counts as standing there.</summary>
    public const double HomeTolerance = 1.5;

    /// <summary>How often it looks about for hostile creatures, in seconds.</summary>
    public const double ScanSeconds = 1;

    /// <summary>How far past its radius it keeps after a creature it went for before letting it go, in blocks.</summary>
    public const double ChaseSlack = 4;

    public static bool Watches(double distanceToPoint, double radius) => distanceToPoint <= radius;

    public static bool AtHome(double dx, double dy, double dz) =>
        dx * dx + dz * dz < HomeTolerance * HomeTolerance && Math.Abs(dy) < HomeTolerance;

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
