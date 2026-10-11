using SeraphHorizons.Mod.Trading.Window.Core;

namespace SeraphHorizons.Mod.Tests.Trading.Window;

/// <summary>A monospaced stand-in for the game's text measuring: 7 units a character, 16 a line,
/// wrapped at spaces as the game wraps.</summary>
internal sealed class FakeMeasure : ITextMeasure
{
    public const double Char = 7, Line = 16;

    public double Width(UiFont font, string text) => text.Split('\n').Max(l => l.Length) * Char;

    public double Height(UiFont font, string text, double width)
    {
        int perLine = Math.Max(1, (int)(width / Char));
        int lines = 0;
        foreach (string para in text.Split('\n'))
        {
            int used = 0;
            lines++;
            foreach (string word in para.Split(' '))
            {
                int len = word.Length;
                if (used > 0 && used + 1 + len > perLine)
                {
                    lines++;
                    used = 0;
                }
                used += (used > 0 ? 1 : 0) + len;
                while (used > perLine)
                {
                    lines++;
                    used -= perLine;
                }
            }
        }
        return lines * Line;
    }
}

public class WindowLayoutTests
{
    private static readonly SlotMetrics Slots = new(48, 3);
    private static readonly FakeMeasure M = new();
    private static double Width => WindowLayout.ContentWidth(Slots);

    private static string Long(string word, int n) => string.Join(" ", Enumerable.Repeat(word, n));

    private static void NoProblems(Flow flow) => Assert.Equal("", string.Join("; ", WindowLayout.Problems(flow.Boxes, flow.Width)));

    [Fact]
    public void TheOrdersIntroAndEveryLineStackWithoutOverlap()
    {
        // The playtest's Orders tab: the intro ran into the first order and Take sat on its text.
        var flow = new Flow(M, Width);
        var lines = Enumerable.Range(0, 6).Select(i => new ActionLine(
            $"{i + 4} × Squire boots of the long name: 0.4 g each, 1 g premium. 4 days to deliver once taken (on offer 3.13 days more).",
            [("Take", "take-" + i, i)])).ToList();
        WindowLayout.Actions(flow, Long("Take an order, then sell the goods here or hand them in.", 3), lines, "none");
        NoProblems(flow);
        var intro = flow.Boxes.Single(b => b.Key == "intro");
        var first = flow.Boxes.Single(b => b.Key == "line-0");
        Assert.True(first.Y >= intro.Bottom);
        Assert.True(intro.H > FakeMeasure.Line, "the intro wraps");
        var take = flow.Boxes.Single(b => b.Key == "take-0");
        Assert.True(take.X >= first.Right, "the button is beside its line, not on it");
    }

    [Fact]
    public void DeliveryButtonsStackBesideTheirLine()
    {
        var flow = new Flow(M, Width);
        WindowLayout.Actions(flow, null, [
            new ActionLine("Short", [("Take", "dtake-0", 0), ("Mark on map", "dmark-0", 0)]),
            new ActionLine(Long("a long delivery line", 12), [("Hand in", "dhandin-1", 1)]),
        ], null);
        NoProblems(flow);
        var mark = flow.Boxes.Single(b => b.Key == "dmark-0");
        Assert.True(flow.Boxes.Single(b => b.Key == "line-1").Y >= mark.Bottom);
    }

    [Fact]
    public void EveryMapAndLeadIsOneRowWithItsIconTextPriceAndBuyButtonInColumns()
    {
        // The playtest's Maps & leads tab: maps were icons with their lines apart below, held to buy,
        // and leads lines with a Buy button. Now every offer is one row alike, grouped.
        foreach (double width in new[] { Width, Width - WindowLayout.ScrollbarWidth })
        {
            var flow = new Flow(M, width);
            WindowLayout.Offers(flow,
            [
                new OfferSection("Ore maps",
                [
                    new OfferLine("Galena", "large · poor · in Syenite · about 1 km · rough, ±150 m", "19 g", "Buy", true, true, "buymap-0", 0),
                    new OfferLine("Hematite", Long("medium · rich · in Syenite", 4), "", "Sold out", false, false, "buymap-3", 3),
                ], []),
                new OfferSection("Leads",
                [
                    new OfferLine("Tailor's camp", "about 1 km north", "124 g", "Buy", false, true, "lead-0", 0, "#d08070"),
                ], ["Each lead bought here costs more than the last (0 so far)."]),
            ], "none");
            NoProblems(flow);
            var heading = flow.Boxes.Single(b => b.Key == "heading-0");
            var icon = flow.Boxes.Single(b => b.Key == "icon-buymap-0");
            var title = flow.Boxes.Single(b => b.Key == "title-buymap-0");
            var details = flow.Boxes.Single(b => b.Key == "details-buymap-0");
            var price = flow.Boxes.Single(b => b.Key == "price-buymap-0");
            var buy = flow.Boxes.Single(b => b.Key == "buymap-0");
            Assert.True(icon.Y >= heading.Bottom);
            Assert.Equal(BoxKind.Element, icon.Kind);
            Assert.True(title.X >= icon.Right && details.Y >= title.Bottom, "the text is beside the icon, its details under its title");
            Assert.True(price.X >= title.Right && buy.X >= price.Right, "the price, then the button, at the right");
            Assert.Equal(width, buy.Right);
            // Every button is as wide as the widest, so they line up; the sold-out one is off and its icon greyed.
            var soldOut = flow.Boxes.Single(b => b.Key == "buymap-3");
            Assert.Equal(buy.W, soldOut.W);
            Assert.False(soldOut.Enabled);
            Assert.False(flow.Boxes.Single(b => b.Key == "icon-buymap-3").Enabled);
            Assert.Equal(3, soldOut.Id);
            Assert.True(flow.Boxes.Single(b => b.Key == "details-buymap-3").H > FakeMeasure.Line, "long details wrap within the row");
            Assert.True(flow.Boxes.Single(b => b.Key == "icon-buymap-3").Y >= buy.Bottom);
            // A lead the player can't afford: Buy is off, but its icon is not greyed.
            Assert.False(flow.Boxes.Single(b => b.Key == "lead-0").Enabled);
            Assert.True(flow.Boxes.Single(b => b.Key == "icon-lead-0").Enabled);
            Assert.Equal("#d08070", flow.Boxes.Single(b => b.Key == "price-lead-0").Color);
            Assert.True(flow.Boxes.Single(b => b.Key == "note-1-0").Y >= flow.Boxes.Single(b => b.Key == "icon-lead-0").Bottom);
        }
        var empty = new Flow(M, Width);
        WindowLayout.Offers(empty, [], "No maps or leads on offer here this week.");
        Assert.Equal("empty", Assert.Single(empty.Boxes).Key);
    }

    [Fact]
    public void TheTradeTabFitsTheFourSellSlotsTheButtonAndTheOfferWithoutOverlap()
    {
        var flow = WindowLayout.Header(M, Width, "Trader · farmer · cold, igneous", "stranger [0 / 60]");
        WindowLayout.Trade(flow, new TradeTabText("Sells", "Buys", 13, 14, "-", "-", Long("Not for you yet", 4), 3,
            "8 × Red apple\n1 g for 8\n8 in stock\nHold to buy.", "Sell", "Hold to sell",
            "Trader pays 1 g per 28\nWorth 0.42 g so far: a sale is a whole gear, so put in more."), Slots);
        var footer = new Flow(M, Width, flow.Bottom);
        WindowLayout.Footer(footer, "", "You have 935 g · Wilhelmina the trader has 88 g · for goods off her list 21 g");
        NoProblems(flow);
        NoProblems(footer);
        var slots = flow.Boxes.Single(b => b.Key == "sellslots");
        var button = flow.Boxes.Single(b => b.Key == "sellbutton");
        var offer = flow.Boxes.Single(b => b.Key == "offer");
        Assert.True(button.Y >= slots.Bottom && offer.Y >= button.Bottom);
        Assert.Equal(Slots.GridWidth(4), slots.W);
        Assert.True(footer.Boxes.Single(b => b.Key == "footer").H > FakeMeasure.Line, "the footer wraps rather than running off");
    }

    [Fact]
    public void TheStandingTabGrowsWithItsText()
    {
        var flow = new Flow(M, Width - 20);
        WindowLayout.Standing(flow, [("stranger [0]", false), ("known [60]", true), ("regular [250]", false)],
            [new TextSection("What known gives you here", [Long("you pay ×0.97, it pays ×1.02", 4), "orders ×1.5"]),
             new TextSection("How to earn standing", Enumerable.Range(0, 8).Select(i => Long("bring the goods", i + 1)).ToList())]);
        NoProblems(flow);
        Assert.Equal(WindowLayout.Gold, flow.Boxes.Single(b => b.Key == "tier-1").Color);
        Assert.True(flow.Bottom > 400);
    }

    [Fact]
    public void TheWindowSitsBelowTheMinimapAndItsCoordinates()
    {
        // 1920 × 1080, scale 1: the minimap (254 px at the top right) and the coordinates under it.
        var minimap = new ScreenRect(1656, 10, 254, 254);
        var coords = new ScreenRect(1700, 274, 200, 58);
        var p = WindowPlacement.Place(1920, 1080, 480, 600, 26, 10, [minimap, coords], []);
        Assert.True(p.Below);
        Assert.Equal(332 + 10 + 26, p.Y);
        Assert.Equal(1920 - 10 - 480, p.X);
        Assert.Equal(1080 - 10 - p.Y, p.MaxHeight);
        // Scaled up, it has no room under them: left of the stack instead, from the top.
        var big = WindowPlacement.Place(1920, 1080, 720, 900, 39, 15, [minimap with { W = 381, X = 1524, H = 381 }, coords with { Y = 411, H = 87, X = 1590 }], []);
        Assert.False(big.Below);
        Assert.True(big.X + 720 <= 1524);
        Assert.Equal(15 + 39, big.Y);
        // No minimap: at the top.
        Assert.Equal(10 + 26, WindowPlacement.Place(1920, 1080, 480, 600, 26, 10, [], []).Y);
        // A minimap at the bottom right leaves the window less room.
        var low = WindowPlacement.Place(1920, 1080, 480, 600, 26, 10, [], [new ScreenRect(1656, 816, 254, 254)]);
        Assert.Equal(816 - 10 - low.Y, low.MaxHeight);
    }
}
