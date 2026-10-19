namespace SeraphHorizons.Mod.Machines.Core;

/// <summary>
/// A wearing part of a machine (#711): not a build stage, but a set of items fitted with the built
/// machine and worn by use, renewed by right-click (the stamp battery's five shoes, a set of five
/// <c>game:poundercap-steel</c>). <see cref="Codes"/> are the items it may be (patterns as
/// <see cref="CodePattern"/>), <see cref="Count"/> how many make a set, <see cref="Uses"/> the work a
/// set lasts (in the machine's own unit: rocks crushed, hours run), and <see cref="Requires"/> the rig
/// part drawn while one is fitted, or null. A <see cref="Durable"/> part is one item whose durability is
/// its uses (the draw bench's die): it goes in with the durability it has left, can be taken back out
/// with what is left of it (<see cref="WearingParts.Take"/>), and breaking the machine gives it back so.
/// <see cref="UsesOf"/>, where the machine gives it, is the uses of a set by the item fitted, in place of
/// <see cref="Uses"/> (a figure per metal: the bucking mill's blade kits, each its own durability).
/// </summary>
public sealed record WearingPart(string Id, IReadOnlyList<string> Codes, int Count, double Uses, string? Requires = null, bool Durable = false,
    Func<string, double>? UsesOf = null)
{
    public bool Matches(string code) => Codes.Any(p => CodePattern.Matches(p, code));

    /// <summary>The uses a new set of <paramref name="code"/> lasts: <see cref="UsesOf"/>'s figure where it gives a
    /// positive one, else <see cref="Uses"/>.</summary>
    public double UsesFor(string? code) =>
        UsesOf != null && code != null && UsesOf(code) is var u && u > 0 && double.IsFinite(u) ? u : Uses;
}

/// <summary>Why a held stack does or does not renew a wearing part.</summary>
public enum RenewVerdict
{
    Renews,
    /// <summary>No wearing part takes it.</summary>
    NotAPart,
    /// <summary>The machine's build is not complete: wearing parts go in after the last stage.</summary>
    NotBuilt,
    /// <summary>A set is fitted with use left; it is renewed only once worn out.</summary>
    NotWorn,
    /// <summary>Fewer are held than a set.</summary>
    TooFew,
    /// <summary>A durable part's item with no durability left.</summary>
    Spent,
}

/// <summary>
/// The wearing parts of one machine and how much of each is left. A machine with a wearing part
/// missing or worn out does not run (<see cref="AllFitted"/>). Wear is counted in the part's own uses;
/// a set worn to nothing is gone (nothing comes back); a set with use left comes back by its code when
/// the machine is broken.
/// </summary>
public sealed class WearingParts
{
    private readonly Dictionary<string, (string Code, double Left)> _fitted = new(StringComparer.Ordinal);

    public WearingParts(IReadOnlyList<WearingPart> parts) => Parts = parts;

    public IReadOnlyList<WearingPart> Parts { get; }

    /// <summary>The fitted set's code and the uses it has left; null when none is fitted.</summary>
    public (string Code, double Left)? Fitted(string id) => _fitted.TryGetValue(id, out var f) ? f : null;

    /// <summary>Whether every wearing part has a set with use left.</summary>
    public bool AllFitted => Parts.All(p => _fitted.TryGetValue(p.Id, out var f) && f.Left > 0);

    /// <summary>Whether a rig part needing <paramref name="requires"/> is drawn: a wearing part's while
    /// a set is fitted; true for any other <c>requires</c>.</summary>
    public bool Shows(string? requires)
    {
        var part = Parts.FirstOrDefault(p => p.Requires != null && p.Requires == requires);
        return part == null || _fitted.ContainsKey(part.Id);
    }

    /// <summary>The share of use left on <paramref name="id"/>'s set, 0 to 1; 0 when none is fitted.</summary>
    public double ShareLeft(string id)
    {
        var part = Parts.FirstOrDefault(p => p.Id == id);
        return part == null || !_fitted.TryGetValue(id, out var f) ? 0 : Math.Clamp(f.Left / part.UsesFor(f.Code), 0, 1);
    }

    /// <summary>What a click with <paramref name="held"/> of <paramref name="code"/> does, on a machine
    /// whose build is <paramref name="built"/>; <paramref name="part"/> the part it renews.
    /// <paramref name="uses"/> is a durable part's item's durability left (null: a new set).</summary>
    public RenewVerdict CanRenew(string? code, int held, bool built, out WearingPart? part, double? uses = null)
    {
        code = CodePattern.Normalise(code);
        part = code == null ? null : Parts.FirstOrDefault(p => p.Matches(code));
        if (part == null)
            return RenewVerdict.NotAPart;
        if (!built)
            return RenewVerdict.NotBuilt;
        if (_fitted.TryGetValue(part.Id, out var f) && f.Left > 0)
            return RenewVerdict.NotWorn;
        if (held < part.Count)
            return RenewVerdict.TooFew;
        return part.Durable && uses is <= 0 ? RenewVerdict.Spent : RenewVerdict.Renews;
    }

    /// <summary>Fits a new set if <see cref="CanRenew"/> allows; the caller takes the part's
    /// <see cref="WearingPart.Count"/> from the slot. A durable part goes in with <paramref name="uses"/>
    /// left (never past its <see cref="WearingPart.UsesFor"/>), a new one's when not given.</summary>
    public RenewVerdict Renew(string? code, int held, bool built, double? uses = null)
    {
        var verdict = CanRenew(code, held, built, out var part, uses);
        if (verdict == RenewVerdict.Renews)
        {
            string fitted = CodePattern.Normalise(code)!;
            double max = part!.UsesFor(fitted);
            _fitted[part.Id] = (fitted, part.Durable && uses is { } u ? Math.Min(u, max) : max);
        }
        return verdict;
    }

    /// <summary>Takes <paramref name="id"/>'s set back out (the draw bench's die, by Ctrl + right-click): its
    /// code and the uses it had left, or null when none is fitted. The part is then missing.</summary>
    public (string Code, double Left)? Take(string id)
    {
        if (!_fitted.Remove(id, out var f))
            return null;
        return f;
    }

    /// <summary>The creative shortcut: a new set of every part, of its first code.</summary>
    public void FillAll(string wildcard = "steel")
    {
        foreach (var p in Parts)
        {
            string code = CodePattern.Normalise(p.Codes[0])!.Replace("*", wildcard);
            _fitted[p.Id] = (code, p.UsesFor(code));
        }
    }

    /// <summary>Wears <paramref name="id"/>'s set by <paramref name="uses"/>. Returns whether this
    /// wore it out (it is then gone).</summary>
    public bool Wear(string id, double uses)
    {
        if (uses <= 0 || !_fitted.TryGetValue(id, out var f))
            return false;
        double left = f.Left - uses;
        if (left > 1e-9)
        {
            _fitted[id] = (f.Code, left);
            return false;
        }
        _fitted.Remove(id);
        return true;
    }

    /// <summary>Wears every part by <paramref name="uses"/>; the ids worn out by it.</summary>
    public IReadOnlyList<string> WearAll(double uses) => Parts.Where(p => Wear(p.Id, uses)).Select(p => p.Id).ToList();

    /// <summary>Every set with use left, as breaking the machine gives it back: its code and count, and a
    /// durable part's uses left.</summary>
    public IReadOnlyList<StageDrop> Returns() =>
        Parts.Where(p => _fitted.ContainsKey(p.Id))
            .Select(p => new StageDrop(_fitted[p.Id].Code, p.Count, p.Durable ? _fitted[p.Id].Left : null)).ToList();

    /// <summary>Each fitted set as <c>id</c> → (code, uses left), for saving.</summary>
    public IReadOnlyDictionary<string, (string Code, double Left)> Snapshot() => new Dictionary<string, (string, double)>(_fitted);

    /// <summary>Restored state: a set whose part is unknown or whose code is not its part's is dropped;
    /// its uses left are clamped to the part's.</summary>
    public static WearingParts Restore(IReadOnlyList<WearingPart> parts, IReadOnlyDictionary<string, (string Code, double Left)> snapshot)
    {
        var wearing = new WearingParts(parts);
        foreach (var p in parts)
            if (snapshot.TryGetValue(p.Id, out var f) && p.Matches(f.Code) && double.IsFinite(f.Left) && f.Left > 0)
                wearing._fitted[p.Id] = (f.Code, Math.Min(f.Left, p.UsesFor(f.Code)));
        return wearing;
    }
}
