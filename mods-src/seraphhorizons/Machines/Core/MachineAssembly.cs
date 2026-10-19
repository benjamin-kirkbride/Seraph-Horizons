namespace SeraphHorizons.Mod.Machines.Core;

/// <summary>What a right-click with a stack did to a machine's assembly.</summary>
public enum AssemblyVerdict
{
    /// <summary>Went into the next build stage.</summary>
    Fitted,
    /// <summary>Renewed a worn-out wearing part.</summary>
    Renewed,
    NotAPart,
    OutOfOrder,
    AlreadyFitted,
    TooFew,
    /// <summary>A wearing part with use left.</summary>
    NotWorn,
    /// <summary>The material is all one kind, and another is in (the water line's metal).</summary>
    OtherKind,
    /// <summary>A durable wearing part's item with no durability left (a spent die).</summary>
    Spent,
}

/// <summary>A click's result: the verdict, how many of the held stack to take, and the stage it went
/// in (or -1).</summary>
public readonly record struct AssemblyClick(AssemblyVerdict Verdict, int Take, int Stage, string? Wearing = null)
{
    public bool Took => Verdict is AssemblyVerdict.Fitted or AssemblyVerdict.Renewed;

    /// <summary>The lang code of the error to show for a click that took nothing
    /// (<c>seraphhorizons:machine-error-{verdict}</c>), or null.</summary>
    public string? ErrorLangCode => Took ? null : "seraphhorizons:machine-error-" + Verdict.ToString().ToLowerInvariant();
}

/// <summary>
/// One machine's build and wearing parts together (#711), as its block entity keeps them: a right-click
/// fits the next stage (<see cref="StagedBuild"/>) or renews a worn-out wearing part
/// (<see cref="WearingParts"/>); a wearing part fitted with a stage gets its first set as that stage
/// completes; breaking gives back every stage's items and every wearing set with use left, each once.
/// </summary>
public sealed class MachineAssembly
{
    public MachineAssembly(MachineBuild spec) : this(spec, spec.NewBuild(), spec.NewWearing())
    {
    }

    private MachineAssembly(MachineBuild spec, StagedBuild build, WearingParts wearing)
    {
        Spec = spec;
        Build = build;
        Wearing = wearing;
    }

    public MachineBuild Spec { get; }
    public StagedBuild Build { get; }
    public WearingParts Wearing { get; }

    /// <summary>Every stage is in.</summary>
    public bool Built => Build.Complete;

    /// <summary>Built, with every wearing part's set fitted and not worn out.</summary>
    public bool Ready => Built && Wearing.AllFitted;

    /// <summary>Whether a rig part needing <paramref name="requires"/> is drawn: the build's and the
    /// wearing parts' rules both.</summary>
    public bool Shows(string? requires) => Build.Fitted(requires) && Wearing.Shows(requires);

    /// <summary>What a click with <paramref name="held"/> of <paramref name="code"/> (stacking to
    /// <paramref name="maxStack"/>) would do, without doing it. <paramref name="uses"/> is the held item's
    /// durability left, for a durable wearing part (null for anything else).</summary>
    public AssemblyClick Check(string? code, int held, int maxStack = int.MaxValue, double? uses = null) =>
        Click(code, held, maxStack, uses, apply: false);

    /// <summary>A right-click with the stack: the caller takes <see cref="AssemblyClick.Take"/> from the
    /// slot when <see cref="AssemblyClick.Took"/>.</summary>
    public AssemblyClick Fit(string? code, int held, int maxStack = int.MaxValue, double? uses = null) =>
        Click(code, held, maxStack, uses, apply: true);

    private AssemblyClick Click(string? code, int held, int maxStack, double? uses, bool apply)
    {
        if (Built)
        {
            var verdict = Wearing.CanRenew(code, held, built: true, out var part, uses);
            if (verdict == RenewVerdict.Renews)
            {
                if (apply)
                    Wearing.Renew(code, held, built: true, uses);
                return new AssemblyClick(AssemblyVerdict.Renewed, part!.Count, -1, part.Id);
            }
            if (verdict != RenewVerdict.NotAPart)
                return new AssemblyClick(verdict switch
                {
                    RenewVerdict.TooFew => AssemblyVerdict.TooFew,
                    RenewVerdict.Spent => AssemblyVerdict.Spent,
                    _ => AssemblyVerdict.NotWorn,
                }, 0, -1, part?.Id);
        }
        // a durable wearing part's first set, fitted with its stage, with nothing left in it
        if (uses is <= 0 && Wearing.Parts.FirstOrDefault(p => p.Durable && CodePattern.Normalise(code) is { } c && p.Matches(c)) is { } spent)
            return new AssemblyClick(AssemblyVerdict.Spent, 0, -1, spent.Id);
        var fit = apply ? Build.Fit(code, held, maxStack) : Build.CanFit(code, held, maxStack);
        if (fit.Verdict != StageFitVerdict.Fits)
        {
            // a wearing part's item clicked before the machine is built: it goes in with its stage or after
            if (fit.Verdict == StageFitVerdict.NotAMaterial && Wearing.CanRenew(code, held, false, out _) == RenewVerdict.NotBuilt)
                return new AssemblyClick(AssemblyVerdict.OutOfOrder, 0, -1);
            return new AssemblyClick(fit.Verdict switch
            {
                StageFitVerdict.OutOfOrder => AssemblyVerdict.OutOfOrder,
                StageFitVerdict.AlreadyFitted => AssemblyVerdict.AlreadyFitted,
                StageFitVerdict.TooFew => AssemblyVerdict.TooFew,
                StageFitVerdict.OtherKind => AssemblyVerdict.OtherKind,
                _ => AssemblyVerdict.NotAPart,
            }, 0, fit.Stage);
        }
        if (apply && Build.Stages[fit.Stage].Materials[fit.Material].Wearing != null)
            _firstUses[fit.Stage * 1000 + fit.Material] = uses;
        if (apply && Build.StageComplete(fit.Stage))
            FitFirstSets(fit.Stage);
        return new AssemblyClick(AssemblyVerdict.Fitted, fit.Count, fit.Stage);
    }

    // a durable first set's durability, from the click that put it in until its stage completes
    private readonly Dictionary<int, double?> _firstUses = [];

    /// <summary>A completed stage's wearing materials become their parts' first sets.</summary>
    private void FitFirstSets(int stage)
    {
        var materials = Build.Stages[stage].Materials;
        for (int m = 0; m < materials.Count; m++)
            if (materials[m].Wearing is { } id && Build.CodeIn(stage, m) is { } code)
            {
                _firstUses.Remove(stage * 1000 + m, out var uses);
                Wearing.Renew(code, int.MaxValue, built: true, uses);
            }
    }

    /// <summary>Takes wearing part <paramref name="id"/>'s set back out (<see cref="WearingParts.Take"/>): its
    /// code and the uses it had left, or null when none is fitted.</summary>
    public (string Code, double Left)? Take(string id) => Wearing.Take(id);

    /// <summary>The creative shortcut: fills the next stage (and its wearing parts' first sets); once
    /// built, renews every wearing part. Returns whether anything changed.</summary>
    public bool FillNext()
    {
        if (Build.Next is { } next)
        {
            Build.FillNext();
            FitFirstSets(next);
            return true;
        }
        if (Wearing.AllFitted)
            return false;
        Wearing.FillAll();
        return true;
    }

    /// <summary>Everything breaking the machine gives back: each stage's items (a wearing part's first
    /// set only through its part, so a worn-out set is not returned twice) and every wearing set with
    /// use left. Before the stage holding a wearing part's first set is complete, its items are still the
    /// stage's.</summary>
    public IReadOnlyList<StageDrop> Returns()
    {
        var drops = new List<StageDrop>();
        for (int s = 0; s < Build.Stages.Count; s++)
        {
            bool complete = Build.StageComplete(s);
            var materials = Build.Stages[s].Materials;
            for (int m = 0; m < materials.Count; m++)
            {
                if (materials[m].Wearing != null && complete)
                    continue;
                foreach (var drop in DropsOf(s, m))
                    drops.Add(drop);
            }
        }
        drops.AddRange(Wearing.Returns());
        return Merge(drops);
    }

    private IEnumerable<StageDrop> DropsOf(int stage, int material) =>
        Build.Snapshot().Where(kv => kv.Key.StartsWith($"{stage}/{material}/", StringComparison.Ordinal))
            .Select(kv => new StageDrop(kv.Key[($"{stage}/{material}/".Length)..], kv.Value));

    /// <summary>Drops of the same code added together, in first-seen order; a durable part's (with its
    /// durability) kept by itself, after them.</summary>
    public static IReadOnlyList<StageDrop> Merge(IEnumerable<StageDrop> drops)
    {
        var order = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var durable = new List<StageDrop>();
        foreach (var d in drops)
        {
            if (d.Durability != null)
            {
                durable.Add(d);
                continue;
            }
            if (!counts.ContainsKey(d.Code))
                order.Add(d.Code);
            counts[d.Code] = counts.GetValueOrDefault(d.Code) + d.Count;
        }
        return [.. order.Select(c => new StageDrop(c, counts[c])), .. durable];
    }

    /// <summary>The build's snapshot (<see cref="StagedBuild.Snapshot"/>) and the wearing parts'
    /// (<see cref="WearingParts.Snapshot"/>), for saving.</summary>
    public (IReadOnlyDictionary<string, int> Build, IReadOnlyDictionary<string, (string Code, double Left)> Wearing) Snapshot() =>
        (Build.Snapshot(), Wearing.Snapshot());

    /// <summary>Restored from a save; what no longer fits the build file is returned in
    /// <paramref name="dropped"/> for the caller to give back.</summary>
    public static MachineAssembly Restore(MachineBuild spec, IReadOnlyDictionary<string, int> build,
        IReadOnlyDictionary<string, (string Code, double Left)> wearing, out IReadOnlyList<StageDrop> dropped)
    {
        var restored = StagedBuild.Restore(spec.Stages, build, out dropped);
        return new MachineAssembly(spec, restored, WearingParts.Restore(spec.Wearing, wearing));
    }
}
