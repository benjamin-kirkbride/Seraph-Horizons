namespace SeraphHorizons.Mod.CreativeModTabs.Core;

/// <summary>
/// Geometry of the scrolling mod tab strip, in pixels: <paramref name="count"/> tabs of
/// <c>tabHeight</c>, each followed by <c>spacing</c>, seen through a viewport of <c>viewport</c> pixels
/// scrolled down by an offset. Game-independent, so tests/ runs it without the game.
/// </summary>
public static class TabScroll
{
    public static double ContentHeight(int count, double tabHeight, double spacing) =>
        count <= 0 ? 0 : count * (tabHeight + spacing);

    public static double TabTop(int index, double tabHeight, double spacing) => index * (tabHeight + spacing);

    public static double MaxOffset(double content, double viewport) => Math.Max(0, content - viewport);

    public static double Clamp(double offset, double content, double viewport) =>
        Math.Clamp(double.IsFinite(offset) ? offset : 0, 0, MaxOffset(content, viewport));

    /// <summary>The offset after the wheel turned by <paramref name="delta"/> notches (positive is up),
    /// <paramref name="step"/> pixels each.</summary>
    public static double Wheel(double offset, double delta, double step, double content, double viewport) =>
        Clamp(offset - delta * step, content, viewport);

    /// <summary>The nearest offset at which tab <paramref name="index"/> is wholly in view.</summary>
    public static double EnsureVisible(double offset, int index, double tabHeight, double spacing, double content, double viewport)
    {
        if (index < 0) return Clamp(offset, content, viewport);
        double top = TabTop(index, tabHeight, spacing), bottom = top + tabHeight;
        if (top < offset) offset = top;
        else if (bottom > offset + viewport) offset = bottom - viewport;
        return Clamp(offset, content, viewport);
    }

    /// <summary>The tab under <paramref name="y"/> pixels below the viewport's top, or -1 (outside the
    /// viewport, past the last tab, or in the gap below a tab).</summary>
    public static int HitTest(double y, double offset, int count, double tabHeight, double spacing, double viewport)
    {
        if (y < 0 || y >= viewport || count <= 0) return -1;
        double contentY = y + offset;
        int i = (int)Math.Floor(contentY / (tabHeight + spacing));
        if (i < 0 || i >= count) return -1;
        return contentY - TabTop(i, tabHeight, spacing) < tabHeight ? i : -1;
    }

    /// <summary>The scroll indicator's top and length within the viewport, or null when nothing overflows.</summary>
    public static (double Top, double Length)? Indicator(double offset, double content, double viewport, double minLength)
    {
        if (content <= viewport || viewport <= 0) return null;
        double length = Math.Max(minLength, viewport * viewport / content);
        length = Math.Min(length, viewport);
        double top = (viewport - length) * (Clamp(offset, content, viewport) / MaxOffset(content, viewport));
        return (top, length);
    }
}
