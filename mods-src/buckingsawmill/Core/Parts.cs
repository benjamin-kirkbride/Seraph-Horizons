namespace BuckingSawmill.Core;

public enum PartKind { None, Sash, Crankshaft, Levers, BladeKit }

/// <summary>Why a part can or cannot be fitted.</summary>
public enum FitVerdict
{
    Fits,
    NotAPart,
    /// <summary>Both sashes, the crankshaft or the levers are already in.</summary>
    AlreadyFitted,
    /// <summary>A blade kit needs a sash with no blade kit in it.</summary>
    NeedsFreeSash,
    BladesFitted,
    /// <summary>The second blade kit must be of the first one's metal.</summary>
    WrongMetal,
}

/// <summary>
/// The mill's assembly rules. Parts are Immersive Woodworking's sawmill items, recognised by code
/// path alone as Immersive Woodworking does: <c>sawmillsash</c> (two), <c>sawmillcrankshaft</c>,
/// <c>sawmilllevers</c> (the linkage that trips the windlass when the saws bottom out) and
/// <c>sawmillblade-{metal}</c> (two kits of one metal, each in a sash). Any order, apart from a blade
/// kit needing a free sash. Immersive Woodworking's carriage drives its log carriage, which this mill
/// does not have, so it is not a part.
/// </summary>
public sealed class Parts
{
    public const int SashesNeeded = 2;
    public const int BladeKitsNeeded = 2;

    public const string SashPath = "sawmillsash";
    public const string CrankshaftPath = "sawmillcrankshaft";
    public const string LeversPath = "sawmilllevers";
    public const string BladePrefix = "sawmillblade-";

    public int Sashes { get; private set; }
    public bool Crankshaft { get; private set; }
    public bool Levers { get; private set; }
    private readonly List<string> _bladeMetals = [];
    public IReadOnlyList<string> BladeMetals => _bladeMetals;

    public bool Complete => Sashes == SashesNeeded && Crankshaft && Levers && _bladeMetals.Count == BladeKitsNeeded;
    public string? BladeMetal => _bladeMetals.Count > 0 ? _bladeMetals[0] : null;

    public Parts(int sashes = 0, bool crankshaft = false, bool levers = false, IEnumerable<string>? bladeMetals = null)
    {
        Sashes = Math.Clamp(sashes, 0, SashesNeeded);
        Crankshaft = crankshaft;
        Levers = levers;
        foreach (var metal in bladeMetals ?? [])
            if (_bladeMetals.Count < Math.Min(Sashes, BladeKitsNeeded))
                _bladeMetals.Add(metal);
    }

    /// <summary>What an item code path is; <paramref name="metal"/> is set for a blade kit.</summary>
    public static PartKind KindOf(string? path, out string? metal)
    {
        metal = null;
        switch (path)
        {
            case SashPath: return PartKind.Sash;
            case CrankshaftPath: return PartKind.Crankshaft;
            case LeversPath: return PartKind.Levers;
        }
        if (path != null && path.StartsWith(BladePrefix, StringComparison.Ordinal) && path.Length > BladePrefix.Length)
        {
            metal = path[BladePrefix.Length..];
            return PartKind.BladeKit;
        }
        return PartKind.None;
    }

    public FitVerdict CanFit(string? path)
    {
        switch (KindOf(path, out var metal))
        {
            case PartKind.Sash: return Sashes < SashesNeeded ? FitVerdict.Fits : FitVerdict.AlreadyFitted;
            case PartKind.Crankshaft: return Crankshaft ? FitVerdict.AlreadyFitted : FitVerdict.Fits;
            case PartKind.Levers: return Levers ? FitVerdict.AlreadyFitted : FitVerdict.Fits;
            case PartKind.BladeKit:
                if (_bladeMetals.Count >= BladeKitsNeeded)
                    return FitVerdict.BladesFitted;
                if (_bladeMetals.Count >= Sashes)
                    return FitVerdict.NeedsFreeSash;
                if (_bladeMetals.Count > 0 && _bladeMetals[0] != metal)
                    return FitVerdict.WrongMetal;
                return FitVerdict.Fits;
            default: return FitVerdict.NotAPart;
        }
    }

    /// <summary>Fits the part if <see cref="CanFit"/> allows it.</summary>
    public FitVerdict Fit(string? path)
    {
        var verdict = CanFit(path);
        if (verdict != FitVerdict.Fits)
            return verdict;
        switch (KindOf(path, out var metal))
        {
            case PartKind.Sash: Sashes++; break;
            case PartKind.Crankshaft: Crankshaft = true; break;
            case PartKind.Levers: Levers = true; break;
            case PartKind.BladeKit: _bladeMetals.Add(metal!); break;
        }
        return verdict;
    }

    /// <summary>Removes the blade kit at <paramref name="index"/>; false when there is none.</summary>
    public bool RemoveBladeKit(int index)
    {
        if (index < 0 || index >= _bladeMetals.Count)
            return false;
        _bladeMetals.RemoveAt(index);
        return true;
    }

    /// <summary>The parts still needed, as item code paths (a blade kit as <c>sawmillblade-*</c>),
    /// one entry per missing item.</summary>
    public IEnumerable<string> Missing()
    {
        for (int i = Sashes; i < SashesNeeded; i++)
            yield return SashPath;
        if (!Crankshaft)
            yield return CrankshaftPath;
        if (!Levers)
            yield return LeversPath;
        for (int i = _bladeMetals.Count; i < BladeKitsNeeded; i++)
            yield return BladePrefix + "*";
    }
}
