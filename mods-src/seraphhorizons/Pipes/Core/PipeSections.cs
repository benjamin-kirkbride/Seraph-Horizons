namespace SeraphHorizons.Mod.Pipes.Core;

/// <summary>
/// The pipe chain's pieces and figures (README "Unified pipes"), game-independent:
/// <list type="bullet">
/// <item>the <b>angle</b>, <c>seraphhorizons:angle-{copper,lead}</c>: a plate bent once at a right
/// angle, forged on the anvil from one ingot (<c>recipes/smithing/angle.json</c>) or folded on the
/// press brake from one plate;</item>
/// <item>the <b>hollow section</b>, the game's chute section (<see cref="ChuteSections"/>): two
/// angles soldered together on the grid (<c>recipes/grid/chutesection.json</c>);</item>
/// <item>the <b>pipe section</b>, <c>seraphhorizons:pipesection-{copper,lead,iron,steel}</c>: a
/// pipe-sized square tube half a block long, which no grid or anvil recipe makes: the mandrel
/// station and the draw bench make copper and lead ones from hollow sections, and Steelmaking
/// Expanded's canal casts iron and steel ones (<see cref="CastPipeMold"/>);</item>
/// <item>the <b>pipes</b>, Pipes and Power Expanded's, from pipe sections in the game's chute
/// patterns (<see cref="PipeShapes"/>, <c>recipes/grid/unifiedpipes.json</c>): copper and lead
/// soldered (a solder bar a section and the soldering iron), iron and steel banded (one nails and
/// strips of their metal and a hammer).</item>
/// </list>
/// </summary>
public static class PipeSections
{
    public const string Domain = "seraphhorizons";

    /// <summary>The angle's metals: the soldered ones.</summary>
    public static readonly string[] AngleMetals = [PipeRules.Copper, PipeRules.Lead];

    /// <summary>The pipe section's metals, as its item type lists them: every pipe metal.</summary>
    public static readonly string[] Metals = [PipeRules.Copper, PipeRules.Lead, PipeRules.Iron, PipeRules.Steel];

    /// <summary>The metals joined with solder (copper and lead); iron and steel take nails and strips.</summary>
    public static readonly string[] SolderedMetals = [PipeRules.Copper, PipeRules.Lead];

    public static readonly string[] NailedMetals = [PipeRules.Iron, PipeRules.Steel];

    /// <summary>Each pipe shape: the ppex block it makes, as it lists in creative, the pipe sections
    /// it takes (in the game's chute patterns: one alone, an elbow, a T, a cross), and how many it
    /// makes.</summary>
    public static readonly (string Shape, int Sections, int Pipes)[] PipeShapes =
    [
        ("straight-ns", 1, 1),
        ("bend-nw", 2, 1),
        ("tjunction-uns", 3, 1),
        ("xjunction-nswe", 4, 1),
    ];

    /// <summary>Nails and strips in an iron or steel pipe recipe, whatever its shape.</summary>
    public const int NailsAndStrips = 1;

    /// <summary>The chain's figures, per ingot of copper or lead: an angle from an ingot (or a plate,
    /// two ingots, on the press brake); a hollow section from two angles; two pipe sections from a
    /// hollow on the mandrel station, four on the draw bench; and a fill of the pipe mold (one ingot
    /// of iron or steel) casts two.</summary>
    public const int AnglesPerIngot = 1;
    public const int PipeSectionsPerHollowMandrel = 2;
    public const int PipeSectionsPerHollowDrawn = 4;

    public static string Angle(string metal) => $"{Domain}:angle-{metal}";

    public static string PipeSection(string metal) => $"{Domain}:pipesection-{metal}";

    public static string Pipe(string shape, string metal) => $"ppex:pipe-{shape}-{metal}";

    /// <summary>Solder bars for a copper or lead pipe recipe, as for a chute: one per section.</summary>
    public static int SolderBars(string shape) => PipeShapes.Single(p => p.Shape == shape).Sections;

    /// <summary>Hollow sections per ingot of copper or lead: an angle an ingot, two angles a hollow.</summary>
    public static double HollowsPerIngot => (double)AnglesPerIngot / ChuteSections.AnglesPerSection;

    /// <summary>Straight pipes per ingot of copper or lead, by the machine that makes the pipe sections
    /// (a straight pipe is one section).</summary>
    public static double MandrelPipesPerIngot => HollowsPerIngot * PipeSectionsPerHollowMandrel;

    public static double DrawnPipesPerIngot => HollowsPerIngot * PipeSectionsPerHollowDrawn;
}
