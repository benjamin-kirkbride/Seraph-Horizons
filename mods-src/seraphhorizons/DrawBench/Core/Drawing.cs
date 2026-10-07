namespace SeraphHorizons.Mod.DrawBench.Core;

/// <summary>Why an ingot can or cannot go on the bench.</summary>
public enum DrawLoadVerdict
{
    Loads,
    NotAnIngot,
    /// <summary>A stage is missing (a spent die counts).</summary>
    Incomplete,
    /// <summary>An ingot is already being drawn.</summary>
    Occupied,
    /// <summary>The die fitted does not draw this metal (an iron die and copper).</summary>
    DieRefuses,
}

/// <summary>
/// The draw's arithmetic: an ingot of class k (1 lead, 2 copper), cast hollow, is drawn into
/// <see cref="SectionsPerIngot"/> chute sections (the game's <c>chutesection</c>, closed and seamless),
/// one per stroke cycle of <c>TurnsPerSection(k)</c> axle turns, its progress W in sections advancing
/// with the shaft's angle as the gear cutter's teeth do. A section comes off each time W crosses a
/// whole number; the job ends at W = 3.
/// </summary>
public static class Drawing
{
    public const int SectionsPerIngot = 3;
    public const string LeadIngot = "game:ingot-lead";
    public const string CopperIngot = "game:ingot-copper";

    /// <summary>The die metals there are: <c>seraphhorizons:drawdie-{metal}</c>.</summary>
    public static readonly IReadOnlyList<string> DieMetalNames = ["iron", "steel"];

    /// <summary>The class of a metal: 1 lead, 2 copper, 0 else.</summary>
    public static int ClassOfMetal(string? metal) => metal switch { "lead" => 1, "copper" => 2, _ => 0 };

    public static string? MetalOf(int k) => k switch { 1 => "lead", 2 => "copper", _ => null };

    /// <summary>The class an ingot is drawn as: 1 lead, 2 copper, 0 not one the bench draws (a chute
    /// section included: what comes off is never taken back on).</summary>
    public static int ClassOfIngot(string? code) => code switch { LeadIngot => 1, CopperIngot => 2, _ => 0 };

    public static string? IngotFor(int k) => k switch { 1 => LeadIngot, 2 => CopperIngot, _ => null };

    /// <summary>The section an ingot of class <paramref name="k"/> is drawn into: the game's chute
    /// section in that metal (vanilla has copper; lead is a state the pack's patch adds).</summary>
    public static string? SectionFor(int k) => MetalOf(k) is { } metal ? "game:chutesection-" + metal : null;

    /// <summary>Sections drawn by <paramref name="radians"/> of axle rotation.</summary>
    public static double SectionsFor(double radians, double turnsPerSection) =>
        turnsPerSection > 0 ? Math.Max(0, radians) / (2 * Math.PI * turnsPerSection) : 0;

    /// <summary>Whether an ingot <paramref name="code"/> goes on: the bench complete
    /// (<paramref name="complete"/>), nothing on it, and the die fitted drawing its metal
    /// (<paramref name="dieDraws"/>, the metals the die draws).</summary>
    public static DrawLoadVerdict CanLoad(string? code, bool complete, bool occupied, IReadOnlyCollection<string> dieDraws)
    {
        int k = ClassOfIngot(code);
        if (k == 0)
            return DrawLoadVerdict.NotAnIngot;
        if (!complete)
            return DrawLoadVerdict.Incomplete;
        if (occupied)
            return DrawLoadVerdict.Occupied;
        return dieDraws.Contains(MetalOf(k)!) ? DrawLoadVerdict.Loads : DrawLoadVerdict.DieRefuses;
    }

    /// <summary>Whether the draw runs: complete (a die in), an ingot on, the shaft at
    /// <paramref name="minSpeed"/> or faster.</summary>
    public static bool Running(bool complete, bool jobOn, float speed, float minSpeed) =>
        complete && jobOn && Math.Abs(speed) >= minSpeed;
}

/// <summary>An ingot on the bench: its class and the sections drawn so far, W (0..3).</summary>
public readonly record struct DrawJob(int Class, double Work)
{
    public static readonly DrawJob None = new(0, 0);

    public bool On => Class is 1 or 2;

    /// <summary>The work's end: three sections.</summary>
    public int End => On ? Drawing.SectionsPerIngot : 0;

    public bool Done => On && Work >= End - 1e-9;

    /// <summary>Whole sections drawn so far.</summary>
    public int SectionsDone => On ? (int)Math.Floor(Math.Min(Work, End) + 1e-9) : 0;

    /// <summary>The draw after <paramref name="radians"/> more of the axle, W held at the end;
    /// how many sections came off in this step (W crossing 1, 2 or 3); and whether it finished the
    /// ingot.</summary>
    public (DrawJob Job, int Sections, bool Finished) Advance(double radians, double turnsPerSection)
    {
        if (!On || Done)
            return (this, 0, false);
        double w = Math.Min(End, Work + Drawing.SectionsFor(radians, turnsPerSection));
        var next = this with { Work = w };
        return (next, next.SectionsDone - SectionsDone, next.Done);
    }

    /// <summary>A saved draw: W clamped to 0..3; none when the class is not an ingot's.</summary>
    public static DrawJob Restore(int k, double work) =>
        k is 1 or 2 ? new DrawJob(k, double.IsFinite(work) ? Math.Clamp(work, 0, Drawing.SectionsPerIngot) : 0) : None;
}
