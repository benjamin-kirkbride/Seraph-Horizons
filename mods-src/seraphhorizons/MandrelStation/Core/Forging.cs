namespace SeraphHorizons.Mod.MandrelStation.Core;

/// <summary>The mandrel station's <c>requires</c> vocabulary (the model's contract, "Build order"):
/// the one fitted part, <c>mandrel</c>, and the work's (<c>hollowlead</c>, <c>hollowcopper</c>: a
/// hollow of that metal on the mandrel).</summary>
public static class MandrelRequires
{
    public const string Mandrel = "mandrel";
    public const string HollowLead = "hollowlead";
    public const string HollowCopper = "hollowcopper";

    /// <summary>The hollow <c>requires</c> of class <paramref name="k"/>: 1 lead, 2 copper.</summary>
    public static string? Hollow(int k) => k switch { 1 => HollowLead, 2 => HollowCopper, _ => null };

    /// <summary>Every <c>requires</c> value the rig may use.</summary>
    public static readonly IReadOnlySet<string> KnownRequires = new HashSet<string> { Mandrel, HollowLead, HollowCopper };
}

/// <summary>Why a held item can or cannot be fitted as the mandrel.</summary>
public enum MandrelFitVerdict
{
    Fits,
    NotAMandrel,
    /// <summary>A mandrel is already in the bracket.</summary>
    AlreadyFitted,
}

/// <summary>Why a hollow can or cannot go on the mandrel.</summary>
public enum HollowLoadVerdict
{
    Loads,
    NotAHollow,
    /// <summary>No mandrel in the bracket.</summary>
    NoMandrel,
    /// <summary>A hollow is already on the mandrel.</summary>
    Occupied,
}

/// <summary>
/// The station's one fitted part, the mandrel: a rod of iron, meteoric iron or steel, recognised by
/// full code (<c>domain:path</c>). Its code is kept, so taking back and breaking return exactly what
/// went in. Ctrl + right-click takes it back while no hollow is on.
/// </summary>
public static class MandrelPart
{
    /// <summary>The mandrel: a rod of iron, meteoric iron or steel, iron first (the creative shortcut's).</summary>
    public static readonly IReadOnlyList<string> Codes = ["game:rod-iron", "game:rod-meteoriciron", "game:rod-steel"];

    /// <summary>A code with no domain is the game's, as the game reads one.</summary>
    public static string? Normalise(string? code) =>
        string.IsNullOrEmpty(code) ? null : code.Contains(':') ? code : "game:" + code;

    public static bool IsMandrel(string? code) => Normalise(code) is { } c && Codes.Contains(c);

    /// <summary>The metal of a mandrel's code (<c>game:rod-steel</c> is <c>steel</c>); null for anything else.</summary>
    public static string? MetalOf(string? code) =>
        Normalise(code) is { } c && IsMandrel(c) ? c[(c.LastIndexOf('-') + 1)..] : null;

    /// <summary>Whether <paramref name="code"/> goes in as the mandrel, <paramref name="fitted"/> the
    /// one in the bracket (null with none).</summary>
    public static MandrelFitVerdict CanFit(string? code, string? fitted) =>
        !IsMandrel(code) ? MandrelFitVerdict.NotAMandrel
        : fitted != null ? MandrelFitVerdict.AlreadyFitted
        : MandrelFitVerdict.Fits;

    /// <summary>Whether Ctrl + right-click takes the mandrel back now: one is fitted and no hollow is on.</summary>
    public static bool CanTakeBack(string? fitted, bool hollowOn) => fitted != null && !hollowOn;

    /// <summary>Whether a rig part needing <paramref name="requires"/> is drawn as a part: null
    /// always, the mandrel once fitted. The hollows are the work's, not a part's: never here.</summary>
    public static bool Fitted(string? requires, string? fitted) =>
        requires == null || requires == MandrelRequires.Mandrel && fitted != null;

    /// <summary>A saved mandrel: its code if it is one, else none.</summary>
    public static string? Restore(string? code) => IsMandrel(code) ? Normalise(code) : null;
}

/// <summary>
/// The forging's arithmetic: a hollow section of class k (1 lead, 2 copper; the game's chute
/// section) goes on the mandrel and is hammered over it, one blow a right-click with a hammer, as on
/// the anvil. W, the forging of one hollow (0..1), advances by 1 / blowsPerHollow[k] a blow; at the
/// last blow (W = 1) the hollow is used up and <see cref="SectionsPerHollow"/> pipe sections of its
/// metal come off the mandrel's tip.
/// </summary>
public static class Forging
{
    public const int SectionsPerHollow = 2;
    public const string LeadHollow = "game:chutesection-lead";
    public const string CopperHollow = "game:chutesection-copper";

    /// <summary>The shortest time between two blows on one station: right-click held does not
    /// hammer faster than a smith swings.</summary>
    public const long BlowIntervalMs = 300;

    public static string? MetalOf(int k) => k switch { 1 => "lead", 2 => "copper", _ => null };

    /// <summary>The class a hollow is forged as: 1 lead, 2 copper, 0 not one the station takes (an
    /// angle, a pipe section or an ingot included: what comes off is never taken back on).</summary>
    public static int ClassOfHollow(string? code) => code switch { LeadHollow => 1, CopperHollow => 2, _ => 0 };

    public static string? HollowFor(int k) => k switch { 1 => LeadHollow, 2 => CopperHollow, _ => null };

    /// <summary>The pipe section a hollow of class <paramref name="k"/> is forged into.</summary>
    public static string? SectionFor(int k) => MetalOf(k) is { } metal ? "seraphhorizons:pipesection-" + metal : null;

    /// <summary>Whether a hammer of item code <paramref name="code"/> strikes: any of the game's hammers.</summary>
    public static bool IsHammer(string? code) =>
        MandrelPart.Normalise(code) is { } c && c.StartsWith("game:hammer-", StringComparison.Ordinal);

    /// <summary>Whether a hollow <paramref name="code"/> goes on: a mandrel fitted
    /// (<paramref name="mandrel"/>) and nothing on it.</summary>
    public static HollowLoadVerdict CanLoad(string? code, bool mandrel, bool occupied)
    {
        if (ClassOfHollow(code) == 0)
            return HollowLoadVerdict.NotAHollow;
        if (!mandrel)
            return HollowLoadVerdict.NoMandrel;
        return occupied ? HollowLoadVerdict.Occupied : HollowLoadVerdict.Loads;
    }

    /// <summary>Whether a blow is struck now: <paramref name="sinceLastMs"/> since the last one.</summary>
    public static bool Ready(long sinceLastMs) => sinceLastMs >= BlowIntervalMs;
}

/// <summary>A hollow on the mandrel: its class, the blows struck on it and W, the forging so far (0..1).</summary>
public readonly record struct ForgeJob(int Class, int Blows, double Work)
{
    public static readonly ForgeJob None = new(0, 0, 0);

    public bool On => Class is 1 or 2;

    public bool Done => On && Work >= 1 - 1e-9;

    /// <summary>Whether nothing has been done to the hollow yet (it can be taken back).</summary>
    public bool Untouched => On && Blows == 0 && Work <= 0;

    /// <summary>The job after one blow, of <paramref name="blowsPerHollow"/> the hollow takes, W held
    /// at 1, and whether this blow finished it.</summary>
    public (ForgeJob Job, bool Finished) Strike(int blowsPerHollow)
    {
        if (!On || Done || blowsPerHollow < 1)
            return (this, false);
        var next = this with { Blows = Blows + 1, Work = Math.Min(1, Work + 1.0 / blowsPerHollow) };
        if (next.Work >= 1 - 1e-9)
            next = next with { Work = 1 };
        return (next, next.Done);
    }

    /// <summary>A saved job: W clamped to 0..1, blows at least 0; none when the class is not a hollow's.</summary>
    public static ForgeJob Restore(int k, int blows, double work) =>
        k is 1 or 2 ? new ForgeJob(k, Math.Max(0, blows), double.IsFinite(work) ? Math.Clamp(work, 0, 1) : 0) : None;
}
