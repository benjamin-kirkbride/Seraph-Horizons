namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>
/// An Interesting Ore Gen 2.3.8 ore shoot's ground plan (<c>OreShootSampler</c>, #946), at the top of
/// its fault where its dip has not shifted it: centred on a fault segment, it measures
/// <c>s = d · tangent</c> along the segment's own tangent and <c>t = d · across</c> along the fault's
/// global across direction, and holds the points with <c>(s / halfStrike)² + (t / halfAcross)² ≤ 1</c>
/// at density <c>1 − that</c>. The two axes are not at right angles where the segment's tangent
/// turns from the fault's overall heading (a shoot sits at a bend), so the ellipse is sheared, and
/// the fault rock IOG lays along the curving spline leaves it within a few dozen blocks of the
/// centre, however long the strike: a fault width times <c>4r/3</c>, which assumed the fault ran
/// along the strike the whole way, was up to six times the fault rock of a shoot widened four times
/// along its strike (#863). IOG only tests block centres inside the zone's box, which is the centre
/// ± (halfStrike + 2) each way (<see cref="BoxHalf"/>).
/// </summary>
public sealed record ShootShape(double CentreX, double CentreZ, double TangentX, double TangentZ, double AcrossX, double AcrossZ,
    double HalfStrike, double HalfAcross, double BoxHalf)
{
    /// <summary>IOG's density at a point of the plan (0 outside the shoot or its box).</summary>
    public double DensityAt(double x, double z)
    {
        double dx = x - CentreX, dz = z - CentreZ;
        if (Math.Abs(dx) > BoxHalf || Math.Abs(dz) > BoxHalf) return 0;
        double s = (dx * TangentX + dz * TangentZ) / HalfStrike;
        double t = (dx * AcrossX + dz * AcrossZ) / HalfAcross;
        double e = s * s + t * t;
        return e <= 1 ? 1 - e : 0;
    }

    /// <summary>Its density summed over every block of a layer (block centres, as IOG tests them).</summary>
    public double Whole()
    {
        double sum = 0;
        int x0 = (int)Math.Floor(CentreX - BoxHalf), x1 = (int)Math.Ceiling(CentreX + BoxHalf);
        int z0 = (int)Math.Floor(CentreZ - BoxHalf), z1 = (int)Math.Ceiling(CentreZ + BoxHalf);
        for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
                sum += DensityAt(x + 0.5, z + 0.5);
        return sum;
    }

    /// <summary>Its density summed over the given blocks of a layer (the fault rock, each once).</summary>
    public double Over(IEnumerable<(int X, int Z)> blocks) => blocks.Sum(b => DensityAt(b.X + 0.5, b.Z + 0.5));

    /// <summary>Its density summed over points of the plan (block centres moved back up the dip).</summary>
    public double Over(IEnumerable<(double X, double Z)> points) => points.Sum(p => DensityAt(p.X, p.Z));

    /// <summary>
    /// Up to about <paramref name="count"/> points of the plan, each with its density as its weight,
    /// on a square grid over the shoot: where its country rock is sampled.
    /// </summary>
    public IReadOnlyList<(double X, double Z, double Weight)> Samples(int count = 24)
    {
        int area = 0;
        int x0 = (int)Math.Floor(CentreX - BoxHalf), x1 = (int)Math.Ceiling(CentreX + BoxHalf);
        int z0 = (int)Math.Floor(CentreZ - BoxHalf), z1 = (int)Math.Ceiling(CentreZ + BoxHalf);
        for (int x = x0; x <= x1; x += 2)
            for (int z = z0; z <= z1; z += 2)
                if (DensityAt(x + 0.5, z + 0.5) > 0) area += 4;
        if (area == 0) return [];
        double step = Math.Max(2, Math.Sqrt(area / (double)Math.Max(1, count)));
        var points = new List<(double, double, double)>();
        for (double x = CentreX - BoxHalf + step / 2; x <= CentreX + BoxHalf; x += step)
            for (double z = CentreZ - BoxHalf + step / 2; z <= CentreZ + BoxHalf; z += step)
            {
                double w = DensityAt(x, z);
                if (w > 0) points.Add((x, z, w));
            }
        if (points.Count == 0) points.Add((CentreX, CentreZ, 1));
        return points;
    }
}
