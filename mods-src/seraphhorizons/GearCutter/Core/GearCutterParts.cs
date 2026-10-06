namespace SeraphHorizons.Mod.GearCutter.Core;

/// <summary>The gear cutter's build stages after the frame, in the only order they go in (#480's
/// decided build order). Each is a <c>requires</c> name of the rig (<see cref="GearCutterRequires"/>);
/// <see cref="Master"/> is <c>master</c> or <c>masterlarge</c> by the temporal gear fitted.</summary>
public enum GearCutterStage { Spindle, FeedScrew, CamFeed, CamIndex, LiftCam, Index, Oiler, Head, Cutter, Master }

/// <summary>Why a held item can or cannot be fitted.</summary>
public enum GearCutterFitVerdict
{
    Fits,
    NotAPart,
    /// <summary>A part of a later stage: the next missing stage goes in first.</summary>
    OutOfOrder,
    /// <summary>Every stage that takes this item is fitted.</summary>
    AlreadyFitted,
    /// <summary>The cutter kit has no durability left.</summary>
    KitSpent,
}

/// <summary>What Ctrl + right-click takes back, in order: the cutter kit, then the blank on the
/// arbor, then the master; the other stages only come back by breaking the frame.</summary>
public enum GearCutterTakeBack { Nothing, Kit, Blank, Master }

/// <summary>A fitted item as breaking the frame gives it back: its code, and the cutter kit's
/// durability left (null for every other part).</summary>
public readonly record struct GearCutterDrop(string Code, int? Durability = null);

/// <summary>The gear cutter's <c>requires</c> vocabulary.</summary>
public static class GearCutterRequires
{
    public const string MasterSmall = "master";
    public const string MasterLarge = "masterlarge";
    public const string BlankSmall = "blanksmall";
    public const string BlankLarge = "blanklarge";
    public const string Cover = "cover";

    public static readonly IReadOnlyList<GearCutterStage> Stages = Enum.GetValues<GearCutterStage>();

    /// <summary>The <c>requires</c> name of a stage (the small master's for <see cref="GearCutterStage.Master"/>).</summary>
    public static string Name(GearCutterStage stage) => stage switch
    {
        GearCutterStage.Spindle => "spindle",
        GearCutterStage.FeedScrew => "feedscrew",
        GearCutterStage.CamFeed => "camfeed",
        GearCutterStage.CamIndex => "camindex",
        GearCutterStage.LiftCam => "liftcam",
        GearCutterStage.Index => "index",
        GearCutterStage.Oiler => "oiler",
        GearCutterStage.Head => "head",
        GearCutterStage.Cutter => "cutter",
        _ => MasterSmall,
    };

    /// <summary>Every <c>requires</c> value the rig may use.</summary>
    public static readonly IReadOnlySet<string> KnownRequires =
        Stages.Select(Name).Concat([MasterLarge, BlankSmall, BlankLarge, Cover]).ToHashSet();

    public static bool TryParse(string? name, out GearCutterStage stage)
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
/// The gear cutter's assembly rules (#480): ten stages after the frame, fitted one item at a time
/// in <see cref="GearCutterStage"/> order (the next missing stage is the only one a click fills),
/// recognised by full code (<c>domain:path</c>). The cutter kit carries its durability; the master
/// is either temporal gear, and which one decides the gear size. Every fitted code is kept, so
/// breaking returns exactly what went in, the kit with what is left of it.
/// </summary>
public sealed class GearCutterParts
{
    public const string SpindleCode = "seraphhorizons:gearcutterspindle";
    public const string FeedScrewCode = "seraphhorizons:gearcutterfeedscrew";
    public const string GearboxCode = "game:jonasframes-gearbox02";
    public const string LiftCamCode = "seraphhorizons:gearcutterliftcam";
    public const string IndexCode = "seraphhorizons:gearcutterindex";
    public const string ValveCode = "game:jonasparts-valve01";
    public const string HeadCode = "game:jonasframes-gears02";
    public const string HeadAltCode = "game:jonasframes-gears01";
    public const string KitCode = "seraphhorizons:gearcutterkit-steel";
    public const string MasterCode = "game:gear-temporal";
    public const string LargeMasterCode = "game:largegear-temporal";

    /// <summary>The codes a stage takes, the creative shortcut's first.</summary>
    public static IReadOnlyList<string> CodesFor(GearCutterStage stage) => stage switch
    {
        GearCutterStage.Spindle => [SpindleCode],
        GearCutterStage.FeedScrew => [FeedScrewCode],
        GearCutterStage.CamFeed or GearCutterStage.CamIndex => [GearboxCode],
        GearCutterStage.LiftCam => [LiftCamCode],
        GearCutterStage.Index => [IndexCode],
        GearCutterStage.Oiler => [ValveCode],
        GearCutterStage.Head => [HeadCode, HeadAltCode],
        GearCutterStage.Cutter => [KitCode],
        _ => [MasterCode, LargeMasterCode],
    };

    private readonly Dictionary<GearCutterStage, string> _fitted = [];

    /// <summary>The cutter kit's durability left; 0 without a kit.</summary>
    public int KitLeft { get; private set; }

    /// <summary>The cutter kit's full durability; 0 without a kit.</summary>
    public int KitCapacity { get; private set; }

    /// <summary>A code with no domain is the game's, as the game reads one.</summary>
    public static string? Normalise(string? code) =>
        string.IsNullOrEmpty(code) ? null : code.Contains(':') ? code : "game:" + code;

    /// <summary>The stages that take <paramref name="code"/>, in order; empty when it is not a part.</summary>
    public static IReadOnlyList<GearCutterStage> StagesOf(string? code)
    {
        code = Normalise(code);
        return code == null ? [] : GearCutterRequires.Stages.Where(s => CodesFor(s).Contains(code)).ToList();
    }

    public static bool IsPart(string? code) => StagesOf(code).Count > 0;

    /// <summary>The class a master cuts: 1 the temporal gear (12 teeth), 2 the large one (20); 0 else.</summary>
    public static int MasterClass(string? code) => Normalise(code) switch
    {
        MasterCode => 1,
        LargeMasterCode => 2,
        _ => 0,
    };

    public bool Has(GearCutterStage stage) => _fitted.ContainsKey(stage);

    public string? FittedIn(GearCutterStage stage) => _fitted.GetValueOrDefault(stage);

    /// <summary>The first stage not fitted, in order; null when complete.</summary>
    public GearCutterStage? Next => GearCutterRequires.Stages.Cast<GearCutterStage?>().FirstOrDefault(s => !Has(s!.Value));

    /// <summary>Every stage is in, the cutter kit with durability left and a master.</summary>
    public bool Complete => Next == null;

    /// <summary>The master's class: 1 small, 2 large, 0 none.</summary>
    public int Master => MasterClass(FittedIn(GearCutterStage.Master));

    /// <summary>Whether a rig part needing <paramref name="requires"/> is drawn: null and the cover
    /// always; a stage when it is fitted; each master only while it is the one fitted. The blanks
    /// are the work's, not a part's: never here.</summary>
    public bool Fitted(string? requires) => requires switch
    {
        null or GearCutterRequires.Cover => true,
        GearCutterRequires.MasterSmall => Master == 1,
        GearCutterRequires.MasterLarge => Master == 2,
        _ => GearCutterRequires.TryParse(requires, out var stage) && Has(stage),
    };

    /// <summary>Whether <paramref name="code"/> can be fitted now, and the stage it would go in.</summary>
    public GearCutterFitVerdict CanFit(string? code, out GearCutterStage stage, int kitDurability = 1)
    {
        stage = default;
        var stages = StagesOf(code);
        if (stages.Count == 0)
            return GearCutterFitVerdict.NotAPart;
        var open = stages.Where(s => !Has(s)).ToList();
        if (open.Count == 0)
            return GearCutterFitVerdict.AlreadyFitted;
        if (Next is not { } next || !open.Contains(next))
            return GearCutterFitVerdict.OutOfOrder;
        if (next == GearCutterStage.Cutter && kitDurability <= 0)
            return GearCutterFitVerdict.KitSpent;
        stage = next;
        return GearCutterFitVerdict.Fits;
    }

    /// <summary>Fits <paramref name="code"/> in the next stage if the rules allow; a cutter kit with
    /// <paramref name="kitLeft"/> of <paramref name="kitCapacity"/> durability.</summary>
    public GearCutterFitVerdict Fit(string? code, int kitLeft = 0, int kitCapacity = 0)
    {
        var verdict = CanFit(code, out var stage, kitLeft);
        if (verdict != GearCutterFitVerdict.Fits)
            return verdict;
        _fitted[stage] = Normalise(code)!;
        if (stage == GearCutterStage.Cutter)
        {
            KitCapacity = Math.Max(1, kitCapacity);
            KitLeft = Math.Clamp(kitLeft, 1, KitCapacity);
        }
        return verdict;
    }

    /// <summary>The creative shortcut's next item: the next stage's first code; null when complete.</summary>
    public string? NextPart => Next is { } next ? CodesFor(next)[0] : null;

    /// <summary>Wears the cutter kit by <paramref name="points"/>. At 0 it is spent: removed, nothing
    /// comes back. Returns whether this wear spent it.</summary>
    public bool WearKit(int points)
    {
        if (!Has(GearCutterStage.Cutter) || points <= 0)
            return false;
        KitLeft -= points;
        if (KitLeft > 0)
            return false;
        ClearKit();
        return true;
    }

    private void ClearKit()
    {
        _fitted.Remove(GearCutterStage.Cutter);
        KitLeft = KitCapacity = 0;
    }

    /// <summary>What Ctrl + right-click takes now: the kit, else the blank on the arbor
    /// (<paramref name="blankOn"/>), else the master.</summary>
    public GearCutterTakeBack TakeBack(bool blankOn) =>
        Has(GearCutterStage.Cutter) ? GearCutterTakeBack.Kit
        : blankOn ? GearCutterTakeBack.Blank
        : Has(GearCutterStage.Master) ? GearCutterTakeBack.Master
        : GearCutterTakeBack.Nothing;

    /// <summary>Takes the kit out: its durability left and full; null without one.</summary>
    public (int Left, int Capacity)? RemoveKit()
    {
        if (!Has(GearCutterStage.Cutter))
            return null;
        var kit = (KitLeft, KitCapacity);
        ClearKit();
        return kit;
    }

    /// <summary>Takes the master out (only once the kit is out): its code, or null.</summary>
    public string? RemoveMaster()
    {
        if (Has(GearCutterStage.Cutter) || FittedIn(GearCutterStage.Master) is not { } code)
            return null;
        _fitted.Remove(GearCutterStage.Master);
        return code;
    }

    /// <summary>Every fitted item, in stage order, the kit with its durability left.</summary>
    public IReadOnlyList<GearCutterDrop> Returns() =>
        GearCutterRequires.Stages.Where(Has)
            .Select(s => new GearCutterDrop(_fitted[s], s == GearCutterStage.Cutter ? KitLeft : null))
            .ToList();

    /// <summary>Each stage's fitted code by its stage name, for saving (the master under <c>master</c>).</summary>
    public IReadOnlyDictionary<string, string> Snapshot() =>
        _fitted.ToDictionary(kv => GearCutterRequires.Name(kv.Key), kv => kv.Value);

    /// <summary>Restored state, as <see cref="Snapshot"/> gave it. The stages up to the head must be
    /// a run from the first (what follows a gap is dropped, as it could not have been fitted); the
    /// kit and the master each need the head; a code that is not its stage's part is dropped, as is a
    /// kit with no durability left.</summary>
    public static GearCutterParts Restore(IReadOnlyDictionary<string, string> fitted, int kitLeft, int kitCapacity)
    {
        var parts = new GearCutterParts();
        foreach (var stage in GearCutterRequires.Stages)
        {
            if (!fitted.TryGetValue(GearCutterRequires.Name(stage), out var code) || !CodesFor(stage).Contains(Normalise(code)!))
            {
                if (stage < GearCutterStage.Cutter)
                    break;
                continue;
            }
            if (stage == GearCutterStage.Cutter)
            {
                if (kitLeft <= 0)
                    continue;
                parts.KitCapacity = Math.Max(1, kitCapacity);
                parts.KitLeft = Math.Min(kitLeft, parts.KitCapacity);
            }
            parts._fitted[stage] = Normalise(code)!;
        }
        return parts;
    }
}
