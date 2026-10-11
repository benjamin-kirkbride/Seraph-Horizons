namespace SeraphHorizons.Mod.CrucibleFurnace.Core;

/// <summary>Why a held stack does or does not go into the furnace's build.</summary>
public enum BuildVerdict
{
    Fits,
    /// <summary>No stage takes it.</summary>
    NotAMaterial,
    /// <summary>A later stage takes it: the stage ahead of it goes in first.</summary>
    OutOfOrder,
    /// <summary>The stage takes it, but has all of it already.</summary>
    Enough,
    /// <summary>A brick of another type than the furnace's.</summary>
    OtherBrick,
    /// <summary>A fired brick of a type the furnace has no texture for (another mod's).</summary>
    UnknownBrick,
    /// <summary>Every stage is in.</summary>
    Built,
}

/// <summary>What a click with a stack would fit: how many, into which stage and material, and if
/// none, why.</summary>
public readonly record struct BuildFit(BuildVerdict Verdict, int Count, int Stage, int Material);

/// <summary>A fitted item as breaking the furnace gives it back.</summary>
public readonly record struct BuildDrop(string Code, int Count);

/// <summary>
/// The crucible furnace's build (#796, the rig's <c>build</c>): after the iron frame, its stages in
/// order, each fitted a stack a click (as many of the held stack as the stage still needs), complete
/// when every material is in. A stage takes nothing before the one ahead of it is complete. The
/// brickwork's stage is <see cref="BuildStage.OneType"/>: its first brick sets the furnace's
/// <see cref="BrickType"/>, every other brick must match, and a type the rig has no texture for is
/// refused. Every fitted item is counted, so breaking gives back exactly what went in.
/// </summary>
public sealed class FurnaceBuild(IReadOnlyList<BuildStage> stages, BrickTypes bricks)
{
    private readonly int[][] _counts = stages.Select(s => new int[s.Materials.Count]).ToArray();

    public IReadOnlyList<BuildStage> Stages { get; } = stages;
    public BrickTypes Bricks { get; } = bricks;

    /// <summary>The furnace's brick type, set by its first brick; null before.</summary>
    public string? BrickType { get; private set; }

    /// <summary>How many of material <paramref name="material"/> stage <paramref name="stage"/> holds.</summary>
    public int Count(int stage, int material) => _counts[stage][material];

    /// <summary>How many more of the material it needs.</summary>
    public int Missing(int stage, int material) => Stages[stage].Materials[material].Quantity - _counts[stage][material];

    public bool StageComplete(int stage) => Stages[stage].Materials.Select((_, m) => Missing(stage, m)).All(n => n <= 0);

    /// <summary>The first stage not complete; null when the furnace is built.</summary>
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

    public bool Complete => Next == null;

    /// <summary>Whether the stage that fits <paramref name="requires"/> is complete.</summary>
    public bool Has(string requires)
    {
        for (int s = 0; s < Stages.Count; s++)
            if (Stages[s].Requires == requires)
                return StageComplete(s);
        return false;
    }

    /// <summary>Whether a rig part needing <paramref name="requires"/> may be drawn as far as the build
    /// goes: null always (the frame); a stage's when it is complete; a stage's <c>also</c> (the
    /// damper's places) once it is. Gameplay's state (pots, coke, the damper's place) decides the rest.</summary>
    public bool Fitted(string? requires)
    {
        if (requires == null)
            return true;
        for (int s = 0; s < Stages.Count; s++)
            if (Stages[s].Requires == requires || Stages[s].Also.Contains(requires))
                return StageComplete(s);
        return true;
    }

    /// <summary>The material of stage <paramref name="stage"/> that <paramref name="code"/> is, or -1.</summary>
    private int MaterialOf(int stage, string code) =>
        Stages[stage].Materials.Select((m, i) => (m, i)).FirstOrDefault(p => p.m.Matches(code), (null!, -1)).Item2;

    /// <summary>What a click with <paramref name="available"/> of <paramref name="code"/> would fit.</summary>
    public BuildFit CanFit(string? code, int available)
    {
        if (code == null)
            return new BuildFit(BuildVerdict.NotAMaterial, 0, -1, -1);
        if (!code.Contains(':'))
            code = "game:" + code;
        if (Next is not { } next)
            return new BuildFit(Stages.Any(s => s.Materials.Any(m => m.Matches(code))) ? BuildVerdict.Built : BuildVerdict.NotAMaterial, 0, -1, -1);
        int material = MaterialOf(next, code);
        if (material < 0)
        {
            bool later = Enumerable.Range(next + 1, Stages.Count - next - 1).Any(s => MaterialOf(s, code) >= 0);
            bool earlier = Enumerable.Range(0, next).Any(s => MaterialOf(s, code) >= 0);
            return new BuildFit(later ? BuildVerdict.OutOfOrder : earlier ? BuildVerdict.Enough : BuildVerdict.NotAMaterial, 0, next, -1);
        }
        if (Stages[next].OneType)
        {
            string? type = Bricks.TypeOf(code);
            if (type == null || Bricks.Get(type) == null)
                return new BuildFit(BuildVerdict.UnknownBrick, 0, next, material);
            if (BrickType != null && type != BrickType)
                return new BuildFit(BuildVerdict.OtherBrick, 0, next, material);
        }
        int n = Math.Min(available, Missing(next, material));
        return n <= 0 ? new BuildFit(BuildVerdict.Enough, 0, next, material) : new BuildFit(BuildVerdict.Fits, n, next, material);
    }

    /// <summary>Fits as much of <paramref name="available"/> <paramref name="code"/> as the next stage
    /// takes; the fit (its <see cref="BuildFit.Count"/> the number taken).</summary>
    public BuildFit Fit(string? code, int available)
    {
        var fit = CanFit(code, available);
        if (fit.Verdict != BuildVerdict.Fits)
            return fit;
        _counts[fit.Stage][fit.Material] += fit.Count;
        if (Stages[fit.Stage].OneType)
            BrickType ??= Bricks.TypeOf(code!.Contains(':') ? code : "game:" + code);
        return fit;
    }

    /// <summary>The creative shortcut: fills the next stage, the furnace's brick type (or the default)
    /// for its bricks. Returns whether a stage was filled.</summary>
    public bool FillNext()
    {
        if (Next is not { } next)
            return false;
        if (Stages[next].OneType)
            BrickType ??= Bricks.Default;
        for (int m = 0; m < Stages[next].Materials.Count; m++)
            _counts[next][m] = Stages[next].Materials[m].Quantity;
        return true;
    }

    /// <summary>The concrete item a material's fitted items are: the brick of the furnace's type for a
    /// one-type stage, else the material's code.</summary>
    public string? ItemOf(int stage, int material)
    {
        if (Stages[stage].OneType)
            return BrickType is { } t ? Bricks.ItemOf(t) : null;
        string code = Stages[stage].Materials[material].Code;
        return code.Contains('*') ? null : code;
    }

    /// <summary>Every fitted item, in stage order.</summary>
    public IReadOnlyList<BuildDrop> Returns()
    {
        var drops = new List<BuildDrop>();
        for (int s = 0; s < Stages.Count; s++)
            for (int m = 0; m < Stages[s].Materials.Count; m++)
                if (_counts[s][m] > 0 && ItemOf(s, m) is { } code)
                    drops.Add(new BuildDrop(code, _counts[s][m]));
        return drops;
    }

    /// <summary>Each material's count, keyed "stage/material", for saving.</summary>
    public IReadOnlyDictionary<string, int> Snapshot()
    {
        var snap = new Dictionary<string, int>();
        for (int s = 0; s < Stages.Count; s++)
            for (int m = 0; m < Stages[s].Materials.Count; m++)
                if (_counts[s][m] > 0)
                    snap[Key(s, m)] = _counts[s][m];
        return snap;
    }

    public static string Key(int stage, int material) => $"{stage}/{material}";

    /// <summary>Restored state, as <see cref="Snapshot"/> gave it, with its brick type. Counts are
    /// clamped to the stage's quantities; a stage after one not complete keeps nothing (it could not
    /// have been fitted); a brick type the rig does not know drops the brickwork.</summary>
    public static FurnaceBuild Restore(IReadOnlyList<BuildStage> stages, BrickTypes bricks, IReadOnlyDictionary<string, int> counts, string? brickType)
    {
        var build = new FurnaceBuild(stages, bricks);
        if (brickType != null && bricks.Get(brickType) != null)
            build.BrickType = brickType;
        for (int s = 0; s < stages.Count; s++)
        {
            if (stages[s].OneType && build.BrickType == null)
                break;
            for (int m = 0; m < stages[s].Materials.Count; m++)
                build._counts[s][m] = Math.Clamp(counts.GetValueOrDefault(Key(s, m)), 0, stages[s].Materials[m].Quantity);
            if (!build.StageComplete(s))
                break;
        }
        if (build._counts.All(c => c.All(n => n == 0)))
            build.BrickType = null;
        return build;
    }
}
