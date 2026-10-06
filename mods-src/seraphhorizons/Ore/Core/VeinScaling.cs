namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>A uniform range, as the game's NatFloat: avg ± var.</summary>
public readonly record struct SizeRange(float Avg, float Var)
{
    public SizeRange Times(double k) => new((float)(Avg * k), (float)(Var * k));
}

/// <summary>The size parameters of one Interesting Ore Gen vein variant.</summary>
public readonly record struct VeinShape(string VeinType, SizeRange Radius, SizeRange BranchCount, SizeRange BranchLength);

/// <summary>
/// Shrinks a vein to a fraction of its ore volume (#439), through the parameter that sets its
/// size for its vein type in Interesting Ore Gen 2.3.8 (<c>TiltedDiscDepositGenerator</c>), never
/// its grade:
/// <list type="bullet">
/// <item><c>fault</c>, <c>seam</c>: a disc, volume ∝ radius² × thickness. The radius is scaled
/// by √factor; thickness is kept.</item>
/// <item><c>chimney*</c>, <c>hydrotube*</c>: tubes of fixed bore. Volume ∝ tendril count ×
/// length, but the radius only sets which nearby chunks the vein is drawn in (beyond it the tubes
/// are clipped), and long tendrils mostly run out of that box, so the count is the parameter
/// that scales cleanly: count × factor. Below 1.5 tendrils the count is set to exactly one and
/// the rest of the factor goes on the length.</item>
/// </list>
/// The spread (var/avg) is kept, so small and large deposits keep their ratio.
/// </summary>
public static class VeinScaling
{
    public static VeinShape Scale(VeinShape vein, double factor)
    {
        if (factor <= 0 || double.IsNaN(factor)) throw new ArgumentOutOfRangeException(nameof(factor), factor, "must be positive");
        if (factor >= 1) return vein;
        switch (vein.VeinType)
        {
            case "fault":
            case "seam":
                return vein with { Radius = vein.Radius.Times(Math.Sqrt(factor)) };
            case "chimney":
            case "chimneywithdepo":
            case "hydrotube":
            case "hydrotubewithdepo":
            {
                double count = vein.BranchCount.Avg * factor;
                if (count >= 1.5)
                    return vein with { BranchCount = vein.BranchCount.Times(factor) };
                // The game truncates the drawn count, so a range around n gives about n - 0.5.
                double meanCount = Math.Max(1, vein.BranchCount.Var >= 0.5f ? vein.BranchCount.Avg - 0.5 : vein.BranchCount.Avg);
                double lengthFactor = Math.Min(1, meanCount * factor);
                return vein with
                {
                    BranchCount = new SizeRange(1, 0),
                    BranchLength = vein.BranchLength.Times(lengthFactor),
                };
            }
            default:
                return vein;
        }
    }
}

/// <summary>
/// The size cut per metal (ore-sizes.json): either a measured median and a target, giving
/// target / median, or a factor given outright. Never above 1: deposits are not made larger.
/// </summary>
public sealed class OreSizeTable
{
    public sealed record Entry(double? MedianIngots = null, double? TargetIngots = null, double? Factor = null);

    private readonly IReadOnlyDictionary<string, Entry> _entries;

    public OreSizeTable(IReadOnlyDictionary<string, Entry> entries) => _entries = entries;

    /// <summary>The volume factor for a metal, or null if the table leaves it alone.</summary>
    public double? FactorFor(string metal)
    {
        if (!_entries.TryGetValue(metal, out var e)) return null;
        double? f = e.Factor ?? (e.MedianIngots is > 0 && e.TargetIngots is > 0 ? e.TargetIngots / e.MedianIngots : null);
        return f is > 0 ? Math.Min(1, f.Value) : null;
    }

    public IEnumerable<string> Metals => _entries.Keys;
}
