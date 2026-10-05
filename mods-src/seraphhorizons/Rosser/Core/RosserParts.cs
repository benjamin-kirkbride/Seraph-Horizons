namespace SeraphHorizons.Mod.Rosser.Core;

/// <summary>The rosser's sub-assemblies, in the order the creative shortcut fits them. Each is a
/// <c>requires</c> name of the rig (<see cref="RosserRequires"/>).</summary>
public enum RosserStage { Shaft, Ring, Tyres, RollsIn, RollsOut, Breaker, Levers, Heads }

/// <summary>Why a held item can or cannot be fitted.</summary>
public enum RosserFitVerdict
{
    Fits,
    NotAPart,
    /// <summary>Every stage that takes this item is full.</summary>
    AlreadyFitted,
    /// <summary>The tyres and the scraper heads go on the ring, which is not complete.</summary>
    NeedsRing,
    /// <summary>A hoop, rod or plate of a metal the rosser does not take.</summary>
    WrongMetal,
    /// <summary>The heads go on as a set of four of one metal from one stack; fewer are held.</summary>
    NeedsFullSet,
}

/// <summary>What a fit did: the verdict, how many items to take from the held stack, and which
/// stage(s) they went into.</summary>
public readonly record struct RosserFit(RosserFitVerdict Verdict, int Taken)
{
    public bool Fitted => Verdict == RosserFitVerdict.Fits && Taken > 0;
}

/// <summary>The rosser's <c>requires</c> vocabulary: which fitted stage draws which rig parts.</summary>
public static class RosserRequires
{
    /// <summary>The <c>requires</c> name of a stage.</summary>
    public static string Name(RosserStage stage) => stage switch
    {
        RosserStage.Shaft => "shaft",
        RosserStage.Ring => "ring",
        RosserStage.Tyres => "tyres",
        RosserStage.RollsIn => "rollsin",
        RosserStage.RollsOut => "rollsout",
        RosserStage.Breaker => "breaker",
        RosserStage.Levers => "levers",
        _ => "heads",
    };

    public static readonly IReadOnlyList<RosserStage> Stages = Enum.GetValues<RosserStage>();

    /// <summary>The <c>requires</c> values the gameplay knows.</summary>
    public static readonly IReadOnlySet<string> KnownRequires = Stages.Select(Name).ToHashSet();

    public static bool TryParse(string? name, out RosserStage stage)
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
/// The rosser's assembly rules (design §5). It is built from existing items only, recognised by
/// full code (<c>domain:path</c>):
/// <list type="table">
/// <item><c>shaft</c>: 1 <c>immersivewoodworking:sawmillcrankshaft</c></item>
/// <item><c>ring</c>: 4 <c>game:largegearsection-wood</c></item>
/// <item><c>tyres</c>: 2 <c>game:hoop-{metal}</c>, after the ring</item>
/// <item><c>rollsin</c>, <c>rollsout</c>: 2 <c>game:rod-{metal}</c> each, the infeed's first</item>
/// <item><c>breaker</c>: 2 <c>game:metalplate-{metal}</c></item>
/// <item><c>levers</c>: 1 <c>immersivewoodworking:sawmilllevers</c></item>
/// <item><c>heads</c>: 4 <c>immersivewoodworking:barkspudhead-{metal}</c> of one metal from one
/// stack, after the ring; the wearing part</item>
/// </list>
/// A click takes from the held stack as many as the stages taking that item still need. Hoops,
/// rods and plates must be of the allowed metals (iron work, <see cref="IronMetals"/>, unless the
/// caller allows any); the heads may be of any metal. Every fitted item's code is kept, so breaking
/// returns exactly what went in, the heads only while unworn.
/// </summary>
public sealed class RosserParts
{
    public const string ShaftCode = "immersivewoodworking:sawmillcrankshaft";
    public const string RingCode = "game:largegearsection-wood";
    public const string LeversCode = "immersivewoodworking:sawmilllevers";
    public const string HoopPrefix = "game:hoop-";
    public const string RodPrefix = "game:rod-";
    public const string PlatePrefix = "game:metalplate-";
    public const string HeadPrefix = "immersivewoodworking:barkspudhead-";

    /// <summary>The metals hoops, rods and plates must be of while the woodworking machines are
    /// iron work (<c>WoodworkingMachineCosts.Metals</c>; keep the two the same).</summary>
    public static readonly IReadOnlySet<string> IronMetals = new HashSet<string> { "iron", "meteoriciron", "steel" };

    /// <summary>Items each stage takes.</summary>
    public static int Needed(RosserStage stage) => stage switch
    {
        RosserStage.Shaft or RosserStage.Levers => 1,
        RosserStage.Ring or RosserStage.Heads => 4,
        _ => 2,
    };

    private readonly Dictionary<RosserStage, List<string>> _fitted = RosserRequires.Stages.ToDictionary(s => s, _ => new List<string>());
    private readonly IReadOnlySet<string>? _metals;

    /// <summary>The heads' remaining capacity, in wear points; 0 without heads.</summary>
    public int HeadsLeft { get; private set; }

    /// <summary>The heads' capacity when fitted; 0 without heads.</summary>
    public int HeadsCapacity { get; private set; }

    /// <param name="allowedMetals">The metals hoops, rods and plates may be of; null takes any.
    /// Defaults to <see cref="IronMetals"/>.</param>
    public RosserParts(IReadOnlySet<string>? allowedMetals = null, bool anyMetal = false)
    {
        _metals = anyMetal ? null : allowedMetals ?? IronMetals;
    }

    /// <summary>Restored state, as <see cref="Snapshot"/> gave it. Codes that are not the stage's
    /// part, or of a metal no longer allowed, are dropped, as are items beyond a stage's need; tyres
    /// and heads without a complete ring, and heads that are not four of one metal, are dropped as
    /// they could not have been fitted. Heads with no capacity left are spent and dropped.</summary>
    public static RosserParts Restore(IReadOnlyDictionary<string, IReadOnlyList<string>> fitted, int headsLeft, int headsCapacity,
                                      IReadOnlySet<string>? allowedMetals = null, bool anyMetal = false)
    {
        var parts = new RosserParts(allowedMetals, anyMetal);
        foreach (var stage in RosserRequires.Stages)
        {
            if (!fitted.TryGetValue(RosserRequires.Name(stage), out var codes))
                continue;
            foreach (var code in codes)
                if (parts._fitted[stage].Count < Needed(stage) && parts.Accepts(stage, code, out _) == RosserFitVerdict.Fits)
                    parts._fitted[stage].Add(code);
        }
        if (!parts.Has(RosserStage.Ring))
        {
            parts._fitted[RosserStage.Tyres].Clear();
            parts._fitted[RosserStage.Heads].Clear();
        }
        var heads = parts._fitted[RosserStage.Heads];
        if (heads.Count != Needed(RosserStage.Heads) || heads.Distinct().Count() != 1 || headsLeft <= 0)
            heads.Clear();
        if (heads.Count > 0)
        {
            parts.HeadsCapacity = Math.Max(1, headsCapacity);
            parts.HeadsLeft = Math.Min(headsLeft, parts.HeadsCapacity);
        }
        return parts;
    }

    /// <summary>The same fitted parts and heads under another metal rule, for what is fitted next:
    /// a save is restored with any metal (what went in passed the rule of its day and is kept), then
    /// given the rule in force now.</summary>
    public RosserParts WithMetals(IReadOnlySet<string>? allowedMetals = null, bool anyMetal = false)
    {
        var parts = new RosserParts(allowedMetals, anyMetal) { HeadsLeft = HeadsLeft, HeadsCapacity = HeadsCapacity };
        foreach (var (stage, codes) in _fitted)
            parts._fitted[stage].AddRange(codes);
        return parts;
    }

    /// <summary>Each stage's fitted codes by its <c>requires</c> name, for saving.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Snapshot() =>
        _fitted.ToDictionary(kv => RosserRequires.Name(kv.Key), kv => (IReadOnlyList<string>)kv.Value.ToArray());

    /// <summary>The codes fitted in a stage.</summary>
    public IReadOnlyList<string> FittedIn(RosserStage stage) => _fitted[stage];

    /// <summary>Whether a stage has all its items.</summary>
    public bool Has(RosserStage stage) => _fitted[stage].Count >= Needed(stage);

    /// <summary>Whether a rig part needing <paramref name="requires"/> is drawn: null always, a stage
    /// name when that stage is complete, anything else never.</summary>
    public bool Fitted(string? requires) =>
        requires == null || RosserRequires.TryParse(requires, out var stage) && Has(stage);

    /// <summary>The fitted heads' metal; null without heads (spent heads are gone).</summary>
    public string? HeadMetal => Has(RosserStage.Heads) ? _fitted[RosserStage.Heads][0][HeadPrefix.Length..] : null;

    /// <summary>Whether the heads are fitted and have never been used (they can come back out).</summary>
    public bool HeadsUnworn => HeadMetal != null && HeadsLeft >= HeadsCapacity;

    /// <summary>Every stage is in (spent heads are removed, so a rosser whose heads are spent is not complete).</summary>
    public bool Complete => RosserRequires.Stages.All(Has);

    /// <summary>The stage an item code is for (the rods' first: the infeed rolls), and the metal of
    /// a hoop, rod, plate or head; null when it is not a part.</summary>
    public static RosserStage? StageOf(string? code, out string? metal)
    {
        metal = null;
        code = Normalise(code);
        switch (code)
        {
            case null: return null;
            case ShaftCode: return RosserStage.Shaft;
            case RingCode: return RosserStage.Ring;
            case LeversCode: return RosserStage.Levers;
        }
        foreach (var (prefix, stage) in new[] { (HoopPrefix, RosserStage.Tyres), (RodPrefix, RosserStage.RollsIn),
                                                (PlatePrefix, RosserStage.Breaker), (HeadPrefix, RosserStage.Heads) })
            if (code.StartsWith(prefix, StringComparison.Ordinal) && code.Length > prefix.Length && !code[prefix.Length..].Contains('-'))
            {
                metal = code[prefix.Length..];
                return stage;
            }
        return null;
    }

    /// <summary>A code with no domain is the game's, as the game reads one.</summary>
    private static string? Normalise(string? code) =>
        string.IsNullOrEmpty(code) ? null : code.Contains(':') ? code : "game:" + code;

    /// <summary>The stages an item goes into, in fitting order (rods: infeed, then outfeed).</summary>
    private static IEnumerable<RosserStage> StagesFor(RosserStage first) =>
        first == RosserStage.RollsIn ? [RosserStage.RollsIn, RosserStage.RollsOut] : [first];

    private RosserFitVerdict Accepts(RosserStage stage, string? code, out string? normalised)
    {
        normalised = Normalise(code);
        var first = StageOf(normalised, out var metal);
        if (first is not { } f || !StagesFor(f).Contains(stage))
            return RosserFitVerdict.NotAPart;
        if (stage is RosserStage.Tyres or RosserStage.RollsIn or RosserStage.RollsOut or RosserStage.Breaker
            && _metals != null && !_metals.Contains(metal!))
            return RosserFitVerdict.WrongMetal;
        return RosserFitVerdict.Fits;
    }

    /// <summary>Whether <paramref name="available"/> items of <paramref name="code"/> (the held
    /// stack) can be fitted, and how many would be taken.</summary>
    public RosserFit CanFit(string? code, int available)
    {
        var first = StageOf(code, out _);
        if (first is not { } f || available <= 0)
            return new(RosserFitVerdict.NotAPart, 0);
        var stages = StagesFor(f).ToList();
        int room = stages.Sum(s => Needed(s) - _fitted[s].Count);
        if (room <= 0)
            return new(RosserFitVerdict.AlreadyFitted, 0);
        var verdict = Accepts(f, code, out _);
        if (verdict != RosserFitVerdict.Fits)
            return new(verdict, 0);
        if (f is RosserStage.Tyres or RosserStage.Heads && !Has(RosserStage.Ring))
            return new(RosserFitVerdict.NeedsRing, 0);
        if (f == RosserStage.Heads && available < Needed(RosserStage.Heads))
            return new(RosserFitVerdict.NeedsFullSet, 0);
        return new(RosserFitVerdict.Fits, Math.Min(available, room));
    }

    /// <summary>Fits what <see cref="CanFit"/> allows; the caller takes <see cref="RosserFit.Taken"/>
    /// from the held stack. Heads start with <paramref name="headCapacity"/> wear points
    /// (<see cref="HeadCapacity"/>).</summary>
    public RosserFit Fit(string? code, int available, int headCapacity = 1)
    {
        var fit = CanFit(code, available);
        if (!fit.Fitted)
            return fit;
        var first = StageOf(code, out _)!.Value;
        string normalised = Normalise(code)!;
        int left = fit.Taken;
        foreach (var stage in StagesFor(first))
            while (left > 0 && _fitted[stage].Count < Needed(stage))
            {
                _fitted[stage].Add(normalised);
                left--;
            }
        if (first == RosserStage.Heads)
            HeadsLeft = HeadsCapacity = Math.Max(1, headCapacity);
        return fit;
    }

    /// <summary>The heads' capacity: <paramref name="multiple"/> × the durability of the bark spud
    /// of their metal, at least 1.</summary>
    public static int HeadCapacity(int spudDurability, float multiple) =>
        Math.Max(1, (int)Math.Round(Math.Max(0, spudDurability) * (double)multiple));

    /// <summary>Wear points a debarked trunk costs: ceil(storedLogs × perStoredLog).</summary>
    public static int WearFor(int storedLogs, float perStoredLog) =>
        Math.Max(0, (int)Math.Ceiling(Math.Max(0, storedLogs) * (double)perStoredLog - 1e-6));

    /// <summary>Wears the heads by <paramref name="points"/>. At 0 or below they are spent: removed,
    /// nothing comes back. Returns whether they were spent by this wear.</summary>
    public bool WearHeads(int points)
    {
        if (HeadMetal == null || points <= 0)
            return false;
        HeadsLeft -= points;
        if (HeadsLeft > 0)
            return false;
        ClearHeads();
        return true;
    }

    /// <summary>Takes unworn heads back out: their code and count, or null when there are none or
    /// they are worn (worn heads stay until spent).</summary>
    public (string Code, int Count)? RemoveHeads()
    {
        if (!HeadsUnworn)
            return null;
        var code = _fitted[RosserStage.Heads][0];
        ClearHeads();
        return (code, Needed(RosserStage.Heads));
    }

    private void ClearHeads()
    {
        _fitted[RosserStage.Heads].Clear();
        HeadsLeft = HeadsCapacity = 0;
    }

    /// <summary>What still has to go in, in the creative shortcut's order: per stage, the code
    /// (metal parts with <c>*</c> for the metal) and how many.</summary>
    public IEnumerable<(RosserStage Stage, string Code, int Count)> Missing()
    {
        foreach (var stage in RosserRequires.Stages)
        {
            int count = Needed(stage) - _fitted[stage].Count;
            if (count > 0)
                yield return (stage, stage switch
                {
                    RosserStage.Shaft => ShaftCode,
                    RosserStage.Ring => RingCode,
                    RosserStage.Levers => LeversCode,
                    RosserStage.Tyres => HoopPrefix + "*",
                    RosserStage.RollsIn or RosserStage.RollsOut => RodPrefix + "*",
                    RosserStage.Breaker => PlatePrefix + "*",
                    _ => HeadPrefix + "*",
                }, count);
        }
    }

    /// <summary>The creative shortcut's next stage: the first missing stage's code with
    /// <paramref name="metal"/> for the metal parts (the heads too) and its count; null when
    /// complete. It always fits: the ring comes before the tyres and the heads.</summary>
    public (string Code, int Count)? NextPart(string metal) =>
        Missing().Select(m => ((string Code, int Count)?)(m.Code.Replace("*", metal, StringComparison.Ordinal), m.Count)).FirstOrDefault();

    /// <summary>What breaking the rosser gives back: every fitted item, by code with its count, in
    /// stage order; the heads only while unworn.</summary>
    public IReadOnlyList<(string Code, int Count)> Returns()
    {
        var result = new List<(string Code, int Count)>();
        foreach (var stage in RosserRequires.Stages)
        {
            if (stage == RosserStage.Heads && !HeadsUnworn)
                continue;
            foreach (var group in _fitted[stage].GroupBy(c => c))
            {
                int i = result.FindIndex(r => r.Code == group.Key);
                if (i >= 0)
                    result[i] = (group.Key, result[i].Count + group.Count());
                else
                    result.Add((group.Key, group.Count()));
            }
        }
        return result;
    }
}
