namespace SeraphHorizons.Mod.EidolonGantry.Core;

/// <summary>The gantry's winch stages after the frame, in the only order they go in (README "The
/// build"). Each is a <c>requires</c> name of the rig (<see cref="GantryRequires"/>); the spine is
/// the last, and the eidolon's body stages follow it (<see cref="GantryRequires.BodyStages"/>,
/// fitted by the body's own rules, not these).</summary>
public enum GantryStage { Axles, CrankShaft, Gears, Drum, Strapping, Ratchet, Crank, Chain, Spine }

/// <summary>Why a held item can or cannot be fitted to the winch.</summary>
public enum GantryFitVerdict
{
    Fits,
    NotAPart,
    /// <summary>A part of a later stage: the next missing stage goes in first.</summary>
    OutOfOrder,
    /// <summary>Every stage that takes this item is fitted.</summary>
    AlreadyFitted,
    /// <summary>Fewer are held than the stage takes (<see cref="GantryParts.Needed"/>).</summary>
    TooFew,
}

/// <summary>A fitted item as breaking the gantry gives it back: its code and how many of it the stage took.</summary>
public readonly record struct GantryDrop(string Code, int Count);

/// <summary>The gantry rig's <c>requires</c> vocabulary: the nine winch stages, then the six body
/// stages (the eidolon's, <c>config/eidolon-stages.json</c>), which ride the spine.</summary>
public static class GantryRequires
{
    public static readonly IReadOnlyList<GantryStage> Stages = Enum.GetValues<GantryStage>();

    /// <summary>The eidolon's body stages in build order, each a rig <c>requires</c>: drawn only when
    /// a body-stage extension of the gantry says so (<c>IEidolonGantryExtension.Shows</c>).</summary>
    public static readonly IReadOnlyList<string> BodyStages = ["torso", "pelvis", "legs", "arms", "head", "mind"];

    public static string Name(GantryStage stage) => stage switch
    {
        GantryStage.Axles => "axles",
        GantryStage.CrankShaft => "crankshaft",
        GantryStage.Gears => "gears",
        GantryStage.Drum => "drum",
        GantryStage.Strapping => "strapping",
        GantryStage.Ratchet => "ratchet",
        GantryStage.Crank => "crank",
        GantryStage.Chain => "chain",
        _ => "spine",
    };

    /// <summary>Every <c>requires</c> value the rig may use.</summary>
    public static readonly IReadOnlySet<string> KnownRequires = Stages.Select(Name).Concat(BodyStages).ToHashSet();

    public static bool TryParse(string? name, out GantryStage stage)
    {
        foreach (var s in Stages)
            if (Name(s) == name)
            {
                stage = s;
                return true;
            }
        stage = default;
        return false;
    }

    public static bool IsBodyStage(string? name) => name != null && BodyStages.Contains(name);
}

/// <summary>
/// The eidolon gantry's winch rules: nine stages after the frame, fitted in <see cref="GantryStage"/>
/// order (the next missing stage is the only one a click fills), recognised by full code
/// (<c>domain:path</c>). Each stage takes several of one item (<see cref="Needed"/>), all from the
/// held stack in one click. The drum's planks and the spine's support beams are of the gantry's own
/// wood (<see cref="Wood"/>); the iron parts are iron, meteoric iron or steel, as the woodworking
/// machines take them. Every fitted code is kept, so breaking returns exactly what went in.
/// </summary>
public sealed class GantryParts
{
    /// <summary>The woods a gantry comes in: the game's <c>block/wood</c> property's, which its
    /// support beams and planks come in.</summary>
    public static readonly IReadOnlyList<string> Woods =
        ["birch", "oak", "maple", "pine", "acacia", "kapok", "baldcypress", "larch", "redwood", "ebony", "walnut", "purpleheart"];

    public const string AxleCode = "game:woodenaxle-ud";
    public const string SpurGearCode = "game:spurgear-s";

    /// <summary>The iron, meteoric iron and steel forms of a game part, iron first.</summary>
    private static IReadOnlyList<string> Ferrous(string prefix) =>
        [$"game:{prefix}-iron", $"game:{prefix}-meteoriciron", $"game:{prefix}-steel"];

    public static readonly IReadOnlyList<string> RodCodes = Ferrous("rod");
    public static readonly IReadOnlyList<string> NailCodes = Ferrous("metalnailsandstrips");
    public static readonly IReadOnlyList<string> PlateCodes = Ferrous("metalplate");
    public static readonly IReadOnlyList<string> ChainCodes = Ferrous("metalchain");

    public static string PlankCode(string wood) => "game:plank-" + wood;
    public static string BeamCode(string wood) => "game:supportbeam-" + wood;

    private readonly Dictionary<GantryStage, string> _fitted = [];

    public GantryParts(string wood) => Wood = wood;

    /// <summary>The gantry's wood (its <c>wood</c> variant): the drum's planks and the spine's beams are of it.</summary>
    public string Wood { get; }

    /// <summary>The codes a stage takes on a gantry of <paramref name="wood"/>; the creative
    /// shortcut takes the first.</summary>
    public static IReadOnlyList<string> CodesFor(GantryStage stage, string wood) => stage switch
    {
        GantryStage.Axles => [AxleCode],
        GantryStage.CrankShaft or GantryStage.Crank => RodCodes,
        GantryStage.Gears => [SpurGearCode],
        GantryStage.Drum => [PlankCode(wood)],
        GantryStage.Strapping => NailCodes,
        GantryStage.Ratchet => PlateCodes,
        GantryStage.Chain => ChainCodes,
        _ => [BeamCode(wood)],
    };

    public IReadOnlyList<string> CodesFor(GantryStage stage) => CodesFor(stage, Wood);

    /// <summary>How many of its item a stage takes, all at once (README "The build"): an axle a
    /// block of the two wooden shafts, a spur gear each of the train's four gears, a plank a stave
    /// of the drum and two for the sheave, a nails and strips a band, the game's chain a block of the
    /// working length, a support beam each of the mast's three lengths.</summary>
    public static int Needed(GantryStage stage) => stage switch
    {
        GantryStage.Axles => 8,
        GantryStage.Gears or GantryStage.Chain => 4,
        GantryStage.Drum or GantryStage.Strapping => 10,
        GantryStage.Spine => 3,
        _ => 1,
    };

    /// <summary>A code with no domain is the game's, as the game reads one.</summary>
    public static string? Normalise(string? code) =>
        string.IsNullOrEmpty(code) ? null : code.Contains(':') ? code : "game:" + code;

    /// <summary>The stages that take <paramref name="code"/>, in order; empty when it is not a part
    /// of this gantry (a plank or beam of another wood is not).</summary>
    public IReadOnlyList<GantryStage> StagesOf(string? code)
    {
        code = Normalise(code);
        return code == null ? [] : GantryRequires.Stages.Where(s => CodesFor(s).Contains(code)).ToList();
    }

    public bool IsPart(string? code) => StagesOf(code).Count > 0;

    public bool Has(GantryStage stage) => _fitted.ContainsKey(stage);

    public string? FittedIn(GantryStage stage) => _fitted.GetValueOrDefault(stage);

    /// <summary>The first stage not fitted, in order; null when the winch and spine are complete.</summary>
    public GantryStage? Next => GantryRequires.Stages.Cast<GantryStage?>().FirstOrDefault(s => !Has(s!.Value));

    /// <summary>Every winch stage and the spine are in: the body can be built on the spine.</summary>
    public bool Complete => Next == null;

    /// <summary>Whether a rig part needing <paramref name="requires"/> is drawn by the winch's
    /// state: null always; a winch stage when it is fitted. The body's stages are never the winch's
    /// (an extension draws them).</summary>
    public bool Fitted(string? requires) =>
        requires == null || GantryRequires.TryParse(requires, out var stage) && Has(stage);

    /// <summary>Whether <paramref name="code"/> can be fitted now, <paramref name="held"/> of it in
    /// hand (enough when not given), and the stage it would go in.</summary>
    public GantryFitVerdict CanFit(string? code, out GantryStage stage, int held = int.MaxValue)
    {
        stage = default;
        var stages = StagesOf(code);
        if (stages.Count == 0)
            return GantryFitVerdict.NotAPart;
        var open = stages.Where(s => !Has(s)).ToList();
        if (open.Count == 0)
            return GantryFitVerdict.AlreadyFitted;
        if (Next is not { } next || !open.Contains(next))
            return GantryFitVerdict.OutOfOrder;
        if (held < Needed(next))
            return GantryFitVerdict.TooFew;
        stage = next;
        return GantryFitVerdict.Fits;
    }

    /// <summary>Fits <paramref name="code"/> in the next stage if the rules allow, <paramref name="held"/>
    /// of it in hand (the caller takes <see cref="Needed"/> of them).</summary>
    public GantryFitVerdict Fit(string? code, int held = int.MaxValue)
    {
        var verdict = CanFit(code, out var stage, held);
        if (verdict == GantryFitVerdict.Fits)
            _fitted[stage] = Normalise(code)!;
        return verdict;
    }

    /// <summary>The creative shortcut's next item: the next stage's first code; null when complete.</summary>
    public string? NextPart => Next is { } next ? CodesFor(next)[0] : null;

    /// <summary>Fits every stage still open with its first code (the assembled creative stack).</summary>
    public void FitAll()
    {
        while (NextPart is { } code)
            Fit(code);
    }

    /// <summary>Every fitted item, in stage order, as many as its stage took.</summary>
    public IReadOnlyList<GantryDrop> Returns() =>
        GantryRequires.Stages.Where(Has).Select(s => new GantryDrop(_fitted[s], Needed(s))).ToList();

    /// <summary>Each stage's fitted code by its stage name, for saving.</summary>
    public IReadOnlyDictionary<string, string> Snapshot() =>
        _fitted.ToDictionary(kv => GantryRequires.Name(kv.Key), kv => kv.Value);

    /// <summary>Restored state, as <see cref="Snapshot"/> gave it, on a gantry of <paramref name="wood"/>.
    /// The stages must be a run from the first (what follows a gap is dropped, as it could not have
    /// been fitted); a code that is not its stage's part is dropped too.</summary>
    public static GantryParts Restore(string wood, IReadOnlyDictionary<string, string> fitted)
    {
        var parts = new GantryParts(wood);
        foreach (var stage in GantryRequires.Stages)
        {
            if (!fitted.TryGetValue(GantryRequires.Name(stage), out var code) || !parts.CodesFor(stage).Contains(Normalise(code)!))
                break;
            parts._fitted[stage] = Normalise(code)!;
        }
        return parts;
    }
}
