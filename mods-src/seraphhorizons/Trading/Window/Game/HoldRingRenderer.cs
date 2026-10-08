using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Trading.Window;

/// <summary>
/// The hold-to-trade ring: the look of Carry On's hold-to-pick-up progress (its
/// <c>HudOverlayRenderer</c>, public domain): a light grey ring 24 px out (times the GUI scale), its
/// inner edge at three quarters, filling clockwise from the top in sixteen steps, fading in over
/// 0.2 s and out over 0.4 s, drawn at the mouse (the window has the mouse free). Each lot the server
/// confirms flashes it gold and swells it for a moment (<see cref="Pulse"/>) before it fills again.
///
/// <para>Drawn after the GUI (ortho stage, order 1.05, after the dialogs at 1.0) and in front of
/// them in depth. The first version drew at depth 600 and never showed: the game renders each open
/// dialog a step further forward (<c>GuiManager.OnRenderFrameGUI</c> translates by every dialog's
/// <c>ZSize</c> in turn, and the ortho stage keeps the depth test on), so with the HUD's dialogs and
/// this window open the dialog under the mouse sat well in front of 600 and hid the ring. It now
/// draws at <see cref="Depth"/>, near the front of the ortho projection's range (the modelview is
/// at −19849 and the far plane at 20001, so depth runs to about 19848).</para>
/// </summary>
public sealed class HoldRingRenderer : IRenderer, IDisposable
{
    private const int Steps = 16;
    private const float Radius = 24f;
    private const float Inner = 0.75f;
    private const float FadeIn = 0.2f, FadeOut = 0.4f;
    private const float PulseSeconds = 0.35f;

    /// <summary>In front of every dialog (their depth is the sum of the open dialogs' ZSize, a few
    /// thousand at most).</summary>
    public const float Depth = 19000f;

    private readonly ICoreClientAPI _capi;
    private MeshRef? _mesh;
    private float _alpha, _progress, _meshProgress = -1, _pulse;

    public HoldRingRenderer(ICoreClientAPI capi)
    {
        _capi = capi;
        capi.Event.RegisterRenderer(this, EnumRenderStage.Ortho, "seraphhorizons-holdring");
    }

    public double RenderOrder => 1.05;

    public int RenderRange => 10;

    public bool Visible { get; set; }

    public float Progress
    {
        get => _progress;
        set => _progress = GameMath.Clamp(value, 0, 1);
    }

    /// <summary>A lot went through: a short flash.</summary>
    public void Pulse() => _pulse = PulseSeconds;

    private void UpdateMesh(float progress)
    {
        int n = 1 + (int)Math.Ceiling(Steps * progress);
        var mesh = new MeshData(n * 2, n * 6, false, false, true, false);
        for (int i = 0; i < n; i++)
        {
            double angle = Math.Min(progress, i * (1f / Steps)) * Math.PI * 2;
            float x = (float)Math.Sin(angle), y = -(float)Math.Cos(angle);
            mesh.AddVertexSkipTex(x, y, 0, -1);
            mesh.AddVertexSkipTex(x * Inner, y * Inner, 0, -1);
            if (i > 0)
            {
                mesh.AddIndices([i * 2 - 2, i * 2 - 1, i * 2]);
                mesh.AddIndices([i * 2, i * 2 - 1, i * 2 + 1]);
            }
        }
        if (_mesh != null) _capi.Render.UpdateMesh(_mesh, mesh);
        else _mesh = _capi.Render.UploadMesh(mesh);
        _meshProgress = progress;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        _alpha = Math.Clamp(_alpha + deltaTime / (Visible ? FadeIn : -FadeOut), 0, 1);
        float pulse = _pulse > 0 ? _pulse / PulseSeconds : 0;
        _pulse = Math.Max(0, _pulse - deltaTime);
        // The flash shows a full ring whatever the next lot's progress is.
        float progress = pulse > 0 ? 1 : _progress;
        if (progress <= 0 || _alpha <= 0) return;
        if (Math.Abs(_meshProgress - progress) > 1e-4) UpdateMesh(progress);
        var render = _capi.Render;
        var shader = render.CurrentActiveShader;
        if (shader is null) return;
        var color = new Vec4f(0.8f + 0.15f * pulse, 0.8f + 0.02f * pulse, 0.8f - 0.45f * pulse, _alpha);
        shader.Uniform("rgbaIn", color);
        shader.Uniform("extraGlow", 0);
        shader.Uniform("applyColor", 0);
        shader.Uniform("tex2d", 0);
        shader.Uniform("noTexture", 1f);
        shader.UniformMatrix("projectionMatrix", render.CurrentProjectionMatrix);
        int x = _capi.Input.MouseGrabbed ? render.FrameWidth / 2 : _capi.Input.MouseX;
        int y = _capi.Input.MouseGrabbed ? render.FrameHeight / 2 : _capi.Input.MouseY;
        float radius = Radius * RuntimeEnv.GUIScale * (1 + 0.25f * pulse);
        render.GlPushMatrix();
        render.GlTranslate(x, y, Depth);
        render.GlScale(radius, radius, 0);
        shader.UniformMatrix("modelViewMatrix", render.CurrentModelviewMatrix);
        render.GlPopMatrix();
        render.RenderMesh(_mesh);
        shader.Uniform("noTexture", 0f);
    }

    public void Dispose()
    {
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.Ortho);
        if (_mesh != null) _capi.Render.DeleteMesh(_mesh);
        _mesh = null;
    }
}
