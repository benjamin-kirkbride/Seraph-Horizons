namespace SeraphHorizons.Mod.Trading.Window.Core;

/// <summary>A screen rectangle in pixels.</summary>
public readonly record struct ScreenRect(double X, double Y, double W, double H)
{
    public double Right => X + W;
    public double Bottom => Y + H;
}

/// <summary>Where the trade window goes: its top-left corner in pixels (the tab row sits above it),
/// and how tall its body may be.</summary>
/// <param name="Below">True when it sits under the right-hand top stack (the minimap and the
/// coordinates); false when there is no room under it and the window sits left of the stack.</param>
public readonly record struct Placement(double X, double Y, double MaxHeight, bool Below);

/// <summary>
/// The trade window's place on screen (the playtest after #436: its tab row ran over the minimap's
/// coordinate line). The game stacks the minimap (250 px, <c>EnumDialogArea.RightTop</c>, unless the
/// player moved it) and, under it, the coordinates (<c>HudElementCoordinates</c>, which sets its
/// own offset below the first other right-top dialog every 250 ms); other mods may add their own
/// HUDs there. So the window, aligned right, starts below the lowest of them, its tab row included,
/// at whatever size and GUI scale; with no room for it there, it moves left of the stack instead.
/// Game-independent, in pixels: the window passes the open dialogs' bounds.
/// </summary>
public static class WindowPlacement
{
    /// <summary>Where the window goes.</summary>
    /// <param name="screenW">The window's width in pixels.</param>
    /// <param name="screenH">Its height.</param>
    /// <param name="width">The dialog's width, pixels.</param>
    /// <param name="height">The dialog's height without the tab row, pixels.</param>
    /// <param name="tabs">The tab row's height above the dialog, pixels.</param>
    /// <param name="pad">The gap to the screen's edges and to other HUDs, pixels.</param>
    /// <param name="rightTop">The open right-top HUDs and dialogs (not this one).</param>
    /// <param name="rightBottom">The open right-bottom ones (a minimap moved there).</param>
    public static Placement Place(double screenW, double screenH, double width, double height, double tabs, double pad,
        IEnumerable<ScreenRect> rightTop, IEnumerable<ScreenRect> rightBottom)
    {
        var top = rightTop.Where(r => r.W > 0 && r.H > 0 && r.Y < screenH / 2).ToList();
        var bottom = rightBottom.Where(r => r.W > 0 && r.H > 0 && r.Y > screenH / 2).ToList();
        double x = screenW - pad - width;
        double floor = bottom.Count > 0 ? Math.Min(screenH - pad, bottom.Min(r => r.Y) - pad) : screenH - pad;

        double stackBottom = top.Count > 0 ? top.Max(r => r.Bottom) : 0;
        double y = Math.Max(pad, stackBottom + pad) + tabs;
        if (y + height <= floor || top.Count == 0)
            return new Placement(x, y, Math.Max(0, floor - y), true);

        // No room under the stack: left of it, from the top.
        double left = top.Min(r => r.X);
        double y2 = pad + tabs;
        return new Placement(Math.Max(pad, Math.Min(x, left - pad - width)), y2, Math.Max(0, screenH - pad - y2), false);
    }
}
