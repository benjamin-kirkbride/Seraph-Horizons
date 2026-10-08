namespace SeraphHorizons.Mod.NanMotion.Core;

/// <summary>Which side of a traced call a checkpoint is on.</summary>
public enum CheckPhase
{
    Before,
    After,
}

/// <summary>How the watched motion was first seen non-finite.</summary>
public enum TripKind
{
    /// <summary>Finite when the call began and non-finite when it returned: the call (or something it
    /// called that is not traced on its own) made it.</summary>
    Inside,

    /// <summary>Non-finite at a checkpoint whose previous one saw it finite, without a traced call
    /// returning in between that made it: something between the two checkpoints that is not traced
    /// made it.</summary>
    Between,
}

/// <summary>What a checkpoint at the start of a call saw, handed to the matching checkpoint at its
/// end. A value type, so a trace costs no allocation (Harmony keeps it as a <c>__state</c> local).</summary>
public readonly struct MotionProbe
{
    public MotionProbe(double x, double y, double z)
    {
        Watched = true;
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>Whether the start checkpoint looked at all: false off the watched thread, with no
    /// watched entity, or once the tracker has tripped.</summary>
    public bool Watched { get; }

    public double X { get; }
    public double Y { get; }
    public double Z { get; }

    public bool Finite => NanTracker.IsFinite(X, Y, Z);
}

/// <summary>The first time the watched motion was seen non-finite. <see cref="Site"/> and
/// <see cref="Instance"/> are whatever the caller passed (the game side passes the traced method and
/// its instance) and are described by the game side, which also fills <see cref="Detail"/>.</summary>
public sealed class NanTrip
{
    public required TripKind Kind { get; init; }

    /// <summary>The checkpoint that saw it non-finite.</summary>
    public required object Site { get; init; }
    public object? Instance { get; init; }
    public required CheckPhase Phase { get; init; }

    /// <summary>The last checkpoint that saw it finite, if any did.</summary>
    public object? LastFiniteSite { get; init; }
    public object? LastFiniteInstance { get; init; }
    public CheckPhase LastFinitePhase { get; init; }

    /// <summary>The motion before: at the start of the call for <see cref="TripKind.Inside"/>, at the
    /// last finite checkpoint for <see cref="TripKind.Between"/> (all NaN when there was none).</summary>
    public required (double X, double Y, double Z) Before { get; init; }
    public required (double X, double Y, double Z) After { get; init; }

    /// <summary>How many checkpoints looked before this one, this one included.</summary>
    public long Checkpoint { get; init; }

    /// <summary>Filled by the game side when the trip happens: the game time, the stack and the
    /// described sites.</summary>
    public string? Detail { get; set; }
}

/// <summary>
/// NaN motion diagnostics (<c>NanMotionDiagnostics</c>, #405): finds where a watched motion vector
/// first goes from finite to non-finite, from checkpoints at the start and end of traced calls.
/// Game-independent, so tests/ runs it without the game; the game side
/// (<c>NanMotionDiagnostics</c>) calls <see cref="Before"/> and <see cref="After"/> from Harmony
/// prefixes and postfixes, with the local player's <c>Pos.Motion</c>.
///
/// Nested calls each have their own pair, so the innermost traced call that made the NaN trips
/// first and the calls around it are not blamed. A NaN made by code that is not traced is seen at
/// the next checkpoint, and the trip names the last checkpoint that saw the motion finite, which
/// bounds the culprit between the two. After the first trip the tracker stops looking: it keeps
/// that record, and every later checkpoint returns at once.
///
/// Single-threaded: the game side only calls it on the client's main thread.
/// </summary>
public sealed class NanTracker
{
    private object? _lastFiniteSite;
    private object? _lastFiniteInstance;
    private CheckPhase _lastFinitePhase;
    private double _lastX = double.NaN, _lastY = double.NaN, _lastZ = double.NaN;

    /// <summary>The first trip, or null while the motion has only been seen finite.</summary>
    public NanTrip? Trip { get; private set; }

    public bool Tripped => Trip != null;

    /// <summary>How many checkpoints have looked.</summary>
    public long Checkpoints { get; private set; }

    /// <summary>The last checkpoint that saw the motion finite, and the motion it saw.</summary>
    public (object? Site, object? Instance, CheckPhase Phase, double X, double Y, double Z) LastFinite =>
        (_lastFiniteSite, _lastFiniteInstance, _lastFinitePhase, _lastX, _lastY, _lastZ);

    public static bool IsFinite(double x, double y, double z) =>
        double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(z);

    /// <summary>A checkpoint at the start of a traced call. Returns the probe to hand to
    /// <see cref="After"/>; <paramref name="tripped"/> is whether this checkpoint tripped the tracker
    /// (the motion arrived non-finite).</summary>
    public MotionProbe Before(object site, object? instance, double x, double y, double z, out bool tripped) =>
        Check(site, instance, CheckPhase.Before, x, y, z, out tripped);

    /// <summary>A checkpoint standing alone, at the start or the end of <paramref name="site"/>, with no
    /// matching one: it can only see what arrived (<see cref="TripKind.Between"/>).</summary>
    public MotionProbe Check(object site, object? instance, CheckPhase phase, double x, double y, double z, out bool tripped)
    {
        tripped = false;
        if (Trip != null)
            return default;
        Checkpoints++;
        if (IsFinite(x, y, z))
            Seen(site, instance, phase, x, y, z);
        else
            tripped = TripAt(TripKind.Between, site, instance, phase, (_lastX, _lastY, _lastZ), x, y, z);
        return new MotionProbe(x, y, z);
    }

    /// <summary>A checkpoint at the end of a traced call, with what its start saw. Returns whether this
    /// checkpoint tripped the tracker.</summary>
    public bool After(object site, object? instance, in MotionProbe before, double x, double y, double z)
    {
        if (Trip != null || !before.Watched)
            return false;
        Checkpoints++;
        if (IsFinite(x, y, z))
        {
            Seen(site, instance, CheckPhase.After, x, y, z);
            return false;
        }
        // Seen non-finite at the start, the start checkpoint would have tripped already; so here the
        // start saw it finite, and this call made it.
        return TripAt(TripKind.Inside, site, instance, CheckPhase.After, (before.X, before.Y, before.Z), x, y, z);
    }

    private void Seen(object site, object? instance, CheckPhase phase, double x, double y, double z)
    {
        _lastFiniteSite = site;
        _lastFiniteInstance = instance;
        _lastFinitePhase = phase;
        _lastX = x;
        _lastY = y;
        _lastZ = z;
    }

    private bool TripAt(TripKind kind, object site, object? instance, CheckPhase phase,
        (double, double, double) before, double x, double y, double z)
    {
        Trip = new NanTrip
        {
            Kind = kind,
            Site = site,
            Instance = instance,
            Phase = phase,
            LastFiniteSite = _lastFiniteSite,
            LastFiniteInstance = _lastFiniteInstance,
            LastFinitePhase = _lastFinitePhase,
            Before = before,
            After = (x, y, z),
            Checkpoint = Checkpoints,
        };
        return true;
    }

    /// <summary>The trip as report lines, the sites described by <paramref name="describe"/>
    /// (site, instance), or a statement that no checkpoint saw it non-finite.</summary>
    public IEnumerable<string> Describe(Func<object, object?, string> describe)
    {
        if (Trip is not { } t)
        {
            yield return $"No traced checkpoint saw the motion non-finite ({Checkpoints} checkpoints looked).";
            if (_lastFiniteSite != null)
                yield return $"Last seen finite {Where(_lastFinitePhase)} {describe(_lastFiniteSite, _lastFiniteInstance)}: "
                             + NanFormat.Vec(_lastX, _lastY, _lastZ);
            yield break;
        }
        if (t.Kind == TripKind.Inside)
        {
            yield return $"Went non-finite INSIDE {describe(t.Site, t.Instance)}";
            yield return $"  motion at its start: {NanFormat.Vec(t.Before.X, t.Before.Y, t.Before.Z)}";
            yield return $"  motion at its end:   {NanFormat.Vec(t.After.X, t.After.Y, t.After.Z)}";
        }
        else
        {
            yield return $"Went non-finite BETWEEN two checkpoints, in code that is not traced:";
            yield return t.LastFiniteSite == null
                ? "  last seen finite: never (this was the first checkpoint)"
                : $"  last seen finite {Where(t.LastFinitePhase)} {describe(t.LastFiniteSite, t.LastFiniteInstance)}: "
                  + NanFormat.Vec(t.Before.X, t.Before.Y, t.Before.Z);
            yield return $"  first seen non-finite {Where(t.Phase)} {describe(t.Site, t.Instance)}: "
                         + NanFormat.Vec(t.After.X, t.After.Y, t.After.Z);
        }
        yield return $"  checkpoint #{t.Checkpoint}";
    }

    private static string Where(CheckPhase phase) => phase == CheckPhase.Before ? "at the start of" : "at the end of";
}
