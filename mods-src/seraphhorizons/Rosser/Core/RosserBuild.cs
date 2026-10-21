using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.Rosser.Core;

/// <summary>Why scraper heads can or cannot be fitted.</summary>
public enum HeadsVerdict
{
    Fits,
    NotHeads,
    /// <summary>The heads go on the ring, which is not complete.</summary>
    NeedsRing,
    /// <summary>Heads are already on.</summary>
    AlreadyFitted,
    /// <summary>They go on as a set of four of one metal from one stack; fewer are held.</summary>
    NeedsFullSet,
}

/// <summary>The rosser's <c>requires</c> vocabulary: the rig parts its build and its heads draw.</summary>
public static class RosserRequires
{
    /// <summary>The drip's pipes in one metal (<c>pipecopper</c>, <c>pipelead</c>): the rig has a
    /// part per metal, and only the fitted metal's is drawn.</summary>
    public static string Pipe(string metal) => "pipe" + metal;

    /// <summary>The <c>requires</c> values the rig may use: each of the build's parts', the pipes' per metal,
    /// and the heads'.</summary>
    public static readonly IReadOnlySet<string> KnownRequires =
        new[] { "shaft", "ring", "tyres", "rollsin", "rollsout", "breaker", "levers", RosserBuild.HeadsPart }
            .Concat(RosserBuild.PipeMetals.Select(Pipe)).ToHashSet();
}

/// <summary>
/// The rosser's parts (design §5), all existing items, recognised by full code (<c>domain:path</c>). Its build
/// is <c>config/builds/rosser.json</c> (the shared machine base's): one stage, its parts in any order, each drawn
/// as it is all in:
/// <list type="table">
/// <item><c>shaft</c>: 1 <c>immersivewoodworking:sawmillcrankshaft</c></item>
/// <item><c>ring</c>: 4 <c>game:largegearsection-wood</c></item>
/// <item><c>tyres</c>: 2 <c>game:hoop-{metal}</c>, after the ring</item>
/// <item><c>rollsin</c>, <c>rollsout</c>: 4 <c>game:rod-{metal}</c>, one part, the infeed's drawn once two are in
/// (<see cref="Shows"/>)</item>
/// <item><c>breaker</c>: 2 <c>game:metalplate-{metal}</c></item>
/// <item><c>levers</c>: 1 <c>immersivewoodworking:sawmilllevers</c></item>
/// <item><c>pipes</c>: 4 <c>ppex:pipe-straight-*-{copper,lead}</c> of one metal from one stack: the
/// drip's water line, drawn in that metal (<c>pipecopper</c>, <c>pipelead</c>); only while those
/// pipes exist (<c>ifExists</c>)</item>
/// </list>
/// A click takes from the held stack as many as the part still needs (the pipes all four at once). Hoops, rods and
/// plates must be iron work (<see cref="IronMetals"/>) unless any metal is allowed (<see cref="AnyMetal"/>). The
/// wearing part is the scraper heads, <c>heads</c>: 4 <c>immersivewoodworking:barkspudhead-{metal}</c> of one
/// metal from one stack, after the ring, any metal, lasting <see cref="HeadCapacity"/> wear points.
/// </summary>
public static class RosserBuild
{
    public const string ShaftCode = "immersivewoodworking:sawmillcrankshaft";
    public const string RingCode = "game:largegearsection-wood";
    public const string LeversCode = "immersivewoodworking:sawmilllevers";
    public const string HoopPrefix = "game:hoop-";
    public const string RodPrefix = "game:rod-";
    public const string PlatePrefix = "game:metalplate-";
    public const string HeadPrefix = "immersivewoodworking:barkspudhead-";
    /// <summary>Pipes and Power Expanded's straight pipe, <c>ppex:pipe-straight-{orientation}-{metal}</c>.</summary>
    public const string PipePrefix = "ppex:pipe-straight-";

    /// <summary>The wearing part's id in the build file: the scraper heads.</summary>
    public const string HeadsPart = "heads";

    /// <summary>How many heads go on, as a set.</summary>
    public const int HeadsNeeded = 4;

    /// <summary>The metals the drip's pipes may be: UnifiedPipes' soldered pipes (its
    /// <c>PipeSections.SolderedMetals</c>), copper first (the creative shortcut's).</summary>
    public static readonly IReadOnlyList<string> PipeMetals = ["copper", "lead"];

    /// <summary>A straight pipe's orientations; the item form is <c>ns</c>.</summary>
    private static readonly string[] PipeOrientations = ["ns", "we", "ud"];

    /// <summary>The straight pipe of <paramref name="metal"/> as the item it is: its <c>ns</c> block.</summary>
    public static string PipeCode(string metal) => PipePrefix + "ns-" + metal;

    /// <summary>The metals hoops, rods and plates must be of while the woodworking machines are
    /// iron work (<c>WoodworkingMachineCosts.Metals</c>; keep the two the same).</summary>
    public static readonly IReadOnlySet<string> IronMetals = new HashSet<string> { "iron", "meteoriciron", "steel" };

    /// <summary>The <c>requires</c> drawn by the iron work's parts: the tyres, the rolls and the limb breaker.</summary>
    private static readonly IReadOnlySet<string> IronWork = new HashSet<string> { "tyres", "rollsin", "rollsout", "breaker" };

    /// <summary>The metal of a single-metal part's code after <paramref name="prefix"/>, or null.</summary>
    private static string? MetalAfter(string? code, string prefix) =>
        code != null && code.StartsWith(prefix, StringComparison.Ordinal) && code.Length > prefix.Length && !code[prefix.Length..].Contains('-')
            ? code[prefix.Length..]
            : null;

    /// <summary>A scraper head's metal, or null when <paramref name="code"/> is not one.</summary>
    public static string? HeadMetalOf(string? code) => MetalAfter(code, HeadPrefix);

    /// <summary>Whether <paramref name="code"/> is a hoop, rod or metal plate, of any metal: the iron work.</summary>
    public static bool IsIronWork(string? code) =>
        MetalAfter(code, HoopPrefix) != null || MetalAfter(code, RodPrefix) != null || MetalAfter(code, PlatePrefix) != null;

    /// <summary>Whether <paramref name="code"/> is a hoop, the tyres' part (they go on the ring).</summary>
    public static bool IsHoop(string? code) => MetalAfter(code, HoopPrefix) != null;

    /// <summary>A straight pipe's metal (any: only copper and lead are parts), or null when <paramref name="code"/>
    /// is not a straight pipe.</summary>
    public static string? PipeMetalOf(string? code) =>
        code != null && code.StartsWith(PipePrefix, StringComparison.Ordinal)
        && code[PipePrefix.Length..].Split('-') is [var orientation, { Length: > 0 } metal] && PipeOrientations.Contains(orientation)
            ? metal
            : null;

    /// <summary>The build with its hoops, rods and plates of any metal (with <c>IronWoodworkingMachines</c> off),
    /// each material's first code still first (the creative shortcut's).</summary>
    public static MachineBuild AnyMetal(MachineBuild build) => build with
    {
        Stages = build.Stages.Select(s => s with
        {
            Materials = s.Materials.Select(m => m.Draws?.Any(IronWork.Contains) == true && m.Codes.Count > 0
                ? m with { Codes = [m.Codes[0], m.Codes[0][..(m.Codes[0].LastIndexOf('-') + 1)] + "*"] }
                : m).ToList(),
        }).ToList(),
    };

    /// <summary>The rods the infeed rolls take: the first two of the four.</summary>
    public const int InfeedRods = 2;

    /// <summary>Whether a rig part needing <paramref name="requires"/> is drawn: the drip's pipes in the metal
    /// fitted only (none while the rosser needs none), the infeed rolls once two of the four rods are in, anything
    /// else as the build and the heads say (<see cref="MachineAssembly.Shows"/>).</summary>
    public static bool Shows(MachineAssembly assembly, string? requires)
    {
        if (PipeMetals.FirstOrDefault(m => RosserRequires.Pipe(m) == requires) is { } metal)
            return PipeMetal(assembly) == metal;
        if (requires == "rollsin" && Rods(assembly) is var (stage, material))
            return assembly.Build.Count(stage, material) >= InfeedRods;
        return assembly.Shows(requires);
    }

    /// <summary>The drip's pipes' metal, copper or lead, once all four are in; null before, or while the rosser
    /// needs none.</summary>
    public static string? PipeMetal(MachineAssembly assembly)
    {
        var build = assembly.Build;
        for (int s = 0; s < build.Stages.Count; s++)
            for (int m = 0; m < build.Stages[s].Materials.Count; m++)
                if (build.Stages[s].Materials[m].Picks != null && build.Missing(s, m) == 0)
                    return PipeMetalOf(build.CodeIn(s, m));
        return null;
    }

    /// <summary>The rods' stage and material in the build, or null.</summary>
    private static (int Stage, int Material)? Rods(MachineAssembly assembly)
    {
        var stages = assembly.Build.Stages;
        for (int s = 0; s < stages.Count; s++)
            for (int m = 0; m < stages[s].Materials.Count; m++)
                if (stages[s].Materials[m].Draws?.Contains("rollsin") == true)
                    return (s, m);
        return null;
    }

    /// <summary>Whether the four ring sections are in <paramref name="assembly"/>: the tyres and the heads go on it.</summary>
    public static bool RingIn(MachineAssembly assembly) => assembly.Build.Fitted("ring");

    /// <summary>Whether <paramref name="held"/> scraper heads of <paramref name="code"/> can go on now: on the ring,
    /// four of one metal from one stack, while none are on (spent heads are gone; worn ones stay until spent).</summary>
    public static HeadsVerdict CanFitHeads(MachineAssembly assembly, string? code, int held) =>
        HeadMetalOf(code) == null ? HeadsVerdict.NotHeads
        : !RingIn(assembly) ? HeadsVerdict.NeedsRing
        : assembly.Wearing.Fitted(HeadsPart) != null ? HeadsVerdict.AlreadyFitted
        : held < HeadsNeeded ? HeadsVerdict.NeedsFullSet
        : HeadsVerdict.Fits;

    /// <summary>Fits a set of heads if <see cref="CanFitHeads"/> allows: they last their metal's uses
    /// (<see cref="WearingPart.UsesFor"/>, the build's <see cref="HeadCapacity"/>).</summary>
    public static HeadsVerdict FitHeads(MachineAssembly assembly, string code, int held)
    {
        var verdict = CanFitHeads(assembly, code, held);
        if (verdict == HeadsVerdict.Fits)
            assembly.Wearing.Renew(code, held, built: true);
        return verdict;
    }

    /// <summary>Whether the heads in <paramref name="assembly"/> are on and have never been used: they can come back
    /// out, and come back with the frame; worn heads stay until spent.</summary>
    public static bool HeadsUnworn(MachineAssembly assembly) =>
        assembly.Wearing.Fitted(HeadsPart) is { } heads
        && assembly.Wearing.Parts.FirstOrDefault(p => p.Id == HeadsPart) is { } part
        && heads.Left >= part.UsesFor(heads.Code) - 1e-9;

    /// <summary>What breaking <paramref name="assembly"/> gives back: every part put in, the heads only while unworn.</summary>
    public static IReadOnlyList<StageDrop> Returns(MachineAssembly assembly) =>
        assembly.Returns().Where(d => HeadMetalOf(d.Code) == null || HeadsUnworn(assembly)).ToList();

    /// <summary>The heads' capacity: <paramref name="multiple"/> × the durability of the bark spud
    /// of their metal, at least 1.</summary>
    public static int HeadCapacity(int spudDurability, float multiple) =>
        Math.Max(1, (int)Math.Round(Math.Max(0, spudDurability) * (double)multiple));

    /// <summary>Wear points a debarked trunk costs: ceil(storedLogs × perStoredLog).</summary>
    public static int WearFor(int storedLogs, float perStoredLog) =>
        Math.Max(0, (int)Math.Ceiling(Math.Max(0, storedLogs) * (double)perStoredLog - 1e-6));
}
