namespace SeraphHorizons.Mod.Pipes.Core;

/// <summary>
/// The unified pipes' rules, game-independent: which metals the pipes come in, when a lead pipe
/// bursts, and which recipes Pipes and Power Expanded's recipe cost catalogue may rewrite.
/// </summary>
public static class PipeRules
{
    public const string PpexDomain = "ppex";

    public const string Lead = "lead";
    public const string Copper = "copper";
    public const string Iron = "iron";
    public const string Steel = "steel";

    /// <summary>The metals ppex's pipe blocks (straight, bend, T- and X-junction) ship with.</summary>
    public static readonly string[] PpexMaterials = [Iron, Steel];

    /// <summary>The metals this adds to them.</summary>
    public static readonly string[] AddedPipeMaterials = [Copper, Lead];

    /// <summary>Every pipe metal, in the order the handbook lists them.</summary>
    public static readonly string[] PipeMaterials = [Lead, Copper, Iron, Steel];

    /// <summary>The valves' and pressure valves' metals: the game's three bronzes.</summary>
    public static readonly string[] Bronzes = ["tinbronze", "bismuthbronze", "blackbronze"];

    /// <summary>The media a lead pipe bursts on: steam and exhaust (exlib's gas names). Air (blast
    /// air) and water are safe in lead.</summary>
    public static readonly string[] LeadBurstMedia = ["Steam", "Exhaust"];

    public static bool IsBronze(string? material) => material != null && Bronzes.Contains(material);

    /// <summary>Whether a run bursts its lead pipes: it holds a gas (not a liquid), there is some,
    /// and it is steam or exhaust. A mixed gas is named by its strongest part, so steam with air in
    /// it counts as steam.</summary>
    public static bool LeadBursts(bool isLiquid, float volume, string? medium) =>
        !isLiquid && volume > 0 && medium != null && LeadBurstMedia.Contains(medium);

    /// <summary>Whether exlib's recipe cost catalogue may rewrite a grid recipe: for an output of
    /// ppex's, only ppex's own recipes (the recipe's name carries the domain of the file it came
    /// from), so this mod's pipe recipes keep their quantities whatever level ppex's costs are
    /// set to. Outputs of other mods are left as exlib finds them.</summary>
    public static bool KeepsCostRecipe(string? outputDomain, string? recipeDomain) =>
        outputDomain != PpexDomain || recipeDomain == PpexDomain;
}
