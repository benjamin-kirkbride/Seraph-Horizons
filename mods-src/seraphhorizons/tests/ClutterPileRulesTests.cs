using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Mod.Tests;

public class ClutterPileRulesTests
{
    // More Piles' catch-all cube pile as its AppendBehavior writes it (GroundStoragePropertiesExtended
    // serialised, MorePilesProperties included), and as big piles rewrites it (no whitespace).
    private const string MorePilesCube =
        """{"MorePilesProperties":{"FixPlacement":true,"ForceReplaceBehavior":false,"MatchesShape":"game:block/basic/cube"},"Layout":4,"StackingCapacity":64}""";
    private const string MorePilesCubeIndented = """
        {
          "MorePilesProperties": {
            "FixPlacement": true,
            "MatchesShape": "game:block/basic/cube"
          },
          "Layout": 4
        }
        """;
    // More Piles' own rule for a block with a block entity: the henbox's names no shape.
    private const string MorePilesHenbox =
        """{"MorePilesProperties":{"FixPlacement":true,"ForceReplaceBehavior":true},"Layout":4,"StackingCapacity":64}""";
    // A vanilla pile from a type file has no More Piles key.
    private const string VanillaPile = """{"layout":"Stacking","stackingCapacity":32,"itemsPerModel":2}""";

    private static readonly string[] Clutter = ["BlockClutter", "BlockShapeFromAttributes", "Block", "CollectibleObject", "RegistryObject", "Object"];
    private static readonly string[] Bookshelf = ["BlockClutterBookshelfWithLore", "BlockClutterBookshelf", "BlockShapeFromAttributes", "Block"];
    private static readonly string[] Chisel = ["BlockChisel", "BlockMicroBlock", "Block", "CollectibleObject"];
    private static readonly string[] Cube = ["Block", "CollectibleObject", "RegistryObject", "Object"];
    private static readonly string[] Rail = ["BlockToggleCollisionBox", "Block"];

    [Fact]
    public void Clutter_loses_More_Piles_cube_pile()
    {
        Assert.True(ClutterPileRules.TakesNoPile(Clutter, [], null, MorePilesCube));
        Assert.True(ClutterPileRules.TakesNoPile(Bookshelf, [], null, MorePilesCube));
    }

    [Fact]
    public void A_block_drawn_by_the_ShapeFromAttributes_behavior_loses_it_too()
    {
        Assert.True(ClutterPileRules.TakesNoPile(Rail, ["ShapeFromAttributes", "ToggleCollisionBox"], null, MorePilesCube));
        Assert.True(ClutterPileRules.TakesNoPile(Rail, ["shapefromattributes"], null, MorePilesCube));
    }

    [Fact]
    public void A_block_with_a_block_entity_loses_it()
    {
        // Chiseled blocks (entity class Chisel / MicroBlock), and crates, baskets, cheese.
        Assert.True(ClutterPileRules.TakesNoPile(Chisel, ["SupportBeam", "MicroblockSnowCover"], "Chisel", MorePilesCube));
        Assert.True(ClutterPileRules.TakesNoPile(["BlockMicroBlock", "Block"], [], "MicroBlock", MorePilesCubeIndented));
        Assert.True(ClutterPileRules.TakesNoPile(["BlockCrate", "Block"], [], "Crate", MorePilesCube));
        Assert.True(ClutterPileRules.TakesNoPile(["BlockCheese", "Block"], [], "Cheese", MorePilesCube));
    }

    [Fact]
    public void A_real_cube_keeps_its_pile()
    {
        Assert.False(ClutterPileRules.TakesNoPile(Cube, [], null, MorePilesCube));
        Assert.False(ClutterPileRules.TakesNoPile(Cube, [], "", MorePilesCube));
        Assert.False(ClutterPileRules.TakesNoPile(Rail, ["ToggleCollisionBox"], null, MorePilesCube));
    }

    [Fact]
    public void More_Piles_own_rule_for_a_block_with_a_block_entity_stays()
    {
        Assert.False(ClutterPileRules.TakesNoPile(["BlockHenbox", "Block"], [], "Henbox", MorePilesHenbox));
    }

    [Fact]
    public void A_pile_the_block_was_given_by_its_own_mod_stays()
    {
        Assert.False(ClutterPileRules.TakesNoPile(Clutter, [], null, VanillaPile));
        Assert.False(ClutterPileRules.TakesNoPile(Chisel, [], "Chisel", VanillaPile));
        Assert.False(ClutterPileRules.TakesNoPile(Clutter, [], null, null));
        Assert.False(ClutterPileRules.TakesNoPile(Clutter, [], null, ""));
    }

    [Fact]
    public void The_cube_rule_is_read_with_any_spacing_and_case_and_only_for_the_cube()
    {
        Assert.True(ClutterPileRules.IsCubeShapePile(MorePilesCube));
        Assert.True(ClutterPileRules.IsCubeShapePile(MorePilesCubeIndented));
        Assert.True(ClutterPileRules.IsCubeShapePile("""{"morepilesproperties":{"matchesshape":"game:block/basic/cube"}}"""));
        Assert.False(ClutterPileRules.IsCubeShapePile("""{"MorePilesProperties":{"MatchesShape":"game:block/basic/stairs/normal"}}"""));
        Assert.False(ClutterPileRules.IsCubeShapePile(MorePilesHenbox));
        // The shape alone is not More Piles'.
        Assert.False(ClutterPileRules.IsCubeShapePile("""{"MatchesShape":"game:block/basic/cube"}"""));
    }

    [Fact]
    public void More_Piles_key_is_read_in_any_case_but_not_inside_another_key()
    {
        Assert.True(ClutterPileRules.IsMorePilesPile("""{"morepilesproperties":{}}"""));
        Assert.False(ClutterPileRules.IsMorePilesPile("""{"notMorePilesProperties":{}}"""));
        Assert.False(ClutterPileRules.IsMorePilesPile(VanillaPile));
    }

    [Fact]
    public void A_null_class_or_behavior_name_is_no_match()
    {
        Assert.False(ClutterPileRules.DrawsShapeFromAttributes([null, "Block"], [null]));
    }
}
