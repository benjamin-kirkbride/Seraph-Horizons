using SeraphHorizons.Mod.Eidolon.Core;
using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.EidolonGantry.Core;

/// <summary>One ingredient of a body stage: an item's (for a support beam, a block's) full code and how
/// many of it the stage takes. <paramref name="AnyMaterial"/>: a layer's material before its first piece
/// fixes it, any the layer allows (<paramref name="Code"/> is then the default build's, for showing).</summary>
public readonly record struct BodyIngredient(string Code, int Count, bool AnyMaterial = false);

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
    /// <summary>The layer being fitted takes this kind of item, but not in this material: one the layer
    /// does not allow (copper plates, rotten beams), or not the one its first piece fixed.</summary>
    WrongMaterial,
}

/// <summary>
/// The eidolon's body bill (Eidolon/README.md "Build stages"), in <see cref="GantryRequires.BodyStages"/>
/// order: the torso (crafted in the grid, <see cref="EidolonFittings.TorsoRecipe"/>, placed whole), the
/// five layers of material (<see cref="EidolonFittings.Bill"/>: a material the builder chooses, with the
/// layer's Jonas parts) and the mind (one temporal gear). Everything is
/// <see cref="EidolonFittings"/>' (counts <see cref="FittingCounts"/>); <c>config/eidolon-stages.json</c>
/// (written by Eidolon/tools/make_shape.py) lists the same with the default build's materials as
/// {tokens}, held to this by a test.
/// </summary>
public static class BodyBill
{
    public const string Torso = EidolonFittings.TorsoCode;
    public const string TemporalGear = EidolonFittings.TemporalGear;
    public const string Mind = "mind";
    public const string TorsoStage = "torso";

    /// <summary>The stage names in build order.</summary>
    public static IReadOnlyList<string> Order => GantryRequires.BodyStages;

    /// <summary>The last stage, the mind: after it the crank wakes the eidolon.</summary>
    public static string Last => Order[^1];

    /// <summary>The layer a stage fits, or null for the torso and the mind.</summary>
    public static FittingLayer? LayerOf(string stage) => EidolonFittings.LayerOfTextureCode(stage);

    /// <summary>The stage a layer is fitted in (its texture code).</summary>
    public static string StageOf(FittingLayer layer) => EidolonFittings.TextureCode(layer);

    /// <summary>The default build's material for a layer: how every eidolon was built before fittings
    /// (<see cref="EidolonBuild.OldBuild"/>), what an old body loads as and the creative shortcut fits.</summary>
    public static string DefaultMaterial(FittingLayer layer) => EidolonBuild.OldBuild[layer]!;

    /// <summary>A stage's ingredients; for a layer in <paramref name="material"/> (null: the default
    /// build's). Empty for a layer that does not take the material.</summary>
    public static IReadOnlyList<BodyIngredient> Of(string stage, string? material = null)
    {
        if (stage == TorsoStage)
            return [new BodyIngredient(Torso, 1)];
        if (stage == Mind)
            return EidolonFittings.Mind.Select(i => new BodyIngredient(i.Code, i.Count)).ToList();
        if (LayerOf(stage) is not { } layer)
            throw new ArgumentException($"no body stage {stage}", nameof(stage));
        return EidolonFittings.Bill(layer, material ?? DefaultMaterial(layer)).Select(i => new BodyIngredient(i.Code, i.Count)).ToList();
    }

    /// <summary>Whether <paramref name="code"/> is anything a body stage takes in any material (the
    /// torso, a layer's material or Jonas part, the temporal gear).</summary>
    public static bool IsBodyItem(string? code)
    {
        code = BodyParts.Normalise(code);
        return code != null && StagesTaking(code).Any();
    }

    /// <summary>The stages that take <paramref name="code"/> in some material, in build order.</summary>
    public static IEnumerable<string> StagesTaking(string code)
    {
        if (code == Torso)
            yield return TorsoStage;
        foreach (var layer in EidolonFittings.Layers)
            if (EidolonFittings.Identify(code) is { } id && id.Layer == layer
                || EidolonFittings.JonasParts(layer).Any(i => i.Code == code))
                yield return StageOf(layer);
        if (EidolonFittings.Mind.Any(i => i.Code == code))
            yield return Mind;
    }

    /// <summary>The layer whose material family <paramref name="code"/> is (a support beam, rod, nails and
    /// strips, plate, normal leather, cloth or chain), whatever its material: so a copper plate offered to
    /// the plates is refused as the wrong material, not ignored.</summary>
    public static FittingLayer? FamilyLayer(string? code)
    {
        code = BodyParts.Normalise(code);
        if (code == null || !code.StartsWith("game:", StringComparison.Ordinal))
            return null;
        string path = code["game:".Length..];
        foreach (var (layer, prefix) in Families)
            if (path.StartsWith(prefix, StringComparison.Ordinal))
                return layer;
        return null;
    }

    private static readonly (FittingLayer, string)[] Families =
    [
        (FittingLayer.Bones, "supportbeam-"), (FittingLayer.Joints, "rod-"), (FittingLayer.Joints, "metalnailsandstrips-"),
        (FittingLayer.Plates, "metalplate-"), (FittingLayer.Sash, "leather-normal-"), (FittingLayer.Sash, "cloth-"),
        (FittingLayer.Skirt, "metalchain-"),
    ];
}

/// <summary>
/// The eidolon's body on a gantry's spine: seven stages after the winch (<see cref="BodyBill"/>), in
/// order, each complete before the next takes anything. Within a stage any order: a click fits what
/// the held stack can give toward one ingredient still needed (all of it, up to what the stage still
/// needs of that item). A layer's first piece of material fixes its material (<see cref="Material"/>),
/// and every piece after must match it; its Jonas parts go in whatever the material. Every fitted count
/// is kept, so breaking returns exactly what went in. <see cref="Clear"/> empties it when the eidolon
/// wakes and leaves.
/// </summary>
public sealed class BodyParts
{
    // fitted[stage][code] = how many are in
    private readonly Dictionary<string, Dictionary<string, int>> _fitted = BodyBill.Order.ToDictionary(s => s, _ => new Dictionary<string, int>());
    // what an old body (limb by limb) had that the new stages do not take, to be given back
    private readonly Dictionary<string, int> _spare = [];

    /// <summary>A code with no domain is the game's, as the game reads one.</summary>
    public static string? Normalise(string? code) => CodePattern.Normalise(code);

    public int Fitted(string stage, string code) => _fitted[stage].GetValueOrDefault(code);

    /// <summary>A layer's material, fixed by its first piece; null before.</summary>
    public string? Material(FittingLayer layer) =>
        _fitted[BodyBill.StageOf(layer)].Where(kv => kv.Value > 0)
            .Select(kv => EidolonFittings.Identify(kv.Key)).FirstOrDefault(id => id?.Layer == layer)?.Material;

    /// <summary>A stage's bill as it stands: a layer's in its material, or, before one is fixed, the
    /// default build's with its material marked <see cref="BodyIngredient.AnyMaterial"/>.</summary>
    public IReadOnlyList<BodyIngredient> Bill(string stage)
    {
        if (BodyBill.LayerOf(stage) is not { } layer)
            return BodyBill.Of(stage);
        if (Material(layer) is { } material)
            return BodyBill.Of(stage, material);
        int pieces = EidolonFittings.MaterialCodes(layer, BodyBill.DefaultMaterial(layer)).Count;
        return BodyBill.Of(stage).Select((i, n) => n < pieces ? i with { AnyMaterial = true } : i).ToList();
    }

    /// <summary>How many more of <paramref name="code"/> <paramref name="stage"/> needs (0 when none or not its).</summary>
    public int Missing(string stage, string code)
    {
        code = Normalise(code) ?? "";
        int need = BillFor(stage, code).Where(i => i.Code == code).Sum(i => i.Count);
        return Math.Max(0, need - Fitted(stage, code));
    }

    // The bill a piece of code is measured against: a layer with no material yet takes the piece's.
    private IReadOnlyList<BodyIngredient> BillFor(string stage, string code)
    {
        if (BodyBill.LayerOf(stage) is { } layer && Material(layer) == null
            && EidolonFittings.Identify(code) is { } id && id.Layer == layer)
            return BodyBill.Of(stage, id.Material);
        return Bill(stage);
    }

    /// <summary>What a stage still needs, in the bill's order; empty when it is complete.</summary>
    public IReadOnlyList<BodyIngredient> StillNeeded(string stage) =>
        Bill(stage).Select(i => i with { Count = Math.Max(0, i.Count - (i.AnyMaterial ? 0 : Fitted(stage, i.Code))) })
            .Where(i => i.Count > 0).ToList();

    public bool StageComplete(string stage)
    {
        if (BodyBill.LayerOf(stage) is { } layer && Material(layer) == null)
            return false;
        return Bill(stage).All(i => Fitted(stage, i.Code) >= i.Count);
    }

    /// <summary>The first stage not complete, in order; null when the whole body is in.</summary>
    public string? Next => BodyBill.Order.FirstOrDefault(s => !StageComplete(s));

    /// <summary>Every stage in, the mind too: the crank wakes it.</summary>
    public bool Complete => Next == null;

    /// <summary>Anything fitted at all.</summary>
    public bool Started => _fitted.Values.Any(f => f.Values.Any(n => n > 0));

    /// <summary>The torso is on the spine: what the hung body is drawn round.</summary>
    public bool HasTorso => StageComplete(BodyBill.TorsoStage);

    /// <summary>The layers complete so far, each in its material, as the eidolon will wear them.</summary>
    public EidolonBuild Build
    {
        get
        {
            var b = EidolonBuild.Bare;
            foreach (var layer in EidolonFittings.Layers)
            {
                if (!StageComplete(BodyBill.StageOf(layer)) || b.With(layer, Material(layer)) is not { } next)
                    break;
                b = next;
            }
            return b;
        }
    }

    /// <summary>What an old body had that the new stages do not take, until it is given back
    /// (<see cref="TakeSpare"/>) or the gantry is broken (<see cref="Returns"/>).</summary>
    public IReadOnlyList<GantryDrop> Spare => _spare.Where(kv => kv.Value > 0).Select(kv => new GantryDrop(kv.Key, kv.Value)).ToList();

    /// <summary>The spare items, emptied: the caller gives them back.</summary>
    public IReadOnlyList<GantryDrop> TakeSpare()
    {
        var spare = Spare;
        _spare.Clear();
        return spare;
    }

    /// <summary>Whether fitting <paramref name="code"/> now, <paramref name="held"/> in hand, is allowed:
    /// the stage it goes in and how many it takes (what is held, up to what the stage still needs).</summary>
    public BodyFitVerdict CanFit(string? code, int held, out string stage, out int take)
    {
        stage = "";
        take = 0;
        code = Normalise(code);
        if (code == null || held <= 0)
            return BodyFitVerdict.NotAPart;
        var next = Next;
        var stages = BodyBill.StagesTaking(code).ToList();
        if (stages.Count == 0)
        {
            // a material of the right family but not one the layer allows
            if (next != null && BodyBill.FamilyLayer(code) is { } family && BodyBill.StageOf(family) == next)
            {
                stage = next;
                return BodyFitVerdict.WrongMaterial;
            }
            return BodyFitVerdict.NotAPart;
        }
        if (next != null && stages.Contains(next))
        {
            if (BodyBill.LayerOf(next) is { } layer && EidolonFittings.Identify(code) is { } id && id.Layer == layer
                && Material(layer) is { } fixedMaterial && fixedMaterial != id.Material)
            {
                stage = next;
                return BodyFitVerdict.WrongMaterial;
            }
            if (Missing(next, code) > 0)
            {
                stage = next;
                take = Math.Min(held, Missing(next, code));
                return BodyFitVerdict.Fits;
            }
        }
        int nextIndex = next is { } n ? IndexOf(n) : BodyBill.Order.Count;
        return stages.Any(s => IndexOf(s) > nextIndex) ? BodyFitVerdict.OutOfOrder : BodyFitVerdict.AlreadyFitted;
    }

    private static int IndexOf(string stage) => BodyBill.Order.ToList().IndexOf(stage);

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

    /// <summary>Fills the next stage with everything it still needs (the creative shortcut), a layer in
    /// its fixed material, else one drawn from <paramref name="rng"/>, else the default build's; its name,
    /// or null when the body is complete.</summary>
    public string? FitNextStage(Random? rng = null)
    {
        if (Next is not { } stage)
            return null;
        var bill = BodyBill.LayerOf(stage) is { } layer
            ? BodyBill.Of(stage, Material(layer) ?? (rng == null ? null : EidolonBuild.RandomMaterial(layer, rng)))
            : BodyBill.Of(stage);
        foreach (var i in bill)
            _fitted[stage][i.Code] = Math.Max(Fitted(stage, i.Code), i.Count);
        return stage;
    }

    /// <summary>Every fitted item (and any spare an old body left), merged by code in bill order, as
    /// breaking gives it back.</summary>
    public IReadOnlyList<GantryDrop> Returns()
    {
        var totals = new Dictionary<string, int>();
        var order = new List<string>();
        void Add(string code, int n)
        {
            if (n <= 0)
                return;
            if (!totals.ContainsKey(code))
                order.Add(code);
            totals[code] = totals.GetValueOrDefault(code) + n;
        }
        foreach (var stage in BodyBill.Order)
            foreach (var (code, n) in _fitted[stage])
                Add(code, n);
        foreach (var (code, n) in _spare)
            Add(code, n);
        return order.Select(c => new GantryDrop(c, totals[c])).ToList();
    }

    /// <summary>Empties the body (it woke and left the spine).</summary>
    public void Clear()
    {
        foreach (var f in _fitted.Values)
            f.Clear();
    }

    // ---- A docked eidolon (Eidolon/README.md, "Docking") ----

    /// <summary>The body of a woken eidolon docked on the spine wearing <paramref name="build"/>: the torso
    /// and the mind in, and each layer it wears whole, in its material. The layers it does not wear are
    /// then fitted item by item as in the build (<see cref="Next"/> is the first of them), and
    /// <see cref="Build"/> is what it wears with every layer completed since.</summary>
    public static BodyParts Docked(EidolonBuild build)
    {
        var parts = new BodyParts();
        parts._fitted[BodyBill.TorsoStage][BodyBill.Torso] = 1;
        foreach (var i in BodyBill.Of(BodyBill.Mind))
            parts._fitted[BodyBill.Mind][i.Code] = i.Count;
        foreach (var layer in EidolonFittings.Layers)
            if (build[layer] is { } material)
                foreach (var i in BodyBill.Of(BodyBill.StageOf(layer), material))
                    parts._fitted[BodyBill.StageOf(layer)][i.Code] = i.Count;
        return parts;
    }

    /// <summary>
    /// A docked body as saved, made again from the eidolon it holds, wearing <paramref name="build"/>.
    /// While an eidolon is docked the body is only a saved copy of what it is made of, and a bill changed
    /// since (a mod update raising a layer's count) leaves that copy short: a layer it wears would load
    /// part fitted, the body's <see cref="Build"/> fall short of the eidolon's, and its pieces count as
    /// loose. So the layers it wears (with the torso and the mind) are <see cref="Docked"/>'s, whatever was
    /// saved of them, their pieces the eidolon's and never loose; only what was fitted in the stages after
    /// them (a layer being fitted) and any spare are kept as saved.
    /// </summary>
    public BodyParts Reconciled(EidolonBuild build)
    {
        var parts = Docked(build);
        foreach (var layer in EidolonFittings.Layers.Where(l => build[l] == null))
        {
            string stage = BodyBill.StageOf(layer);
            foreach (var (code, n) in _fitted[stage])
                parts._fitted[stage][code] = n;
        }
        foreach (var (code, n) in _spare)
            parts._spare[code] = n;
        return parts;
    }

    /// <summary>The most a docked eidolon can be wearing, for when it is not loaded to ask: each layer
    /// from the bones out with a material fixed, as far as they run unbroken. A saved count short of a
    /// changed bill still fixes its layer's material, so loose pieces measured against this
    /// (<see cref="Loose"/>) never take in a worn layer's; a layer part fitted is counted in too, and its
    /// pieces wait for the eidolon.</summary>
    public EidolonBuild MostWorn
    {
        get
        {
            var b = EidolonBuild.Bare;
            foreach (var layer in EidolonFittings.Layers)
            {
                if (b.With(layer, Material(layer)) is not { } next)
                    break;
                b = next;
            }
            return b;
        }
    }

    /// <summary>The build to put on a docked eidolon wearing <paramref name="worn"/> once a layer is
    /// completed on the spine: <see cref="Build"/>, when it wears every layer <paramref name="worn"/> does,
    /// in the same material, and more; else null, so a body short of the eidolon never strips it.</summary>
    public EidolonBuild? Dressing(EidolonBuild worn)
    {
        var build = Build;
        bool covers = EidolonFittings.Layers.All(l => worn[l] == null || worn[l] == build[l]);
        return covers && !build.Equals(worn) ? build : null;
    }

    /// <summary>What is fitted beyond what a docked eidolon wearing <paramref name="build"/> is made of
    /// (<see cref="Docked"/>): the pieces of a layer being fitted, and any spare. These are the gantry's
    /// to give back, never the eidolon's.</summary>
    public IReadOnlyList<GantryDrop> Loose(EidolonBuild build)
    {
        var worn = Docked(build);
        var totals = new Dictionary<string, int>();
        var order = new List<string>();
        foreach (var stage in BodyBill.Order)
            foreach (var (code, n) in _fitted[stage])
            {
                int extra = n - worn.Fitted(stage, code);
                if (extra <= 0)
                    continue;
                if (!totals.ContainsKey(code))
                    order.Add(code);
                totals[code] = totals.GetValueOrDefault(code) + extra;
            }
        foreach (var (code, n) in _spare.Where(kv => kv.Value > 0))
        {
            if (!totals.ContainsKey(code))
                order.Add(code);
            totals[code] = totals.GetValueOrDefault(code) + n;
        }
        return order.Select(c => new GantryDrop(c, totals[c])).ToList();
    }

    /// <summary>The loose pieces (<see cref="Loose"/>), taken out: the caller gives them back.</summary>
    public IReadOnlyList<GantryDrop> TakeLoose(EidolonBuild build)
    {
        var loose = Loose(build);
        var worn = Docked(build);
        foreach (var stage in BodyBill.Order)
            foreach (var code in _fitted[stage].Keys.ToList())
            {
                int keep = Math.Min(_fitted[stage][code], worn.Fitted(stage, code));
                if (keep > 0)
                    _fitted[stage][code] = keep;
                else
                    _fitted[stage].Remove(code);
            }
        _spare.Clear();
        return loose;
    }

    /// <summary>Takes <paramref name="layer"/> off: its stage emptied (the caller gives its bill back).</summary>
    public void Strip(FittingLayer layer) => _fitted[BodyBill.StageOf(layer)].Clear();

    /// <summary>Each fitted count by <c>stage/code</c>, and each spare by <c>spare/code</c>, for saving.</summary>
    public IReadOnlyDictionary<string, int> Snapshot() =>
        _fitted.SelectMany(s => s.Value.Where(kv => kv.Value > 0).Select(kv => (Key: s.Key + "/" + kv.Key, kv.Value)))
            .Concat(_spare.Where(kv => kv.Value > 0).Select(kv => (Key: SpareKey + "/" + kv.Key, kv.Value)))
            .ToDictionary(x => x.Key, x => x.Value);

    private const string SpareKey = "spare";

    /// <summary>The limb-by-limb stages a body was built in before fittings (#672); a save with any of
    /// them, or with the old torso's parts, is migrated (<see cref="Migrate"/>).</summary>
    public static readonly IReadOnlySet<string> OldStages = new HashSet<string> { "pelvis", "legs", "arms", "head" };

    /// <summary>Restored state, as <see cref="Snapshot"/> gave it. An unknown stage or a code its stage
    /// does not take is dropped; a count is held to what its stage takes; a layer keeps only pieces of
    /// one material (the first in the save). Counts in a stage after one that is not complete are kept
    /// (they are returned on breaking), though nothing more goes in there until the stages before it are
    /// done. A save from before fittings is migrated (<see cref="Migrate"/>).</summary>
    public static BodyParts Restore(IReadOnlyDictionary<string, int> snapshot)
    {
        if (IsOld(snapshot))
            return Migrate(snapshot);
        var parts = new BodyParts();
        foreach (var (key, n) in snapshot)
        {
            if (!Split(key, out var stage, out var code) || n <= 0)
                continue;
            if (stage == SpareKey)
            {
                parts._spare[code] = parts._spare.GetValueOrDefault(code) + n;
                continue;
            }
            if (!parts._fitted.ContainsKey(stage))
                continue;
            var bill = BodyBill.LayerOf(stage) is { } layer
                ? BodyBill.Of(stage, parts.Material(layer) ?? (EidolonFittings.Identify(code) is { } id && id.Layer == layer ? id.Material : null))
                : BodyBill.Of(stage);
            int max = bill.Where(i => i.Code == code).Sum(i => i.Count);
            if (max > 0)
                parts._fitted[stage][code] = Math.Min(n, max);
        }
        return parts;
    }

    private static bool Split(string key, out string stage, out string code)
    {
        int slash = key.IndexOf('/');
        stage = slash > 0 ? key[..slash] : "";
        code = slash > 0 ? Normalise(key[(slash + 1)..]) ?? "" : "";
        return slash > 0 && code != "";
    }

    private static bool IsOld(IReadOnlyDictionary<string, int> snapshot) =>
        snapshot.Keys.Any(k => Split(k, out var stage, out var code)
                               && (OldStages.Contains(stage) || stage == BodyBill.TorsoStage && code != BodyBill.Torso));

    /// <summary>
    /// A body built limb by limb before fittings (torso, pelvis, legs, arms, head, mind: all steel), as
    /// the new stages (Eidolon/README.md, "Old builds"). Everything fitted is pooled. A complete old torso
    /// becomes the crafted torso, from its recipe's parts (the pump head, the two tanks, 4 metal parts
    /// and 3 stainless gears were all in the old torso); its plates and nails go back in the pool. The
    /// pool then fills the layers in order, each as far as it goes, in the old build's materials: the
    /// Jonas parts their layer's (the bones' frame and vessel, the joints' mechanism, gears and metal
    /// parts), the steel rods and nails and strips the joints', the steel plates the plates'. The old
    /// build had no wood, leather or chain, so the bones, sash and skirt wait for theirs; the joints and
    /// plates keep what they hold behind the bones. What is left (an incomplete old torso's parts, nails
    /// and strips and rods beyond the joints' 20 and 4) is spare, given back.
    /// </summary>
    public static BodyParts Migrate(IReadOnlyDictionary<string, int> snapshot)
    {
        var pool = new Dictionary<string, int>();
        var oldTorso = new Dictionary<string, int>();
        foreach (var (key, n) in snapshot)
        {
            if (!Split(key, out var stage, out var code) || n <= 0)
                continue;
            var into = stage == BodyBill.TorsoStage ? oldTorso : pool;
            into[code] = into.GetValueOrDefault(code) + n;
        }
        var parts = new BodyParts();
        bool torsoWhole = OldTorso.All(i => oldTorso.GetValueOrDefault(i.Code) >= i.Count);
        if (torsoWhole)
        {
            parts._fitted[BodyBill.TorsoStage][BodyBill.Torso] = 1;
            foreach (var i in EidolonFittings.TorsoRecipe)
                oldTorso[i.Code] -= i.Count;
        }
        foreach (var (code, n) in oldTorso)
            pool[code] = pool.GetValueOrDefault(code) + n;
        if (torsoWhole)
            foreach (var layer in EidolonFittings.Layers)
            {
                string stage = BodyBill.StageOf(layer);
                foreach (var i in BodyBill.Of(stage))
                {
                    int n = Math.Min(i.Count, pool.GetValueOrDefault(i.Code));
                    if (n <= 0)
                        continue;
                    parts._fitted[stage][i.Code] = n;
                    pool[i.Code] -= n;
                }
            }
        foreach (var (code, n) in pool)
            if (n > 0)
                parts._spare[code] = n;
        return parts;
    }

    /// <summary>The old torso stage's bill (#672), whose completion makes a migrated body's torso.</summary>
    public static readonly IReadOnlyList<BodyIngredient> OldTorso =
    [
        new("game:jonasparts-tank01", 1), new("game:jonasparts-tank02", 1), new("game:jonasparts-pumphead", 1),
        new("game:metalplate-steel", 4), new("game:metalnailsandstrips-steel", 8), new(EidolonFittings.MetalParts, 4),
        new(EidolonFittings.StainlessGear, 3),
    ];
}
