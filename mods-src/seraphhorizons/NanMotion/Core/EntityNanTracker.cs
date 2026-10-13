namespace SeraphHorizons.Mod.NanMotion.Core;

/// <summary>How another entity's position or motion was first seen non-finite.</summary>
public enum EntityTripKind
{
    /// <summary>Finite when a traced call on the entity began and non-finite when it returned: the
    /// call (or something it called that is not traced on its own) made it.</summary>
    Inside,

    /// <summary>Already non-finite when a traced call on the entity began: something that is not
    /// traced made it, since the last sweep that found every entity finite.</summary>
    Arrived,

    /// <summary>Found by the periodic sweep of every loaded entity, with no traced call having seen
    /// it: something that is not traced made it, between the last clean sweep and this one.</summary>
    Sweep,
}

/// <summary>An entity's position and motion as a checkpoint saw them, and whether the traced call's
/// own state (the game side checks the interpolation's) was finite. A value type, so a trace costs
/// no allocation.</summary>
public readonly struct EntityProbe
{
    public EntityProbe(double px, double py, double pz, double mx, double my, double mz, bool stateFinite)
    {
        Watched = true;
        PX = px;
        PY = py;
        PZ = pz;
        MX = mx;
        MY = my;
        MZ = mz;
        StateFinite = stateFinite;
    }

    /// <summary>Whether the checkpoint looked at all: false off the client thread, for an instance
    /// that is no entity's, for the watched player, or once the tracker has tripped.</summary>
    public bool Watched { get; }

    public double PX { get; }
    public double PY { get; }
    public double PZ { get; }
    public double MX { get; }
    public double MY { get; }
    public double MZ { get; }

    /// <summary>Whether the state the game side checks along with the entity (the interpolation's
    /// snapshots, accumulator and speed) was finite; true where there is none.</summary>
    public bool StateFinite { get; }

    public bool PositionFinite => NanTracker.IsFinite(PX, PY, PZ);
    public bool MotionFinite => NanTracker.IsFinite(MX, MY, MZ);
    public bool Finite => PositionFinite && MotionFinite && StateFinite;

    public string Describe() =>
        $"pos {NanFormat.Vec(PX, PY, PZ)} motion {NanFormat.Vec(MX, MY, MZ)}"
        + (StateFinite ? "" : "; its traced state non-finite" + NanFormat.Flag);
}

/// <summary>The first time another entity's position or motion was seen non-finite. Site, instance
/// and entity are whatever the caller passed and are described by the game side, which also fills
/// <see cref="Detail"/>.</summary>
public sealed class EntityNanTrip
{
    public required EntityTripKind Kind { get; init; }
    public required object Site { get; init; }
    public object? Instance { get; init; }
    public object? Entity { get; init; }
    public long EntityId { get; init; }

    /// <summary>At the start of the call for <see cref="EntityTripKind.Inside"/>; not watched for
    /// the others (nothing saw this entity finite).</summary>
    public EntityProbe Before { get; init; }
    public required EntityProbe After { get; init; }

    /// <summary>The time the caller passed (the game side: world elapsed milliseconds).</summary>
    public long AtMs { get; init; }

    /// <summary>When the last sweep that found every entity finite ran, or -1 if none had.</summary>
    public long LastCleanSweepMs { get; init; }

    public long Checkpoint { get; init; }

    /// <summary>Filled by the game side when the trip happens.</summary>
    public string? Detail { get; set; }
}

/// <summary>
/// NaN motion diagnostics, other entities (#405): the player's motion went NaN in its repulsion
/// because a neighbour's position already was, and dozens of entities of every kind had NaN
/// positions on the client. This finds where an entity other than the watched player first goes from
/// finite to non-finite. Game-independent, so tests/ runs it without the game.
///
/// Unlike <see cref="NanTracker"/> there are many entities, so no entity's last finite checkpoint is
/// kept: a traced call checks its own entity at its start and its end (<see cref="Before"/>,
/// <see cref="After"/>), and a periodic sweep of every loaded entity (<see cref="Sweep"/>) catches a
/// writer that is not traced and bounds when it wrote. After the first trip it stops looking.
///
/// Single-threaded: the game side only calls it on the client's main thread.
/// </summary>
public sealed class EntityNanTracker
{
    /// <summary>The first trip, or null while every entity has only been seen finite.</summary>
    public EntityNanTrip? Trip { get; private set; }

    public bool Tripped => Trip != null;

    /// <summary>How many checkpoints have looked.</summary>
    public long Checkpoints { get; private set; }

    /// <summary>How many sweeps found every entity finite.</summary>
    public long CleanSweeps { get; private set; }

    /// <summary>When the last sweep that found every entity finite ran, or -1.</summary>
    public long LastCleanSweepMs { get; private set; } = -1;

    /// <summary>A checkpoint at the start of a traced call on an entity. Returns the probe to hand to
    /// <see cref="After"/>; <paramref name="tripped"/> is whether the entity arrived non-finite.</summary>
    public EntityProbe Before(object site, object? instance, object? entity, long entityId, long nowMs,
        double px, double py, double pz, double mx, double my, double mz, bool stateFinite, out bool tripped)
    {
        tripped = false;
        if (Trip != null)
            return default;
        Checkpoints++;
        var probe = new EntityProbe(px, py, pz, mx, my, mz, stateFinite);
        if (!probe.Finite)
            tripped = TripAt(EntityTripKind.Arrived, site, instance, entity, entityId, nowMs, default, probe);
        return probe;
    }

    /// <summary>A checkpoint at the end of a traced call on an entity, with what its start saw.
    /// Returns whether it tripped the tracker.</summary>
    public bool After(object site, object? instance, object? entity, long entityId, long nowMs, in EntityProbe before,
        double px, double py, double pz, double mx, double my, double mz, bool stateFinite)
    {
        if (Trip != null || !before.Watched)
            return false;
        Checkpoints++;
        var probe = new EntityProbe(px, py, pz, mx, my, mz, stateFinite);
        if (probe.Finite)
            return false;
        // Non-finite at the start, the start would have tripped; so the call made it.
        return TripAt(EntityTripKind.Inside, site, instance, entity, entityId, nowMs, before, probe);
    }

    /// <summary>One entity of a sweep. Returns whether it tripped the tracker.</summary>
    public bool Sweep(object site, object? entity, long entityId, long nowMs,
        double px, double py, double pz, double mx, double my, double mz)
    {
        if (Trip != null || (NanTracker.IsFinite(px, py, pz) && NanTracker.IsFinite(mx, my, mz)))
            return false;
        return TripAt(EntityTripKind.Sweep, site, entity, entity, entityId, nowMs, default,
            new EntityProbe(px, py, pz, mx, my, mz, true));
    }

    /// <summary>The end of a sweep: with nothing tripped, every entity was finite at
    /// <paramref name="nowMs"/>.</summary>
    public void SweepDone(long nowMs)
    {
        if (Trip != null)
            return;
        LastCleanSweepMs = nowMs;
        CleanSweeps++;
    }

    private bool TripAt(EntityTripKind kind, object site, object? instance, object? entity, long entityId, long nowMs,
        EntityProbe before, EntityProbe after)
    {
        Trip = new EntityNanTrip
        {
            Kind = kind,
            Site = site,
            Instance = instance,
            Entity = entity,
            EntityId = entityId,
            Before = before,
            After = after,
            AtMs = nowMs,
            LastCleanSweepMs = LastCleanSweepMs,
            Checkpoint = Checkpoints,
        };
        return true;
    }

    /// <summary>The trip as report lines, the site described by <paramref name="describe"/> (site,
    /// instance), or a statement that nothing was seen non-finite.</summary>
    public IEnumerable<string> Describe(Func<object, object?, string> describe)
    {
        if (Trip is not { } t)
        {
            yield return $"No other entity was seen with a non-finite position or motion ({Checkpoints} checkpoints, "
                         + $"{CleanSweeps} clean sweeps{(LastCleanSweepMs >= 0 ? $", the last at {LastCleanSweepMs} ms" : "")}).";
            yield break;
        }
        string since = t.LastCleanSweepMs >= 0
            ? $"the last sweep that found every entity finite ran at {t.LastCleanSweepMs} ms, {t.AtMs - t.LastCleanSweepMs} ms before"
            : "no sweep had run yet";
        switch (t.Kind)
        {
            case EntityTripKind.Inside:
                yield return $"Went non-finite INSIDE {describe(t.Site, t.Instance)}";
                yield return $"  at its start: {t.Before.Describe()}";
                yield return $"  at its end:   {t.After.Describe()}";
                break;
            case EntityTripKind.Arrived:
                yield return $"Already non-finite at the start of {describe(t.Site, t.Instance)}: made by code that is not traced";
                yield return $"  seen: {t.After.Describe()}";
                yield return $"  {since}";
                break;
            default:
                yield return $"Found by the sweep of loaded entities, {describe(t.Site, t.Instance)}: "
                             + "no traced call saw it go non-finite, so code that is not traced made it";
                yield return $"  seen: {t.After.Describe()}";
                yield return $"  {since}";
                break;
        }
        yield return $"  entity #{t.EntityId}, world elapsed {t.AtMs} ms, checkpoint #{t.Checkpoint}";
    }
}

/// <summary>One client frame: when it began, its dt and whether the game was paused.</summary>
public readonly record struct ClientFrame(long ElapsedMs, float Dt, bool Paused);

/// <summary>The last <see cref="Capacity"/> client frames (NaN motion diagnostics, #405), written in
/// a circle so recording one allocates nothing: a stall or an odd dt shows here.</summary>
public sealed class FrameRing
{
    private readonly ClientFrame[] _frames;
    private int _next;

    public FrameRing(int capacity)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "at least one frame");
        _frames = new ClientFrame[capacity];
    }

    public int Capacity => _frames.Length;

    public int Count { get; private set; }

    public void Add(in ClientFrame frame)
    {
        _frames[_next] = frame;
        _next = (_next + 1) % _frames.Length;
        if (Count < _frames.Length)
            Count++;
    }

    /// <summary>The frames held, oldest first.</summary>
    public List<ClientFrame> Frames()
    {
        var list = new List<ClientFrame>(Count);
        int start = Count < _frames.Length ? 0 : _next;
        for (int i = 0; i < Count; i++)
            list.Add(_frames[(start + i) % _frames.Length]);
        return list;
    }

    /// <summary>The frames as one line each, oldest first, timed from the last; a non-finite,
    /// negative, zero or long (over 0.25 s) dt is flagged.</summary>
    public IEnumerable<string> Describe()
    {
        var frames = Frames();
        if (frames.Count == 0)
        {
            yield return "(no client frame recorded)";
            yield break;
        }
        long last = frames[^1].ElapsedMs;
        foreach (var f in frames)
        {
            bool odd = !float.IsFinite(f.Dt) || f.Dt <= 0 || f.Dt > 0.25f;
            yield return $"t {f.ElapsedMs - last,6}ms dt {NanFormat.Num(f.Dt)}{(f.Paused ? " paused" : "")}{(odd ? " <-- ODD DT" : "")}";
        }
    }
}
