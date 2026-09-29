namespace SeraphHorizons.IconExport.Core;

public enum ImageCheck
{
    /// <summary>Not looked at (an icon adopted from an earlier run).</summary>
    Unchecked,
    Ok,
    /// <summary>Every pixel is fully transparent: nothing was drawn.</summary>
    Transparent,
    /// <summary>Only a few shades of grey: the white, shaded shape of an untextured draw.</summary>
    Untextured,
}

/// <summary>
/// Flags renders that look like the known failure: with the GUI shader's noTexture uniform set,
/// an icon is its vertex colour alone, i.e. white with two or three shades from face lighting.
/// An image counts as untextured when no visible pixel has colour (max - min channel above
/// <see cref="ChromaTolerance"/>) and it has at most <see cref="MaxShades"/> distinct visible
/// RGBA values. A real grey texture (stone, iron) has many shades, which is what the second
/// condition is for. tools/icons.py applies the same rule; keep the two in step.
/// </summary>
public static class ImageChecks
{
    public const int ChromaTolerance = 2;
    public const int MaxShades = 16;

    public static string Name(ImageCheck c) => c switch
    {
        ImageCheck.Ok => "ok",
        ImageCheck.Transparent => "transparent",
        ImageCheck.Untextured => "untextured",
        _ => "unchecked",
    };

    public static ImageCheck Parse(string? s) => s switch
    {
        "ok" => ImageCheck.Ok,
        "transparent" => ImageCheck.Transparent,
        "untextured" => ImageCheck.Untextured,
        _ => ImageCheck.Unchecked,
    };

    /// <summary>Pixels as R, G, B, A bytes.</summary>
    public static ImageCheck ClassifyRgba(ReadOnlySpan<byte> rgba)
    {
        var state = new State();
        for (int i = 0; i + 3 < rgba.Length; i += 4)
        {
            state.Add(rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]);
        }
        return state.Result();
    }

    /// <summary>Pixels as 0xAARRGGBB, the layout of the game's BitmapRef.Pixels.</summary>
    public static ImageCheck ClassifyArgb(ReadOnlySpan<int> argb)
    {
        var state = new State();
        foreach (int p in argb)
        {
            state.Add((byte)(p >> 16), (byte)(p >> 8), (byte)p, (byte)(p >>> 24));
        }
        return state.Result();
    }

    private sealed class State
    {
        private readonly HashSet<uint> _shades = new();
        private bool _visible;
        private bool _coloured;

        public void Add(byte r, byte g, byte b, byte a)
        {
            if (a == 0 || _coloured)
            {
                return;
            }
            _visible = true;
            int max = Math.Max(r, Math.Max(g, b));
            int min = Math.Min(r, Math.Min(g, b));
            if (max - min > ChromaTolerance)
            {
                _coloured = true;
                return;
            }
            if (_shades.Count <= MaxShades)
            {
                _shades.Add((uint)(r << 24 | g << 16 | b << 8 | a));
            }
        }

        public ImageCheck Result()
        {
            if (!_visible)
            {
                return ImageCheck.Transparent;
            }
            return !_coloured && _shades.Count <= MaxShades ? ImageCheck.Untextured : ImageCheck.Ok;
        }
    }
}
