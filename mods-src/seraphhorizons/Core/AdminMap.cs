using System.Text.Json;
using System.Text.Json.Serialization;

namespace SeraphHorizons.Mod.Core;

/// <summary>An outlined square or rectangle on the admin map layer, in world blocks.</summary>
public sealed record OverlayRect(int X1, int Z1, int X2, int Z2, int Color, string? Label = null);

/// <summary>A filled marker, <see cref="Size"/> pixels across whatever the zoom.</summary>
public sealed record OverlayMark(int X, int Z, int Color, string? Label = null, int Size = 8);

/// <summary>A circle outline in world blocks (districts, camp reach).</summary>
public sealed record OverlayRing(int X, int Z, int Radius, int Color, string? Label = null);

/// <summary>A line between two world points (delivery routes, #459).</summary>
public sealed record OverlayLine(int X1, int Z1, int X2, int Z2, int Color, string? Label = null);

/// <summary>
/// What the admin map layer draws for one overlay (<c>ore</c> or <c>trade</c>): the server builds it
/// around an admin and sends it as JSON (<c>AdminMapPacket</c>); the client draws it on its world
/// map. Colours are ARGB ints as the game's <c>ColorUtil</c> makes them.
/// </summary>
public sealed class MapOverlay
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Key { get; set; } = "";

    /// <summary>A line for the map's hover text when nothing is under the mouse, e.g. the legend.</summary>
    public string? Legend { get; set; }

    public List<OverlayRect> Rects { get; set; } = [];
    public List<OverlayMark> Marks { get; set; } = [];
    public List<OverlayRing> Rings { get; set; } = [];
    public List<OverlayLine> Lines { get; set; } = [];

    public int Count => Rects.Count + Marks.Count + Rings.Count + Lines.Count;

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static MapOverlay FromJson(string json) => JsonSerializer.Deserialize<MapOverlay>(json, Options) ?? new MapOverlay();

    /// <summary>ARGB from bytes, as <c>ColorUtil.ColorFromRgba</c> packs it for the GUI shader.</summary>
    public static int Argb(int r, int g, int b, int a = 255) => (a << 24) | (b << 16) | (g << 8) | r;

    /// <summary>A colour for a name (a metal, a trader type), stable across runs: hue from FNV-1a.</summary>
    public static int ColorFor(string name, int alpha = 255)
    {
        uint h = 2166136261;
        foreach (char c in name)
        {
            h ^= c;
            h *= 16777619;
        }
        double hue = h % 360 / 60.0;
        double x = 1 - Math.Abs(hue % 2 - 1);
        (double r, double g, double b) = (int)hue switch
        {
            0 => (1d, x, 0d),
            1 => (x, 1d, 0d),
            2 => (0d, 1d, x),
            3 => (0d, x, 1d),
            4 => (x, 0d, 1d),
            _ => (1d, 0d, x),
        };
        // Lifted towards white so every colour reads on the dark map.
        return Argb((int)(80 + r * 175), (int)(80 + g * 175), (int)(80 + b * 175), alpha);
    }
}
