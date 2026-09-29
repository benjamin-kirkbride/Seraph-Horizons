using SeraphHorizons.IconExport.Core;

namespace SeraphHorizons.IconExport.Tests;

public class ImageCheckTests
{
    private static readonly byte[] Clear = { 0, 0, 0, 0 };

    // An 8x8 image: a transparent border and a shaded white cube in the middle, the three shades
    // found in the pack's broken export (255, 224 and 184 grey on the three visible faces).
    private static byte[] ShadedWhite()
    {
        var px = new List<byte>();
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                byte[] p = x is 0 or 7 || y is 0 or 7 ? Clear
                    : y < 3 ? new byte[] { 255, 255, 255, 255 }
                    : x < 4 ? new byte[] { 224, 224, 224, 255 }
                    : new byte[] { 184, 184, 184, 255 };
                px.AddRange(p);
            }
        }
        return px.ToArray();
    }

    [Fact]
    public void Shaded_white_shape_is_untextured()
    {
        Assert.Equal(ImageCheck.Untextured, ImageChecks.ClassifyRgba(ShadedWhite()));
    }

    [Fact]
    public void One_coloured_pixel_makes_it_textured()
    {
        byte[] img = ShadedWhite();
        int i = (3 * 8 + 3) * 4;
        img[i] = 200;
        img[i + 1] = 120;
        img[i + 2] = 80;
        Assert.Equal(ImageCheck.Ok, ImageChecks.ClassifyRgba(img));
    }

    [Fact]
    public void Fully_transparent_is_counted_apart()
    {
        byte[] img = new byte[8 * 8 * 4];
        // Colour under zero alpha is invisible and does not count.
        img[0] = 255;
        img[5] = 255;
        Assert.Equal(ImageCheck.Transparent, ImageChecks.ClassifyRgba(img));
    }

    [Fact]
    public void Grey_texture_with_many_shades_is_not_flagged()
    {
        // Stone or iron: no colour, but far more than 16 shades.
        var img = new List<byte>();
        for (int v = 60; v < 100; v++)
        {
            img.AddRange(new[] { (byte)v, (byte)v, (byte)v, (byte)255 });
        }
        Assert.Equal(ImageCheck.Ok, ImageChecks.ClassifyRgba(img.ToArray()));
    }

    [Fact]
    public void Sixteen_grey_shades_are_untextured_seventeen_are_not()
    {
        var img = new List<byte>();
        for (int v = 0; v < 16; v++)
        {
            img.AddRange(new[] { (byte)(100 + v), (byte)(100 + v), (byte)(100 + v), (byte)255 });
        }
        Assert.Equal(ImageCheck.Untextured, ImageChecks.ClassifyRgba(img.ToArray()));
        img.AddRange(new byte[] { 200, 200, 200, 255 });
        Assert.Equal(ImageCheck.Ok, ImageChecks.ClassifyRgba(img.ToArray()));
    }

    [Fact]
    public void Chroma_of_two_is_grey_three_is_colour()
    {
        Assert.Equal(ImageCheck.Untextured, ImageChecks.ClassifyRgba(new byte[] { 100, 101, 102, 255 }));
        Assert.Equal(ImageCheck.Ok, ImageChecks.ClassifyRgba(new byte[] { 100, 100, 103, 255 }));
    }

    [Fact]
    public void Argb_pixels_from_the_game()
    {
        // BitmapRef.Pixels is 0xAARRGGBB.
        Assert.Equal(ImageCheck.Ok, ImageChecks.ClassifyArgb(new[] { unchecked((int)0xFFFF0000) }));
        Assert.Equal(ImageCheck.Untextured, ImageChecks.ClassifyArgb(new[] { unchecked((int)0xFFE0E0E0), unchecked((int)0xFFB8B8B8) }));
        // Alpha is the top byte: red at alpha 0 is invisible, and alpha 1 is visible.
        Assert.Equal(ImageCheck.Transparent, ImageChecks.ClassifyArgb(new[] { 0x00FF0000, 0x0000FF00 }));
        Assert.Equal(ImageCheck.Ok, ImageChecks.ClassifyArgb(new[] { 0x01FF0000 }));
    }
}
