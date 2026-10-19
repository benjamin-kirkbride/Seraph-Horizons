using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.BuckingSawmill.Core;

/// <summary>The mill's <c>requires</c> vocabulary: the rig parts its build and blade kit draw
/// (<c>config/builds/buckingmill.json</c>: each part as it goes in, the blade kit while one is fitted).</summary>
public static class MillRequires
{
    /// <summary>The <c>requires</c> values the gameplay knows.</summary>
    public static readonly IReadOnlySet<string> KnownRequires = new HashSet<string> { "crankshaft", "levers", "sash1", "sash2", "blade" };
}

/// <summary>Why a blade kit can or cannot be fitted.</summary>
public enum BladeVerdict
{
    Fits,
    NotABlade,
    /// <summary>The kit puts a blade in both saws, so it needs both sashes.</summary>
    NeedsSashes,
    /// <summary>A blade kit is already in.</summary>
    BladeFitted,
    /// <summary>A kit with no durability left.</summary>
    Spent,
}

/// <summary>Immersive Woodworking's sawmill blade kits, <c>immersivewoodworking:sawmillblade-{metal}</c>: the
/// mill's wearing part (<c>blade</c> in its build file), recognised by code as Immersive Woodworking does. One kit
/// of any metal, fitted once both sashes are in, before or after the crankshaft and levers: so with no stage of
/// the build, which would hold it until the last part is in.</summary>
public static class BladeKits
{
    public const string Prefix = "immersivewoodworking:sawmillblade-";

    /// <summary>The wearing part's id in the mill's build file.</summary>
    public const string Part = "blade";

    /// <summary>A blade kit's metal, from its full code; null when the code is not a blade kit's.</summary>
    public static string? MetalOf(string? code) =>
        code != null && code.StartsWith(Prefix, StringComparison.Ordinal) && code.Length > Prefix.Length ? code[Prefix.Length..] : null;

    /// <summary>Whether a kit of <paramref name="code"/> can go into <paramref name="assembly"/> now.</summary>
    public static BladeVerdict CanFit(MachineAssembly assembly, string? code) =>
        MetalOf(code) == null ? BladeVerdict.NotABlade
        : assembly.Wearing.Fitted(Part) != null ? BladeVerdict.BladeFitted
        : !(assembly.Build.Fitted("sash1") && assembly.Build.Fitted("sash2")) ? BladeVerdict.NeedsSashes
        : BladeVerdict.Fits;

    /// <summary>Fits a kit of <paramref name="code"/> with <paramref name="uses"/> durability left (a new one's when
    /// null) if <see cref="CanFit"/> allows.</summary>
    public static BladeVerdict Fit(MachineAssembly assembly, string code, double? uses)
    {
        var verdict = CanFit(assembly, code);
        if (verdict != BladeVerdict.Fits)
            return verdict;
        return assembly.Wearing.Renew(code, 1, built: true, uses) == RenewVerdict.Renews ? BladeVerdict.Fits : BladeVerdict.Spent;
    }
}
