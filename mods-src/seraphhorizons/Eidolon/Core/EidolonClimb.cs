namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>
/// A climb up a step of two blocks (README "Eidolon", pathfinding; Eidolon/README.md, <c>climb</c>). The
/// game's physics lifts the eidolon one block (its step height), not two, so at the foot of a step of two
/// on its path it stops and climbs, moved by script over the shape's <c>climb</c> animation: to the foot
/// node (until <see cref="GrabFrame"/>, its hands reaching the ledge), straight up the step's height with
/// its hands on the ledge (until <see cref="TopFrame"/>, its right foot then on the step), then forward
/// onto the top node (until <see cref="StandFrame"/>), where it stands. Up then forward, never both, its
/// box is always where <see cref="WidePath"/> found it free: over the foot, at every height up to the
/// top's, and between the two at the top's height. The frames are the generator's
/// (<c>EVENTS["climb"]</c> in Eidolon/tools/make_shape.py), which poses the body against this motion;
/// tools/tests/test_eidolon_model.py holds the two together.
/// </summary>
public static class EidolonClimb
{
    /// <summary>The <c>climb</c> animation's frames, at 30 a second.</summary>
    public const int Frames = 60;
    /// <summary>Its hands are on the ledge: it rises from here.</summary>
    public const int GrabFrame = 12;
    /// <summary>Risen the step's height, its right foot on the step: it moves forward from here.</summary>
    public const int TopFrame = 36;
    /// <summary>On the top node, standing; the animation settles to rest by its last frame.</summary>
    public const int StandFrame = 52;

    public const double FramesPerSecond = 30;

    public const string Animation = "climb";

    public static double Seconds => Frames / FramesPerSecond;

    /// <summary>Whether the step from <paramref name="from"/> to <paramref name="to"/> is climbed: up
    /// two blocks or more (one is walked, the game's step height).</summary>
    public static bool IsClimb(PathCell from, PathCell to) => to.Y - from.Y >= 2;

    /// <summary>The first index from <paramref name="start"/> on whose step to the next node is a climb, or
    /// −1 for none: the walk goes up to that node, then climbs.</summary>
    public static int NextClimb(IReadOnlyList<PathCell> path, int start)
    {
        for (int i = Math.Max(0, start); i + 1 < path.Count; i++)
            if (IsClimb(path[i], path[i + 1]))
                return i;
        return -1;
    }

    /// <summary>Where its feet are <paramref name="seconds"/> into a climb begun at (<paramref name="x"/>,
    /// <paramref name="y"/>, <paramref name="z"/>) from the <paramref name="foot"/> node to the
    /// <paramref name="top"/> one: to the foot, up, then forward, each in a straight line at an even pace.</summary>
    public static (double X, double Y, double Z) At(double seconds, double x, double y, double z, PathCell foot, PathCell top)
    {
        double frame = seconds * FramesPerSecond;
        if (frame < GrabFrame)
        {
            double t = Share(frame, 0, GrabFrame);
            return (x + (foot.X - x) * t, y + (foot.Y - y) * t, z + (foot.Z - z) * t);
        }
        if (frame < TopFrame)
            return (foot.X, foot.Y + (top.Y - foot.Y) * Share(frame, GrabFrame, TopFrame), foot.Z);
        double s = Share(frame, TopFrame, StandFrame);
        return (foot.X + (top.X - foot.X) * s, top.Y, foot.Z + (top.Z - foot.Z) * s);
    }

    /// <summary>The way it faces while it climbs: from the foot to the top (the entity's yaw).</summary>
    public static float Yaw(PathCell foot, PathCell top) => (float)Math.Atan2(top.X - foot.X, top.Z - foot.Z);

    private static double Share(double frame, int from, int to) => Math.Clamp((frame - from) / (to - from), 0, 1);
}
