using SeraphHorizons.Mod.TidyVariants.Core;

namespace SeraphHorizons.Mod.Core;

/// <summary>
/// Rock and earth stack to 16 (<c>SmallerRockStacks</c>, #863): which blocks are cut, to what, and
/// the stack trading judges their worth by. Game-independent, so tests/ runs it without the game.
/// </summary>
public static class RockStackRules
{
    /// <summary>The most a rock or earth block stacks to.</summary>
    public const int Cap = 16;

    /// <summary>The trade window's sell slots (<c>SeraphTraderInventory.SellSlots</c>): a cut block
    /// is worth trading when this many full stacks of it are worth a gear.</summary>
    public const int SellSlots = 4;

    /// <summary>The block materials a cut block may have (<c>EnumBlockMaterial</c> by name). The game
    /// gives a block type that names none <c>Stone</c>, so the material only guards the codes below,
    /// never picks a block on its own.</summary>
    public static readonly IReadOnlySet<string> Materials = new HashSet<string>(StringComparer.Ordinal)
    {
        "Stone", "Ore", "Soil", "Sand", "Gravel",
    };

    /// <summary>The rock and earth blocks, <c>domain:path</c> with <c>*</c> wildcards: the full blocks
    /// of rock and earth, as they lie in the ground and as cut or laid into whole blocks.
    /// <list type="bullet">
    /// <item>Rock: raw and cracked rock, cobblestone (mossy and lichen too), stone bricks (cracked,
    /// mossy, lichen and aged), polished rock (mossy and lichen), drystone, and ore-bearing rock
    /// (<c>game:ore-*</c>: every ore, coal and mineral block; the rock is the same bulk with ore in
    /// it or not).</item>
    /// <item>Earth: soil, forest floor, bony soil, sand, gravel (rich, dirty and sludgy too), packed
    /// dirt, clay and peat deposits, and earth laid into blocks: cob, rammed earth and mud brick (the
    /// game's and Material Needs' colours).</item>
    /// </list>
    /// Not here: slabs, stairs, fences, paths, roads and roofing (building shapes, not bulk); fired
    /// clay (bricks, tiles and shingles are ceramic); peat bricks (fuel); muddy gravel, which Stone
    /// Quarry's rubble storage makes a quarter of its stack at a time from a bucket of water (16 a
    /// bucket at 64; cut, it would make 4); and every item (stones, clay, bricks).</summary>
    public static readonly IReadOnlyList<string> Blocks =
    [
        "game:rock-*",
        "game:crackedrock-*",
        "game:cobblestone-*",
        "game:mossycobblestone-*",
        "game:lichencobblestone-*",
        "game:stonebricks-*",
        "game:crackedstonebricks-*",
        "game:mossystonebricks-*",
        "game:lichenstonebricks-*",
        "game:agedstonebricks-*",
        "game:rockpolished-*",
        "game:mossyrockpolished-*",
        "game:lichenrockpolished-*",
        "game:drystone-*",
        "game:ore-*",
        "game:soil-*",
        "game:forestfloor-*",
        "game:bonysoil*",
        "game:sand-*",
        "game:sandwavy-*",
        "game:gravel-*",
        "game:richgravel-*",
        "game:dirtygravel-*",
        "game:sludgygravel",
        "game:packeddirt*",
        "game:drypackeddirt",
        "game:rawclay-*",
        "game:peat-*",
        "game:cob-*",
        "game:rammed-*",
        "game:mudbrick-*",
        "materialneeds:rammed-*",
        "materialneeds:*mudbrick-*",
    ];

    /// <summary>Whether a block is rock or earth: its code is in <see cref="Blocks"/>, its material
    /// one of <see cref="Materials"/>, and it has no block entity (no container, device or pile).</summary>
    public static bool IsRockOrEarth(string? code, string? material, bool hasBlockEntity)
    {
        if (string.IsNullOrEmpty(code) || hasBlockEntity || material is null || !Materials.Contains(material))
            return false;
        string normalized = code.Contains(':') ? code : "game:" + code;
        foreach (string pattern in Blocks)
            if (Wildcard.IsMatch(pattern, normalized))
                return true;
        return false;
    }

    /// <summary>A rock or earth block's stack: at most <see cref="Cap"/>, never raised.</summary>
    public static int Cut(int maxStackSize) => Math.Min(maxStackSize, Cap);

    /// <summary>The stack trading judges a cut block's worth by: what the sell slots hold of it
    /// together (64 at 16, the stack it had).</summary>
    public static int TradeStack(int maxStackSize) => SellSlots * Math.Max(1, maxStackSize);
}
