using SeraphHorizons.IconExport.Core;

namespace SeraphHorizons.IconExport.Tests;

public class CommandTests
{
    private static ExportRequest Ok(string sub, string rest)
    {
        Assert.True(ExportRequest.TryParse(sub, rest, out ExportRequest r, out string error), error);
        return r;
    }

    private static string Error(string sub, string rest)
    {
        Assert.False(ExportRequest.TryParse(sub, rest, out _, out string error));
        return error;
    }

    [Fact]
    public void One_with_default_size()
    {
        ExportRequest r = Ok("one", "game:pickaxe-copper");
        Assert.Equal(ExportMode.One, r.Mode);
        Assert.Equal("game:pickaxe-copper", r.Argument);
        Assert.Equal(128, r.Size);
        Assert.False(r.Force);
        Assert.Null(r.Reset);
    }

    [Fact]
    public void List_with_a_quoted_path_size_and_force()
    {
        ExportRequest r = Ok("list", "\"/home/me/my exports/recipes.json\" 64 --force");
        Assert.Equal(ExportMode.List, r.Mode);
        Assert.Equal("/home/me/my exports/recipes.json", r.Argument);
        Assert.Equal(64, r.Size);
        Assert.True(r.Force);
    }

    [Fact]
    public void All_with_size_and_domain_and_out()
    {
        ExportRequest r = Ok("all", "100 materialneeds --out /tmp/x");
        Assert.Equal(100, r.Size);
        Assert.Equal("materialneeds", r.Argument);
        Assert.Equal("/tmp/x", r.OutDir);
        Assert.Null(Ok("all", "").Argument);
    }

    [Fact]
    public void Hand_takes_only_a_size()
    {
        Assert.Equal(256, Ok("hand", "256").Size);
        Assert.Contains("too many", Error("hand", "256 extra"));
    }

    [Fact]
    public void Reset_option()
    {
        Assert.Equal("noTexture", Ok("one", "game:stick --reset noTexture").Reset);
        Assert.Equal("none", Ok("one", "game:stick --reset none").Reset);
        Assert.Null(Ok("one", "game:stick --reset all").Reset);
    }

    [Theory]
    [InlineData("one", "", "needs an item code")]
    [InlineData("one", "stick", "no domain")]
    [InlineData("one", "game:stick 8", "size must be")]
    [InlineData("one", "game:stick 2000", "size must be")]
    [InlineData("one", "game:stick big", "size must be")]
    [InlineData("list", "", "needs a file")]
    [InlineData("list", "\"unclosed", "unclosed quote")]
    [InlineData("all", "64 game extra", "too many")]
    [InlineData("all", "--out", "needs a value")]
    [InlineData("all", "--fast", "unknown option")]
    [InlineData("bogus", "", "unknown subcommand")]
    public void Errors(string sub, string rest, string message)
    {
        Assert.Contains(message, Error(sub, rest));
    }

    [Fact]
    public void Tokenizer()
    {
        Assert.True(ExportRequest.Tokenize("  a  \"b c\" d\"e\" \"\" ", out List<string> t, out _));
        Assert.Equal(new[] { "a", "b c", "de", "" }, t);
    }
}

public class GuiUniformTests
{
    // Every uniform gui.vsh and gui.fsh (1.22.7) declare, less the matrices that
    // RenderItemstackToGui always sets and lightPosition, copied by hand from the shader files.
    private static readonly string[] FromTheShaders =
    {
        "noTexture", "alphaTest", "darkEdges", "tempGlowMode", "transparentCenter", "tex2d", "normalShaded",
        "sepiaLevel", "damageEffect", "tex2dOverlay", "overlayOpacity", "overlayTextureSize", "baseTextureSize",
        "baseUvOrigin", "rgbaIn", "rgbaGlowIn", "extraGlow", "applyModelMat", "applyColor", "applyAnimation",
    };

    [Fact]
    public void Defaults_cover_every_uniform_the_shaders_read()
    {
        Assert.Equal(FromTheShaders.OrderBy(s => s), GuiUniforms.Defaults.Select(u => u.Name).OrderBy(s => s));
    }

    [Fact]
    public void Textures_on_and_animation_off()
    {
        UniformDefault U(string n) => GuiUniforms.Defaults.Single(u => u.Name == n);
        Assert.Equal(new[] { 0f }, U("noTexture").Value);
        Assert.Equal(new[] { 0f }, U("applyAnimation").Value);
        Assert.Equal(new[] { 1f, 1f, 1f, 1f }, U("rgbaIn").Value);
        Assert.Equal(new[] { 1f }, U("tex2dOverlay").Value);
    }

    [Fact]
    public void Reset_selection()
    {
        Assert.True(GuiUniforms.TrySelect(null, out var all));
        Assert.Equal(20, all.Count);
        Assert.True(GuiUniforms.TrySelect("none", out var none));
        Assert.Empty(none);
        Assert.True(GuiUniforms.TrySelect("noTexture", out var one));
        Assert.Equal("noTexture", one.Single().Name);
        Assert.False(GuiUniforms.TrySelect("notexture", out _));
    }
}
