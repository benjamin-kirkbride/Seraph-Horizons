namespace SeraphHorizons.Mod.Trading.Window.Core;

/// <summary>The trade window's fonts (the game side maps them to its <c>CairoFont</c>s).</summary>
public enum UiFont
{
    /// <summary><c>CairoFont.WhiteSmallText</c>: headings, the header, order and delivery lines.</summary>
    Small,
    /// <summary><c>CairoFont.WhiteDetailText</c>: details, hints, the sell offer.</summary>
    Detail,
    /// <summary>Small, bold: the Standing tab's headings.</summary>
    Bold,
    /// <summary><c>CairoFont.SmallButtonText</c>: a small button's label.</summary>
    Button,
}

/// <summary>Measures text as the game draws it (wrapped to a width), in unscaled GUI units.</summary>
public interface ITextMeasure
{
    /// <summary>The height <paramref name="text"/> takes wrapped to <paramref name="width"/>.</summary>
    double Height(UiFont font, string text, double width);

    /// <summary>The width of <paramref name="text"/> on one line.</summary>
    double Width(UiFont font, string text);
}

public enum BoxKind
{
    /// <summary>Text, wrapped to the box's width; the box is as tall as the wrapped text.</summary>
    Text,
    /// <summary>A small button (<see cref="UiBox.Text"/> its label), sized to its label.</summary>
    Button,
    /// <summary>A slot grid, a bar or another element of a size the window knows.</summary>
    Element,
}

/// <summary>Something placed in the window, in unscaled units relative to the dialog's content.</summary>
/// <param name="Key">What it is (the window's element key).</param>
/// <param name="Color">A VTML-style colour for text, or null for the font's own.</param>
/// <param name="Id">The order or delivery id a button acts on, or a count.</param>
public sealed record UiBox(string Key, BoxKind Kind, double X, double Y, double W, double H, string Text = "", UiFont Font = UiFont.Small,
    string? Color = null, int Id = 0)
{
    public double Bottom => Y + H;
    public double Right => X + W;

    public bool Overlaps(UiBox other) => X < other.Right - 0.5 && other.X < Right - 0.5 && Y < other.Bottom - 0.5 && other.Y < Bottom - 0.5;
}

/// <summary>
/// A top-to-bottom flow of boxes: each block of text is measured wrapped to its width, and the next
/// row starts below the tallest thing in the one before, so nothing overlaps whatever the text
/// (the playtest found fixed heights cutting text off and running it into the next line).
/// </summary>
public sealed class Flow(ITextMeasure measure, double width, double y = 0)
{
    public const double ButtonPadX = 8, ButtonPadY = 4;

    private readonly List<UiBox> _boxes = [];
    private double _rowBottom = y;

    public ITextMeasure Measure => measure;
    public double Width => width;

    /// <summary>Where the current row starts.</summary>
    public double Y { get; private set; } = y;

    public IReadOnlyList<UiBox> Boxes => _boxes;

    /// <summary>The bottom of everything placed so far.</summary>
    public double Bottom => Math.Max(_rowBottom, Y);

    public UiBox Add(UiBox box)
    {
        _boxes.Add(box);
        _rowBottom = Math.Max(_rowBottom, box.Bottom);
        return box;
    }

    /// <summary>Text in the current row at <paramref name="x"/>, <paramref name="w"/> wide (the rest of
    /// the width by default), as tall as it wraps to (at least <paramref name="minLines"/> lines of
    /// that font's height). Empty text takes no room.</summary>
    public UiBox Text(string key, string text, UiFont font = UiFont.Small, double x = 0, double? w = null, string? color = null, double dy = 0, int minLines = 0)
    {
        double bw = Math.Max(1, w ?? width - x);
        double h = text.Length == 0 ? 0 : Math.Ceiling(measure.Height(font, text, bw));
        if (minLines > 0) h = Math.Max(h, Math.Ceiling(measure.Height(font, string.Join("\n", Enumerable.Repeat("X", minLines)), bw)));
        return Add(new UiBox(key, BoxKind.Text, x, Y + dy, bw, h, text, font, color));
    }

    /// <summary>A small button in the current row, its right edge at <paramref name="right"/> (or its
    /// left at <paramref name="x"/>).</summary>
    public UiBox Button(string key, string label, double? right = null, double x = 0, double dy = 0, int id = 0)
    {
        var (w, h) = ButtonSize(measure, label);
        return Add(new UiBox(key, BoxKind.Button, right is double r ? r - w : x, Y + dy, w, h, label, UiFont.Button, Id: id));
    }

    public static (double W, double H) ButtonSize(ITextMeasure measure, string label) =>
        (Math.Ceiling(measure.Width(UiFont.Button, label)) + 2 * ButtonPadX, Math.Ceiling(measure.Height(UiFont.Button, label, 1000)) + 2 * ButtonPadY);

    /// <summary>An element of a known size in the current row.</summary>
    public UiBox Element(string key, double x, double w, double h, double dy = 0, int id = 0) =>
        Add(new UiBox(key, BoxKind.Element, x, Y + dy, w, h, Id: id));

    /// <summary>Ends the row: the next starts <paramref name="gap"/> below the tallest thing in it.</summary>
    public Flow Next(double gap = 4)
    {
        Y = Math.Max(Y, _rowBottom) + gap;
        _rowBottom = Y;
        return this;
    }

    /// <summary>Moves the current row down by <paramref name="by"/> without ending it.</summary>
    public Flow Skip(double by)
    {
        Y += by;
        _rowBottom = Math.Max(_rowBottom, Y);
        return this;
    }
}

/// <summary>The slot grids' sizes, as the game's <c>ElementStdBounds.SlotGrid</c> makes them (with the
/// padding the window grows them by).</summary>
public readonly record struct SlotMetrics(double Size = 48, double Pad = 3)
{
    public double GridWidth(int cols) => cols * Size + (cols - 1) * Pad + 2 * Pad;

    public double GridHeight(int count, int cols)
    {
        int rows = Math.Max(1, (count + cols - 1) / Math.Max(1, cols));
        return rows * Size + (rows - 1) * Pad + 2 * Pad;
    }
}

/// <summary>What the Trade tab shows, resolved to text.</summary>
public sealed record TradeTabText(
    string Sells, string Buys, int SellCount, int BuyCount, string SellsNone, string BuysNone,
    string? Locked, int LockedCount, string Details, string Sell, string HoldSell, string Offer);

/// <summary>An order or delivery line with its buttons (label, key, id).</summary>
public sealed record ActionLine(string Text, IReadOnlyList<(string Label, string Key, int Id)> Buttons);

/// <summary>A heading and its lines (the Standing tab).</summary>
public sealed record TextSection(string Heading, IReadOnlyList<string> Lines);

/// <summary>
/// The trade window's layout (the playtest after #436): the header, every tab's body and the
/// footer, as measured boxes. Every block of text is measured wrapped to its width and the next
/// thing goes below it; the tabs whose content can run long (orders, deliveries, maps, standing)
/// lay out in a scroll area of their own. Game-independent: the window turns the boxes into
/// elements, and a test (and a local render harness) checks that nothing overlaps or runs past the
/// window's width.
/// </summary>
public static class WindowLayout
{
    public const string Gold = "#e8c86a", Quiet = "#b0a890";

    /// <summary>The content's width: two four-slot shelves side by side.</summary>
    public static double ContentWidth(SlotMetrics slots) => 2 * slots.GridWidth(4) + 20;

    /// <summary>The header: the trader's line and, with standing on, its tier label beside the bar.</summary>
    public static Flow Header(ITextMeasure m, double width, string header, string? barLabel, double y = 25)
    {
        var flow = new Flow(m, width, y);
        flow.Text("header", header);
        flow.Next(2);
        if (barLabel != null)
        {
            double labelW = Math.Min(width * 0.5, Math.Ceiling(m.Width(UiFont.Detail, barLabel)) + 4);
            var label = flow.Text("barlabel", barLabel, UiFont.Detail, 0, labelW);
            flow.Element("bar", labelW + 6, width - labelW - 6, 9, dy: Math.Max(0, (label.H - 9) / 2));
            flow.Next(2);
        }
        return flow.Next(6);
    }

    /// <summary>The Trade tab: shelves, locked goods, and below, the selected good beside the four
    /// sell slots with Hold to sell under them and what the trader pays under that.</summary>
    public static void Trade(Flow flow, TradeTabText t, SlotMetrics slots)
    {
        double gridW = slots.GridWidth(4), right = gridW + 20;
        flow.Text("sells-title", t.Sells, UiFont.Small, 0, gridW);
        flow.Text("buys-title", t.Buys, UiFont.Small, right, gridW);
        flow.Next(2);
        if (t.SellCount > 0) flow.Element("sells", 0, gridW, slots.GridHeight(t.SellCount, 4));
        else flow.Text("sells-none", t.SellsNone, UiFont.Detail, 0, gridW, dy: 6);
        if (t.BuyCount > 0) flow.Element("buys", right, gridW, slots.GridHeight(t.BuyCount, 4));
        else flow.Text("buys-none", t.BuysNone, UiFont.Detail, right, gridW, dy: 6);
        flow.Next(8);
        if (t.Locked != null && t.LockedCount > 0)
        {
            flow.Text("locked-title", t.Locked, UiFont.Detail);
            flow.Next(2);
            flow.Element("locked", 0, Math.Min(flow.Width, slots.GridWidth(Math.Min(8, t.LockedCount))), slots.GridHeight(t.LockedCount, 8));
            flow.Next(8);
        }
        double top = flow.Y;
        flow.Text("details", t.Details, UiFont.Detail, 0, gridW - 4, minLines: 4);
        // The sell column, stacked by hand within the row.
        var title = flow.Text("sell-title", t.Sell, UiFont.Small, right, gridW);
        double y = title.Bottom + 2 - top;
        var grid = flow.Element("sellslots", right, slots.GridWidth(4), slots.GridHeight(4, 4), dy: y);
        y = grid.Bottom + 4 - top;
        var button = flow.Button("sellbutton", t.HoldSell, x: right, dy: y);
        y = button.Bottom + 4 - top;
        flow.Text("offer", t.Offer, UiFont.Detail, right, gridW, dy: y);
        flow.Next(6);
    }

    /// <summary>Lines with buttons at their right (orders, deliveries), an optional intro first.</summary>
    public static void Actions(Flow flow, string? intro, IReadOnlyList<ActionLine> lines, string? empty)
    {
        if (lines.Count == 0)
        {
            if (empty != null) flow.Text("empty", empty);
            flow.Next(6);
            return;
        }
        if (intro != null)
        {
            flow.Text("intro", intro, UiFont.Detail);
            flow.Next(8);
        }
        double buttonsW = 0;
        foreach (var line in lines)
            foreach (var b in line.Buttons)
                buttonsW = Math.Max(buttonsW, Flow.ButtonSize(flow.Measure, b.Label).W);
        double textW = buttonsW > 0 ? flow.Width - buttonsW - 10 : flow.Width;
        int n = 0;
        foreach (var line in lines)
        {
            flow.Text("line-" + n, line.Text, UiFont.Small, 0, textW);
            double dy = 0;
            foreach (var (label, key, id) in line.Buttons)
            {
                var button = flow.Button(key, label, right: flow.Width, dy: dy, id: id);
                dy += button.H + 4;
            }
            flow.Next(10);
            n++;
        }
    }

    /// <summary>The Maps &amp; leads tab below its slot grid: a line per offer, the locked leads' note.</summary>
    public static void MapLines(Flow flow, IReadOnlyList<string> lines, string? leadsLocked, string details)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            flow.Text("map-" + i, lines[i], UiFont.Detail);
            flow.Next(4);
        }
        if (leadsLocked != null)
        {
            flow.Text("leadslocked", leadsLocked, UiFont.Detail);
            flow.Next(6);
        }
        flow.Text("details", details, UiFont.Detail, minLines: 2);
        flow.Next(6);
    }

    /// <summary>The Standing tab: the tiers (the current one gold, marked), then each section.</summary>
    public static void Standing(Flow flow, IReadOnlyList<(string Line, bool Current)> tiers, IReadOnlyList<TextSection> sections)
    {
        int i = 0;
        foreach (var (line, current) in tiers)
        {
            flow.Text("tier-" + i++, current ? "» " + line : line, current ? UiFont.Bold : UiFont.Small, current ? 0 : 14, color: current ? Gold : Quiet);
            flow.Next(1);
        }
        int s = 0;
        foreach (var section in sections)
        {
            flow.Next(8);
            flow.Text("heading-" + s, section.Heading, UiFont.Bold);
            flow.Next(2);
            int l = 0;
            foreach (string line in section.Lines)
            {
                flow.Text($"fact-{s}-{l++}", "- " + line, UiFont.Small, 6);
                flow.Next(1);
            }
            s++;
        }
        flow.Next(4);
    }

    /// <summary>The status line and the footer, wrapped.</summary>
    public static void Footer(Flow flow, string status, string footer)
    {
        flow.Text("status", status, UiFont.Detail);
        flow.Next(status.Length > 0 ? 4 : 0);
        flow.Text("footer", footer, UiFont.Small);
        flow.Next(0);
    }

    /// <summary>The padding between the dialog's edge and its content (the game's <c>ElementToDialogPadding</c>).</summary>
    public const double DialogPadding = 20;

    /// <summary>The tab row's height above the dialog.</summary>
    public const double TabsHeight = 26;

    /// <summary>A long tab's scroll area is never shorter than this (unless its content is).</summary>
    public const double MinScroll = 80;

    public const double ScrollbarWidth = 20;

    /// <summary>The whole window laid out: the header and fixed body (<see cref="Main"/>, with a
    /// <c>scroll</c> element where a long tab's content goes), that content (<see cref="Scroll"/>,
    /// <see cref="Visible"/> of it shown at once), the footer below, and where it all goes on screen.</summary>
    public sealed record Frame(Flow Main, Flow? Scroll, double Visible, Flow Footer, Placement Place, double Width, double Height);

    /// <summary>
    /// Lays the window out for a screen: the header, then <paramref name="body"/> (the Trade tab, or
    /// what sits above a long tab's scroll area), then <paramref name="scroll"/>'s content in a
    /// scroll area as tall as the screen leaves room for, then the status and the footer; placed by
    /// <see cref="WindowPlacement"/> below the right-hand HUDs. All in unscaled units but the screen
    /// and the HUDs' rectangles, which are pixels.
    /// </summary>
    public static Frame Compose(ITextMeasure m, SlotMetrics slots, string header, string? barLabel, Action<Flow>? body, Action<Flow>? scroll,
        string status, string footer, double screenW, double screenH, double scale, IReadOnlyList<ScreenRect> top, IReadOnlyList<ScreenRect> bottom)
    {
        double width = ContentWidth(slots);
        var main = Header(m, width, header, barLabel);
        body?.Invoke(main);
        var foot = new Flow(m, width);
        Footer(foot, status, footer);
        Flow? content = null;
        if (scroll != null)
        {
            content = new Flow(m, width);
            scroll(content);
        }
        double fixedH = main.Bottom + foot.Bottom + 2 * DialogPadding;
        double minBody = content is null ? 0 : Math.Min(content.Bottom, MinScroll) + 6;
        var place = WindowPlacement.Place(screenW, screenH, (width + 2 * DialogPadding) * scale, (fixedH + minBody) * scale,
            TabsHeight * scale, 10 * scale, top, bottom);
        double visible = 0;
        if (content != null)
        {
            double room = place.MaxHeight / scale - fixedH - 6;
            visible = Math.Max(Math.Min(MinScroll, content.Bottom), Math.Min(content.Bottom, room));
            if (content.Bottom > visible + 0.5)
            {
                // With a scrollbar the content is narrower, and laid out again for it.
                content = new Flow(m, width - ScrollbarWidth);
                scroll!(content);
            }
            main.Element("scroll", 0, width, visible);
            main.Next(6);
        }
        var footer2 = new Flow(m, width, main.Bottom);
        Footer(footer2, status, footer);
        double height = footer2.Bottom + 2 * DialogPadding;
        return new Frame(main, content, visible, footer2, place, width, height);
    }

    /// <summary>Everything that overlaps or runs past <paramref name="width"/>, for the tests and the render harness.</summary>
    public static List<string> Problems(IEnumerable<UiBox> boxes, double width)
    {
        var list = boxes.Where(b => b.H > 0).ToList();
        var problems = new List<string>();
        foreach (var b in list)
            if (b.X < -0.5 || b.Right > width + 0.5) problems.Add($"{b.Key} runs past the width ({b.X:0}..{b.Right:0} of {width:0})");
        for (int i = 0; i < list.Count; i++)
            for (int j = i + 1; j < list.Count; j++)
                if (list[i].Overlaps(list[j])) problems.Add($"{list[i].Key} overlaps {list[j].Key}");
        return problems;
    }
}
