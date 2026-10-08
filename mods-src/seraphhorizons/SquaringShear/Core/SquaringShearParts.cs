namespace SeraphHorizons.Mod.SquaringShear.Core;

/// <summary>The squaring shear's build stages after the frame, in the only order they go in (the
/// model's contract, "Build order"). Each is a <c>requires</c> name of the rig
/// (<see cref="SquaringShearRequires"/>).</summary>
public enum SquaringShearStage { Blade, Gauge }

/// <summary>Why a held item can or cannot be fitted.</summary>
public enum SquaringShearFitVerdict
{
    Fits,
    NotAPart,
    /// <summary>A part of a later stage: the next missing stage goes in first.</summary>
    OutOfOrder,
    /// <summary>Every stage that takes this item is fitted.</summary>
    AlreadyFitted,
}

/// <summary>The squaring shear's <c>requires</c> vocabulary: the two stages, and the work's
/// (<c>platelead</c>, <c>platecopper</c>: a plate of that metal on the table).</summary>
public static class SquaringShearRequires
{
    public const string PlateLead = "platelead";
    public const string PlateCopper = "platecopper";

    public static readonly IReadOnlyList<SquaringShearStage> Stages = Enum.GetValues<SquaringShearStage>();

    public static string Name(SquaringShearStage stage) => stage switch
    {
        SquaringShearStage.Blade => "blade",
        _ => "gauge",
    };

    /// <summary>The plate <c>requires</c> of class <paramref name="k"/>: 1 lead, 2 copper.</summary>
    public static string? Plate(int k) => k switch { 1 => PlateLead, 2 => PlateCopper, _ => null };

    /// <summary>Every <c>requires</c> value the rig may use.</summary>
    public static readonly IReadOnlySet<string> KnownRequires =
        Stages.Select(Name).Concat([PlateLead, PlateCopper]).ToHashSet();

    public static bool TryParse(string? name, out SquaringShearStage stage)
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
/// The squaring shear's assembly rules: two stages after the frame, fitted one item at a time in
/// <see cref="SquaringShearStage"/> order (the next missing stage is the only one a click fills),
/// recognised by full code (<c>domain:path</c>): the blades (a plate) and the back gauge with the
/// hold-down (a rod). Every fitted code is kept, so taking back and breaking return exactly what
/// went in. Ctrl + right-click takes the last stage fitted back out while no plate is on the table.
/// </summary>
public sealed class SquaringShearParts
{
    /// <summary>The upper and lower blades: a plate of iron or steel, iron first.</summary>
    public static readonly IReadOnlyList<string> BladeCodes = ["game:metalplate-iron", "game:metalplate-steel"];

    /// <summary>The back gauge and the hold-down: a rod of iron, meteoric iron or steel, iron first.</summary>
    public static readonly IReadOnlyList<string> GaugeCodes = ["game:rod-iron", "game:rod-meteoriciron", "game:rod-steel"];

    /// <summary>The codes a stage takes; the creative shortcut takes the first.</summary>
    public static IReadOnlyList<string> CodesFor(SquaringShearStage stage) => stage switch
    {
        SquaringShearStage.Blade => BladeCodes,
        _ => GaugeCodes,
    };

    private readonly Dictionary<SquaringShearStage, string> _fitted = [];

    /// <summary>A code with no domain is the game's, as the game reads one.</summary>
    public static string? Normalise(string? code) =>
        string.IsNullOrEmpty(code) ? null : code.Contains(':') ? code : "game:" + code;

    /// <summary>The stages that take <paramref name="code"/>, in order; empty when it is not a part.</summary>
    public static IReadOnlyList<SquaringShearStage> StagesOf(string? code)
    {
        code = Normalise(code);
        return code == null ? [] : SquaringShearRequires.Stages.Where(s => CodesFor(s).Contains(code)).ToList();
    }

    public static bool IsPart(string? code) => StagesOf(code).Count > 0;

    /// <summary>The metal of a part's code (<c>game:rod-steel</c> is <c>steel</c>); null for anything else.</summary>
    public static string? MetalOf(string? code) =>
        Normalise(code) is { } c && IsPart(c) ? c[(c.LastIndexOf('-') + 1)..] : null;

    public bool Has(SquaringShearStage stage) => _fitted.ContainsKey(stage);

    public string? FittedIn(SquaringShearStage stage) => _fitted.GetValueOrDefault(stage);

    /// <summary>The first stage not fitted, in order; null when complete.</summary>
    public SquaringShearStage? Next => SquaringShearRequires.Stages.Cast<SquaringShearStage?>().FirstOrDefault(s => !Has(s!.Value));

    /// <summary>The last stage fitted; null with none.</summary>
    public SquaringShearStage? Last => SquaringShearRequires.Stages.Cast<SquaringShearStage?>().LastOrDefault(s => Has(s!.Value));

    /// <summary>Every stage is in.</summary>
    public bool Complete => Next == null;

    /// <summary>Whether a rig part needing <paramref name="requires"/> is drawn: null always; a
    /// stage when it is fitted. The plates are the work's, not a part's: never here.</summary>
    public bool Fitted(string? requires) =>
        requires == null || SquaringShearRequires.TryParse(requires, out var stage) && Has(stage);

    /// <summary>Whether <paramref name="code"/> can be fitted now, and the stage it would go in.</summary>
    public SquaringShearFitVerdict CanFit(string? code, out SquaringShearStage stage)
    {
        stage = default;
        var stages = StagesOf(code);
        if (stages.Count == 0)
            return SquaringShearFitVerdict.NotAPart;
        var open = stages.Where(s => !Has(s)).ToList();
        if (open.Count == 0)
            return SquaringShearFitVerdict.AlreadyFitted;
        if (Next is not { } next || !open.Contains(next))
            return SquaringShearFitVerdict.OutOfOrder;
        stage = next;
        return SquaringShearFitVerdict.Fits;
    }

    /// <summary>Fits <paramref name="code"/> in the next stage if the rules allow.</summary>
    public SquaringShearFitVerdict Fit(string? code)
    {
        var verdict = CanFit(code, out var stage);
        if (verdict == SquaringShearFitVerdict.Fits)
            _fitted[stage] = Normalise(code)!;
        return verdict;
    }

    /// <summary>The creative shortcut's next item: the next stage's first code; null when complete.</summary>
    public string? NextPart => Next is { } next ? CodesFor(next)[0] : null;

    /// <summary>Whether Ctrl + right-click takes a part back now: one is fitted and no plate is on
    /// the table (<paramref name="plateOn"/>).</summary>
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
    public IReadOnlyList<string> Returns() => SquaringShearRequires.Stages.Where(Has).Select(s => _fitted[s]).ToList();

    /// <summary>Each stage's fitted code by its stage name, for saving.</summary>
    public IReadOnlyDictionary<string, string> Snapshot() =>
        _fitted.ToDictionary(kv => SquaringShearRequires.Name(kv.Key), kv => kv.Value);

    /// <summary>Restored state, as <see cref="Snapshot"/> gave it. The stages must be a run from
    /// the first (what follows a gap is dropped, as it could not have been fitted); a code that is
    /// not its stage's part is dropped.</summary>
    public static SquaringShearParts Restore(IReadOnlyDictionary<string, string> fitted)
    {
        var parts = new SquaringShearParts();
        foreach (var stage in SquaringShearRequires.Stages)
        {
            if (!fitted.TryGetValue(SquaringShearRequires.Name(stage), out var code) || !CodesFor(stage).Contains(Normalise(code)!))
                break;
            parts._fitted[stage] = Normalise(code)!;
        }
        return parts;
    }
}
