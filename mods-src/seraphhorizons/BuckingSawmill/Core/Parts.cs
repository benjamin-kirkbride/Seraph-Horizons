namespace SeraphHorizons.Mod.BuckingSawmill.Core;

public enum PartKind { None, Sash, Crankshaft, Levers, BladeKit }

/// <summary>Why a part can or cannot be fitted.</summary>
public enum FitVerdict
{
    Fits,
    NotAPart,
    /// <summary>Both sashes, the crankshaft or the levers are already in.</summary>
    AlreadyFitted,
    /// <summary>The blade kit puts a blade in both saws, so it needs both sashes.</summary>
    NeedsSashes,
    /// <summary>A blade kit is already in.</summary>
    BladeFitted,
}

/// <summary>
/// The mill's assembly rules. Parts are Immersive Woodworking's sawmill items, recognised by code
/// path alone as Immersive Woodworking does: <c>sawmillsash</c> (two), <c>sawmillcrankshaft</c>,
/// <c>sawmilllevers</c> (the linkage that trips the windlass when the saws bottom out) and one
/// <c>sawmillblade-{metal}</c>, a kit that puts a blade in each saw. Any order, apart from the
/// blade kit needing both sashes, one for each of its blades. Immersive Woodworking's carriage
/// drives its log carriage, which this mill does not have, so it is not a part.
/// </summary>
public sealed class Parts
{
    public const int SashesNeeded = 2;

    public const string SashPath = "sawmillsash";
    public const string CrankshaftPath = "sawmillcrankshaft";
    public const string LeversPath = "sawmilllevers";
    public const string BladePrefix = "sawmillblade-";

    public int Sashes { get; private set; }
    public bool Crankshaft { get; private set; }
    public bool Levers { get; private set; }
    /// <summary>The fitted blade kit's metal, or null without one.</summary>
    public string? BladeMetal { get; private set; }
    public bool BladeKit => BladeMetal != null;

    public bool Complete => Sashes == SashesNeeded && Crankshaft && Levers && BladeKit;

    /// <summary>Restored state; a blade kit without both sashes is dropped, as it could not have
    /// been fitted.</summary>
    public Parts(int sashes = 0, bool crankshaft = false, bool levers = false, string? bladeMetal = null)
    {
        Sashes = Math.Clamp(sashes, 0, SashesNeeded);
        Crankshaft = crankshaft;
        Levers = levers;
        BladeMetal = Sashes == SashesNeeded && !string.IsNullOrEmpty(bladeMetal) ? bladeMetal : null;
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

    public FitVerdict CanFit(string? path) => KindOf(path, out _) switch
    {
        PartKind.Sash => Sashes < SashesNeeded ? FitVerdict.Fits : FitVerdict.AlreadyFitted,
        PartKind.Crankshaft => Crankshaft ? FitVerdict.AlreadyFitted : FitVerdict.Fits,
        PartKind.Levers => Levers ? FitVerdict.AlreadyFitted : FitVerdict.Fits,
        PartKind.BladeKit => BladeKit ? FitVerdict.BladeFitted : Sashes < SashesNeeded ? FitVerdict.NeedsSashes : FitVerdict.Fits,
        _ => FitVerdict.NotAPart,
    };

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
            case PartKind.BladeKit: BladeMetal = metal; break;
        }
        return verdict;
    }

    /// <summary>Removes the blade kit; false when there is none.</summary>
    public bool RemoveBladeKit()
    {
        if (!BladeKit)
            return false;
        BladeMetal = null;
        return true;
    }

    /// <summary>The parts still needed, as item code paths (the blade kit as <c>sawmillblade-*</c>),
    /// one entry per missing item, in the order the creative shortcut fits them.</summary>
    public IEnumerable<string> Missing()
    {
        for (int i = Sashes; i < SashesNeeded; i++)
            yield return SashPath;
        if (!Crankshaft)
            yield return CrankshaftPath;
        if (!Levers)
            yield return LeversPath;
        if (!BladeKit)
            yield return BladePrefix + "*";
    }

    /// <summary>The creative shortcut's next stage: the first missing part's code path, a blade kit
    /// of <paramref name="bladeMetal"/>; null when the mill is complete. It always fits: the sashes
    /// come before the kit.</summary>
    public string? NextPart(string bladeMetal) =>
        Missing().FirstOrDefault() is { } path ? path.Replace("*", bladeMetal, StringComparison.Ordinal) : null;
}
