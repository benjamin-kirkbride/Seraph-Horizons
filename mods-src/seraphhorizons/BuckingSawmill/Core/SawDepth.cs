using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.BuckingSawmill.Core;

/// <summary>What the mill is doing. A complete, turning mill cycles without stopping: its saws
/// sink (empty, or cutting a trunk), are wound back up, and sink again.</summary>
public enum MillPhase
{
    /// <summary>Incomplete, or its shaft is not turning fast enough: everything waits where it is.</summary>
    Stopped,
    /// <summary>The saws are going down with no trunk on the bed.</summary>
    Sinking,
    /// <summary>The saws are going down through a trunk.</summary>
    Cutting,
    /// <summary>The saws are being wound back up to the top.</summary>
    Raising,
}

/// <summary>Where the saws are in their cycle: depth 0 (top) to 1 (bed), whether they are being
/// wound up, and how far the loaded trunk is cut (0 without one).</summary>
public readonly record struct SawCycle(float Depth, bool Rising, float Progress = 0);

/// <summary>A step of the cycle: the new state, whether a cut finished during it (the trunk is
/// through: the caller gives the logs and clears the bed), and whether the saws passed the top.</summary>
public readonly record struct CycleStep(SawCycle Cycle, bool CutFinished, bool PassedTop);

/// <summary>How far the saws travel, rig.json's optional <c>saw</c>: the height of the blades'
/// cutting edge at the top of the cycle and at the end of a cut, in native-frame blocks.</summary>
public sealed record SawTravel(float TopY, float BottomY)
{
    public static readonly SawTravel Default = new(3.0f, 0.5f);
}

/// <summary>
/// The saws' cycle. Depth runs from 0 (at the top) to 1 (at the bed, through the trunk). While the
/// mill turns, empty saws sink at a fixed rate; a loaded trunk drops them at once to where they
/// touch it and the cut sinks them the rest of the way with its progress; at the bed the trip throws
/// the lift in and the windlass winds them back up to the top, where they start down again.
/// </summary>
public static class SawDepth
{
    /// <summary>The rate, per second, at which the client's shown depth closes the gap to its
    /// estimate when a sync moves it (a new trunk's drop takes about a quarter second).</summary>
    public const float EaseRate = 12f;

    /// <summary>A trunk's thickness in blocks from its Logging Expanded <c>size</c> variant: that of
    /// the model the mill shows for it (<see cref="TrunkBox"/>), so <c>xl</c> and <c>xxl</c> are two
    /// blocks high, the rest one.</summary>
    public static int TrunkThickness(string? size) => TrunkBox.Size(TrunkBox.ClassOf(size)).Height;

    /// <summary>The depth at which the saws touch the top of a trunk of <paramref name="size"/>
    /// lying on <paramref name="bed"/>; without a bed, the trunk lies at the saws' bottom.</summary>
    public static float Touch(SawTravel saw, TrunkBed? bed, string? size)
    {
        float trunkTop = (bed?.Origin.Y ?? saw.BottomY) + TrunkThickness(size);
        float travel = saw.TopY - saw.BottomY;
        return travel <= 0 ? 0 : Math.Clamp((saw.TopY - trunkTop) / travel, 0, 1);
    }

    /// <summary>The depth while cutting: from <paramref name="touch"/> at progress 0 to 1 when
    /// the trunk is through.</summary>
    public static float Cutting(float touch, float progress) =>
        touch + (1 - touch) * Math.Clamp(progress, 0, 1);

    /// <summary>The depth after <paramref name="radians"/> of shaft rotation wind the saws up:
    /// all the way takes <paramref name="raiseRevolutions"/> turns.</summary>
    public static float Raise(float depth, float radians, float raiseRevolutions)
    {
        if (radians <= 0)
            return depth;
        if (raiseRevolutions <= 0)
            return 0;
        return Math.Max(0, depth - (float)(radians / (2 * Math.PI * raiseRevolutions)));
    }

    /// <summary>How close to the top the saws must be for a trunk to go on, at the end of the
    /// rise or the start of the fall: about half a shaft turn on the default 6-turn travel.</summary>
    public const float LoadWindow = 0.08f;

    /// <summary>Whether a trunk can be put on now: the saws are at (or within <see cref="LoadWindow"/>
    /// of) the top of their cycle.</summary>
    public static bool AtTop(float depth) => depth <= LoadWindow;

    /// <summary>The depth after <paramref name="radians"/> of shaft rotation let empty saws sink:
    /// all the way down takes <paramref name="revolutions"/> turns, as the way up does.</summary>
    public static float Sink(float depth, float radians, float revolutions)
    {
        if (radians <= 0)
            return depth;
        if (revolutions <= 0)
            return 1;
        return Math.Min(1, depth + (float)(radians / (2 * Math.PI * revolutions)));
    }

    /// <summary>A trunk goes on: going down, the saws come to rest on its top (dropping onto it,
    /// or, near the top on a two-block trunk, eased back up onto it); going up, they finish the
    /// rise and drop onto it at the top.</summary>
    public static SawCycle Load(SawCycle cycle, float touch) =>
        cycle.Rising ? cycle with { Progress = 0 } : new SawCycle(touch, false, 0);

    /// <summary>Turns the cycle by <paramref name="radians"/> of shaft rotation. Up and down empty
    /// each take <paramref name="travelRevolutions"/> turns for the whole travel; with a trunk
    /// (<paramref name="touch"/> not null) the saws drop onto it and sink with the cut, which takes
    /// <paramref name="revolutionsPerStoredLog"/> × <paramref name="storedLogs"/> turns. When the
    /// cut is through they start back up from the bed (the rest of this step's turning is dropped).</summary>
    public static CycleStep Advance(SawCycle cycle, float radians, float? touch, int storedLogs,
                                    float revolutionsPerStoredLog, float travelRevolutions)
    {
        float depth = cycle.Depth, progress = cycle.Progress, r = Math.Max(radians, 0);
        bool rising = cycle.Rising, finished = false, top = false;
        float perTravel = (float)(2 * Math.PI * Math.Max(travelRevolutions, 1e-6f));
        for (int i = 0; i < 6 && r > 0; i++)
        {
            if (rising)
            {
                float need = depth * perTravel;
                if (r < need)
                {
                    depth -= r / perTravel;
                    r = 0;
                }
                else
                {
                    r -= need;
                    depth = 0;
                    rising = false;
                    top = true;
                }
            }
            else if (touch is float t)
            {
                if (depth < t)
                    depth = t;
                progress += Core.Cutting.ProgressFor(r, storedLogs, revolutionsPerStoredLog);
                r = 0;
                if (progress >= 1)
                {
                    progress = 0;
                    depth = 1;
                    rising = true;
                    finished = true;
                }
                else
                    depth = Cutting(t, progress);
            }
            else
            {
                float need = (1 - depth) * perTravel;
                if (r < need)
                {
                    depth += r / perTravel;
                    r = 0;
                }
                else
                {
                    r -= need;
                    depth = 1;
                    rising = true;
                }
            }
        }
        return new CycleStep(new SawCycle(Math.Clamp(depth, 0, 1), rising, progress), finished, top);
    }

    /// <summary>The phase: stopped unless <paramref name="running"/> (complete and turning fast
    /// enough), else raising, cutting or sinking.</summary>
    public static MillPhase Phase(bool running, bool hasTrunk, bool rising) =>
        !running ? MillPhase.Stopped : rising ? MillPhase.Raising : hasTrunk ? MillPhase.Cutting : MillPhase.Sinking;

    /// <summary>Moves a shown depth toward its target by <see cref="EaseRate"/> over
    /// <paramref name="dt"/> seconds, landing on it once within a hair.</summary>
    public static float Ease(float shown, float target, float dt)
    {
        float next = shown + (target - shown) * (1 - MathF.Exp(-EaseRate * Math.Max(dt, 0)));
        return Math.Abs(target - next) < 0.002f ? target : next;
    }
}
