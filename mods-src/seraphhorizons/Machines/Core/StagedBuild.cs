namespace SeraphHorizons.Mod.Machines.Core;

/// <summary>
/// One material of a build stage: the codes it may be (full <c>domain:path</c> codes, a <c>*</c>
/// matching any run of characters, so <c>game:supportbeam-*</c> is a support beam of any wood) and
/// how many of it the stage takes. <see cref="Picks"/> maps a code pattern to an extra rig
/// <c>requires</c> drawn when the material put in matches it: the services' pipe sections
/// (<c>seraphhorizons:pipesection-copper</c> → <c>pipecopper</c>), so the water line is drawn in the
/// metal fitted. <see cref="Draws"/> are <c>requires</c> drawn as soon as this material is all in,
/// before the rest of its stage (the jaw breaker's toggles, then its springs). <see cref="Wearing"/>
/// names the wearing part this material is the first set of (<see cref="MachineAssembly"/>).
/// <see cref="OneType"/>: every item of it must be the first one's code (the stall roaster's brickwork, any
/// fired brick but all of one type, #925).
/// </summary>
public sealed record BuildMaterial(IReadOnlyList<string> Codes, int Quantity, IReadOnlyDictionary<string, string>? Picks = null,
    IReadOnlyList<string>? Draws = null, string? Wearing = null, bool OneType = false)
{
    /// <summary>Whether <paramref name="code"/> (a full code) is this material.</summary>
    public bool Matches(string code) => Codes.Any(p => CodePattern.Matches(p, code));

    /// <summary>The <c>requires</c> a code put in for this material adds, or null.</summary>
    public string? PickOf(string code) =>
        Picks?.FirstOrDefault(kv => CodePattern.Matches(kv.Key, code)).Value;
}

/// <summary>
/// One build stage after the frame (the model README's stage table and its rig's
/// <c>buildStages</c>): its id, the rig <c>requires</c> values it draws once complete
/// (<see cref="Adds"/>), and its materials, which are its recipe. A <c>requires</c> in
/// <see cref="Adds"/> that is also a value of a material's <see cref="BuildMaterial.Picks"/> is drawn
/// only when that pick was put in (the water line's two metals).
/// </summary>
public sealed record MachineStage(string Id, IReadOnlyList<string> Adds, IReadOnlyList<BuildMaterial> Materials, string? Name = null);

/// <summary>Why a held stack does or does not go into a machine's build.</summary>
public enum StageFitVerdict
{
    Fits,
    /// <summary>No stage takes it.</summary>
    NotAMaterial,
    /// <summary>A later stage takes it: the next stage goes in first.</summary>
    OutOfOrder,
    /// <summary>Every stage that takes it has all of it (or the machine is built).</summary>
    AlreadyFitted,
    /// <summary>Fewer are held than the stage still needs of it, and they are not a full stack.</summary>
    TooFew,
    /// <summary>A material that is all one kind (one with <see cref="BuildMaterial.Picks"/>: the water
    /// line's metal; or <see cref="BuildMaterial.OneType"/>: one brick type) already holds another.</summary>
    OtherKind,
}

/// <summary>What a click with a stack would fit: the verdict, the stage and material it goes in, and
/// how many of the stack it takes.</summary>
public readonly record struct StageFit(StageFitVerdict Verdict, int Stage, int Material, int Count);

/// <summary>A fitted item as breaking the machine gives it back.</summary>
public readonly record struct StageDrop(string Code, int Count);

/// <summary>Code patterns: full codes with <c>*</c> for any run of characters.</summary>
public static class CodePattern
{
    /// <summary>A code with no domain is the game's, as the game reads one.</summary>
    public static string? Normalise(string? code) =>
        string.IsNullOrEmpty(code) ? null : code.Contains(':') ? code : "game:" + code;

    public static bool Matches(string pattern, string code)
    {
        pattern = Normalise(pattern)!;
        if (!pattern.Contains('*'))
            return pattern == code;
        var pieces = pattern.Split('*');
        if (!code.StartsWith(pieces[0], StringComparison.Ordinal))
            return false;
        int at = pieces[0].Length;
        for (int i = 1; i < pieces.Length - 1; i++)
        {
            int found = code.IndexOf(pieces[i], at, StringComparison.Ordinal);
            if (found < 0)
                return false;
            at = found + pieces[i].Length;
        }
        string last = pieces[^1];
        return code.Length - at >= last.Length && code.EndsWith(last, StringComparison.Ordinal);
    }
}

/// <summary>
/// A machine's staged build (#711), shared by the ore machines: the frame is placed, then each stage
/// is fitted in order by right-click with its materials (the draw bench's, gear cutter's and eidolon
/// gantry's rules: <see cref="StageFitVerdict.OutOfOrder"/>, <see cref="StageFitVerdict.AlreadyFitted"/>,
/// <see cref="StageFitVerdict.TooFew"/>). A click puts in one material: all the stage still needs of it
/// from the held stack, or, when the stack held is full and still short of it (bulk timber, 41 support
/// beams), the whole stack, so a bulk material goes in a stack a click. A stage takes nothing before
/// the one ahead of it is complete. Every item put in is kept by its own code, so breaking returns
/// exactly what went in.
/// </summary>
public sealed class StagedBuild
{
    private readonly Dictionary<string, int>[][] _fitted;
    private static readonly Dictionary<string, int> NoneFitted = [];

    public StagedBuild(IReadOnlyList<MachineStage> stages)
    {
        Stages = stages;
        _fitted = stages.Select(s => s.Materials.Select(_ => new Dictionary<string, int>(StringComparer.Ordinal)).ToArray()).ToArray();
    }

    public IReadOnlyList<MachineStage> Stages { get; }

    /// <summary>How many of material <paramref name="material"/> stage <paramref name="stage"/> holds.</summary>
    public int Count(int stage, int material) => _fitted[stage][material].Values.Sum();

    /// <summary>How many more of it the stage needs.</summary>
    public int Missing(int stage, int material) => Math.Max(0, Stages[stage].Materials[material].Quantity - Count(stage, material));

    public bool StageComplete(int stage) => Enumerable.Range(0, Stages[stage].Materials.Count).All(m => Missing(stage, m) == 0);

    /// <summary>The first stage not complete; null when the machine is built.</summary>
    public int? Next
    {
        get
        {
            for (int s = 0; s < Stages.Count; s++)
                if (!StageComplete(s))
                    return s;
            return null;
        }
    }

    /// <summary>Every stage is in: the machine can run.</summary>
    public bool Complete => Next == null;

    /// <summary>How many stages are complete.</summary>
    public int StagesDone => Next ?? Stages.Count;

    /// <summary>Whether a rig part needing <paramref name="requires"/> is drawn as far as the build
    /// goes: null always (the frame); a stage's <see cref="MachineStage.Adds"/> once it is complete, a
    /// pick only when its material was put in that way. A <c>requires</c> no stage adds is the
    /// machine's own (contents, a wearing part) and is not the build's: true here.</summary>
    public bool Fitted(string? requires)
    {
        if (requires == null)
            return true;
        for (int s = 0; s < Stages.Count; s++)
            for (int m = 0; m < Stages[s].Materials.Count; m++)
                if (Stages[s].Materials[m].Draws?.Contains(requires) == true)
                    return Missing(s, m) == 0 && (s == 0 || StageComplete(s - 1));
        for (int s = 0; s < Stages.Count; s++)
        {
            var stage = Stages[s];
            if (!stage.Adds.Contains(requires))
                continue;
            if (!StageComplete(s))
                return false;
            bool picked = false, isPick = false;
            for (int m = 0; m < stage.Materials.Count; m++)
            {
                var material = stage.Materials[m];
                if (material.Picks?.Values.Contains(requires) != true)
                    continue;
                isPick = true;
                picked |= _fitted[s][m].Keys.Any(code => material.PickOf(code) == requires);
            }
            return !isPick || picked;
        }
        return true;
    }

    /// <summary>The stage and material of stage range <paramref name="from"/>..<paramref name="to"/>
    /// (exclusive) that <paramref name="code"/> is, first found; (-1, -1) when none.</summary>
    private (int Stage, int Material) Find(string code, int from, int to)
    {
        for (int s = from; s < to; s++)
            for (int m = 0; m < Stages[s].Materials.Count; m++)
                if (Stages[s].Materials[m].Matches(code))
                    return (s, m);
        return (-1, -1);
    }

    /// <summary>What a click with <paramref name="held"/> of <paramref name="code"/> would fit, the
    /// item stacking to <paramref name="maxStack"/>.</summary>
    public StageFit CanFit(string? code, int held, int maxStack = int.MaxValue)
    {
        code = CodePattern.Normalise(code);
        if (code == null || held <= 0)
            return new StageFit(StageFitVerdict.NotAMaterial, -1, -1, 0);
        if (Next is not { } next)
            return new StageFit(Find(code, 0, Stages.Count).Stage >= 0 ? StageFitVerdict.AlreadyFitted : StageFitVerdict.NotAMaterial, -1, -1, 0);
        // the next stage's first material it is and still needs
        int material = -1, anyMaterial = -1;
        for (int m = 0; m < Stages[next].Materials.Count; m++)
        {
            if (!Stages[next].Materials[m].Matches(code))
                continue;
            if (anyMaterial < 0)
                anyMaterial = m;
            if (Missing(next, m) > 0)
            {
                material = m;
                break;
            }
        }
        if (material < 0)
        {
            if (anyMaterial >= 0 || Find(code, 0, next).Stage >= 0)
                return new StageFit(StageFitVerdict.AlreadyFitted, next, anyMaterial, 0);
            return new StageFit(Find(code, next + 1, Stages.Count).Stage >= 0 ? StageFitVerdict.OutOfOrder : StageFitVerdict.NotAMaterial,
                next, -1, 0);
        }
        var picked = Stages[next].Materials[material];
        if (picked.Picks != null && _fitted[next][material].Keys.Any(c => picked.PickOf(c) != picked.PickOf(code)))
            return new StageFit(StageFitVerdict.OtherKind, next, material, 0);
        if (picked.OneType && _fitted[next][material].Any(kv => kv.Value > 0 && kv.Key != code))
            return new StageFit(StageFitVerdict.OtherKind, next, material, 0);
        int missing = Missing(next, material);
        if (held >= missing)
            return new StageFit(StageFitVerdict.Fits, next, material, missing);
        if (held >= maxStack)
            return new StageFit(StageFitVerdict.Fits, next, material, held);
        return new StageFit(StageFitVerdict.TooFew, next, material, 0);
    }

    /// <summary>Fits what <see cref="CanFit"/> allows; the caller takes <see cref="StageFit.Count"/>
    /// from the slot when the verdict is <see cref="StageFitVerdict.Fits"/>.</summary>
    public StageFit Fit(string? code, int held, int maxStack = int.MaxValue)
    {
        var fit = CanFit(code, held, maxStack);
        if (fit.Verdict != StageFitVerdict.Fits)
            return fit;
        var into = _fitted[fit.Stage][fit.Material];
        string key = CodePattern.Normalise(code)!;
        into[key] = into.GetValueOrDefault(key) + fit.Count;
        return fit;
    }

    /// <summary>What the next stage still takes: each material missing and how many; empty when built.</summary>
    public IReadOnlyList<(BuildMaterial Material, int Missing)> NextNeeds()
    {
        if (Next is not { } next)
            return [];
        return Stages[next].Materials.Select((m, i) => (m, Missing(next, i))).Where(p => p.Item2 > 0).ToList();
    }

    /// <summary>The creative shortcut: fills the next stage with each material's first code that is not
    /// a pattern (or the pattern's first code with <c>*</c> taken as <paramref name="wildcard"/>).
    /// Returns whether a stage was filled.</summary>
    public bool FillNext(string wildcard = "oak")
    {
        if (Next is not { } next)
            return false;
        for (int m = 0; m < Stages[next].Materials.Count; m++)
        {
            int missing = Missing(next, m);
            if (missing <= 0)
                continue;
            string code = CodePattern.Normalise(Stages[next].Materials[m].Codes[0])!.Replace("*", wildcard);
            _fitted[next][m][code] = _fitted[next][m].GetValueOrDefault(code) + missing;
        }
        return true;
    }

    /// <summary>The code put in for material <paramref name="material"/> of stage <paramref name="stage"/>
    /// (the most of, when several were), or null.</summary>
    public string? CodeIn(int stage, int material) =>
        _fitted[stage][material].Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault();

    /// <summary>Every item put in, in stage order, by code; a wearing part's first set
    /// (<see cref="BuildMaterial.Wearing"/>) left out when <paramref name="withWearing"/> is false, as the
    /// wearing part gives it back itself.</summary>
    public IReadOnlyList<StageDrop> Returns(bool withWearing = true)
    {
        var drops = new List<StageDrop>();
        for (int s = 0; s < Stages.Count; s++)
            for (int m = 0; m < Stages[s].Materials.Count; m++)
                foreach (var (code, n) in withWearing || Stages[s].Materials[m].Wearing == null ? _fitted[s][m] : NoneFitted)
                    if (n > 0)
                        drops.Add(new StageDrop(code, n));
        return drops;
    }

    /// <summary>The key a fitted code is saved under: <c>stage/material/code</c>.</summary>
    public static string Key(int stage, int material, string code) => $"{stage}/{material}/{code}";

    /// <summary>Each fitted code's count, keyed by <see cref="Key"/>, for saving.</summary>
    public IReadOnlyDictionary<string, int> Snapshot()
    {
        var snapshot = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int s = 0; s < Stages.Count; s++)
            for (int m = 0; m < Stages[s].Materials.Count; m++)
                foreach (var (code, n) in _fitted[s][m])
                    if (n > 0)
                        snapshot[Key(s, m, code)] = n;
        return snapshot;
    }

    /// <summary>Restored state, as <see cref="Snapshot"/> gave it. A code that is not its material's is
    /// dropped; counts are clamped to the material's quantity; a stage after one not complete keeps
    /// nothing (it could not have been fitted). What was dropped is returned in
    /// <paramref name="dropped"/>, so the caller can give it back rather than lose it (a changed recipe).</summary>
    public static StagedBuild Restore(IReadOnlyList<MachineStage> stages, IReadOnlyDictionary<string, int> snapshot,
        out IReadOnlyList<StageDrop> dropped)
    {
        var build = new StagedBuild(stages);
        var lost = new List<StageDrop>();
        var byStage = new Dictionary<(int, int), List<(string Code, int Count)>>();
        foreach (var (key, count) in snapshot.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var parts = key.Split('/', 3);
            if (count <= 0)
                continue;
            if (parts.Length != 3 || !int.TryParse(parts[0], out int s) || !int.TryParse(parts[1], out int m)
                || s < 0 || s >= stages.Count || m < 0 || m >= stages[s].Materials.Count || !stages[s].Materials[m].Matches(parts[2]))
            {
                if (parts.Length == 3)
                    lost.Add(new StageDrop(parts[2], count));
                continue;
            }
            if (!byStage.TryGetValue((s, m), out var list))
                byStage[(s, m)] = list = [];
            list.Add((parts[2], count));
        }
        bool open = true;
        for (int s = 0; s < stages.Count; s++)
        {
            for (int m = 0; m < stages[s].Materials.Count; m++)
            {
                if (!byStage.TryGetValue((s, m), out var list))
                    continue;
                int room = open ? stages[s].Materials[m].Quantity : 0;
                foreach (var (code, count) in list)
                {
                    int keep = Math.Min(room, count);
                    room -= keep;
                    if (keep > 0)
                        build._fitted[s][m][code] = keep;
                    if (count > keep)
                        lost.Add(new StageDrop(code, count - keep));
                }
            }
            if (!build.StageComplete(s))
                open = false;
        }
        dropped = lost;
        return build;
    }

    public static StagedBuild Restore(IReadOnlyList<MachineStage> stages, IReadOnlyDictionary<string, int> snapshot) =>
        Restore(stages, snapshot, out _);
}
