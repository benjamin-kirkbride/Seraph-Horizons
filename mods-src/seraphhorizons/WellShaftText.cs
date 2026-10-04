using Vintagestory.API.Common;

namespace SeraphHorizons.Mod;

/// <summary>
/// Hydrate or Diedrate: the Wells handbook page says to dig straight down and that better walls
/// retain more, and nothing more. A well spring counts the shaft above it level by level
/// (<c>BlockEntityWellSpring.OnPeriodicShaftCheck</c>): a level counts when its cell is open and
/// all four horizontal neighbors are full liquid barriers, so a shaft wider than one block holds
/// nothing. Each wall block is rated for a depth (<c>WellwaterDepthMaxBase</c> 5,
/// <c>WellwaterDepthMaxClay</c> 7 for <c>game:brick*</c>, <c>WellwaterDepthMaxStone</c> 10 for
/// <c>game:stonebrick*</c>), and the count stops at the first level that is no lower than the
/// lowest rating seen so far. The prefixes are of the block code, so <c>brickcourse-*</c> and
/// <c>stonebricks-*</c> are rated, and <c>claybricks-*</c> (fireclay, uneven) and
/// <c>agedstonebricks-*</c> are not. This tweak says so in the English page. It changes text only.
/// </summary>
public static class WellShaftText
{
    public const string ModId = "hydrateordiedrate";

    /// <summary>The Deep Well passages of the Wells page. Each edit replaces one exact passage of
    /// Hydrate or Diedrate's text; if it is reworded, the edit is skipped with a warning. The
    /// quoted look-at line is its own <c>well.retentionVolume</c>, and the depths and liters are
    /// its default settings (70 liters a block).</summary>
    public static readonly LangEdit[] LangEdits =
    [
        new("en", "hydrateordiedrate:wellinfo-text",
            "mode and dig into a rock. It is recommended to build a",
            "mode and dig into a rock.<br><br>The shaft above the spring has to be one block wide. The well "
            + "holds water only in the levels of the shaft that are open and have a full solid block on all "
            + "four sides, counted upward from the spring to the first level that does not. In a wider hole, "
            + "2x2 for example, every level has an open side and the well holds nothing: looking at the spring "
            + "reads \"Max Well Volume: 0 liters\".<br><br>It is recommended to build a"),

        new("en", "hydrateordiedrate:wellinfo-text",
            "a deep well can retain can be upgraded by replacing the walls with better materials. Retention "
            + "depth in blocks:<br><br> - default: 5  <br> - bricks: 7<br> - ashlar: 10<br><br>",
            "a deep well can retain is capped by what the shaft's walls are made of, however deep the shaft "
            + "is, and can be upgraded by replacing the walls with better materials. Retention depth in "
            + "blocks:<br><br> - default (raw rock, soil and any other block): 5, or 350 liters<br> - bricks "
            + "(the plain clay bricks named after their bond, large or small): 7, or 490 liters<br> - ashlar "
            + "blocks: 10, or 700 liters<br><br>Fireclay bricks, uneven bricks and aged ashlar count as "
            + "default.<br><br>The weakest wall block counts, so all four walls of every level have to be "
            + "upgraded, up to the depth you want. One lesser block caps the well at its own depth when it is "
            + "within that many levels of the spring, and just below itself when it is higher: a single raw "
            + "rock block in an ashlar shaft caps it at 5 blocks from the bottom five levels, and at 7 blocks "
            + "from the eighth.<br><br>"),
    ];

    public static bool Applies(ICoreAPI api) => api.ModLoader.IsModEnabled(ModId);

    /// <summary>Applies <see cref="LangEdits"/> (<see cref="LangText.Apply"/>).</summary>
    public static void RewriteText(ILogger logger) => LangText.Apply(LangEdits, ModId, logger);
}
