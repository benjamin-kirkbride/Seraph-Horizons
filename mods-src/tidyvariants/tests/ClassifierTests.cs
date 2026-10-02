using SeraphHorizons.TidyVariants.Core;
using static SeraphHorizons.TidyVariants.Tests.Fx;

namespace SeraphHorizons.TidyVariants.Tests;

public class ClassifierTests
{
    [Fact]
    public void RockValuesAreRockWhateverTheName()
    {
        // vanilla ore: rock <- block/rockwithdeposit; MoreRoads ancientroad: `type` holding rocks.
        Assert.Equal((DimensionClass.Material, MaterialKind.Rock), C(Classify("rock", RockWithDeposit)));
        var moreRoads = new[] { "andesite", "basalt", "bauxite", "chalk", "chert", "claystone", "conglomerate", "granite", "limestone", "suevite", "travertine", "kimberlite", "obsidian" };
        Assert.Equal((DimensionClass.Material, MaterialKind.Rock), C(Classify("type", moreRoads)));
    }

    [Fact]
    public void GeologyRocksMissingFromTheListStillPassTheRatio()
    {
        // GeologyAdditions adds arkose, dolostone, marl... When the patched list isn't read, 60% still holds.
        string[] values = [.. Rock[..10], "arkose", "dolostone", "marl", "mudstone", "serpentinite"];
        var info = Classify("rock", values);
        Assert.Equal(MaterialKind.Rock, info.Material);
        Assert.Contains("10/15", info.Reason);
    }

    [Fact]
    public void WoodWithAgedExtrasIsWood()
    {
        // DoorVariants: wood[aged,veryaged,larch,oak,...]
        string[] values = ["aged", "veryaged", "larch", "oak", "baldcypress", "redwood", "purpleheart", "walnut", "ebony", "kapok", "acacia", "pine", "maple", "birch"];
        Assert.Equal((DimensionClass.Material, MaterialKind.Wood), C(Classify("wood", values)));
    }

    [Fact]
    public void MetalWithScrapAndAdminIsMetal()
    {
        // vanilla blade: metal[scrap,copper,tinbronze,...,admin,ruined]
        string[] values = ["scrap", "copper", "tinbronze", "bismuthbronze", "blackbronze", "gold", "silver", "iron", "meteoriciron", "steel", "admin", "ruined"];
        Assert.Equal((DimensionClass.Material, MaterialKind.Metal), C(Classify("metal", values)));
    }

    [Fact]
    public void ClayAndClothColorsAreColor()
    {
        // MoreRoads clayroad type; vanilla cloth color; DoorVariants bamboo door cloth.
        Assert.Equal(MaterialKind.Color, Classify("type", "tan", "red", "brown", "cream", "gray", "black", "clinker", "orange", "fire").Material);
        Assert.Equal(MaterialKind.Color, Classify("color", "plain", "mordant", "blue", "red", "yellow", "green", "purple", "pink", "orange", "brown", "gray", "black", "white").Material);
        Assert.Equal(MaterialKind.Color, Classify("cloth", "blue", "red", "yellow", "green", "purple", "pink", "orange", "brown", "gray", "black", "white").Material);
    }

    [Fact]
    public void SmallColorDimensionsStayMeaningful()
    {
        // Two generic color words are not enough to call a dimension a color (e.g. red/white wine).
        Assert.Equal(DimensionClass.Meaningful, Classify("type", "red", "white").Class);
        // But a small rock or wood dimension is fine when every value is one.
        Assert.Equal(MaterialKind.Wood, Classify("wood", "oak", "pine").Material);
    }

    [Fact]
    public void OreTypesAreMeaningful()
    {
        // ore <- block/ore-graded is not a material list: one group per ore type.
        Assert.Equal(DimensionClass.Meaningful, Classify("ore", OreGraded).Class);
    }

    [Fact]
    public void MostlyNonMaterialValuesStayMeaningful()
    {
        // Food Shelves tablewshelf type[normal,whitemarble,redmarble,greenmarble] is 75% rock and collapses;
        // a dimension with only a few rock names among others does not.
        Assert.Equal(DimensionClass.Material, Classify("type", "normal", "whitemarble", "redmarble", "greenmarble").Class);
        Assert.Equal(DimensionClass.Meaningful, Classify("type", "granite", "plain", "fancy", "rough", "smooth").Class);
    }

    [Theory]
    [InlineData("horizontalorientation", "north,east,south,west")]
    [InlineData("rot", "north,east,south,west,up,down")] // plankslab
    [InlineData("side", "north,east,south,west")]        // chest, Food Shelves
    [InlineData("v", "up,left,down,right")]              // legacy trapdoor
    [InlineData("orientation", "ns,we")]                // MoreRoads stonefootpath
    [InlineData("type", "n,w")]                          // vanilla fence piece
    [InlineData("updown", "up")]                         // MoreRoads stairs
    [InlineData("rotation", "0,90,180,270")]             // named rotation, whatever the values
    public void Orientation(string name, string values) =>
        Assert.Equal(DimensionClass.Orientation, Classify(name, values.Split(',')).Class);

    [Theory]
    [InlineData("state", "closed,opened")]
    [InlineData("state", "opened,closed")]
    [InlineData("open", "open,closed")]
    [InlineData("status", "opened,closed")]                  // slidingwoodenshutters
    public void OpenClosed(string name, string values) =>
        Assert.Equal(DimensionClass.OpenClosedState, Classify(name, values.Split(',')).Class);

    [Theory]
    [InlineData("state", "extinct,burnedout,lit")]          // torch
    [InlineData("state", "partbaked,perfect,charred")]       // ExpandedFoods cooked vegetable
    [InlineData("type", "raw,partbaked,cooked,charred")]     // ExpandedFoods dumpling: `type` holding cooking states
    [InlineData("type", "normal,cut,raw,tenderpartbaked,tender,tendercharred,meatballpartbaked,meatball,meatballcharred")] // agedmeat
    [InlineData("state", "raw,bake1,bake2,bake3,bake4")]     // hardtack
    [InlineData("cover", "free,snow")]                       // stairs and slabs
    [InlineData("stage", "1,2,3,4,5,6,7,8,9")]               // crops
    public void ProcessState(string name, string values) =>
        Assert.Equal(DimensionClass.ProcessState, Classify(name, values.Split(',')).Class);

    [Theory]
    [InlineData("grade", "poor,medium,rich,bountiful")]
    [InlineData("size", "small,large")]
    [InlineData("quality", "a,b,c")]
    [InlineData("type", "small,medium,large")]
    public void GradeSizeQuality(string name, string values) =>
        Assert.Equal(DimensionClass.GradeSizeQuality, Classify(name, values.Split(',')).Class);

    [Theory]
    [InlineData("type", "crude,basic,cloth")]                // torch type
    [InlineData("style", "sleek-windowed,solid,1x3gate")]    // door style
    [InlineData("type", "army-tool,art/bottle,art/bronze-tablet")] // clutter
    [InlineData("construction", "woodmetal,woodmetalleather,metal")] // shield
    [InlineData("corner", "straight,corner,cap")]            // MoreRoads edging
    [InlineData("side", "left,middle,right")]                // loose name, non-orientation value
    [InlineData("type", "normal,short")]                     // Food Shelves
    [InlineData("state", "left,half,right,halftop,fulltop")] // slidingwoodenshutters: `state` is the shape
    [InlineData("state", "four,eight")]                      // `state` without any process value
    public void Meaningful(string name, string values) =>
        Assert.Equal(DimensionClass.Meaningful, Classify(name, values.Split(',')).Class);

    [Fact]
    public void PropertySourceHintWins()
    {
        var s = Settings();
        // Even an odd value set is rock when the game says it came from block/rock.
        var info = Classifier.Classify("material", ["granite", "weird"], "block/rock", s);
        Assert.Equal(MaterialKind.Rock, info.Material);
        Assert.Contains("loadFromProperties", info.Reason);
        Assert.Equal(DimensionClass.Orientation, Classifier.Classify("x", ["a"], "game:abstract/horizontalorientation", s).Class);
        // A non-material property falls through to values.
        Assert.Equal(DimensionClass.Meaningful, Classifier.Classify("ore", OreGraded, "block/ore-graded", s).Class);
    }

    [Fact]
    public void AttributeDimensionsClassifyByValue()
    {
        Assert.Equal(MaterialKind.Wood, Classify("attr:wood", "oak", "pine", "birch").Material);
        Assert.Equal(DimensionClass.Meaningful, Classify("attr:type", "book-big-closed", "book-small-open", "bottle").Class);
    }

    [Fact]
    public void MissingWorldPropertiesAreReported()
    {
        var issues = new List<TidyIssue>();
        _ = new TidySettings(new Dictionary<string, IReadOnlyList<string>> { ["block/rock"] = Rock }, null, issues);
        Assert.Contains(issues, i => i.Kind == "property-missing" && i.Message.Contains("game:block/wood"));
        Assert.DoesNotContain(issues, i => i.Message.Contains("game:block/rock'"));
    }

    [Fact]
    public void OverrideMaterialsExtendTheLists()
    {
        var o = OverrideFile.Parse("""{ "materials": { "color": { "properties": ["doorvariants:block/door-bricks"], "values": ["seafoam"] } } }""");
        var props = new Dictionary<string, IReadOnlyList<string>>(WorldProperties) { ["doorvariants:block/door-bricks"] = ["black", "brown", "clinker", "cream", "fire", "gray", "zzz"] };
        var s = new TidySettings(props, o);
        Assert.Contains("zzz", s.MaterialValues(MaterialKind.Color));
        Assert.Contains("seafoam", s.MaterialValues(MaterialKind.Color));
        Assert.Equal(MaterialKind.Color, s.PropertyKind("doorvariants:block/door-bricks"));
    }

    [Fact]
    public void DecoratedValuesAreNormalised()
    {
        // Tankards and Goblets: wood[oak.,birch.,pine.,...]
        var info = Classify("wood", "oak.", "birch.", "pine.", "acacia.", "aged.", "larch.", "baldcypress.", "ebony.", "kapok.", "maple.", "purpleheart.", "redwood.", "walnut.");
        Assert.Equal(MaterialKind.Wood, info.Material);
        Assert.Equal("oak", Vocabulary.Normalize("oak."));
        Assert.Equal("leather-plain", Vocabulary.Normalize("leather-plain"));
        Assert.Equal(1, Settings().PreferenceRank("wood", "birch."));
    }

    [Fact]
    public void NestedAttributeNamesUseTheirLastPart()
    {
        // purposefulstorage PSAttributes.wood, foodshelves FSAttributes.wood
        Assert.Equal(MaterialKind.Wood, Classify("attr:FSAttributes.wood", "oak", "pine", "birch").Material);
        Assert.Equal(DimensionClass.Orientation, Classify("attr:PSAttributes.side", "north", "east").Class);
    }

    [Fact]
    public void NamedListsFromTheOverrideFile()
    {
        var o = OverrideFile.Parse("""
            { "lists": { "claycolor": { "values": ["tan", "red", "brown", "cream", "gray", "black", "clinker", "orange", "fire", "blue"], "preferred": ["fire"] } },
              "materials": { "color": { "values": [] } } }
            """);
        var s = new TidySettings(WorldProperties, o);
        // Built-in color still wins for this one (materials are checked before lists)...
        Assert.Equal(DimensionClass.Material, Classifier.Classify("type", ["tan", "red", "brown", "cream"], null, s).Class);
        // ...but a short clay set that the color thresholds reject falls into the list.
        var info = Classifier.Classify("type", ["clinker", "fire"], null, s);
        Assert.Equal(DimensionClass.Filler, info.Class);
        Assert.Equal("claycolor", info.List);
        Assert.Equal("claycolor", info.PreferenceKey);
        Assert.Equal(0, s.PreferenceRank("claycolor", "fire"));
    }

    static (DimensionClass, MaterialKind) C(DimensionInfo i) => (i.Class, i.Material);
}
