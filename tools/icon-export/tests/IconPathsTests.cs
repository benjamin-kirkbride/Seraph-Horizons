using SeraphHorizons.IconExport.Core;

namespace SeraphHorizons.IconExport.Tests;

public class IconPathsTests
{
    // Expected paths are written out by hand from the rule in the doc comment of IconPaths:
    // keep a-z 0-9 _ -, keep "/" as a directory, percent-encode every other UTF-8 byte.
    public static TheoryData<string, IconKind, string> Vectors => new()
    {
        { "game:crate", IconKind.Block, "game/block/crate.png" },
        { "materialneeds:crate", IconKind.Block, "materialneeds/block/crate.png" },
        { "game:pickaxe-copper", IconKind.Item, "game/item/pickaxe-copper.png" },
        // Real codes from the pack's recipe export.
        { "game:clutter-art/bottle", IconKind.Block, "game/block/clutter-art/bottle.png" },
        { "tankardsandgoblets:t&g-winebottle-blue", IconKind.Item, "tankardsandgoblets/item/t%26g-winebottle-blue.png" },
        { "tankardsandgoblets:tankard-woodtype-acacia.-bismuth", IconKind.Item, "tankardsandgoblets/item/tankard-woodtype-acacia%2E-bismuth.png" },
        // Upper case would merge with lower case on Windows and macOS.
        { "game:Foo", IconKind.Item, "game/item/%46oo.png" },
        // A device name on Windows, even with an extension.
        { "game:con", IconKind.Item, "game/item/%63on.png" },
        { "game:lpt1/aux", IconKind.Item, "game/item/%6Cpt1/%61ux.png" },
        // Segments that cannot be directory names keep their slashes, encoded.
        { "game:a//b", IconKind.Item, "game/item/a%2F%2Fb.png" },
        { "game:/lead", IconKind.Item, "game/item/%2Flead.png" },
        { "game:..", IconKind.Item, "game/item/%2E%2E.png" },
        { "game:x.png", IconKind.Item, "game/item/x%2Epng.png" },
        { "game:ümlaut", IconKind.Item, "game/item/%C3%BCmlaut.png" },
        { "game:100%", IconKind.Item, "game/item/100%25.png" },
        { "game:a\\b", IconKind.Item, "game/item/a%5Cb.png" },
    };

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Code_to_path(string code, IconKind kind, string expected)
    {
        Assert.Equal(expected, IconPaths.ToRelativePath(code, kind));
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void Path_to_code_is_the_exact_inverse(string code, IconKind kind, string path)
    {
        Assert.True(IconPaths.TryParse(path, out string gotCode, out IconKind gotKind));
        Assert.Equal(code, gotCode);
        Assert.Equal(kind, gotKind);
    }

    [Fact]
    public void Same_path_in_two_domains_gives_two_files()
    {
        Assert.NotEqual(IconPaths.ToRelativePath("game:crate", IconKind.Block),
            IconPaths.ToRelativePath("materialneeds:crate", IconKind.Block));
    }

    [Fact]
    public void Item_and_block_with_one_code_give_two_files()
    {
        Assert.Equal("game/item/torch.png", IconPaths.ToRelativePath("game:torch", IconKind.Item));
        Assert.Equal("game/block/torch.png", IconPaths.ToRelativePath("game:torch", IconKind.Block));
    }

    [Fact]
    public void Windows_separators_are_read_as_slashes()
    {
        Assert.True(IconPaths.TryParse("game\\block\\clutter-art\\bottle.png", out string code, out _));
        Assert.Equal("game:clutter-art/bottle", code);
    }

    [Theory]
    [InlineData("game/item/Foo.png")]              // raw upper case: the writer encodes it
    [InlineData("game/item/t%2cg.png")]            // lower-case hex
    [InlineData("game/item/a%2Fb.png")]            // "a/b" is written as a directory
    [InlineData("game/item/%2E.png.png")]          // unencoded dot
    [InlineData("game/thing/stick.png")]           // not item or block
    [InlineData("game/item/stick.jpg")]
    [InlineData("game/item/stick.png.tmp")]
    [InlineData("game/item/%ZZ.png")]
    [InlineData("game/item/%C3.png")]              // half a UTF-8 character
    [InlineData("game/item/con.png")]              // reserved name left unencoded
    [InlineData("game/item/.png")]                 // empty path
    [InlineData("Game/item/stick.png")]
    [InlineData("stick.png")]
    public void Paths_the_writer_never_produces_are_rejected(string path)
    {
        Assert.False(IconPaths.TryParse(path, out _, out _));
    }

    [Theory]
    [InlineData("stick")]
    [InlineData("game:")]
    [InlineData(":stick")]
    [InlineData("Game:stick")]
    [InlineData("game:a b")]
    [InlineData("game:a:b")]
    [InlineData("my.mod:stick")]
    public void Invalid_codes_have_no_path(string code)
    {
        Assert.False(IconCode.IsValid(code));
        Assert.Throws<ArgumentException>(() => IconPaths.ToRelativePath(code, IconKind.Item));
    }
}
