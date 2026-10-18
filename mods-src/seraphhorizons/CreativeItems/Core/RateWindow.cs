namespace SeraphHorizons.Mod.CreativeItems.Core;

/// <summary>
/// Items per minute over the last <see cref="Seconds"/> seconds, sliding: counts go into
/// one-second buckets (the current second's and the <see cref="Seconds"/> - 1 before it), and the
/// rate is their sum over the time they cover. Until a full window has
/// passed since it started (or was reset) it averages over the time so far, but over no less
/// than <see cref="MinSpan"/> seconds, so the first item does not read as a burst. Times are the
/// server's, in seconds.
/// </summary>
public sealed class RateWindow
{
    public const int Seconds = 60;
    public const double MinSpan = 5;

    private readonly long[] _buckets = new long[Seconds];
    private long _second;
    private double _since;

    public RateWindow(double now) => Reset(now);

    public void Reset(double now)
    {
        Array.Clear(_buckets);
        _second = (long)Math.Floor(now);
        _since = now;
    }

    public void Add(double now, long count)
    {
        if (count <= 0)
            return;
        Advance(now);
        _buckets[Bucket(_second)] += count;
    }

    public double PerMinute(double now)
    {
        Advance(now);
        long sum = 0;
        foreach (var count in _buckets)
            sum += count;
        double covered = Seconds - 1 + (now - Math.Floor(now));
        double span = Math.Clamp(now - _since, MinSpan, covered);
        return sum * 60.0 / span;
    }

    // Empties the buckets of the seconds passed since the last call; a clock that went back
    // (a reload restarts it) starts the window again.
    private void Advance(double now)
    {
        long second = (long)Math.Floor(now);
        if (second < _second)
        {
            Reset(now);
            return;
        }
        long gap = second - _second;
        if (gap >= Seconds)
            Array.Clear(_buckets);
        else
            for (long s = _second + 1; s <= second; s++)
                _buckets[Bucket(s)] = 0;
        _second = second;
    }

    private static int Bucket(long second) => (int)(((second % Seconds) + Seconds) % Seconds);
}
