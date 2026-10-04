using SeraphHorizons.Mod.Core;

namespace SeraphHorizons.Mod.Tests;

public class WoodworkingGuidePagesTests
{
    private static readonly HandbookPageRef Iw = new("craftinginfo-woodworking", "immersivewoodworking:craftinginfo-woodworking-title");
    private static readonly HandbookPageRef Le = new("introduction", "loggingmod:introduction-title");

    private static HandbookPageRef Own(int i) =>
        new(WoodworkingGuidePages.Pages[i].PageCode, WoodworkingGuidePages.Pages[i].TitleKey());

    private static HandbookPageRef Guide(string code) => new(code, code + "-title");

    // The handbook's list as it is built: game guides, then mods' guides by asset path
    // (immersivewoodworking, loggingmod, seraphhorizons), then item pages.
    private static List<HandbookPageRef> Shipped() =>
    [
        Guide("craftinginfo-starterguide"),
        Guide("craftinginfo-knapping"),
        Iw,
        Le,
        Guide("ppex-fittings"),
        Own(0), Own(1), Own(2), Own(3), Own(4), Own(5),
        new("item-stick", null),
    ];

    private static List<HandbookPageRef> Shown(List<HandbookPageRef> pages, bool unified) =>
        WoodworkingGuidePages.Arrange(pages, unified).Select(i => pages[i]).ToList();

    [Fact]
    public void UnifiedPutsTheSixPagesWhereImmersiveWoodworkingsGuideWas()
    {
        Assert.Equal(
        [
            Guide("craftinginfo-starterguide"), Guide("craftinginfo-knapping"),
            Own(0), Own(1), Own(2), Own(3), Own(4), Own(5),
            Guide("ppex-fittings"), new("item-stick", null),
        ], Shown(Shipped(), true));
    }

    [Fact]
    public void OffKeepsBothModsGuidesAndDropsTheSix()
    {
        Assert.Equal(
        [
            Guide("craftinginfo-starterguide"), Guide("craftinginfo-knapping"), Iw, Le,
            Guide("ppex-fittings"), new("item-stick", null),
        ], Shown(Shipped(), false));
    }

    [Fact]
    public void UnifiedKeepsReadingOrderWhateverTheAssetOrder()
    {
        List<HandbookPageRef> pages = [Own(3), Le, Own(0), Own(5), Iw, Own(1), Own(4), Own(2)];
        Assert.Equal([Own(0), Own(1), Own(2), Own(3), Own(4), Own(5)], Shown(pages, true));
    }

    [Fact]
    public void UnifiedAnchorsOnLoggingExpandedsGuideWhenImmersiveWoodworkingsIsMissing()
    {
        List<HandbookPageRef> pages = [Guide("a"), Le, Guide("b"), Own(0), Own(1)];
        Assert.Equal([Guide("a"), Own(0), Own(1), Guide("b")], Shown(pages, true));
    }

    [Fact]
    public void UnifiedLeavesTheSixWhereTheyAreWhenNeitherGuideIsThere()
    {
        List<HandbookPageRef> pages = [Guide("a"), Own(1), Guide("b"), Own(0)];
        Assert.Equal([Guide("a"), Own(0), Own(1), Guide("b")], Shown(pages, true));
    }

    [Fact]
    public void AnotherIntroductionPageIsNotLoggingExpandedsGuide()
    {
        var other = new HandbookPageRef("introduction", "othermod:introduction-title");
        List<HandbookPageRef> pages = [other, Iw, Own(0)];
        Assert.Equal([other, Own(0)], Shown(pages, true));
        Assert.Equal([other, Iw], Shown(pages, false));
    }

    [Fact]
    public void ThePageCodeAloneDoesNotMakeAPageOursOrTheirs()
    {
        // Same code as the overview, but neither our title nor Immersive Woodworking's.
        var stranger = new HandbookPageRef(WoodworkingGuidePages.OverviewCode, "othermod:title");
        Assert.Equal(-1, WoodworkingGuidePages.OwnIndex(stranger));
        Assert.False(WoodworkingGuidePages.IsReplaced(stranger));
        Assert.Equal([stranger], Shown([stranger], true));
        Assert.Equal([stranger], Shown([stranger], false));
    }

    [Fact]
    public void NoWoodworkingPagesLeavesTheListAlone()
    {
        List<HandbookPageRef> pages = [Guide("a"), new("item-stick", null), Guide("b")];
        Assert.Equal(pages, Shown(pages, true));
        Assert.Equal(pages, Shown(pages, false));
    }

    [Fact]
    public void TheOverviewIsFirstAndKeepsImmersiveWoodworkingsCode()
    {
        Assert.Equal("craftinginfo-woodworking", WoodworkingGuidePages.Pages[0].PageCode);
        Assert.Equal(6, WoodworkingGuidePages.Pages.Count);
        Assert.Equal(6, WoodworkingGuidePages.Pages.Select(p => p.PageCode).Distinct().Count());
        Assert.All(WoodworkingGuidePages.Pages, p => Assert.StartsWith("seraphhorizons:woodworking-", p.LangKey));
    }

    // The recipe export's list of guides a player does not see is what Arrange drops.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HiddenIsWhatArrangeDrops(bool unified)
    {
        var pages = Shipped();
        var shown = WoodworkingGuidePages.Arrange(pages, unified).ToHashSet();
        var dropped = pages.Where((_, i) => !shown.Contains(i)).Select(p => (p.PageCode!, p.TitleKey!)).Order().ToList();
        Assert.Equal(dropped, WoodworkingGuidePages.Hidden(unified).Order().ToList());
    }
}

public class LangPatternKeysTests
{
    [Theory]
    [InlineData("loggingmod:wi-sawhorse-saw", LangKeyStore.Exact, "loggingmod:wi-sawhorse-saw")]
    [InlineData("loggingmod:block-handbooktext-loggingmod:sawhorse-*", LangKeyStore.Wildcard,
        "loggingmod:block-handbooktext-loggingmod:sawhorse-")]
    [InlineData("loggingmod:block-handbooktext-loggingmod:sawhorsestandard-*-*-*", LangKeyStore.Regex,
        "loggingmod:block-handbooktext-loggingmod:sawhorsestandard-*-*-*")]
    [InlineData("loggingmod:block-handbooktext-loggingmod:plankframe-*-*", LangKeyStore.Regex,
        "loggingmod:block-handbooktext-loggingmod:plankframe-*-*")]
    [InlineData("game:a-*-b", LangKeyStore.Regex, "game:a-*-b")]
    public void FilesAKeyWhereTheGameDoes(string key, LangKeyStore store, string stored)
    {
        Assert.Equal(store, LangPatternKeys.StoreOf(key));
        Assert.Equal(stored, LangPatternKeys.StoredKey(key));
    }

    [Fact]
    public void AnExampleKeyMatchesThePattern()
    {
        const string key = "loggingmod:block-handbooktext-loggingmod:sawhorsestandard-*-*-*";
        Assert.Equal("loggingmod:block-handbooktext-loggingmod:sawhorsestandard-x-x-x", LangPatternKeys.Example(key));
        Assert.StartsWith(LangPatternKeys.StoredKey("loggingmod:block-handbooktext-loggingmod:sawhorse-*"),
            LangPatternKeys.Example("loggingmod:block-handbooktext-loggingmod:sawhorse-*"));
    }
}
