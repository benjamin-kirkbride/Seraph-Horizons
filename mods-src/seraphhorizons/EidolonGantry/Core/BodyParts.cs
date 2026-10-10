namespace SeraphHorizons.Mod.EidolonGantry.Core;

/// <summary>One ingredient of a body stage: an item's full code and how many of it the stage takes.</summary>
public readonly record struct BodyIngredient(string Code, int Count);

/// <summary>Why a held item can or cannot be fitted to the body.</summary>
public enum BodyFitVerdict
{
    Fits,
    /// <summary>No body stage takes it.</summary>
    NotAPart,
    /// <summary>Only a later stage takes it: the current stage is finished first.</summary>
    OutOfOrder,
    /// <summary>Every stage that takes it has all it needs of it.</summary>
    AlreadyFitted,
}

/// <summary>
/// The eidolon's body bill (the epic's, #668; Eidolon/README.md "Build stages"): what each body stage
/// takes, in <see cref="GantryRequires.BodyStages"/> order. Every ingredient is one plain code: the
/// steel is steel, the gears the pack's stainless spur gear. The head brings the Resonance Archives'
/// eidolon's elucidatory vessel, its brain; the mind is one temporal gear, its first charge, which wakes it.
/// <c>config/eidolon-stages.json</c> (written by Eidolon/tools/make_shape.py) lists the same, held to
/// this by a test.
/// </summary>
public static class BodyBill
{
    public const string SteelPlate = "game:metalplate-steel";
    public const string SteelRod = "game:rod-steel";
    public const string SteelNails = "game:metalnailsandstrips-steel";
    public const string MetalParts = "game:metal-parts";
    public const string StainlessGear = "seraphhorizons:gear-stainless";
    public const string Vessel = "game:rustypart-eidolon2tr";
    public const string TemporalGear = "game:gear-temporal";

    private static BodyIngredient One(string code) => new(code, 1);

    /// <summary>Each body stage's ingredients, by stage name (<see cref="GantryRequires.BodyStages"/>).</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<BodyIngredient>> Stages =
        new Dictionary<string, IReadOnlyList<BodyIngredient>>
        {
            ["torso"] =
            [
                One("game:jonasparts-tank01"), One("game:jonasparts-tank02"), One("game:jonasparts-pumphead"),
                new(SteelPlate, 4), new(SteelNails, 8), new(MetalParts, 4), new(StainlessGear, 3),
            ],
            ["pelvis"] =
            [
                One("game:jonasframes-gearbox02"),
                new(SteelPlate, 3), new(SteelNails, 8), new(MetalParts, 3), new(StainlessGear, 2),
            ],
            ["legs"] =
            [
                new("game:jonasframes-joint01", 2), new("game:jonasframes-spring01", 2),
                new(SteelRod, 4), new(SteelPlate, 3), new(SteelNails, 8), new(MetalParts, 3), new(StainlessGear, 2),
            ],
            ["arms"] =
            [
                One("game:jonasframes-gears01"), One("game:jonasframes-gears02"), One("game:jonasparts-cylinder01"), One("game:jonasparts-valve01"),
                new(SteelRod, 4), new(SteelPlate, 2), new(SteelNails, 8), new(MetalParts, 4), new(StainlessGear, 3),
            ],
            ["head"] =
            [
                One(Vessel), One("game:jonasframes-oscillator01"), One("game:jonasframes-gearbox01"),
                One("game:jonasparts-cylinder02"), One("game:jonasparts-connector01"),
                new(SteelPlate, 1), new(SteelNails, 4), new(MetalParts, 2), new(StainlessGear, 2),
            ],
            ["mind"] = [One(TemporalGear)],
        };

    /// <summary>The stage names in build order.</summary>
    public static IReadOnlyList<string> Order => GantryRequires.BodyStages;

    public static IReadOnlyList<BodyIngredient> Of(string stage) => Stages[stage];

    /// <summary>The last stage, whose completion wakes the eidolon.</summary>
    public static string Last => Order[^1];
}

/// <summary>
/// The eidolon's body on a gantry's spine: six stages after the winch (<see cref="BodyBill"/>), in
/// order, each complete before the next takes anything. Within a stage any order: a click fits what
/// the held stack can give toward one ingredient still needed (all of it, up to what the stage still
/// needs of that item). Every fitted count is kept, so breaking returns exactly what went in.
/// <see cref="Clear"/> empties it when the eidolon wakes and leaves.
/// </summary>
public sealed class BodyParts
{
    // fitted[stage][code] = how many are in
    private readonly Dictionary<string, Dictionary<string, int>> _fitted = BodyBill.Order.ToDictionary(s => s, _ => new Dictionary<string, int>());

    /// <summary>A code with no domain is the game's, as the game reads one.</summary>
    public static string? Normalise(string? code) => GantryParts.Normalise(code);

    public int Fitted(string stage, string code) => _fitted[stage].GetValueOrDefault(code);

    /// <summary>How many more of <paramref name="code"/> <paramref name="stage"/> needs (0 when none or not its).</summary>
    public int Missing(string stage, string code)
    {
        code = Normalise(code) ?? "";
        int need = BodyBill.Of(stage).Where(i => i.Code == code).Sum(i => i.Count);
        return Math.Max(0, need - Fitted(stage, code));
    }

    /// <summary>What a stage still needs, in the bill's order; empty when it is complete.</summary>
    public IReadOnlyList<BodyIngredient> StillNeeded(string stage) =>
        BodyBill.Of(stage).Select(i => new BodyIngredient(i.Code, Missing(stage, i.Code))).Where(i => i.Count > 0).ToList();

    public bool StageComplete(string stage) => BodyBill.Of(stage).All(i => Fitted(stage, i.Code) >= i.Count);

    /// <summary>The first stage not complete, in order; null when the whole body is in.</summary>
    public string? Next => BodyBill.Order.FirstOrDefault(s => !StageComplete(s));

    public bool Complete => Next == null;

    /// <summary>Anything fitted at all.</summary>
    public bool Started => _fitted.Values.Any(f => f.Values.Any(n => n > 0));

    /// <summary>Whether fitting <paramref name="code"/> now, <paramref name="held"/> in hand, is allowed:
    /// the stage it goes in and how many it takes (what is held, up to what the stage still needs).</summary>
    public BodyFitVerdict CanFit(string? code, int held, out string stage, out int take)
    {
        stage = "";
        take = 0;
        code = Normalise(code);
        if (code == null || held <= 0)
            return BodyFitVerdict.NotAPart;
        var stages = BodyBill.Order.Where(s => BodyBill.Of(s).Any(i => i.Code == code)).ToList();
        if (stages.Count == 0)
            return BodyFitVerdict.NotAPart;
        if (Next is { } next && stages.Contains(next) && Missing(next, code) > 0)
        {
            stage = next;
            take = Math.Min(held, Missing(next, code));
            return BodyFitVerdict.Fits;
        }
        int nextIndex = Next is { } n ? BodyBill.Order.ToList().IndexOf(n) : BodyBill.Order.Count;
        return stages.Any(s => BodyBill.Order.ToList().IndexOf(s) > nextIndex) ? BodyFitVerdict.OutOfOrder : BodyFitVerdict.AlreadyFitted;
    }

    /// <summary>Fits as <see cref="CanFit"/> allows; returns how many went in (the caller takes them).</summary>
    public int Fit(string? code, int held)
    {
        if (CanFit(code, held, out var stage, out int take) != BodyFitVerdict.Fits)
            return 0;
        var f = _fitted[stage];
        string c = Normalise(code)!;
        f[c] = f.GetValueOrDefault(c) + take;
        return take;
    }

    /// <summary>Fills the next stage with everything it still needs (the creative shortcut); its name,
    /// or null when the body is complete.</summary>
    public string? FitNextStage()
    {
        if (Next is not { } stage)
            return null;
        foreach (var i in BodyBill.Of(stage))
            _fitted[stage][i.Code] = Math.Max(Fitted(stage, i.Code), i.Count);
        return stage;
    }

    /// <summary>Every fitted item, merged by code in bill order, as breaking gives it back.</summary>
    public IReadOnlyList<GantryDrop> Returns()
    {
        var totals = new Dictionary<string, int>();
        var order = new List<string>();
        foreach (var stage in BodyBill.Order)
            foreach (var (code, n) in _fitted[stage])
            {
                if (n <= 0)
                    continue;
                if (!totals.ContainsKey(code))
                    order.Add(code);
                totals[code] = totals.GetValueOrDefault(code) + n;
            }
        return order.Select(c => new GantryDrop(c, totals[c])).ToList();
    }

    /// <summary>Empties the body (it woke and left the spine).</summary>
    public void Clear()
    {
        foreach (var f in _fitted.Values)
            f.Clear();
    }

    /// <summary>Each fitted count by <c>stage/code</c>, for saving.</summary>
    public IReadOnlyDictionary<string, int> Snapshot() =>
        _fitted.SelectMany(s => s.Value.Where(kv => kv.Value > 0).Select(kv => (Key: s.Key + "/" + kv.Key, kv.Value)))
            .ToDictionary(x => x.Key, x => x.Value);

    /// <summary>Restored state, as <see cref="Snapshot"/> gave it. An unknown stage or a code its stage
    /// does not take is dropped; a count is held to what its stage takes. Counts in a stage after one
    /// that is not complete are kept (they are returned on breaking), though nothing more goes in there
    /// until the stages before it are done.</summary>
    public static BodyParts Restore(IReadOnlyDictionary<string, int> snapshot)
    {
        var parts = new BodyParts();
        foreach (var (key, n) in snapshot)
        {
            int slash = key.IndexOf('/');
            if (slash <= 0 || n <= 0)
                continue;
            string stage = key[..slash], code = Normalise(key[(slash + 1)..]) ?? "";
            if (!BodyBill.Stages.TryGetValue(stage, out var bill))
                continue;
            int max = bill.Where(i => i.Code == code).Sum(i => i.Count);
            if (max > 0)
                parts._fitted[stage][code] = Math.Min(n, max);
        }
        return parts;
    }
}
