namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>
/// Two eidolons kept apart (Eidolon/README.md, "Spacing"), game-independent. An eidolon's
/// <c>repulseagents</c> is <c>movable: false</c>, so nothing shoves it, another eidolon included,
/// and two could stand wholly inside each other for good (two spawned in one place, two following
/// one player to the same spot). Each pushes itself away from any other eidolon whose circle (half
/// its box's width) overlaps its own, by motion, harder the deeper, at most
/// <see cref="MaxPush"/> a tick; two in the very same place go opposite ways along a direction both
/// work out alike from their ids.
/// </summary>
public static class EidolonSpacing
{
    /// <summary>The most motion, blocks per 1/60 s, one push adds: a little under a walk (about
    /// 0.07), so two overlapping walk apart, and one walking past another is not thrown off its way.</summary>
    public const double MaxPush = 0.05;

    /// <summary>Motion per block of overlap.</summary>
    public const double Stiffness = 0.05;

    /// <summary>The motion to add to an eidolon (id <paramref name="self"/>) for another (id
    /// <paramref name="other"/>) at (<paramref name="dx"/>, <paramref name="dz"/>) from it, their
    /// circles' radii summing to <paramref name="reach"/>; (0, 0) when they do not overlap.</summary>
    public static (double X, double Z) Push(double dx, double dz, double reach, long self, long other)
    {
        double d = Math.Sqrt(dx * dx + dz * dz);
        if (d >= reach || self == other)
            return (0, 0);
        double ux, uz;
        if (d > 1e-6)
            (ux, uz) = (-dx / d, -dz / d);
        else
        {
            // The same place: a direction from the pair, the lower id along it, the higher against.
            double angle = Math.Min(self, other) * 2.399963229728653;
            double sign = self < other ? 1 : -1;
            (ux, uz) = (sign * Math.Cos(angle), sign * Math.Sin(angle));
        }
        double push = Math.Min(MaxPush, (reach - d) * Stiffness);
        return (ux * push, uz * push);
    }
}
