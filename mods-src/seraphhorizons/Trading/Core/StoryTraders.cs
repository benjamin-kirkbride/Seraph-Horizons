using System.Text.RegularExpressions;
using SeraphHorizons.Mod.Trading.Standing.Core;

namespace SeraphHorizons.Mod.Trading.Core;

/// <summary>
/// The story traders (<see cref="TraderTypes.Story"/>): the game's treasure hunter, made one of the
/// pack's traders in a world with the trader grid. It keeps everything that makes it a story NPC
/// (its house, the game's quest dialogue with the pack's additions, its name and outfit) and gets
/// everything the pack's traders have (its own list, standing, the economy, orders, deliveries,
/// maps, the trade window).
///
/// <list type="bullet">
/// <item>Its story structure's spawner is rewritten from the game's code to the pack's, same gender
/// and outfit set (<see cref="PackPath"/>); no other spawner in a story structure is touched.</item>
/// <item>Spawned in that structure, it takes the structure's standing id
/// (<see cref="IdAt"/>, <c>story:treasurehunter</c>), so a revived, respawned or converted one keeps
/// the standing players had with it.</item>
/// <item>A game treasure hunter already in a grid world (spawned before this) is converted where it
/// stands, its dialogue variables carried over (<c>Trading/Game/StoryTraderConversion.cs</c>).</item>
/// </list>
/// </summary>
public static class StoryTraders
{
    /// <summary>Each story trader type's story structure (the game's <c>GenStoryStructures</c> code).</summary>
    public static readonly IReadOnlyDictionary<string, string> Structures = new Dictionary<string, string>
    {
        [TraderTypes.TreasureHunter] = "treasurehunter",
    };

    private static readonly Regex GameTreasureHunter = new(@"^trader-(male|female)-treasurehunter-(cold|temperate|desert)$", RegexOptions.CultureInvariant);

    /// <summary>The pack's entity code path for a story trader the pack takes over, or null:
    /// the game's treasure hunter (<c>game:trader-{gender}-treasurehunter-{climate}</c>) becomes
    /// <c>seraphhorizons:trader-{gender}-treasurehunter-{climate}</c>, same gender and outfit set.</summary>
    /// <param name="domain">The code's domain (<c>game</c> when it has none).</param>
    public static string? PackPath(string domain, string path)
    {
        if (domain != "game") return null;
        var m = GameTreasureHunter.Match(path);
        return m.Success ? TraderTypes.EntityPath(m.Groups[1].Value, TraderTypes.TreasureHunter, m.Groups[2].Value) : null;
    }

    /// <summary><see cref="PackPath(string, string)"/> of a full code, <c>domain:path</c> or a bare
    /// path in the game's domain.</summary>
    public static string? PackPath(string code)
    {
        int colon = code.IndexOf(':');
        return colon < 0 ? PackPath("game", code) : PackPath(code[..colon], code[(colon + 1)..]);
    }

    /// <summary>The story trader type that lives in a story structure, or null.</summary>
    public static string? TypeOfStructure(string structure) =>
        Structures.FirstOrDefault(kv => kv.Value == structure).Key;

    /// <summary>The standing id of a trader of <paramref name="type"/> standing in the story structure
    /// <paramref name="structureAt"/> (null: in none): <c>story:{structure}</c> when it is a story
    /// trader in its own structure, else null (the trader's id is then decided as any other's).</summary>
    public static string? IdAt(string type, string? structureAt) =>
        structureAt != null && Structures.TryGetValue(type, out string? home) && home == structureAt ? TraderIds.Story(home) : null;
}
