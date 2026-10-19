using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.CylinderBorer.Core;

/// <summary>What Ctrl + right-click takes back, in order: the cutter head, then the casting in the
/// cradle; the stages only come back by breaking the frame.</summary>
public enum CylinderBorerTakeBack { Nothing, Head, Blank }

/// <summary>The cylinder borer's <c>requires</c> vocabulary: the four stages (config/builds/cylinderborer.json,
/// <see cref="Stages"/>), and the work's (<c>blankiron</c>, <c>blanksteel</c>: a casting of that metal in the cradle).</summary>
public static class CylinderBorerRequires
{
    public const string BlankIron = "blankiron";
    public const string BlankSteel = "blanksteel";

    /// <summary>The build's stages in their only order (#639: frame, then bushings, bar, feed and cutter head),
    /// each its own <c>requires</c>.</summary>
    public static readonly IReadOnlyList<string> Stages = ["bushings", "bar", "feed", "cutterhead"];

    /// <summary>The blank <c>requires</c> of class <paramref name="k"/>: 1 iron, 2 steel.</summary>
    public static string? Blank(int k) => k switch { 1 => BlankIron, 2 => BlankSteel, _ => null };

    /// <summary>Every <c>requires</c> value the rig may use.</summary>
    public static readonly IReadOnlySet<string> KnownRequires = Stages.Concat([BlankIron, BlankSteel]).ToHashSet();
}

/// <summary>
/// The cylinder borer's build (#639) as the shared staged build holds it (config/builds/cylinderborer.json,
/// <see cref="MachineAssembly"/>): four stages after the frame in one order, the bushings one lot of the game's
/// metal parts and the bar and the feed their materials a click, and the cutter head (<see cref="HeadPart"/>, the
/// one forged item) its durable wearing part fitted as the last stage. What the machine adds to the shared rules:
/// what Ctrl + right-click takes back.
/// </summary>
public static class CylinderBorerBuild
{
    /// <summary>The bar's two bushings: one lot of the game's metal parts (a block, salvaged or bought
    /// from the mechanic), as the press brake's screws. They are drawn in cupronickel.</summary>
    public const string BushingsCode = "game:metal-parts";

    public const string HeadCode = "seraphhorizons:cylinderborerhead-steel";

    /// <summary>The cutter head's wearing part id.</summary>
    public const string HeadPart = "head";

    /// <summary>Whether the cutter head is in with durability left.</summary>
    public static bool HeadIn(MachineAssembly assembly) => assembly.Wearing.Fitted(HeadPart) is { Left: > 0 };

    /// <summary>What Ctrl + right-click takes now: the cutter head, else the casting in the cradle
    /// (<paramref name="blankOn"/>).</summary>
    public static CylinderBorerTakeBack TakeBack(MachineAssembly assembly, bool blankOn) =>
        HeadIn(assembly) ? CylinderBorerTakeBack.Head
        : blankOn ? CylinderBorerTakeBack.Blank
        : CylinderBorerTakeBack.Nothing;
}
