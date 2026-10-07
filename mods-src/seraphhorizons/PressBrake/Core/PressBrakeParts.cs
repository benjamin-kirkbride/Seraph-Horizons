namespace SeraphHorizons.Mod.PressBrake.Core;

/// <summary>The press brake's build stages after the frame, in the only order they go in (the
/// model's contract, "Build order"). Each is a <c>requires</c> name of the rig
/// (<see cref="PressBrakeRequires"/>).</summary>
public enum PressBrakeStage { Screws, Edge }

/// <summary>Why a held item can or cannot be fitted.</summary>
public enum PressBrakeFitVerdict
{
    Fits,
    NotAPart,
    /// <summary>A part of a later stage: the next missing stage goes in first.</summary>
    OutOfOrder,
    /// <summary>Every stage that takes this item is fitted.</summary>
    AlreadyFitted,
}

/// <summary>The press brake's <c>requires</c> vocabulary: the two stages, and the work's
/// (<c>platelead</c>, <c>platecopper</c>: a plate of that metal on the bed).</summary>
public static class PressBrakeRequires
{
    public const string PlateLead = "platelead";
    public const string PlateCopper = "platecopper";

    public static readonly IReadOnlyList<PressBrakeStage> Stages = Enum.GetValues<PressBrakeStage>();

    public static string Name(PressBrakeStage stage) => stage switch
    {
        PressBrakeStage.Screws => "screws",
        _ => "edge",
    };

    /// <summary>The plate <c>requires</c> of class <paramref name="k"/>: 1 lead, 2 copper.</summary>
    public static string? Plate(int k) => k switch { 1 => PlateLead, 2 => PlateCopper, _ => null };

    /// <summary>Every <c>requires</c> value the rig may use.</summary>
    public static readonly IReadOnlySet<string> KnownRequires =
        Stages.Select(Name).Concat([PlateLead, PlateCopper]).ToHashSet();

    public static bool TryParse(string? name, out PressBrakeStage stage)
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
}

/// <summary>
/// The press brake's assembly rules: two stages after the frame, fitted one item at a time in
/// <see cref="PressBrakeStage"/> order (the next missing stage is the only one a click fills),
/// recognised by full code (<c>domain:path</c>): the clamp screws (a rod) and the wearing edges (a
/// plate). Every fitted code is kept, so taking back and breaking return exactly what went in. Ctrl
/// + right-click takes the last stage fitted back out while no plate is on the bed.
/// </summary>
public sealed class PressBrakeParts
{
    /// <summary>The clamp screws: a turned rod of iron, meteoric iron or steel, iron first.</summary>
    public static readonly IReadOnlyList<string> ScrewCodes = ["game:rod-iron", "game:rod-meteoriciron", "game:rod-steel"];

    /// <summary>The wearing edges: plate strips of iron or steel, iron first.</summary>
    public static readonly IReadOnlyList<string> EdgeCodes = ["game:metalplate-iron", "game:metalplate-steel"];

    /// <summary>The codes a stage takes; the creative shortcut takes the first.</summary>
    public static IReadOnlyList<string> CodesFor(PressBrakeStage stage) => stage switch
    {
        PressBrakeStage.Screws => ScrewCodes,
        _ => EdgeCodes,
    };

    private readonly Dictionary<PressBrakeStage, string> _fitted = [];

    /// <summary>A code with no domain is the game's, as the game reads one.</summary>
    public static string? Normalise(string? code) =>
        string.IsNullOrEmpty(code) ? null : code.Contains(':') ? code : "game:" + code;

    /// <summary>The stages that take <paramref name="code"/>, in order; empty when it is not a part.</summary>
    public static IReadOnlyList<PressBrakeStage> StagesOf(string? code)
    {
        code = Normalise(code);
        return code == null ? [] : PressBrakeRequires.Stages.Where(s => CodesFor(s).Contains(code)).ToList();
    }

    public static bool IsPart(string? code) => StagesOf(code).Count > 0;

    /// <summary>The metal of a part's code (<c>game:rod-steel</c> is <c>steel</c>); null for anything else.</summary>
    public static string? MetalOf(string? code) =>
        Normalise(code) is { } c && IsPart(c) ? c[(c.LastIndexOf('-') + 1)..] : null;

    public bool Has(PressBrakeStage stage) => _fitted.ContainsKey(stage);

    public string? FittedIn(PressBrakeStage stage) => _fitted.GetValueOrDefault(stage);

    /// <summary>The first stage not fitted, in order; null when complete.</summary>
    public PressBrakeStage? Next => PressBrakeRequires.Stages.Cast<PressBrakeStage?>().FirstOrDefault(s => !Has(s!.Value));

    /// <summary>The last stage fitted; null with none.</summary>
    public PressBrakeStage? Last => PressBrakeRequires.Stages.Cast<PressBrakeStage?>().LastOrDefault(s => Has(s!.Value));

    /// <summary>Every stage is in.</summary>
    public bool Complete => Next == null;

    /// <summary>Whether a rig part needing <paramref name="requires"/> is drawn: null always; a
    /// stage when it is fitted. The plates are the work's, not a part's: never here.</summary>
    public bool Fitted(string? requires) =>
        requires == null || PressBrakeRequires.TryParse(requires, out var stage) && Has(stage);

    /// <summary>Whether <paramref name="code"/> can be fitted now, and the stage it would go in.</summary>
    public PressBrakeFitVerdict CanFit(string? code, out PressBrakeStage stage)
    {
        stage = default;
        var stages = StagesOf(code);
        if (stages.Count == 0)
            return PressBrakeFitVerdict.NotAPart;
        var open = stages.Where(s => !Has(s)).ToList();
        if (open.Count == 0)
            return PressBrakeFitVerdict.AlreadyFitted;
        if (Next is not { } next || !open.Contains(next))
            return PressBrakeFitVerdict.OutOfOrder;
        stage = next;
        return PressBrakeFitVerdict.Fits;
    }

    /// <summary>Fits <paramref name="code"/> in the next stage if the rules allow.</summary>
    public PressBrakeFitVerdict Fit(string? code)
    {
        var verdict = CanFit(code, out var stage);
        if (verdict == PressBrakeFitVerdict.Fits)
            _fitted[stage] = Normalise(code)!;
        return verdict;
    }

    /// <summary>The creative shortcut's next item: the next stage's first code; null when complete.</summary>
    public string? NextPart => Next is { } next ? CodesFor(next)[0] : null;

    /// <summary>Whether Ctrl + right-click takes a part back now: one is fitted and no plate is on
    /// the bed (<paramref name="plateOn"/>).</summary>
    public bool CanTakeBack(bool plateOn) => Last != null && !plateOn;

    /// <summary>Takes the last stage fitted back out: its code; null with none.</summary>
    public string? RemoveLast()
    {
        if (Last is not { } last)
            return null;
        var code = _fitted[last];
        _fitted.Remove(last);
        return code;
    }

    /// <summary>Every fitted item, in stage order.</summary>
    public IReadOnlyList<string> Returns() => PressBrakeRequires.Stages.Where(Has).Select(s => _fitted[s]).ToList();

    /// <summary>Each stage's fitted code by its stage name, for saving.</summary>
    public IReadOnlyDictionary<string, string> Snapshot() =>
        _fitted.ToDictionary(kv => PressBrakeRequires.Name(kv.Key), kv => kv.Value);

    /// <summary>Restored state, as <see cref="Snapshot"/> gave it. The stages must be a run from
    /// the first (what follows a gap is dropped, as it could not have been fitted); a code that is
    /// not its stage's part is dropped.</summary>
    public static PressBrakeParts Restore(IReadOnlyDictionary<string, string> fitted)
    {
        var parts = new PressBrakeParts();
        foreach (var stage in PressBrakeRequires.Stages)
        {
            if (!fitted.TryGetValue(PressBrakeRequires.Name(stage), out var code) || !CodesFor(stage).Contains(Normalise(code)!))
                break;
            parts._fitted[stage] = Normalise(code)!;
        }
        return parts;
    }
}
