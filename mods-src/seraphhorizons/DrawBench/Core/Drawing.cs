using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.DrawBench.Core;

/// <summary>The draw bench's <c>requires</c> vocabulary: its five build stages (<c>config/builds/drawbench.json</c>;
/// the die's is also its wearing part's), and the work's (<c>billetlead</c>, <c>billetcopper</c>: a hollow section
/// of that metal on the bench).</summary>
public static class DrawBenchRequires
{
    public const string Die = "die";
    public const string BilletLead = "billetlead";
    public const string BilletCopper = "billetcopper";

    /// <summary>The stages' requires, in order.</summary>
    public static readonly IReadOnlyList<string> Stages = ["gearbox", "chain", "dog", "mandrel", Die];

    /// <summary>The billet <c>requires</c> of class <paramref name="k"/>: 1 lead, 2 copper.</summary>
    public static string? Billet(int k) => k switch { 1 => BilletLead, 2 => BilletCopper, _ => null };

    /// <summary>Every <c>requires</c> value the rig may use.</summary>
    public static readonly IReadOnlySet<string> KnownRequires = Stages.Concat([BilletLead, BilletCopper]).ToHashSet();
}

/// <summary>Why a hollow section can or cannot go on the bench.</summary>
public enum DrawLoadVerdict
{
    Loads,
    NotAHollow,
    /// <summary>A stage is missing (a spent die counts).</summary>
    Incomplete,
    /// <summary>A hollow is already being drawn.</summary>
    Occupied,
    /// <summary>The die fitted does not draw this metal (an iron die and copper).</summary>
    DieRefuses,
}

/// <summary>
/// The draw's arithmetic: a hollow section of class k (1 lead, 2 copper), the game's chute section
/// (<c>game:chutesection-{metal}</c>), threaded on the mandrel, is drawn into
/// <see cref="SectionsPerHollow"/> pipe sections (<c>seraphhorizons:pipesection-{metal}</c>), one per
/// stroke cycle of <c>TurnsPerSection(k)</c> axle turns, its progress W in sections advancing with the
/// shaft's angle as the gear cutter's teeth do. Pipe section m + 1 comes off as W crosses
/// m + <see cref="HandOut"/>, as its draw finishes and the jaws open; the job ends at W = 4, the dog
/// back at the die. The hollow on the bench is a <see cref="MachineJob"/> ending at 4 (<see cref="Start"/>).
/// </summary>
public static class Drawing
{
    public const int SectionsPerHollow = 4;

    /// <summary>Where in a section's cycle (W = m + this) the section is handed out: its tail has left the
    /// die and the jaws have sprung open. The model hides the section from there (the rig's
    /// <c>draw.handOut</c>, which the reader holds to this).</summary>
    public const double HandOut = 0.43125;
    public const string LeadHollow = "game:chutesection-lead";
    public const string CopperHollow = "game:chutesection-copper";

    /// <summary>The die metals there are: <c>seraphhorizons:drawdie-{metal}</c>.</summary>
    public static readonly IReadOnlyList<string> DieMetalNames = ["iron", "steel"];

    public const string DieIronCode = "seraphhorizons:drawdie-iron";
    public const string DieSteelCode = "seraphhorizons:drawdie-steel";

    /// <summary>The die's wearing part in the build file.</summary>
    public const string DiePart = "die";

    /// <summary>A die's metal: <c>iron</c> or <c>steel</c>; null for anything else.</summary>
    public static string? DieMetalOf(string? code) => code switch
    {
        DieIronCode => "iron",
        DieSteelCode => "steel",
        _ => null,
    };

    public static string? DieCodeFor(string? metal) => metal switch { "iron" => DieIronCode, "steel" => DieSteelCode, _ => null };

    /// <summary>The class of a metal: 1 lead, 2 copper, 0 else.</summary>
    public static int ClassOfMetal(string? metal) => metal switch { "lead" => 1, "copper" => 2, _ => 0 };

    public static string? MetalOf(int k) => k switch { 1 => "lead", 2 => "copper", _ => null };

    /// <summary>The class a hollow section is drawn as: 1 lead, 2 copper, 0 not one the bench draws
    /// (an ingot, an angle or a pipe section included: what comes off is never taken back on).</summary>
    public static int ClassOfHollow(string? code) => code switch { LeadHollow => 1, CopperHollow => 2, _ => 0 };

    /// <summary>The hollow section of class <paramref name="k"/>: the game's chute section in that
    /// metal (vanilla has copper; lead is a state the pack's patch adds).</summary>
    public static string? HollowFor(int k) => k switch { 1 => LeadHollow, 2 => CopperHollow, _ => null };

    /// <summary>The pipe section a hollow of class <paramref name="k"/> is drawn into, in its metal.</summary>
    public static string? SectionFor(int k) => MetalOf(k) is { } metal ? "seraphhorizons:pipesection-" + metal : null;

    /// <summary>Sections drawn by <paramref name="radians"/> of axle rotation.</summary>
    public static double SectionsFor(double radians, double turnsPerSection) =>
        turnsPerSection > 0 ? Math.Max(0, radians) / (2 * Math.PI * turnsPerSection) : 0;

    /// <summary>Whether a hollow <paramref name="code"/> goes on: the bench complete
    /// (<paramref name="complete"/>), nothing on it, and the die fitted drawing its metal
    /// (<paramref name="dieDraws"/>, the metals the die draws).</summary>
    public static DrawLoadVerdict CanLoad(string? code, bool complete, bool occupied, IReadOnlyCollection<string> dieDraws)
    {
        int k = ClassOfHollow(code);
        if (k == 0)
            return DrawLoadVerdict.NotAHollow;
        if (!complete)
            return DrawLoadVerdict.Incomplete;
        if (occupied)
            return DrawLoadVerdict.Occupied;
        return dieDraws.Contains(MetalOf(k)!) ? DrawLoadVerdict.Loads : DrawLoadVerdict.DieRefuses;
    }

    /// <summary>Whether the draw runs: complete (a die in), a hollow on, the shaft at
    /// <paramref name="minSpeed"/> or faster.</summary>
    public static bool Running(bool complete, bool jobOn, float speed, float minSpeed) =>
        complete && jobOn && Math.Abs(speed) >= minSpeed;

    /// <summary>The draw of a hollow of class <paramref name="k"/> just put on (W ends at 4); none for a class
    /// the bench does not draw.</summary>
    public static MachineJob Start(int k) => MachineJob.Start(k is 1 or 2 ? k : 0, SectionsPerHollow);

    /// <summary>A saved draw: W clamped to 0..4; none when the class is not a hollow's.</summary>
    public static MachineJob Restore(int k, double work) => MachineJob.Restore(k is 1 or 2 ? k : 0, work, SectionsPerHollow);

    /// <summary>Sections handed out so far: those whose hand-out, m + <see cref="HandOut"/>, W has reached.</summary>
    public static int SectionsDone(MachineJob job) =>
        job.On ? Math.Clamp((int)Math.Floor(Math.Min(job.Work, job.End) - HandOut + 1 + 1e-9), 0, (int)job.End) : 0;

    /// <summary>The draw after <paramref name="radians"/> more of the axle, W held at the end; how many
    /// sections came off in this step (W crossing 0.43125, 1.43125, 2.43125 or 3.43125); and whether it
    /// finished the hollow (W reaching 4).</summary>
    public static (MachineJob Job, int Sections, bool Finished) Advance(MachineJob job, double radians, double turnsPerSection)
    {
        var (next, finished) = job.Advance(SectionsFor(radians, turnsPerSection));
        return (next, SectionsDone(next) - SectionsDone(job), finished);
    }
}
