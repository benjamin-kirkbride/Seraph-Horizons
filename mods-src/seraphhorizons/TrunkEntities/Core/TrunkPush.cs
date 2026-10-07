using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.TrunkEntities.Core;

/// <summary>
/// Keeping agents out of a trunk's boxes, game-independent: the trunk as good as solid. The boxes
/// are the trunk's turned collision boxes (<see cref="TrunkBoxes.Turned"/>, axis-aligned, as the
/// game keeps them) and the agent's its collision box, both relative to the trunk's position. The
/// way out is the shortest move along one of ±x, ±z (or up, onto the trunk, when the agent's feet
/// are within <see cref="StandMargin"/> of the top) that clears every box at once, so an agent in
/// the middle of a row of boxes leaves across the trunk rather than along it.
/// </summary>
public static class TrunkPush
{
    /// <summary>The most an agent is moved out in one step, blocks: above a walk (about 0.07
    /// blocks per 1/60 s) so a walking player is held at the surface, and gentle enough that one
    /// found deep inside slides out over a few steps rather than jumping.</summary>
    public const double MaxStep = 0.25;

    /// <summary>How far below the top of the boxes, blocks, an agent's feet may be and still be
    /// lifted onto the trunk rather than pushed off its side.</summary>
    public const double StandMargin = 0.4;

    /// <summary>A little past touching, so a pushed-out agent is clear, not grazing.</summary>
    public const double Skin = 0.001;

    /// <summary>A way out: the direction (a unit axis: one of X, Z ±1, or Y +1) and how far.</summary>
    public readonly record struct Exit(int X, int Y, int Z, double Distance)
    {
        /// <summary>The move, blocks, at most <paramref name="maxStep"/> (the whole way when
        /// <paramref name="maxStep"/> is not positive).</summary>
        public (double X, double Y, double Z) Step(double maxStep = MaxStep)
        {
            double d = maxStep > 0 ? Math.Min(Distance, maxStep) : Distance;
            return (X * d, Y * d, Z * d);
        }

        public bool Up => Y > 0;
    }

    private static readonly (int X, int Y, int Z)[] Sides = [(1, 0, 0), (-1, 0, 0), (0, 0, 1), (0, 0, -1)];

    /// <summary>Whether two boxes overlap (touching is not).</summary>
    public static bool Overlaps(Box a, Box b) =>
        a.X1 < b.X2 && a.X2 > b.X1 && a.Y1 < b.Y2 && a.Y2 > b.Y1 && a.Z1 < b.Z2 && a.Z2 > b.Z1;

    /// <summary>The shortest way for <paramref name="agent"/> out of all of
    /// <paramref name="boxes"/>, or null when it overlaps none.</summary>
    public static Exit? Out(IReadOnlyList<Box> boxes, Box agent)
    {
        if (!boxes.Any(b => Overlaps(b, agent)))
            return null;
        Exit? best = null;
        foreach (var (x, y, z) in Sides)
        {
            double d = Clear(boxes, agent, x, y, z);
            if (best == null || d < best.Value.Distance)
                best = new Exit(x, y, z, d);
        }
        // Up, onto the trunk, only for an agent whose feet are near the top of what it is in.
        double up = Clear(boxes, agent, 0, 1, 0);
        if (up <= StandMargin + Skin && (best == null || up <= best.Value.Distance || up <= StandMargin))
            best = new Exit(0, 1, 0, up);
        return best;
    }

    /// <summary>How far <paramref name="agent"/> must move along (x, y, z) to overlap none of
    /// <paramref name="boxes"/>: moved out of each box it overlaps in turn, until it overlaps none.</summary>
    public static double Clear(IReadOnlyList<Box> boxes, Box agent, int x, int y, int z)
    {
        double t = 0;
        for (int guard = 0; guard <= boxes.Count; guard++)
        {
            var moved = Shift(agent, x * t, y * t, z * t);
            double need = t;
            foreach (var b in boxes)
                if (Overlaps(b, moved))
                    need = Math.Max(need, t + Depth(b, moved, x, y, z) + Skin);
            if (need <= t)
                return t;
            t = need;
        }
        return t;
    }

    // How far `agent` must move along the unit axis to leave `box`.
    private static double Depth(Box box, Box agent, int x, int y, int z) =>
        x > 0 ? box.X2 - agent.X1 : x < 0 ? agent.X2 - box.X1
        : z > 0 ? box.Z2 - agent.Z1 : z < 0 ? agent.Z2 - box.Z1
        : y > 0 ? box.Y2 - agent.Y1 : agent.Y2 - box.Y1;

    /// <summary><paramref name="box"/> moved by (dx, dy, dz).</summary>
    public static Box Shift(Box box, double dx, double dy, double dz) =>
        new((float)(box.X1 + dx), (float)(box.Y1 + dy), (float)(box.Z1 + dz),
            (float)(box.X2 + dx), (float)(box.Y2 + dy), (float)(box.Z2 + dz));

    /// <summary>The motion with its part into the trunk along the exit taken away (a player
    /// walking into the hull stops at it; one standing on it stops falling).</summary>
    public static (double X, double Y, double Z) Stop(Exit exit, double mx, double my, double mz)
    {
        double into = mx * exit.X + my * exit.Y + mz * exit.Z;
        if (into >= 0)
            return (mx, my, mz);
        return (mx - into * exit.X, my - into * exit.Y, mz - into * exit.Z);
    }
}
