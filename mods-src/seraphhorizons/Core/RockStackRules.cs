using SeraphHorizons.Mod.TidyVariants.Core;

namespace SeraphHorizons.Mod.Core;

/// <summary>
/// Rock and earth stack to 16 (<c>SmallerRockStacks</c>, #863): which blocks are cut, to what, and
/// the stack trading judges their worth by. Two tiers: the bulk blocks of rock and earth
/// (<see cref="Blocks"/>) to <see cref="Cap"/>, their building shapes (<see cref="Shapes"/>) to
/// <see cref="ShapeCap"/>. Game-independent, so tests/ runs it without the game.
/// </summary>
public static class RockStackRules
{
    /// <summary>The most a rock or earth block stacks to.</summary>
    public const int Cap = 16;

    /// <summary>The most a rock or earth building shape (slab, stair, fence, path, road, roofing)
    /// stacks to.</summary>
    public const int ShapeCap = 32;

    /// <summary>The block materials a cut block may have (<c>EnumBlockMaterial</c> by name). The game
    /// gives a block type that names none <c>Stone</c>, so the material only guards the codes below,
    /// never picks a block on its own. Ceramic is for the fired clay building blocks, whose other
    /// ceramic blocks (pots, molds, vessels, chimneys) the codes leave out.</summary>
    public static readonly IReadOnlySet<string> Materials = new HashSet<string>(StringComparer.Ordinal)
    {
        "Stone", "Ore", "Soil", "Sand", "Gravel", "Ceramic",
    };

    /// <summary>The rock and earth blocks, <c>domain:path</c> with <c>*</c> wildcards: the full blocks
    /// of rock and earth, as they lie in the ground and as cut or laid into whole blocks.
    /// <list type="bullet">
    /// <item>Rock: raw and cracked rock, cobblestone (mossy and lichen too), stone bricks (cracked,
    /// mossy, lichen and aged), polished rock (mossy and lichen), drystone, and ore-bearing rock
    /// (<c>game:ore-*</c>: every ore, coal and mineral block; the rock is the same bulk with ore in
    /// it or not).</item>
    /// <item>Earth: soil, forest floor, bony soil, sand, gravel (rich, dirty, sludgy and muddy too),
    /// packed dirt, clay and peat deposits, and earth laid into blocks: cob, rammed earth and mud
    /// brick (the game's and Material Needs' colours). Stone Quarry's bucket still makes 16 muddy
    /// gravel (<c>StoneQuarryDrench</c>).</item>
    /// <item>Fired clay: the full building blocks of clay brick (with its courses, ruined bricks and
    /// chimneys), tile, clay shingle and refractory brick.</item>
    /// </list>
    /// Not here: slabs, stairs, fences, paths, roads and roofing (building shapes: <see cref="Shapes"/>);
    /// refractory brick gratings and copper shingles; peat bricks (fuel); and every item (stones,
    /// clay, bricks, refractory bricks).</summary>
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
        "game:muddygravel",
        "game:packeddirt*",
        "game:drypackeddirt",
        "game:rawclay-*",
        "game:peat-*",
        "game:cob-*",
        "game:rammed-*",
        "game:mudbrick-*",
        "materialneeds:rammed-*",
        "materialneeds:*mudbrick-*",
        "game:claybricks-*",
        "game:brickcourse-*",
        "game:brickruin-*",
        "game:tile-*",
        "game:clayshingleblock-*",
        "game:claybrickchimney-*",
        "game:refractorybricks-*",
    ];

    /// <summary>The codes in <see cref="Blocks"/> cut although they have a block entity: the game's
    /// old clay brick chimneys (<c>legacy/chimney.json</c>), whose <c>ParticleEmitter</c> entity
    /// only gives off smoke where the chimney stands and keeps nothing of the stack it was placed
    /// from (the new chimneys, <c>clay/chimneycourse.json</c>, have none).</summary>
    public static readonly IReadOnlyList<string> BlockEntityAllowed =
    [
        "game:claybrickchimney-*",
    ];

    /// <summary>The block materials a cut building shape may have: rock and earth's, and ceramic for
    /// the clay ones (brick, tile and shingle), whose other ceramic blocks the codes leave out. A
    /// wooden or thatch shape of the same family (roofing) is wood or plant, and stays.</summary>
    public static readonly IReadOnlySet<string> ShapeMaterials = new HashSet<string>(StringComparer.Ordinal)
    {
        "Stone", "Soil", "Sand", "Gravel", "Ceramic",
    };

    /// <summary>The rock and earth building shapes, <c>domain:path</c> with <c>*</c> wildcards:
    /// <list type="bullet">
    /// <item>Slabs and stairs: cobblestone, stone brick, polished rock (slabs), clay brick, mud brick
    /// (the game's and Material Needs' colours) and tile (slabs).</item>
    /// <item>Fences: drystone (the game has no stone wall).</item>
    /// <item>Paths and roads: the game's stone path (and its slab, stairs and full block),
    /// Splittable Stone Paths' quarter, smex's slag path, and MoreRoads' stone paths, footpaths,
    /// ancient, clay and stone roads (with their slabs and stairs) and stone, brick and clay edges.</item>
    /// <item>Roofing: clay shingle slabs and stairs, and the slanted roofing of clay and slate (the
    /// full shingle block is in <see cref="Blocks"/>).</item>
    /// </list>
    /// Not here: cobblestone fans, quartz slabs, stairs and pillars, copper shingles, Industrial Deco's iron roads (256 a stack, metal), VS Roofing's roofs (a block
    /// entity) and every wooden shape (MoreRoads' wood edges, wooden paths, fences and roofing).</summary>
    public static readonly IReadOnlyList<string> Shapes =
    [
        "game:cobblestoneslab-*",
        "game:cobblestonestairs-*",
        "game:stonebrickslab-*",
        "game:stonebrickstairs-*",
        "game:polishedrockslab-*",
        "game:brickslabs-*",
        "game:brickstairs-*",
        "game:mudbrickslab-*",
        "materialneeds:*mudbrickslab-*",
        "game:tileslab-*",
        "game:drystonefence-*",
        "game:stonepath*",
        "splittablestonepaths:stonepathquarter-*",
        "smex:slagpath*",
        "moreroads:stonepath*",
        "moreroads:stonefootpath-*",
        "moreroads:ancientroad*",
        "moreroads:clayroad*",
        "moreroads:stoneroad3*",
        "moreroads:edgestone-*",
        "moreroads:edgebrick-*",
        "moreroads:edgeclay-*",
        "game:clayshinglelabs-*",
        "game:clayshinglestairs-*",
        "game:slantedroofing*",
    ];

    /// <summary>Whether a block is rock or earth: its code is in <see cref="Blocks"/>, its material
    /// one of <see cref="Materials"/>, and it has no block entity (no container, device or pile)
    /// unless its code is in <see cref="BlockEntityAllowed"/>.</summary>
    public static bool IsRockOrEarth(string? code, string? material, bool hasBlockEntity) =>
        Matches(code, material, hasBlockEntity && !AnyMatch(code, BlockEntityAllowed), Blocks, Materials);

    /// <summary>Whether a block is a rock or earth building shape: its code is in
    /// <see cref="Shapes"/>, its material one of <see cref="ShapeMaterials"/>, and it has no block
    /// entity.</summary>
    public static bool IsRockOrEarthShape(string? code, string? material, bool hasBlockEntity) =>
        Matches(code, material, hasBlockEntity, Shapes, ShapeMaterials);

    /// <summary>The most a block may stack to: <see cref="Cap"/> for rock and earth,
    /// <see cref="ShapeCap"/> for their building shapes, null for any other block.</summary>
    public static int? CapOf(string? code, string? material, bool hasBlockEntity) =>
        IsRockOrEarth(code, material, hasBlockEntity) ? Cap
        : IsRockOrEarthShape(code, material, hasBlockEntity) ? ShapeCap
        : null;

    private static bool Matches(string? code, string? material, bool hasBlockEntity, IReadOnlyList<string> patterns,
        IReadOnlySet<string> materials)
    {
        if (hasBlockEntity || material is null || !materials.Contains(material))
            return false;
        return AnyMatch(code, patterns);
    }

    private static bool AnyMatch(string? code, IReadOnlyList<string> patterns)
    {
        if (string.IsNullOrEmpty(code))
            return false;
        string normalized = code.Contains(':') ? code : "game:" + code;
        foreach (string pattern in patterns)
            if (Wildcard.IsMatch(pattern, normalized))
                return true;
        return false;
    }

    /// <summary>A block's stack under its cap: at most <paramref name="cap"/>, never raised.</summary>
    public static int Cut(int maxStackSize, int cap) => Math.Min(maxStackSize, cap);
}
