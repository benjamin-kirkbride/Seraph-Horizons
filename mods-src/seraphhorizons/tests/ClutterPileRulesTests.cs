using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Mod.Tests;

public class ClutterPileRulesTests
{
    // More Piles' catch-all cube pile as its AppendBehavior writes it (GroundStoragePropertiesExtended
    // serialised, MorePilesProperties included); a vanilla pile from a type file has no such key.
    private const string MorePilesCube =
        """{"MorePilesProperties":{"FixPlacement":true,"ForceReplaceBehavior":false,"MatchesShape":"game:block/basic/cube"},"Layout":4,"StackingCapacity":64}""";
    private const string VanillaPile = """{"layout":"Stacking","stackingCapacity":32,"itemsPerModel":2}""";

    private static readonly string[] Clutter = ["BlockClutter", "BlockShapeFromAttributes", "Block", "CollectibleObject", "RegistryObject", "Object"];
    private static readonly string[] Bookshelf = ["BlockClutterBookshelfWithLore", "BlockClutterBookshelf", "BlockShapeFromAttributes", "Block"];
    private static readonly string[] Cube = ["Block", "CollectibleObject", "RegistryObject", "Object"];
    private static readonly string[] Rail = ["BlockToggleCollisionBox", "Block"];

    [Fact]
    public void Clutter_loses_More_Piles_pile()
    {
        Assert.True(ClutterPileRules.TakesNoPile(Clutter, ["Generic"], MorePilesCube));
        Assert.True(ClutterPileRules.TakesNoPile(Bookshelf, [], MorePilesCube));
    }

    [Fact]
    public void A_block_drawn_by_the_ShapeFromAttributes_behavior_loses_it_too()
    {
        // The elevator rail: class ToggleCollisionBox, its shape from the block entity behavior.
        Assert.True(ClutterPileRules.TakesNoPile(Rail, ["ShapeFromAttributes", "ToggleCollisionBox"], MorePilesCube));
        Assert.True(ClutterPileRules.TakesNoPile(Rail, ["shapefromattributes"], MorePilesCube));
    }

    [Fact]
    public void A_real_cube_keeps_its_pile()
    {
        Assert.False(ClutterPileRules.TakesNoPile(Cube, [], MorePilesCube));
        Assert.False(ClutterPileRules.TakesNoPile(Rail, ["ToggleCollisionBox"], MorePilesCube));
    }

    [Fact]
    public void A_pile_the_block_was_given_by_its_own_mod_stays()
    {
        Assert.False(ClutterPileRules.TakesNoPile(Clutter, [], VanillaPile));
        Assert.False(ClutterPileRules.TakesNoPile(Clutter, [], null));
        Assert.False(ClutterPileRules.TakesNoPile(Clutter, [], ""));
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
