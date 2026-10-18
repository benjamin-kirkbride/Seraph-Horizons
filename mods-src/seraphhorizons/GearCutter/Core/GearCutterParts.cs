using SeraphHorizons.Mod.Machines.Core;

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
    /// <summary>Every stage that takes this item is fitted (or, in a material stage, has all of it).</summary>
    AlreadyFitted,
    /// <summary>The cutter kit has no durability left.</summary>
    KitSpent,
    /// <summary>A material stage's material: fewer are held than it still needs, and not a full stack.</summary>
    TooFew,
}

/// <summary>What a click with a stack would fit: the verdict, the stage it goes in, and how many of
/// the stack it takes (one for a part; for a material stage all it still needs of that material, or
/// a full stack).</summary>
public readonly record struct GearCutterFit(GearCutterFitVerdict Verdict, GearCutterStage Stage = default, int Take = 0);

/// <summary>What Ctrl + right-click takes back, in order: the cutter kit, then the blank on the
/// arbor, then the master; the other stages only come back by breaking the frame.</summary>
public enum GearCutterTakeBack { Nothing, Kit, Blank, Master }

/// <summary>A fitted item as breaking the frame gives it back: its code, the cutter kit's
/// durability left (null for every other part), and how many (a material stage's materials).</summary>
public readonly record struct GearCutterDrop(string Code, int? Durability = null, int Count = 1);

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
/// The gear cutter's assembly rules (#480): ten stages after the frame, fitted in
/// <see cref="GearCutterStage"/> order (the next missing stage is the only one a click fills),
/// recognised by full code (<c>domain:path</c>). Most stages take one item; the spindle and the
/// index are material stages (<see cref="MaterialsFor"/>), fed their materials at the frame as the
/// ore machines' stages are (a material a click: all the stage still needs of it, or a full stack;
/// the shared <see cref="StagedBuild"/>), and drawn only once all are in. The cutter kit carries its
/// durability; the master is either temporal gear, and which one decides the gear size. Every fitted
/// code is kept, so breaking returns exactly what went in, the kit with what is left of it.
/// </summary>
public sealed class GearCutterParts
{
    public const string FeedScrewCode = "seraphhorizons:gearcutterfeedscrew";
    public const string GearboxCode = "game:jonasframes-gearbox02";
    public const string LiftCamCode = "seraphhorizons:gearcutterliftcam";
    public const string ValveCode = "game:jonasparts-valve01";
    public const string HeadCode = "game:jonasframes-gears02";
    public const string HeadAltCode = "game:jonasframes-gears01";
    public const string KitCode = "seraphhorizons:gearcutterkit-steel";
    public const string MasterCode = "game:gear-temporal";
    public const string LargeMasterCode = "game:largegear-temporal";

    /// <summary>The material stages' materials: steel rods, steel plates and one lot of the game's
    /// metal parts (a block), the bill of the grid recipe each stage's own item had.</summary>
    public const string RodCode = "game:rod-steel";
    public const string PlateCode = "game:metalplate-steel";
    public const string MetalPartsCode = "game:metal-parts";

    /// <summary>The spindle's and the index's own items before they were material stages, each made
    /// from the stage's bill. No longer registered: a cutter saved with one restores with that stage
    /// complete (<see cref="Restore"/>), so breaking it gives back what the item was made from.</summary>
    public const string LegacySpindleCode = "seraphhorizons:gearcutterspindle";
    public const string LegacyIndexCode = "seraphhorizons:gearcutterindex";

    private static readonly IReadOnlyList<BuildMaterial> Bill =
        [new([RodCode], 2), new([PlateCode], 2), new([MetalPartsCode], 1)];

    /// <summary>A material stage's materials (the spindle's and the index's: 2 steel rods, 2 steel
    /// plates, 1 metal parts); empty for a stage that takes one item.</summary>
    public static IReadOnlyList<BuildMaterial> MaterialsFor(GearCutterStage stage) =>
        stage is GearCutterStage.Spindle or GearCutterStage.Index ? Bill : [];

    /// <summary>Whether a stage is fed materials rather than one item.</summary>
    public static bool TakesMaterials(GearCutterStage stage) => MaterialsFor(stage).Count > 0;

    /// <summary>A material stage's former item, for restoring an old save; null for the others.</summary>
    public static string? LegacyCode(GearCutterStage stage) => stage switch
    {
        GearCutterStage.Spindle => LegacySpindleCode,
        GearCutterStage.Index => LegacyIndexCode,
        _ => null,
    };

    /// <summary>The codes a stage takes, the creative shortcut's first (a material stage's materials,
    /// in order).</summary>
    public static IReadOnlyList<string> CodesFor(GearCutterStage stage) => stage switch
    {
        GearCutterStage.Spindle or GearCutterStage.Index => Bill.SelectMany(m => m.Codes).ToList(),
        GearCutterStage.FeedScrew => [FeedScrewCode],
        GearCutterStage.CamFeed or GearCutterStage.CamIndex => [GearboxCode],
        GearCutterStage.LiftCam => [LiftCamCode],
        GearCutterStage.Oiler => [ValveCode],
        GearCutterStage.Head => [HeadCode, HeadAltCode],
        GearCutterStage.Cutter => [KitCode],
        _ => [MasterCode, LargeMasterCode],
    };

    /// <summary>A material stage as the shared staged build's one stage.</summary>
    private static IReadOnlyList<MachineStage> BuildStages(GearCutterStage stage) =>
        [new MachineStage(GearCutterRequires.Name(stage), [], MaterialsFor(stage))];

    private readonly Dictionary<GearCutterStage, string> _fitted = [];

    // Each material stage's materials so far: complete, the stage is fitted.
    private readonly Dictionary<GearCutterStage, StagedBuild> _builds =
        GearCutterRequires.Stages.Where(TakesMaterials).ToDictionary(s => s, s => new StagedBuild(BuildStages(s)));

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

    public bool Has(GearCutterStage stage) =>
        _builds.TryGetValue(stage, out var build) ? build.Complete : _fitted.ContainsKey(stage);

    /// <summary>The item fitted in a one-item stage; null for a material stage.</summary>
    public string? FittedIn(GearCutterStage stage) => _fitted.GetValueOrDefault(stage);

    /// <summary>How many of its material <paramref name="material"/> a material stage holds; 0 for
    /// a one-item stage.</summary>
    public int Count(GearCutterStage stage, int material) =>
        _builds.TryGetValue(stage, out var build) ? build.Count(0, material) : 0;

    /// <summary>The first stage not fitted, in order; null when complete.</summary>
    public GearCutterStage? Next => GearCutterRequires.Stages.Cast<GearCutterStage?>().FirstOrDefault(s => !Has(s!.Value));

    /// <summary>Every stage is in, the cutter kit with durability left and a master.</summary>
    public bool Complete => Next == null;

    /// <summary>The master's class: 1 small, 2 large, 0 none.</summary>
    public int Master => MasterClass(FittedIn(GearCutterStage.Master));

    /// <summary>Whether a rig part needing <paramref name="requires"/> is drawn: null and the cover
    /// always; a stage when it is fitted (a material stage once all its materials are in); each
    /// master only while it is the one fitted. The blanks are the work's, not a part's: never here.</summary>
    public bool Fitted(string? requires) => requires switch
    {
        null or GearCutterRequires.Cover => true,
        GearCutterRequires.MasterSmall => Master == 1,
        GearCutterRequires.MasterLarge => Master == 2,
        _ => GearCutterRequires.TryParse(requires, out var stage) && Has(stage),
    };

    /// <summary>Whether <paramref name="held"/> of <paramref name="code"/> (stacking to
    /// <paramref name="maxStack"/>) can be fitted now, the stage it would go in and how many it takes.</summary>
    public GearCutterFit CanFit(string? code, int kitDurability = 1, int held = 1, int maxStack = int.MaxValue)
    {
        var stages = StagesOf(code);
        if (stages.Count == 0 || held <= 0)
            return new GearCutterFit(GearCutterFitVerdict.NotAPart);
        var open = stages.Where(s => !Has(s)).ToList();
        if (open.Count == 0)
            return new GearCutterFit(GearCutterFitVerdict.AlreadyFitted);
        if (Next is not { } next || !open.Contains(next))
            return new GearCutterFit(GearCutterFitVerdict.OutOfOrder);
        if (_builds.TryGetValue(next, out var build))
        {
            var fit = build.CanFit(code, held, maxStack);
            return fit.Verdict switch
            {
                StageFitVerdict.Fits => new GearCutterFit(GearCutterFitVerdict.Fits, next, fit.Count),
                StageFitVerdict.TooFew => new GearCutterFit(GearCutterFitVerdict.TooFew, next),
                _ => new GearCutterFit(GearCutterFitVerdict.AlreadyFitted, next),
            };
        }
        if (next == GearCutterStage.Cutter && kitDurability <= 0)
            return new GearCutterFit(GearCutterFitVerdict.KitSpent, next);
        return new GearCutterFit(GearCutterFitVerdict.Fits, next, 1);
    }

    /// <summary>Fits <paramref name="code"/> in the next stage if the rules allow: a cutter kit with
    /// <paramref name="kitLeft"/> of <paramref name="kitCapacity"/> durability; a material stage's
    /// material from <paramref name="held"/> of it. The caller takes <see cref="GearCutterFit.Take"/>
    /// from the slot when it fits.</summary>
    public GearCutterFit Fit(string? code, int kitLeft = 0, int kitCapacity = 0, int held = 1, int maxStack = int.MaxValue)
    {
        var fit = CanFit(code, kitLeft, held, maxStack);
        if (fit.Verdict != GearCutterFitVerdict.Fits)
            return fit;
        if (_builds.TryGetValue(fit.Stage, out var build))
        {
            build.Fit(code, held, maxStack);
            return fit;
        }
        _fitted[fit.Stage] = Normalise(code)!;
        if (fit.Stage == GearCutterStage.Cutter)
        {
            KitCapacity = Math.Max(1, kitCapacity);
            KitLeft = Math.Clamp(kitLeft, 1, KitCapacity);
        }
        return fit;
    }

    /// <summary>What the next stage still takes when it is a material stage: each material missing and
    /// how many; empty otherwise.</summary>
    public IReadOnlyList<(BuildMaterial Material, int Missing)> NextNeeds() =>
        Next is { } next && _builds.TryGetValue(next, out var build) ? build.NextNeeds() : [];

    /// <summary>The creative shortcut's next item when the next stage takes one: its first code; null
    /// when complete or when the next stage is a material stage (<see cref="FillNextMaterials"/>).</summary>
    public string? NextPart => Next is { } next && !TakesMaterials(next) ? CodesFor(next)[0] : null;

    /// <summary>The creative shortcut for a material stage: puts in all the next stage still needs.
    /// Returns whether the next stage was one.</summary>
    public bool FillNextMaterials() => Next is { } next && _builds.TryGetValue(next, out var build) && build.FillNext();

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

    /// <summary>Every fitted item, in stage order: the kit with its durability left, a material
    /// stage's materials with their counts (a stage part way in too).</summary>
    public IReadOnlyList<GearCutterDrop> Returns()
    {
        var drops = new List<GearCutterDrop>();
        foreach (var s in GearCutterRequires.Stages)
        {
            if (_builds.TryGetValue(s, out var build))
                drops.AddRange(build.Returns().Select(d => new GearCutterDrop(d.Code, null, d.Count)));
            else if (_fitted.TryGetValue(s, out var code))
                drops.Add(new GearCutterDrop(code, s == GearCutterStage.Cutter ? KitLeft : null));
        }
        return drops;
    }

    /// <summary>Each one-item stage's fitted code by its stage name, for saving (the master under
    /// <c>master</c>); the material stages are <see cref="MaterialSnapshot"/>'s.</summary>
    public IReadOnlyDictionary<string, string> Snapshot() =>
        _fitted.ToDictionary(kv => GearCutterRequires.Name(kv.Key), kv => kv.Value);

    /// <summary>The material stages' counts, for saving: <c>{stage}/{StagedBuild key}</c> to count.</summary>
    public IReadOnlyDictionary<string, int> MaterialSnapshot()
    {
        var snapshot = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (stage, build) in _builds)
            foreach (var (key, n) in build.Snapshot())
                snapshot[GearCutterRequires.Name(stage) + "/" + key] = n;
        return snapshot;
    }

    /// <summary>Restored state, as <see cref="Snapshot"/> and <see cref="MaterialSnapshot"/> gave it.
    /// The stages up to the head must be a run from the first (what follows a gap, or a material stage
    /// part way in, is dropped, as it could not have been fitted); the kit and the master each need
    /// the head; a code that is not its stage's part is dropped, as is a kit with no durability left.
    /// A material stage saved with its former item fitted (<see cref="LegacySpindleCode"/>,
    /// <see cref="LegacyIndexCode"/>) is restored complete, with the item's bill.</summary>
    public static GearCutterParts Restore(IReadOnlyDictionary<string, string> fitted, int kitLeft, int kitCapacity,
        IReadOnlyDictionary<string, int>? materials = null)
    {
        var parts = new GearCutterParts();
        foreach (var stage in GearCutterRequires.Stages)
        {
            string name = GearCutterRequires.Name(stage);
            if (TakesMaterials(stage))
            {
                var build = new StagedBuild(BuildStages(stage));
                if (fitted.TryGetValue(name, out var legacy) && Normalise(legacy) == LegacyCode(stage))
                    build.FillNext();
                else
                {
                    var own = new Dictionary<string, int>(StringComparer.Ordinal);
                    foreach (var (key, n) in materials ?? new Dictionary<string, int>())
                        if (key.StartsWith(name + "/", StringComparison.Ordinal))
                            own[key[(name.Length + 1)..]] = n;
                    build = StagedBuild.Restore(BuildStages(stage), own);
                }
                parts._builds[stage] = build;
                if (!build.Complete)
                    break;
                continue;
            }
            if (!fitted.TryGetValue(name, out var code) || !CodesFor(stage).Contains(Normalise(code)!))
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
