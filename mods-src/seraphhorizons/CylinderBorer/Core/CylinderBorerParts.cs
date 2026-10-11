namespace SeraphHorizons.Mod.CylinderBorer.Core;

/// <summary>The cylinder borer's build stages after the frame, in the only order they go in (#639:
/// frame, then bushings, bar, feed and cutter head). Each is a <c>requires</c> name of the rig
/// (<see cref="CylinderBorerRequires"/>).</summary>
public enum CylinderBorerStage { Bushings, Bar, Feed, CutterHead }

/// <summary>Why a held item can or cannot be fitted.</summary>
public enum CylinderBorerFitVerdict
{
    Fits,
    NotAPart,
    /// <summary>A part of a later stage: the next missing stage goes in first.</summary>
    OutOfOrder,
    /// <summary>Every stage that takes this item is fitted.</summary>
    AlreadyFitted,
    /// <summary>The cutter head has no durability left.</summary>
    HeadSpent,
}

/// <summary>What Ctrl + right-click takes back, in order: the cutter head, then the casting in the
/// cradle; the other stages only come back by breaking the frame.</summary>
public enum CylinderBorerTakeBack { Nothing, Head, Blank }

/// <summary>A fitted item as breaking the frame gives it back: its code, and the cutter head's
/// durability left (null for every other part).</summary>
public readonly record struct CylinderBorerDrop(string Code, int? Durability = null);

/// <summary>The cylinder borer's <c>requires</c> vocabulary: the four stages, and the work's
/// (<c>blankiron</c>, <c>blanksteel</c>: a casting of that metal in the cradle).</summary>
public static class CylinderBorerRequires
{
    public const string BlankIron = "blankiron";
    public const string BlankSteel = "blanksteel";

    public static readonly IReadOnlyList<CylinderBorerStage> Stages = Enum.GetValues<CylinderBorerStage>();

    public static string Name(CylinderBorerStage stage) => stage switch
    {
        CylinderBorerStage.Bushings => "bushings",
        CylinderBorerStage.Bar => "bar",
        CylinderBorerStage.Feed => "feed",
        _ => "cutterhead",
    };

    /// <summary>The blank <c>requires</c> of class <paramref name="k"/>: 1 iron, 2 steel.</summary>
    public static string? Blank(int k) => k switch { 1 => BlankIron, 2 => BlankSteel, _ => null };

    /// <summary>Every <c>requires</c> value the rig may use.</summary>
    public static readonly IReadOnlySet<string> KnownRequires =
        Stages.Select(Name).Concat([BlankIron, BlankSteel]).ToHashSet();

    public static bool TryParse(string? name, out CylinderBorerStage stage)
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
/// The cylinder borer's assembly rules: four stages after the frame, fitted one item at a time in
/// <see cref="CylinderBorerStage"/> order (the next missing stage is the only one a click fills),
/// recognised by full code (<c>domain:path</c>): the bushings are the game's metal parts, the rest the
/// borer's own items. The cutter head, the wearing part, carries its durability (cylinders bored). Every fitted code is kept, so breaking returns exactly what went
/// in, the head with what is left of it.
/// </summary>
public sealed class CylinderBorerParts
{
    /// <summary>The bar's two bushings: one lot of the game's metal parts (a block, salvaged or bought
    /// from the mechanic), as the press brake's screws. One lot is both bushings: one item a stage, as
    /// every stage is. They are drawn in cupronickel.</summary>
    public const string BushingsCode = "game:metal-parts";

    /// <summary>The bushings' own item before they were metal parts (#639), made from one lot of them.
    /// No longer registered: a borer saved with it restores with metal parts in its place
    /// (<see cref="Restore"/>), so breaking it gives back what the item was made from.</summary>
    public const string LegacyBushingsCode = "seraphhorizons:cylinderborerbushings";
    public const string BarCode = "seraphhorizons:cylinderborerbar";
    public const string FeedCode = "seraphhorizons:cylinderborerfeed";
    public const string HeadCode = "seraphhorizons:cylinderborerhead-steel";

    /// <summary>The codes a stage takes, the creative shortcut's first.</summary>
    public static IReadOnlyList<string> CodesFor(CylinderBorerStage stage) => stage switch
    {
        CylinderBorerStage.Bushings => [BushingsCode],
        CylinderBorerStage.Bar => [BarCode],
        CylinderBorerStage.Feed => [FeedCode],
        _ => [HeadCode],
    };

    private readonly Dictionary<CylinderBorerStage, string> _fitted = [];

    /// <summary>The cutter head's durability left; 0 without a head.</summary>
    public int HeadLeft { get; private set; }

    /// <summary>The cutter head's full durability; 0 without a head.</summary>
    public int HeadCapacity { get; private set; }

    /// <summary>A code with no domain is the game's, as the game reads one.</summary>
    public static string? Normalise(string? code) =>
        string.IsNullOrEmpty(code) ? null : code.Contains(':') ? code : "game:" + code;

    /// <summary>The stages that take <paramref name="code"/>, in order; empty when it is not a part.</summary>
    public static IReadOnlyList<CylinderBorerStage> StagesOf(string? code)
    {
        code = Normalise(code);
        return code == null ? [] : CylinderBorerRequires.Stages.Where(s => CodesFor(s).Contains(code)).ToList();
    }

    public static bool IsPart(string? code) => StagesOf(code).Count > 0;

    public bool Has(CylinderBorerStage stage) => _fitted.ContainsKey(stage);

    public string? FittedIn(CylinderBorerStage stage) => _fitted.GetValueOrDefault(stage);

    /// <summary>The first stage not fitted, in order; null when complete.</summary>
    public CylinderBorerStage? Next => CylinderBorerRequires.Stages.Cast<CylinderBorerStage?>().FirstOrDefault(s => !Has(s!.Value));

    /// <summary>Every stage is in, the cutter head with durability left.</summary>
    public bool Complete => Next == null;

    /// <summary>Whether a rig part needing <paramref name="requires"/> is drawn: null always; a stage
    /// when it is fitted. The blanks are the work's, not a part's: never here.</summary>
    public bool Fitted(string? requires) =>
        requires == null || CylinderBorerRequires.TryParse(requires, out var stage) && Has(stage);

    /// <summary>Whether <paramref name="code"/> can be fitted now, and the stage it would go in.</summary>
    public CylinderBorerFitVerdict CanFit(string? code, out CylinderBorerStage stage, int headDurability = 1)
    {
        stage = default;
        var stages = StagesOf(code);
        if (stages.Count == 0)
            return CylinderBorerFitVerdict.NotAPart;
        var open = stages.Where(s => !Has(s)).ToList();
        if (open.Count == 0)
            return CylinderBorerFitVerdict.AlreadyFitted;
        if (Next is not { } next || !open.Contains(next))
            return CylinderBorerFitVerdict.OutOfOrder;
        if (next == CylinderBorerStage.CutterHead && headDurability <= 0)
            return CylinderBorerFitVerdict.HeadSpent;
        stage = next;
        return CylinderBorerFitVerdict.Fits;
    }

    /// <summary>Fits <paramref name="code"/> in the next stage if the rules allow; a cutter head
    /// with <paramref name="headLeft"/> of <paramref name="headCapacity"/> durability.</summary>
    public CylinderBorerFitVerdict Fit(string? code, int headLeft = 0, int headCapacity = 0)
    {
        var verdict = CanFit(code, out var stage, headLeft);
        if (verdict != CylinderBorerFitVerdict.Fits)
            return verdict;
        _fitted[stage] = Normalise(code)!;
        if (stage == CylinderBorerStage.CutterHead)
        {
            HeadCapacity = Math.Max(1, headCapacity);
            HeadLeft = Math.Clamp(headLeft, 1, HeadCapacity);
        }
        return verdict;
    }

    /// <summary>The creative shortcut's next item: the next stage's first code; null when complete.</summary>
    public string? NextPart => Next is { } next ? CodesFor(next)[0] : null;

    /// <summary>Wears the cutter head by <paramref name="points"/>. At 0 it is spent: removed,
    /// nothing comes back. Returns whether this wear spent it.</summary>
    public bool WearHead(int points)
    {
        if (!Has(CylinderBorerStage.CutterHead) || points <= 0)
            return false;
        HeadLeft -= points;
        if (HeadLeft > 0)
            return false;
        ClearHead();
        return true;
    }

    private void ClearHead()
    {
        _fitted.Remove(CylinderBorerStage.CutterHead);
        HeadLeft = HeadCapacity = 0;
    }

    /// <summary>What Ctrl + right-click takes now: the cutter head, else the casting in the cradle
    /// (<paramref name="blankOn"/>).</summary>
    public CylinderBorerTakeBack TakeBack(bool blankOn) =>
        Has(CylinderBorerStage.CutterHead) ? CylinderBorerTakeBack.Head
        : blankOn ? CylinderBorerTakeBack.Blank
        : CylinderBorerTakeBack.Nothing;

    /// <summary>Takes the cutter head out, unworn by it: its durability left and full; null without one.</summary>
    public (int Left, int Capacity)? RemoveHead()
    {
        if (!Has(CylinderBorerStage.CutterHead))
            return null;
        var head = (HeadLeft, HeadCapacity);
        ClearHead();
        return head;
    }

    /// <summary>Every fitted item, in stage order, the head with its durability left.</summary>
    public IReadOnlyList<CylinderBorerDrop> Returns() =>
        CylinderBorerRequires.Stages.Where(Has)
            .Select(s => new CylinderBorerDrop(_fitted[s], s == CylinderBorerStage.CutterHead ? HeadLeft : null))
            .ToList();

    /// <summary>Each stage's fitted code by its stage name, for saving.</summary>
    public IReadOnlyDictionary<string, string> Snapshot() =>
        _fitted.ToDictionary(kv => CylinderBorerRequires.Name(kv.Key), kv => kv.Value);

    /// <summary>Restored state, as <see cref="Snapshot"/> gave it. The stages must be a run from the
    /// first (what follows a gap is dropped, as it could not have been fitted); a code that is not
    /// its stage's part is dropped, as is a head with no durability left. The bushings' old item
    /// (<see cref="LegacyBushingsCode"/>) is restored as the metal parts it was made from.</summary>
    public static CylinderBorerParts Restore(IReadOnlyDictionary<string, string> fitted, int headLeft, int headCapacity)
    {
        var parts = new CylinderBorerParts();
        foreach (var stage in CylinderBorerRequires.Stages)
        {
            if (!fitted.TryGetValue(CylinderBorerRequires.Name(stage), out var code))
                break;
            if (stage == CylinderBorerStage.Bushings && Normalise(code) == LegacyBushingsCode)
                code = BushingsCode;
            if (!CodesFor(stage).Contains(Normalise(code)!))
                break;
            if (stage == CylinderBorerStage.CutterHead)
            {
                if (headLeft <= 0)
                    break;
                parts.HeadCapacity = Math.Max(1, headCapacity);
                parts.HeadLeft = Math.Min(headLeft, parts.HeadCapacity);
            }
            parts._fitted[stage] = Normalise(code)!;
        }
        return parts;
    }
}
