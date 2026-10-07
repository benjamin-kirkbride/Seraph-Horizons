using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.TrunkEntities.Core;

/// <summary>
/// Keeping agents out of a trunk, game-independent: the trunk as good as solid. The trunk is its
/// oriented footprint (<see cref="Footprint"/>: one rectangle, as long and wide as the trunk,
/// turned with its yaw, from its underside to its top), not its axis-aligned collision boxes
/// (<see cref="TrunkBoxes.Turned"/>, which step along a diagonal). The agent is a vertical
/// cylinder: a circle of half its box's width about the box's middle, from its feet to its head.
/// The way out is worked out in the trunk's own frame: from beside the trunk, straight away from
/// the nearest point of its outline (so along a side, square to the trunk's axis, the same depth
/// all along it; round a corner, round it); from inside, the shortest way out across or along the
/// trunk; or up, onto it, when the agent's feet are within <see cref="StandMargin"/> of the top.
/// </summary>
public static class TrunkPush
{
    /// <summary>The most an agent is moved out in one step, blocks: above a walk (about 0.07
    /// blocks per 1/60 s) so a walking player is held at the surface, and gentle enough that one
    /// found deep inside slides out over a few steps rather than jumping.</summary>
    public const double MaxStep = 0.25;

    /// <summary>How far below the top of the trunk, blocks, an agent's feet may be and still be
    /// lifted onto it rather than pushed off its side.</summary>
    public const double StandMargin = 0.4;

    /// <summary>A little past touching, so a pushed-out agent is clear, not grazing.</summary>
    public const double Skin = 0.001;

    /// <summary>
    /// A trunk's solid shape relative to its position (the middle of its underside): a rectangle
    /// <paramref name="HalfWidth"/> to either side of its axis and <paramref name="HalfLength"/>
    /// along it, from 0 to <paramref name="Height"/>, turned by <paramref name="Yaw"/> as the
    /// trunk's boxes are (<see cref="TrunkBoxes.Turned"/>: a turn of yaw + π about y, local
    /// (x, z) to world (x cos a + z sin a, −x sin a + z cos a); the rectangle is symmetric, so
    /// the half turn changes nothing).
    /// </summary>
    public readonly record struct Footprint(double HalfWidth, double HalfLength, double Height, double Yaw)
    {
        /// <summary>A trunk of <paramref name="trunk"/>'s class (<see cref="TrunkBox.Size"/>) at
        /// <paramref name="yaw"/>.</summary>
        public static Footprint Of(TrunkClass trunk, double yaw)
        {
            var (length, width, height) = TrunkBox.Size(trunk);
            return new Footprint(width / 2.0, length / 2.0, height, yaw);
        }

        private double A => Yaw + Math.PI;

        /// <summary>A world offset (from the trunk's position) in the trunk's frame.</summary>
        public (double X, double Z) ToLocal(double x, double z)
        {
            double c = Math.Cos(A), s = Math.Sin(A);
            return (x * c - z * s, x * s + z * c);
        }

        /// <summary>A vector in the trunk's frame in the world's.</summary>
        public (double X, double Z) ToWorld(double x, double z)
        {
            double c = Math.Cos(A), s = Math.Sin(A);
            return (x * c + z * s, -x * s + z * c);
        }
    }

    /// <summary>A way out: a unit direction (horizontal, or straight up) and how far.</summary>
    public readonly record struct Exit(double X, double Y, double Z, double Distance)
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

    /// <summary>Whether two boxes overlap (touching is not).</summary>
    public static bool Overlaps(Box a, Box b) =>
        a.X1 < b.X2 && a.X2 > b.X1 && a.Y1 < b.Y2 && a.Y2 > b.Y1 && a.Z1 < b.Z2 && a.Z2 > b.Z1;

    /// <summary>Whether <paramref name="agent"/> (a box relative to the trunk's position, taken
    /// as its cylinder) is in the trunk (touching is not).</summary>
    public static bool Inside(Footprint trunk, Box agent) => Out(trunk, agent) != null;

    /// <summary>The shortest way for <paramref name="agent"/> (a box relative to the trunk's
    /// position, taken as its cylinder) out of <paramref name="trunk"/>, or null when it is not in
    /// it.</summary>
    public static Exit? Out(Footprint trunk, Box agent)
    {
        if (agent.Y1 >= trunk.Height || agent.Y2 <= 0)
            return null;
        double r = Math.Min(agent.X2 - agent.X1, agent.Z2 - agent.Z1) / 2;
        var (cx, cz) = trunk.ToLocal((agent.X1 + agent.X2) / 2.0, (agent.Z1 + agent.Z2) / 2.0);
        double w = trunk.HalfWidth, l = trunk.HalfLength;
        double ex, ez, d;
        // The nearest point of the rectangle to the middle; beside the trunk, straight away from it.
        double nx = Math.Clamp(cx, -w, w), nz = Math.Clamp(cz, -l, l);
        double gx = cx - nx, gz = cz - nz, gap = Math.Sqrt(gx * gx + gz * gz);
        if (gap > 1e-9)
        {
            if (gap >= r)
                return null;
            (ex, ez, d) = (gx / gap, gz / gap, r - gap + Skin);
        }
        else
        {
            // The middle is inside (or on the outline): out across or along, whichever is shorter.
            double across = w - Math.Abs(cx), along = l - Math.Abs(cz);
            if (across <= along)
                (ex, ez, d) = (cx >= 0 ? 1 : -1, 0, across + r + Skin);
            else
                (ex, ez, d) = (0, cz >= 0 ? 1 : -1, along + r + Skin);
        }
        // Up, onto the trunk, only for an agent whose feet are near the top.
        double up = trunk.Height - agent.Y1 + Skin;
        if (up <= StandMargin + Skin)
            return new Exit(0, 1, 0, up);
        var (wx, wz) = trunk.ToWorld(ex, ez);
        return new Exit(wx, 0, wz, d);
    }

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
