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
