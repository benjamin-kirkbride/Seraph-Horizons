using System.Text.Json;
using System.Text.RegularExpressions;

namespace SeraphHorizons.Mod.Pipes.Core;

/// <summary>
/// Cast pipes (<c>CastPipes</c>, README "Cast pipes"): the figures of the pipe mold and the check
/// that Steelmaking Expanded's tool mold blocktypes are still what
/// <c>patches/castpipes-smexmold.json</c> assumes. Game-independent: the mod's
/// <c>Pipes/Game/CastPipesSystem.cs</c> hands it the asset text, the unit tests a fixture.
///
/// The chain's cast stage: one fill of the mold is one ingot of iron or steel (100 units), and it
/// casts two pipe sections of that metal (<c>seraphhorizons:pipesection-{iron|steel}</c>, an item
/// <c>UnifiedPipes</c> adds), each of which the grid bands into a straight pipe with nails and
/// strips. So an ingot of iron or steel casts two pipes, as many as the draw bench makes from an
/// ingot of copper or lead, and twice the mandrel station's.
/// </summary>
public static class CastPipeMold
{
    /// <summary>The new state of smex's <c>tooltype</c> variant: <c>smex:toolmold-{color}-{raw|fired}-pipe</c>.</summary>
    public const string ToolType = "pipe";

    /// <summary>The variant group the patch appends to (by index, <c>/variantgroups/2/states/-</c>).</summary>
    public const string ToolTypeGroup = "tooltype";
    public const int ToolTypeGroupIndex = 2;

    /// <summary>Units of molten metal in an ingot, the game's and smex's (<c>MoldDefaultUnits</c>).</summary>
    public const int IngotUnits = 100;

    /// <summary>What one fill of the mold takes: one ingot.</summary>
    public const int RequiredUnits = 100;

    /// <summary>What one fill casts: pipe sections.</summary>
    public const int SectionsPerFill = 2;

    /// <summary>The metals the mold is for: the two the canal carries (blast furnace iron, Bessemer
    /// steel). The game's mold takes any metal whose drop resolves, so a crucible poured by hand
    /// also casts copper and lead pipe sections (they exist), and refuses every metal without one.</summary>
    public static readonly string[] Metals = [PipeRules.Iron, PipeRules.Steel];

    public const string Domain = "seraphhorizons";
    public const string SectionCode = "seraphhorizons:pipesection";
    public const string SmexId = "smex";

    /// <summary>The ppex straight pipe the grid makes, as it lists in creative.</summary>
    public static string StraightPipe(string metal) => $"ppex:pipe-straight-ns-{metal}";

    /// <summary>The pipe section the mold casts.</summary>
    public static string Section(string metal) => $"{SectionCode}-{metal}";

    /// <summary>The mold's block code in smex's domain.</summary>
    public static string Mold(string color, string materialType) => $"{SmexId}:toolmold-{color}-{materialType}-{ToolType}";

    /// <summary>Sections per ingot of metal: <see cref="IngotUnits"/> / <see cref="RequiredUnits"/>
    /// fills, each <see cref="SectionsPerFill"/> sections.</summary>
    public static double SectionsPerIngot(int requiredUnits = RequiredUnits, int sectionsPerFill = SectionsPerFill) =>
        requiredUnits <= 0 ? 0 : (double)IngotUnits / requiredUnits * sectionsPerFill;

    /// <summary>Straight pipes per ingot: each section bands into one straight pipe.</summary>
    public static double PipesPerIngot(int requiredUnits = RequiredUnits, int sectionsPerFill = SectionsPerFill) =>
        SectionsPerIngot(requiredUnits, sectionsPerFill) / PipeSections.PipeShapes.Single(p => p.Shape == "straight-ns").Sections;

    private static readonly JsonDocumentOptions Lenient = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>What the patch expects of smex's fired mold (<c>blocktypes/molds/toolmoldfired.json</c>),
    /// or null when it holds: a <c>BlockToolMold</c> coded <c>toolmold</c> (so the game's mold code,
    /// the crucible and smex's canal pedestal take it); its third variant group <c>tooltype</c>,
    /// without <c>pipe</c>; a materialtype group with <c>fired</c>; its shape by type and no plain
    /// shape; and an attributes by type table none of whose keys would answer for the pipe mold
    /// before the patch's own (the game takes the first matching key).</summary>
    public static string? CheckFired(string json)
    {
        using var doc = JsonDocument.Parse(json, Lenient);
        var root = doc.RootElement;
        if (Str(root, "code") != "toolmold" || Str(root, "class") != "BlockToolMold")
            return "its fired mold's code or class (not toolmold, BlockToolMold)";
        if (CheckGroups(root, "fired") is { } groups)
            return "its fired mold's " + groups;
        if (CheckShape(root) is { } shape)
            return "its fired mold's " + shape;
        if (Obj(root, "attributesByType") is not { } byType)
            return "its fired mold's attributes by type";
        string pipe = $"toolmold-blue-fired-{ToolType}";
        if (byType.EnumerateObject().Any(p => Matches(p.Name, pipe)))
            return "its fired mold's attributes by type (a key already answers for the pipe mold)";
        return null;
    }

    /// <summary>What the patch expects of smex's raw mold (<c>blocktypes/molds/toolmoldraw.json</c>),
    /// or null when it holds: coded <c>toolmold</c>; the same variant groups with <c>raw</c>; its
    /// shape by type and no plain shape; and the pit kiln's and beehive kiln's fired stacks keyed by
    /// colour, with <c>{tooltype}</c> in their codes, so a raw pipe mold fires to a fired one.</summary>
    public static string? CheckRaw(string json)
    {
        using var doc = JsonDocument.Parse(json, Lenient);
        var root = doc.RootElement;
        if (Str(root, "code") != "toolmold")
            return "its raw mold's code (not toolmold)";
        if (CheckGroups(root, "raw") is { } groups)
            return "its raw mold's " + groups;
        if (CheckShape(root) is { } shape)
            return "its raw mold's " + shape;
        if (Obj(root, "combustiblePropsByType") is not { } combustible)
            return "its raw mold's firing (no combustible props by type)";
        foreach (string color in Colors(root))
        {
            string pipe = $"toolmold-{color}-raw-{ToolType}";
            var props = combustible.EnumerateObject().FirstOrDefault(p => Matches(p.Name, pipe));
            if (props.Value.ValueKind != JsonValueKind.Object
                || Obj(props.Value, "smeltedStack") is not { } stack
                || Str(stack, "code") is not { } code || !code.Contains("{tooltype}", StringComparison.Ordinal))
                return $"its raw mold's firing (the {color} clay's fired stack is not by tool type)";
            if (Obj(root, "attributesByType") is { } attrs
                && attrs.EnumerateObject().FirstOrDefault(p => Matches(p.Name, pipe)) is { Value.ValueKind: JsonValueKind.Object } kiln
                && Obj(kiln.Value, "beehivekiln") is { } stages
                && stages.EnumerateObject().Any(s => Str(s.Value, "code") is not { } c || !c.Contains("{tooltype}", StringComparison.Ordinal)))
                return $"its raw mold's beehive kiln stages (the {color} clay's are not by tool type)";
        }
        return null;
    }

    private static string? CheckGroups(JsonElement root, string materialType)
    {
        if (Obj(root, "variantgroups") is not { ValueKind: JsonValueKind.Array } groups || groups.GetArrayLength() <= ToolTypeGroupIndex)
            return "variants (fewer than three groups)";
        var material = groups[1];
        if (Str(material, "code") != "materialtype" || !States(material).Contains(materialType))
            return $"variants (the second is not materialtype with {materialType})";
        var tool = groups[ToolTypeGroupIndex];
        if (Str(tool, "code") != ToolTypeGroup || States(tool) is not { Count: > 0 } states)
            return $"variants (the third is not {ToolTypeGroup})";
        if (states.Contains(ToolType))
            return $"variants ({ToolTypeGroup} already has {ToolType})";
        if (Str(groups[0], "code") != "color" || States(groups[0]).Count == 0)
            return "variants (the first is not color)";
        return null;
    }

    private static string? CheckShape(JsonElement root)
    {
        if (Obj(root, "shape") is not null)
            return "shape (a plain shape besides the shape by type)";
        if (Obj(root, "shapebytype") is not { } byType)
            return "shape (no shape by type)";
        return byType.EnumerateObject().Any(p => Matches(p.Name, $"toolmold-blue-fired-{ToolType}") || Matches(p.Name, $"toolmold-blue-raw-{ToolType}"))
            ? "shape (a shape by type already answers for the pipe mold)"
            : null;
    }

    private static IEnumerable<string> Colors(JsonElement root) =>
        States(root.GetProperty("variantgroups")[0]);

    private static List<string> States(JsonElement group) =>
        Obj(group, "states") is { ValueKind: JsonValueKind.Array } states
            ? states.EnumerateArray().Where(s => s.ValueKind == JsonValueKind.String).Select(s => s.GetString()!).ToList()
            : [];

    // Property names as the game reads them: case-insensitive.
    private static JsonElement? Obj(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var p in element.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                return p.Value;
        return null;
    }

    private static string? Str(JsonElement element, string name) =>
        Obj(element, name) is { ValueKind: JsonValueKind.String } s ? s.GetString() : null;

    /// <summary>The game's by-type key match: <c>*</c> for any run of characters, the whole code,
    /// case-insensitive; a key with no domain matches the code's path.</summary>
    public static bool Matches(string pattern, string code)
    {
        if (pattern.Contains(':'))
            pattern = pattern[(pattern.IndexOf(':') + 1)..];
        return Regex.IsMatch(code, "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
