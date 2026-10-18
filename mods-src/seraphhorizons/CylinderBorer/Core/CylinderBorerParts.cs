using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.CylinderBorer.Core;

/// <summary>The cylinder borer's build stages after the frame, in the only order they go in (#639:
/// frame, then bushings, bar, feed and cutter head). Each is a <c>requires</c> name of the rig
/// (<see cref="CylinderBorerRequires"/>).</summary>
public enum CylinderBorerStage { Bushings, Bar, Feed, CutterHead }

/// <summary>Why a held stack can or cannot go in.</summary>
public enum CylinderBorerFitVerdict
{
    Fits,
    NotAPart,
    /// <summary>A material of a later stage: the next stage goes in first.</summary>
    OutOfOrder,
    /// <summary>Every stage that takes this item has all of it.</summary>
    AlreadyFitted,
    /// <summary>The cutter head has no durability left.</summary>
    HeadSpent,
    /// <summary>Fewer are held than the stage still needs of it, and they are not a full stack.</summary>
    TooFew,
}

/// <summary>What a click with a stack would fit: the verdict, the stage it goes in, and how many of
/// the stack it takes.</summary>
public readonly record struct CylinderBorerFit(CylinderBorerFitVerdict Verdict, CylinderBorerStage Stage, int Count);

/// <summary>What Ctrl + right-click takes back, in order: the cutter head, then the casting in the
/// cradle; the other stages only come back by breaking the frame.</summary>
public enum CylinderBorerTakeBack { Nothing, Head, Blank }

/// <summary>Items put in as breaking the frame gives them back: their code, how many, and the cutter
/// head's durability left (null for everything else).</summary>
public readonly record struct CylinderBorerDrop(string Code, int Count = 1, int? Durability = null);

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
/// The cylinder borer's assembly rules: four stages after the frame, fitted by right-click with their
/// materials in <see cref="CylinderBorerStage"/> order only (a stage takes nothing before the one ahead
/// of it is complete), each material recognised by full code (<c>domain:path</c>). The bushings are
/// one lot of the game's metal parts; the bar and the feed take their materials directly, as the ore
/// machines' stages do, a material a click (all the stage still needs of it from the held stack, or a
/// whole full stack): <see cref="StagedBuild"/> holds the counts and the rules. A stage is drawn only
/// once it is complete, and a stage partly in keeps what it holds. The cutter head, the wearing part
/// and the one forged item, carries its durability (cylinders bored). Every item put in is kept by its
/// own code (the rods and the plate may be of mixed metals), so breaking returns exactly what went in,
/// the head with what is left of it.
/// </summary>
public sealed class CylinderBorerParts
{
    /// <summary>The bar's two bushings: one lot of the game's metal parts (a block, salvaged or bought
    /// from the mechanic), as the press brake's screws. They are drawn in cupronickel.</summary>
    public const string BushingsCode = "game:metal-parts";

    /// <summary>The bushings' own item before they were metal parts (#639), made from one lot of them.
    /// No longer registered: a borer saved with it restores with metal parts in its place
    /// (<see cref="RestoreLegacy"/>), so breaking it gives back what the item was made from.</summary>
    public const string LegacyBushingsCode = "seraphhorizons:cylinderborerbushings";

    /// <summary>The bar's and the feed's own items, made on the grid, before their stages took those
    /// materials directly. No longer registered: a borer saved with one fitted restores with that stage
    /// complete in what it was made from, its rods and plate iron (<see cref="RestoreLegacy"/>).</summary>
    public const string LegacyBarCode = "seraphhorizons:cylinderborerbar";
    public const string LegacyFeedCode = "seraphhorizons:cylinderborerfeed";

    public const string HeadCode = "seraphhorizons:cylinderborerhead-steel";

    /// <summary>The bar's and the feed's metal, as the frame's: iron (first, the creative shortcut's),
    /// meteoric iron or steel.</summary>
    private static string[] OfMetal(string family) => [$"game:{family}-iron", $"game:{family}-meteoriciron", $"game:{family}-steel"];

    private static readonly IReadOnlyList<BuildMaterial> Bushings = [new([BushingsCode], 1)];

    /// <summary>The boring bar: three rods end to end, metal parts for the feather the cutter head
    /// slides on and the collars, and a large stainless gear for the great wheel keyed on its drive end.</summary>
    private static readonly IReadOnlyList<BuildMaterial> Bar =
        [new(OfMetal("rod"), 3), new([BushingsCode], 1), new(["seraphhorizons:largegear-stainless"], 1)];

    /// <summary>The feed: the crosshead of two oak planks, a rod for its control rod, the drum's two ropes
    /// and a plate for the weight.</summary>
    private static readonly IReadOnlyList<BuildMaterial> Feed =
        [new(["game:plank-oak"], 2), new(OfMetal("rod"), 1), new(["game:rope"], 2), new(OfMetal("metalplate"), 1)];

    private static readonly IReadOnlyList<BuildMaterial> Head = [new([HeadCode], 1)];

    /// <summary>What a stage takes: each material, the creative shortcut's code first.</summary>
    public static IReadOnlyList<BuildMaterial> MaterialsFor(CylinderBorerStage stage) => stage switch
    {
        CylinderBorerStage.Bushings => Bushings,
        CylinderBorerStage.Bar => Bar,
        CylinderBorerStage.Feed => Feed,
        _ => Head,
    };

    /// <summary>The stages as the shared staged build's, each adding its own <c>requires</c>.</summary>
    private static readonly IReadOnlyList<MachineStage> BuildStages = CylinderBorerRequires.Stages
        .Select(s => new MachineStage(CylinderBorerRequires.Name(s), [CylinderBorerRequires.Name(s)], MaterialsFor(s)))
        .ToList();

    /// <summary>The cutter head's keys in a <see cref="Snapshot"/>.</summary>
    private static readonly string HeadKeyPrefix = $"{(int)CylinderBorerStage.CutterHead}/";

    private StagedBuild _build = new(BuildStages);

    /// <summary>The cutter head's durability left; 0 without a head.</summary>
    public int HeadLeft { get; private set; }

    /// <summary>The cutter head's full durability; 0 without a head.</summary>
    public int HeadCapacity { get; private set; }

    /// <summary>A code with no domain is the game's, as the game reads one.</summary>
    public static string? Normalise(string? code) => CodePattern.Normalise(code);

    /// <summary>The stages that take <paramref name="code"/>, in order; empty when it is not a part.</summary>
    public static IReadOnlyList<CylinderBorerStage> StagesOf(string? code)
    {
        code = Normalise(code);
        return code == null ? [] : CylinderBorerRequires.Stages.Where(s => MaterialsFor(s).Any(m => m.Matches(code))).ToList();
    }

    public static bool IsPart(string? code) => StagesOf(code).Count > 0;

    /// <summary>Whether <paramref name="stage"/> has everything it takes.</summary>
    public bool Has(CylinderBorerStage stage) => _build.StageComplete((int)stage);

    /// <summary>How many of material <paramref name="material"/> (<see cref="MaterialsFor"/>'s index)
    /// <paramref name="stage"/> holds.</summary>
    public int Count(CylinderBorerStage stage, int material) => _build.Count((int)stage, material);

    /// <summary>The first stage not complete, in order; null when complete.</summary>
    public CylinderBorerStage? Next => _build.Next is { } next ? (CylinderBorerStage)next : null;

    /// <summary>Every stage is in, the cutter head with durability left.</summary>
    public bool Complete => Next == null;

    /// <summary>What the next stage still takes: each material missing and how many; empty when complete.</summary>
    public IReadOnlyList<(BuildMaterial Material, int Missing)> NextNeeds() => _build.NextNeeds();

    /// <summary>Whether a rig part needing <paramref name="requires"/> is drawn: null always; a stage
    /// once it is complete. The blanks are the work's, not a part's: never here.</summary>
    public bool Fitted(string? requires) =>
        requires == null || CylinderBorerRequires.TryParse(requires, out var stage) && Has(stage);

    /// <summary>What a click with <paramref name="held"/> of <paramref name="code"/> (stacking to
    /// <paramref name="maxStack"/>) would fit; a cutter head with <paramref name="headDurability"/> left.</summary>
    public CylinderBorerFit CanFit(string? code, int held = 1, int maxStack = int.MaxValue, int headDurability = 1)
    {
        var fit = _build.CanFit(code, held, maxStack);
        var stage = fit.Stage >= 0 ? (CylinderBorerStage)fit.Stage : default;
        var verdict = fit.Verdict switch
        {
            StageFitVerdict.Fits when stage == CylinderBorerStage.CutterHead && headDurability <= 0 => CylinderBorerFitVerdict.HeadSpent,
            StageFitVerdict.Fits => CylinderBorerFitVerdict.Fits,
            StageFitVerdict.OutOfOrder => CylinderBorerFitVerdict.OutOfOrder,
            StageFitVerdict.AlreadyFitted => CylinderBorerFitVerdict.AlreadyFitted,
            StageFitVerdict.TooFew => CylinderBorerFitVerdict.TooFew,
            _ => CylinderBorerFitVerdict.NotAPart,
        };
        return new CylinderBorerFit(verdict, stage, verdict == CylinderBorerFitVerdict.Fits ? fit.Count : 0);
    }

    /// <summary>Fits what <see cref="CanFit"/> allows (the caller takes <see cref="CylinderBorerFit.Count"/>
    /// from the slot when it fits); a cutter head with <paramref name="headLeft"/> of
    /// <paramref name="headCapacity"/> durability.</summary>
    public CylinderBorerFit Fit(string? code, int held = 1, int maxStack = int.MaxValue, int headLeft = 0, int headCapacity = 0)
    {
        var fit = CanFit(code, held, maxStack, headLeft);
        if (fit.Verdict != CylinderBorerFitVerdict.Fits)
            return fit;
        _build.Fit(code, held, maxStack);
        if (fit.Stage == CylinderBorerStage.CutterHead)
            SetHead(headLeft, headCapacity);
        return fit;
    }

    /// <summary>The creative shortcut: fills the next stage with what it still takes, each material's
    /// first code (a new cutter head of <paramref name="headCapacity"/> durability last). Returns
    /// whether a stage was filled.</summary>
    public bool FillNext(int headCapacity = 1)
    {
        if (Next is not { } next || !_build.FillNext())
            return false;
        if (next == CylinderBorerStage.CutterHead)
            SetHead(headCapacity, headCapacity);
        return true;
    }

    private void SetHead(int left, int capacity)
    {
        HeadCapacity = Math.Max(1, capacity);
        HeadLeft = Math.Clamp(left, 1, HeadCapacity);
    }

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
        _build = StagedBuild.Restore(BuildStages, WithoutHead(_build.Snapshot()));
        HeadLeft = HeadCapacity = 0;
    }

    private static Dictionary<string, int> WithoutHead(IReadOnlyDictionary<string, int> snapshot) =>
        snapshot.Where(kv => !kv.Key.StartsWith(HeadKeyPrefix, StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

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

    /// <summary>Every item put in, in stage order, by code and count, the head with its durability left.</summary>
    public IReadOnlyList<CylinderBorerDrop> Returns() =>
        _build.Returns()
            .Select(d => new CylinderBorerDrop(d.Code, d.Count, d.Code == HeadCode && Has(CylinderBorerStage.CutterHead) ? HeadLeft : null))
            .ToList();

    /// <summary>Each fitted code's count, keyed <c>stage/material/code</c> (<see cref="StagedBuild.Key"/>,
    /// the stage its <see cref="CylinderBorerStage"/> index), for saving.</summary>
    public IReadOnlyDictionary<string, int> Snapshot() => _build.Snapshot();

    /// <summary>Restored state, as <see cref="Snapshot"/> gave it (<see cref="StagedBuild.Restore(IReadOnlyList{MachineStage}, IReadOnlyDictionary{string, int})"/>:
    /// a code that is not its material's is dropped, counts are clamped, and a stage after one not
    /// complete keeps nothing, as it could not have been fitted); a head with no durability left is dropped.</summary>
    public static CylinderBorerParts Restore(IReadOnlyDictionary<string, int> fitted, int headLeft, int headCapacity)
    {
        var parts = new CylinderBorerParts { _build = StagedBuild.Restore(BuildStages, headLeft > 0 ? fitted : WithoutHead(fitted)) };
        if (parts.Has(CylinderBorerStage.CutterHead))
        {
            parts.HeadCapacity = Math.Max(1, headCapacity);
            parts.HeadLeft = Math.Min(headLeft, parts.HeadCapacity);
        }
        return parts;
    }

    /// <summary>The items each stage took one of before the bar and the feed took their materials:
    /// the stage's code then (the bushings' metal parts or their old item, the bar's and the feed's
    /// items, the cutter head).</summary>
    private static bool WasLegacyItem(CylinderBorerStage stage, string code) => stage switch
    {
        CylinderBorerStage.Bushings => code is BushingsCode or LegacyBushingsCode,
        CylinderBorerStage.Bar => code == LegacyBarCode,
        CylinderBorerStage.Feed => code == LegacyFeedCode,
        _ => code == HeadCode,
    };

    /// <summary>Restored from a save made when each stage held one item, its code by stage name. The
    /// stages must be a run from the first (what follows a gap is dropped, as it could not have been
    /// fitted); a code that was not its stage's item ends the run. Each stage in is restored complete,
    /// in its materials' first codes: the bushings' old item as metal parts, the bar and the feed as
    /// what they were made from on the grid, their rods and plate iron, so breaking the borer gives
    /// those back.</summary>
    public static CylinderBorerParts RestoreLegacy(IReadOnlyDictionary<string, string> fitted, int headLeft, int headCapacity)
    {
        var snapshot = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var stage in CylinderBorerRequires.Stages)
        {
            if (!fitted.TryGetValue(CylinderBorerRequires.Name(stage), out var code) || !WasLegacyItem(stage, Normalise(code)!))
                break;
            var materials = MaterialsFor(stage);
            for (int m = 0; m < materials.Count; m++)
                snapshot[StagedBuild.Key((int)stage, m, Normalise(materials[m].Codes[0])!)] = materials[m].Quantity;
        }
        return Restore(snapshot, headLeft, headCapacity);
    }
}
