using SeraphHorizons.IconExport.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.IconExport.Game;

/// <summary>
/// Draws one stack into an offscreen framebuffer and reads it back, the way the game's own
/// .blockitempngexport does (SystemClientCommands.ExportPNGfromItemstack), but without trusting
/// the state that earlier renderers left: before every draw the GUI shader is made the active
/// program and every uniform its shaders read is set to a known value. Runs in the Ortho render
/// stage only, where the engine keeps the GUI shader active.
/// </summary>
internal sealed class IconDrawer : IDisposable
{
    private static readonly float[] Transparent = { 0f, 0f, 0f, 0f };

    private readonly ICoreClientAPI _api;
    private readonly IReadOnlyList<UniformDefault> _reset;
    private FrameBufferRef? _fb;

    public IconDrawer(ICoreClientAPI api, int size, IReadOnlyList<UniformDefault> reset)
    {
        _api = api;
        Size = size;
        _reset = reset;
    }

    public int Size { get; }

    /// <summary>What <see cref="EndFrame"/> puts back.</summary>
    internal sealed record Saved(FrameBufferRef? Framebuffer, IShaderProgram? Shader);

    public Saved BeginFrame()
    {
        IRenderAPI r = _api.Render;
        var saved = new Saved(r.CurrentFrameBuffer, r.CurrentActiveShader);
        _fb ??= CreateFramebuffer();
        r.CurrentFrameBuffer = _fb; // binds it and sets the viewport to its size
        // A scissor or a colour/depth mask left on by a dialog would clip or block the clear
        // and the draw; blend, depth and culling as the game's export sets them.
        r.GlScissorFlag(false);
        r.GlColorMask(true, true, true, true);
        r.GLDepthMask(true);
        r.GLEnableDepthTest();
        r.GlDisableCullFace();
        r.GlToggleBlend(true);
        r.OrthoMode(Size, Size);
        return saved;
    }

    public void EndFrame(Saved saved)
    {
        IRenderAPI r = _api.Render;
        r.PerspectiveMode(); // pops the matrices OrthoMode pushed
        if (r.CurrentActiveShader != saved.Shader)
        {
            r.CurrentActiveShader?.Stop();
            saved.Shader?.Use();
        }
        r.CurrentFrameBuffer = saved.Framebuffer;
        if (saved.Framebuffer == null)
        {
            // Binding the default framebuffer does not reset the viewport.
            r.GlViewport(0, 0, r.FrameWidth, r.FrameHeight);
        }
        if (r.ScissorStack is { Count: > 0 })
        {
            r.GlScissorFlag(true);
        }
    }

    /// <summary>Draws <paramref name="stack"/>; the caller disposes the bitmap.</summary>
    public BitmapRef Draw(ItemStack stack)
    {
        IRenderAPI r = _api.Render;
        IShaderProgram gui = r.GetEngineShader(EnumShaderProgram.Gui);
        IShaderProgram? active = r.CurrentActiveShader;
        if (active != gui)
        {
            // A custom item renderer, or a renderer earlier in the frame, left another program on.
            active?.Stop();
            gui.Use();
        }
        ApplyDefaults(gui);
        r.ClearFrameBuffer(_fb, Transparent);
        r.RenderItemstackToGui(new DummySlot(stack), Size / 2, Size / 2, 500, Size / 2, ColorUtil.WhiteArgb,
            shading: true, rotate: false, showStackSize: false);
        return r.GrabScreenshot(Size, Size, scaleScreenshot: false, flip: true, withAlpha: true);
    }

    private void ApplyDefaults(IShaderProgram gui)
    {
        foreach (UniformDefault u in _reset)
        {
            // The driver drops uniforms a shader does not use; setting one of those throws.
            if (!gui.HasUniform(u.Name))
            {
                continue;
            }
            float[] v = u.Value;
            switch (u.Type)
            {
                case UniformType.Float:
                    gui.Uniform(u.Name, v[0]);
                    break;
                case UniformType.Int:
                    gui.Uniform(u.Name, (int)v[0]);
                    break;
                case UniformType.Vec2:
                    gui.Uniform(u.Name, v[0], v[1]);
                    break;
                case UniformType.Vec4:
                    gui.Uniform(u.Name, v[0], v[1], v[2], v[3]);
                    break;
            }
        }
    }

    private FrameBufferRef CreateFramebuffer()
    {
        // The same attachments as the game's export: RGBA8 colour and a 32-bit depth buffer.
        return _api.Render.CreateFrameBuffer(new FramebufferAttrs("SeraphIconExport", Size, Size)
        {
            Attachments = new[]
            {
                new FramebufferAttrsAttachment
                {
                    AttachmentType = EnumFramebufferAttachment.ColorAttachment0,
                    Texture = new RawTexture
                    {
                        Width = Size,
                        Height = Size,
                        PixelFormat = EnumTexturePixelFormat.Rgba,
                        PixelInternalFormat = EnumTextureInternalFormat.Rgba8,
                    },
                },
                new FramebufferAttrsAttachment
                {
                    AttachmentType = EnumFramebufferAttachment.DepthAttachment,
                    Texture = new RawTexture
                    {
                        Width = Size,
                        Height = Size,
                        PixelFormat = EnumTexturePixelFormat.DepthComponent,
                        PixelInternalFormat = EnumTextureInternalFormat.DepthComponent32,
                    },
                },
            },
        });
    }

    public void Dispose()
    {
        if (_fb == null)
        {
            return;
        }
        int[] colour = _fb.ColorTextureIds ?? Array.Empty<int>();
        int depth = _fb.DepthTextureId;
        // DestroyFrameBuffer keeps the attachment textures, so they are deleted here.
        _api.Render.DestroyFrameBuffer(_fb);
        foreach (int id in colour.Append(depth).Where(id => id != 0))
        {
            _api.Render.GLDeleteTexture(id);
        }
        _fb = null;
    }
}
