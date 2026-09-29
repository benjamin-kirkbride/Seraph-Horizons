namespace SeraphHorizons.IconExport.Core;

public sealed record RenderTarget(string Code, IconKind Kind);

public sealed record RenderJob(string Code, IconKind Kind, string RelativePath);

public sealed class Plan
{
    public List<RenderJob> ToRender { get; } = new();
    public List<RenderJob> SkippedExisting { get; } = new();
    public int Duplicates { get; set; }
}

public static class ExportPlanner
{
    /// <summary>
    /// Turns targets into jobs: one per output file, in the order given. A file that already
    /// exists is skipped unless <paramref name="force"/>, which is what makes a second run resume.
    /// </summary>
    public static Plan Build(IEnumerable<RenderTarget> targets, Func<string, bool> exists, bool force)
    {
        var plan = new Plan();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (RenderTarget t in targets)
        {
            string rel = IconPaths.ToRelativePath(t.Code, t.Kind);
            if (!seen.Add(rel))
            {
                plan.Duplicates++;
                continue;
            }
            var job = new RenderJob(t.Code, t.Kind, rel);
            if (!force && exists(rel))
            {
                plan.SkippedExisting.Add(job);
            }
            else
            {
                plan.ToRender.Add(job);
            }
        }
        return plan;
    }
}

/// <summary>
/// Spreads the jobs over frames: each frame renders at least one and then keeps going while it
/// is under both the count and the time limit, so the game keeps drawing and reading input.
/// </summary>
public sealed class FrameScheduler
{
    private readonly IReadOnlyList<RenderJob> _jobs;

    public FrameScheduler(IReadOnlyList<RenderJob> jobs, int maxPerFrame, double maxMsPerFrame)
    {
        if (maxPerFrame < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPerFrame), "must be at least 1");
        }
        _jobs = jobs;
        MaxPerFrame = maxPerFrame;
        MaxMsPerFrame = maxMsPerFrame;
    }

    public int MaxPerFrame { get; }
    public double MaxMsPerFrame { get; }
    public int Next { get; private set; }
    public int Count => _jobs.Count;
    public bool Done => Next >= _jobs.Count;

    /// <summary>
    /// Runs <paramref name="render"/> for this frame's share of the jobs. <paramref name="elapsedMs"/>
    /// is the time spent in this frame so far. Returns how many were run.
    /// </summary>
    public int RunFrame(Func<double> elapsedMs, Action<RenderJob> render)
    {
        int n = 0;
        while (!Done && (n == 0 || (n < MaxPerFrame && elapsedMs() < MaxMsPerFrame)))
        {
            RenderJob job = _jobs[Next];
            Next++;
            n++;
            render(job);
        }
        return n;
    }
}

/// <summary>True once every <c>intervalMs</c>, for progress messages.</summary>
public sealed class IntervalTimer
{
    private readonly double _intervalMs;
    private double _last;

    public IntervalTimer(double intervalMs, double startMs)
    {
        _intervalMs = intervalMs;
        _last = startMs;
    }

    public bool Due(double nowMs)
    {
        if (nowMs - _last < _intervalMs)
        {
            return false;
        }
        _last = nowMs;
        return true;
    }
}

public sealed class RunTotals
{
    public int Planned { get; set; }
    public int Written { get; set; }
    public int SkippedExisting { get; set; }
    public int Failed { get; set; }
    public int Transparent { get; set; }
    public int Untextured { get; set; }
    public int CustomRenderer { get; set; }

    public int Processed => Written + Failed;

    /// <summary>More than a tenth of the written icons look untextured: the bug this tool is for.</summary>
    public bool MostlyBroken => Written > 0 && Untextured * 10 > Written;

    public string Summary() =>
        $"written {Written}, skipped as existing {SkippedExisting}, failed {Failed}, "
        + $"fully transparent {Transparent}, untextured-looking {Untextured}";

    public string Progress(double elapsedMs)
    {
        string eta = "";
        if (Processed > 0 && Planned > Processed)
        {
            double left = elapsedMs / Processed * (Planned - Processed) / 1000;
            eta = left >= 60 ? $", about {left / 60:0} min left" : $", about {left:0} s left";
        }
        return $"{Processed}/{Planned} ({Summary()}){eta}";
    }
}
