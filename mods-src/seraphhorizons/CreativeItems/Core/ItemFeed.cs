namespace SeraphHorizons.Mod.CreativeItems.Core;

/// <summary>
/// The creative item source's settings and count (README "Creative item source and sink"): how
/// many items are due to the hopper under it after some seconds, at a rate in items per second
/// (fractions carried over from tick to tick), or as many as fit when unlimited, never past the
/// count limit, and none while paused. Every setting is clamped wherever it comes from (a packet,
/// a saved block, a test).
/// </summary>
public sealed class ItemFeed
{
    public const double MinRate = 0.1, MaxRate = 100, DefaultRate = 1;

    /// <summary>The highest count limit (0 is none).</summary>
    public const long MaxLimit = 1_000_000_000;

    /// <summary>The most seconds of feed a hopper that cannot take it builds up, so a full hopper
    /// that empties gets the rate again, not a burst.</summary>
    public const double MaxOwedSeconds = 1;

    /// <summary>Items per second, <see cref="MinRate"/> to <see cref="MaxRate"/>.</summary>
    public double Rate { get; private set; } = DefaultRate;

    /// <summary>Keeps the hopper full instead of feeding at <see cref="Rate"/>.</summary>
    public bool Unlimited { get; private set; }

    public bool Paused { get; private set; }

    /// <summary>Stops once <see cref="Total"/> reaches it; 0 is no limit.</summary>
    public long Limit { get; private set; }

    /// <summary>Items that went into the hopper since the last reset.</summary>
    public long Total { get; private set; }

    /// <summary>The fraction of an item (or more, up to <see cref="MaxOwedSeconds"/> of the rate)
    /// due but not yet given.</summary>
    public double Owed { get; private set; }

    public bool LimitReached => Limit > 0 && Total >= Limit;

    public bool Running => !Paused && !LimitReached;

    public static double ClampRate(double rate) =>
        double.IsFinite(rate) ? Math.Clamp(rate, MinRate, MaxRate) : DefaultRate;

    public static long ClampLimit(long limit) => Math.Clamp(limit, 0, MaxLimit);

    public void Configure(double rate, bool unlimited, bool paused, long limit)
    {
        Rate = ClampRate(rate);
        Unlimited = unlimited;
        Paused = paused;
        Limit = ClampLimit(limit);
        if (Paused)
            Owed = 0;
    }

    /// <summary>Restores a saved count and carried fraction, clamped.</summary>
    public void Restore(long total, double owed)
    {
        Total = Math.Max(0, total);
        Owed = double.IsFinite(owed) ? Math.Clamp(owed, 0, Math.Max(1, Rate * MaxOwedSeconds)) : 0;
    }

    /// <summary>How many items are due after <paramref name="seconds"/> more: the rate's whole
    /// items, or with <see cref="Unlimited"/> as many as the hopper takes; at most what is left
    /// to the limit. Call <see cref="Delivered"/> with what went in.</summary>
    public long Due(double seconds)
    {
        if (!Running)
        {
            Owed = 0;
            return 0;
        }
        long left = Limit > 0 ? Limit - Total : long.MaxValue;
        if (Unlimited)
            return left;
        if (double.IsFinite(seconds) && seconds > 0)
            Owed = Math.Min(Owed + Rate * seconds, Math.Max(1, Rate * MaxOwedSeconds));
        return Math.Min((long)Math.Floor(Owed), left);
    }

    /// <summary>Counts <paramref name="count"/> items given and takes them off what is owed.</summary>
    public void Delivered(long count)
    {
        if (count <= 0)
            return;
        Total += count;
        Owed = Unlimited ? 0 : Math.Max(0, Owed - count);
    }

    /// <summary>Zeroes the count (the settings stay).</summary>
    public void Reset()
    {
        Total = 0;
        Owed = 0;
    }
}
