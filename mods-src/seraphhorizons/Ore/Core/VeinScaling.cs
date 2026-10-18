namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>A uniform range, as the game's NatFloat: avg ± var.</summary>
public readonly record struct SizeRange(float Avg, float Var)
{
    public SizeRange Times(double k) => new((float)(Avg * k), (float)(Var * k));
}

/// <summary>The size parameters of one Interesting Ore Gen vein variant.</summary>
public readonly record struct VeinShape(string VeinType, SizeRange Radius, SizeRange BranchCount, SizeRange BranchLength);

/// <summary>
/// Scales a vein to a multiple of its ore volume (#439, #863), through the parameter that sets its
/// size for its vein type in Interesting Ore Gen 2.3.8 (<c>TiltedDiscDepositGenerator</c>), never
/// its grade:
/// <list type="bullet">
/// <item><c>fault</c>, <c>seam</c>: a disc, volume ∝ radius² × thickness. The radius is scaled
/// by √factor; thickness is kept.</item>
/// <item><c>chimney*</c>, <c>hydrotube*</c>: tubes of fixed bore. Volume ∝ tendril count ×
/// length, but the radius only sets which nearby chunks the vein is drawn in (beyond it the tubes
/// are clipped), and long tendrils mostly run out of that box, so the count is the parameter
/// that scales cleanly: count × factor. Shrunk below 1.5 tendrils, the count is set to exactly one
/// and the rest of the factor goes on the length.</item>
/// </list>
/// The spread (var/avg) is kept, so small and large deposits keep their ratio. A factor of 1 leaves
/// the vein as it is; one over 1 grows it the same way (#863: graded ore is four times the blocks
/// it was, <see cref="OreSizeTable.VolumeFor"/>).
/// </summary>
public static class VeinScaling
{
    public static VeinShape Scale(VeinShape vein, double factor)
    {
        if (factor <= 0 || double.IsNaN(factor)) throw new ArgumentOutOfRangeException(nameof(factor), factor, "must be positive");
        if (factor == 1) return vein;
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
                if (count >= 1.5 || factor > 1)
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
/// The size factor per metal (ore-sizes.json): either a measured median and a target, giving
/// target / median, or a factor given outright, on the table's basis (<see cref="DepositBasis"/>:
/// one ore a block, twice the ingots of before, so a factor may be over 1). With
/// <c>before</c>, for a world created before that basis, the factors are
/// <see cref="DepositBasis.BeforeFactorShare"/> of the table's and never above 1, and the sizes
/// <see cref="DepositBasis.BeforeSizeShare"/>, as they were then. <see cref="VolumeFor"/> adds the
/// blocks graded ore needs at the pack's units.
/// </summary>
public sealed class OreSizeTable
{
    /// <param name="SmallIngots">The bottom of the metal's deposit range (epic #435), for the
    /// deposit registry's size tiers (<see cref="DepositSizing"/>); with <paramref name="LargeIngots"/>.</param>
    public sealed record Entry(double? MedianIngots = null, double? TargetIngots = null, double? Factor = null,
        double? SmallIngots = null, double? LargeIngots = null);

    private readonly IReadOnlyDictionary<string, Entry> _entries;
    private readonly bool _before;

    public OreSizeTable(IReadOnlyDictionary<string, Entry> entries, bool before = false)
    {
        _entries = entries;
        _before = before;
    }

    /// <summary>Whether this is the table as a world created before the current basis reads it.</summary>
    public bool Before => _before;

    /// <summary>The volume factor for a metal, or null if the table leaves it alone.</summary>
    public double? FactorFor(string metal)
    {
        if (!_entries.TryGetValue(metal, out var e)) return null;
        double? f = e.Factor ?? (e.MedianIngots is > 0 && e.TargetIngots is > 0 ? e.TargetIngots / e.MedianIngots : null);
        if (f is not > 0) return null;
        return _before ? Math.Min(1, f.Value * DepositBasis.BeforeFactorShare) : f.Value;
    }

    /// <summary>
    /// The volume factor for a vein of <paramref name="ore"/> (a deposit variant's code): its metal's
    /// cut (<see cref="FactorFor"/>, 1 for a metal the table leaves alone), times
    /// <paramref name="gradedOreBlocks"/> for a graded metal ore (<see cref="OreProducts.Ores"/>), which
    /// is <see cref="OreProducts.DepositBlocks"/> while ore processing holds an ore item at about a
    /// quarter of vanilla's units (#863), so a deposit keeps the metal the cut gave it. Null for an
    /// ore that is not managed or whose factor comes out at 1 (left as it is).
    /// </summary>
    public double? VolumeFor(string? ore, double gradedOreBlocks)
    {
        if (OreMetals.MetalOf(ore) is not { } metal) return null;
        double factor = FactorFor(metal) ?? 1;
        if (OreProducts.Ores.Contains(ore!)) factor *= gradedOreBlocks;
        return factor == 1 ? null : factor;
    }

    public IEnumerable<string> Metals => _entries.Keys;

    /// <summary>A metal's small / typical / large sizes, or null if the table has no range for it
    /// (coal and the minerals: they aren't sold by size).</summary>
    public SizeTargets? TargetsFor(string metal) =>
        _entries.TryGetValue(metal, out var e) && e is { SmallIngots: > 0, TargetIngots: > 0, LargeIngots: > 0 }
        && e.SmallIngots < e.LargeIngots
            ? new SizeTargets(e.SmallIngots.Value * SizeShare, e.TargetIngots.Value * SizeShare, e.LargeIngots.Value * SizeShare)
            : null;

    private double SizeShare => _before ? DepositBasis.BeforeSizeShare : 1;
}
