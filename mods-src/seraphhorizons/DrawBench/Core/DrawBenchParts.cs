namespace SeraphHorizons.Mod.DrawBench.Core;

/// <summary>The draw bench's build stages after the frame, in the only order they go in (the
/// model's contract, "Build order"). Each is a <c>requires</c> name of the rig
/// (<see cref="DrawBenchRequires"/>).</summary>
public enum DrawBenchStage { Gearbox, Chain, Dog, Mandrel, Die }

/// <summary>Why a held item can or cannot be fitted.</summary>
public enum DrawBenchFitVerdict
{
    Fits,
    NotAPart,
    /// <summary>A part of a later stage: the next missing stage goes in first.</summary>
    OutOfOrder,
    /// <summary>Every stage that takes this item is fitted.</summary>
    AlreadyFitted,
    /// <summary>The die has no durability left.</summary>
    DieSpent,
}

/// <summary>A fitted item as breaking the frame gives it back: its code, and the die's durability
/// left (null for every other part).</summary>
public readonly record struct DrawBenchDrop(string Code, int? Durability = null);

/// <summary>The draw bench's <c>requires</c> vocabulary: the five stages, and the work's
/// (<c>billetlead</c>, <c>billetcopper</c>: a hollow section of that metal on the bench).</summary>
public static class DrawBenchRequires
{
    public const string BilletLead = "billetlead";
    public const string BilletCopper = "billetcopper";

    public static readonly IReadOnlyList<DrawBenchStage> Stages = Enum.GetValues<DrawBenchStage>();

    public static string Name(DrawBenchStage stage) => stage switch
    {
        DrawBenchStage.Gearbox => "gearbox",
        DrawBenchStage.Chain => "chain",
        DrawBenchStage.Dog => "dog",
        DrawBenchStage.Mandrel => "mandrel",
        _ => "die",
    };

    /// <summary>The billet <c>requires</c> of class <paramref name="k"/>: 1 lead, 2 copper.</summary>
    public static string? Billet(int k) => k switch { 1 => BilletLead, 2 => BilletCopper, _ => null };

    /// <summary>Every <c>requires</c> value the rig may use.</summary>
    public static readonly IReadOnlySet<string> KnownRequires =
        Stages.Select(Name).Concat([BilletLead, BilletCopper]).ToHashSet();

    public static bool TryParse(string? name, out DrawBenchStage stage)
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
/// The draw bench's assembly rules: five stages after the frame, fitted one item at a time in
/// <see cref="DrawBenchStage"/> order (the next missing stage is the only one a click fills),
/// recognised by full code (<c>domain:path</c>). The die carries its durability and its metal,
/// which decides what the bench draws. Every fitted code is kept, so breaking returns exactly what
/// went in, the die with what is left of it.
/// </summary>
public sealed class DrawBenchParts
{
    public const string GearboxCode = "game:jonasframes-gearbox01";
    public const string DieIronCode = "seraphhorizons:drawdie-iron";
    public const string DieSteelCode = "seraphhorizons:drawdie-steel";

    /// <summary>The iron, meteoric iron and steel forms of a game part, iron first.</summary>
    private static IReadOnlyList<string> Ferrous(string prefix) =>
        [$"game:{prefix}-iron", $"game:{prefix}-meteoriciron", $"game:{prefix}-steel"];

    public static readonly IReadOnlyList<string> ChainCodes = Ferrous("metalchain");
    public static readonly IReadOnlyList<string> DogCodes = Ferrous("bracket-heavy");
    public static readonly IReadOnlyList<string> MandrelCodes = Ferrous("rod");

    /// <summary>The codes a stage takes; the creative shortcut takes the first (the steel die,
    /// which draws both metals).</summary>
    public static IReadOnlyList<string> CodesFor(DrawBenchStage stage) => stage switch
    {
        DrawBenchStage.Gearbox => [GearboxCode],
        DrawBenchStage.Chain => ChainCodes,
        DrawBenchStage.Dog => DogCodes,
        DrawBenchStage.Mandrel => MandrelCodes,
        _ => [DieSteelCode, DieIronCode],
    };

    private readonly Dictionary<DrawBenchStage, string> _fitted = [];

    /// <summary>The die's durability left; 0 without a die.</summary>
    public int DieLeft { get; private set; }

    /// <summary>The die's full durability; 0 without a die.</summary>
    public int DieCapacity { get; private set; }

    /// <summary>A code with no domain is the game's, as the game reads one.</summary>
    public static string? Normalise(string? code) =>
        string.IsNullOrEmpty(code) ? null : code.Contains(':') ? code : "game:" + code;

    /// <summary>The stages that take <paramref name="code"/>, in order; empty when it is not a part.</summary>
    public static IReadOnlyList<DrawBenchStage> StagesOf(string? code)
    {
        code = Normalise(code);
        return code == null ? [] : DrawBenchRequires.Stages.Where(s => CodesFor(s).Contains(code)).ToList();
    }

    public static bool IsPart(string? code) => StagesOf(code).Count > 0;

    /// <summary>A die's metal: <c>iron</c> or <c>steel</c>; null for anything else.</summary>
    public static string? DieMetalOf(string? code) => Normalise(code) switch
    {
        DieIronCode => "iron",
        DieSteelCode => "steel",
        _ => null,
    };

    public static string? DieCodeFor(string? metal) => metal switch { "iron" => DieIronCode, "steel" => DieSteelCode, _ => null };

    public bool Has(DrawBenchStage stage) => _fitted.ContainsKey(stage);

    public string? FittedIn(DrawBenchStage stage) => _fitted.GetValueOrDefault(stage);

    /// <summary>The first stage not fitted, in order; null when complete.</summary>
    public DrawBenchStage? Next => DrawBenchRequires.Stages.Cast<DrawBenchStage?>().FirstOrDefault(s => !Has(s!.Value));

    /// <summary>Every stage is in, the die with durability left.</summary>
    public bool Complete => Next == null;

    /// <summary>The fitted die's metal, or null without a die.</summary>
    public string? DieMetal => DieMetalOf(FittedIn(DrawBenchStage.Die));

    /// <summary>Whether a rig part needing <paramref name="requires"/> is drawn: null always; a
    /// stage when it is fitted. The billets are the work's, not a part's: never here.</summary>
    public bool Fitted(string? requires) =>
        requires == null || DrawBenchRequires.TryParse(requires, out var stage) && Has(stage);

    /// <summary>Whether <paramref name="code"/> can be fitted now, and the stage it would go in.</summary>
    public DrawBenchFitVerdict CanFit(string? code, out DrawBenchStage stage, int dieDurability = 1)
    {
        stage = default;
        var stages = StagesOf(code);
        if (stages.Count == 0)
            return DrawBenchFitVerdict.NotAPart;
        var open = stages.Where(s => !Has(s)).ToList();
        if (open.Count == 0)
            return DrawBenchFitVerdict.AlreadyFitted;
        if (Next is not { } next || !open.Contains(next))
            return DrawBenchFitVerdict.OutOfOrder;
        if (next == DrawBenchStage.Die && dieDurability <= 0)
            return DrawBenchFitVerdict.DieSpent;
        stage = next;
        return DrawBenchFitVerdict.Fits;
    }

    /// <summary>Fits <paramref name="code"/> in the next stage if the rules allow; a die with
    /// <paramref name="dieLeft"/> of <paramref name="dieCapacity"/> durability.</summary>
    public DrawBenchFitVerdict Fit(string? code, int dieLeft = 0, int dieCapacity = 0)
    {
        var verdict = CanFit(code, out var stage, dieLeft);
        if (verdict != DrawBenchFitVerdict.Fits)
            return verdict;
        _fitted[stage] = Normalise(code)!;
        if (stage == DrawBenchStage.Die)
        {
            DieCapacity = Math.Max(1, dieCapacity);
            DieLeft = Math.Clamp(dieLeft, 1, DieCapacity);
        }
        return verdict;
    }

    /// <summary>The creative shortcut's next item: the next stage's first code; null when complete.</summary>
    public string? NextPart => Next is { } next ? CodesFor(next)[0] : null;

    /// <summary>Wears the die by <paramref name="points"/>. At 0 it is spent: removed, nothing
    /// comes back. Returns whether this wear spent it.</summary>
    public bool WearDie(int points)
    {
        if (!Has(DrawBenchStage.Die) || points <= 0)
            return false;
        DieLeft -= points;
        if (DieLeft > 0)
            return false;
        ClearDie();
        return true;
    }

    private void ClearDie()
    {
        _fitted.Remove(DrawBenchStage.Die);
        DieLeft = DieCapacity = 0;
    }

    /// <summary>Whether Ctrl + right-click takes the die back now: one is fitted and no hollow is on
    /// the bench (<paramref name="jobOn"/>). Nothing else comes back but by breaking the frame.</summary>
    public bool CanTakeDie(bool jobOn) => Has(DrawBenchStage.Die) && !jobOn;

    /// <summary>Takes the die out: its code, durability left and full; null without one.</summary>
    public (string Code, int Left, int Capacity)? RemoveDie()
    {
        if (FittedIn(DrawBenchStage.Die) is not { } code)
            return null;
        var die = (code, DieLeft, DieCapacity);
        ClearDie();
        return die;
    }

    /// <summary>Every fitted item, in stage order, the die with its durability left.</summary>
    public IReadOnlyList<DrawBenchDrop> Returns() =>
        DrawBenchRequires.Stages.Where(Has)
            .Select(s => new DrawBenchDrop(_fitted[s], s == DrawBenchStage.Die ? DieLeft : null))
            .ToList();

    /// <summary>Each stage's fitted code by its stage name, for saving.</summary>
    public IReadOnlyDictionary<string, string> Snapshot() =>
        _fitted.ToDictionary(kv => DrawBenchRequires.Name(kv.Key), kv => kv.Value);

    /// <summary>Restored state, as <see cref="Snapshot"/> gave it. The stages must be a run from
    /// the first (what follows a gap is dropped, as it could not have been fitted); a code that is
    /// not its stage's part is dropped, as is a die with no durability left.</summary>
    public static DrawBenchParts Restore(IReadOnlyDictionary<string, string> fitted, int dieLeft, int dieCapacity)
    {
        var parts = new DrawBenchParts();
        foreach (var stage in DrawBenchRequires.Stages)
        {
            if (!fitted.TryGetValue(DrawBenchRequires.Name(stage), out var code) || !CodesFor(stage).Contains(Normalise(code)!))
                break;
            if (stage == DrawBenchStage.Die)
            {
                if (dieLeft <= 0)
                    break;
                parts.DieCapacity = Math.Max(1, dieCapacity);
                parts.DieLeft = Math.Min(dieLeft, parts.DieCapacity);
            }
            parts._fitted[stage] = Normalise(code)!;
        }
        return parts;
    }
}
