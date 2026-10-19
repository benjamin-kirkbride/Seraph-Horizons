namespace SeraphHorizons.Mod.Machines.Core;

/// <summary>
/// The work on a machine now (the workshop machines' job: the press brake's half plate, the draw
/// bench's hollow section): its class k (1 thin, 2 thick, as the rig's classes; 0 none), its progress W
/// in the machine's own unit (a plate's fold cycle, pipe sections drawn), where W ends for it, and the
/// steps taken on it where a machine counts them (a blow of the hammer). W only grows and is held at
/// its end; a machine turns its clock's radians or a blow into W and asks what crossed.
/// </summary>
public readonly record struct MachineJob(int Class, double Work, double End, int Steps = 0)
{
    public static readonly MachineJob None = new(0, 0, 0);

    /// <summary>Something is on the machine.</summary>
    public bool On => Class > 0 && End > 0;

    public bool Done => On && Work >= End - 1e-9;

    /// <summary>Nothing has been done to the work yet: it can be taken back as it went on.</summary>
    public bool Untouched => On && Work <= 0 && Steps == 0;

    /// <summary>The share of the work done, 0 to 1 (0 with nothing on).</summary>
    public double Progress => On ? Math.Clamp(Work / End, 0, 1) : 0;

    /// <summary>New work of class <paramref name="k"/> ending at <paramref name="end"/>; none when either is not positive.</summary>
    public static MachineJob Start(int k, double end) => k > 0 && end > 0 && double.IsFinite(end) ? new MachineJob(k, 0, end) : None;

    /// <summary>The job after <paramref name="amount"/> more W, held at the end, and whether this step finished it.</summary>
    public (MachineJob Job, bool Finished) Advance(double amount)
    {
        if (!On || Done || !(amount > 0) || !double.IsFinite(amount))
            return (this, false);
        var next = this with { Work = Math.Min(End, Work + amount) };
        if (next.Work >= End - 1e-9)
            next = next with { Work = End };
        return (next, next.Done);
    }

    /// <summary>The job after one counted step (a blow) worth <paramref name="amount"/> W.</summary>
    public (MachineJob Job, bool Finished) Step(double amount)
    {
        if (!On || Done)
            return (this, false);
        var (job, finished) = Advance(amount);
        return (job with { Steps = Steps + 1 }, finished);
    }

    /// <summary>A saved job: W clamped to 0..<paramref name="end"/>, steps at least 0; none when the class or
    /// the end is not positive (a class the machine no longer takes is given an end of 0).</summary>
    public static MachineJob Restore(int k, double work, double end, int steps = 0) =>
        k > 0 && end > 0 && double.IsFinite(end)
            ? new MachineJob(k, double.IsFinite(work) ? Math.Clamp(work, 0, end) : 0, end, Math.Max(0, steps))
            : None;

    /// <summary>The moments in <paramref name="moments"/> (positions of W) that W passed going from
    /// <paramref name="before"/> to <paramref name="after"/>: a fold made, a section handed out.</summary>
    public static int Crossed(IEnumerable<double> moments, double before, double after) =>
        moments.Count(m => before < m && after >= m);
}
