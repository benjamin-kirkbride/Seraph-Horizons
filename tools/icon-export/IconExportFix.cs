using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace SeraphHorizons.IconExportFix;

/// <summary>
/// The client's icon export draws with the GUI shader in whatever state the renderer
/// before it left it. With the pack loaded that state has textures switched off, and
/// every icon comes out as a white shape. This puts the shader back to its defaults
/// just before the export draws.
/// </summary>
public class IconExportFixSystem : ModSystem
{
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        api.Event.RegisterRenderer(new ResetGuiShader(api), EnumRenderStage.Ortho, "seraphiconfix");
    }
}

internal sealed class ResetGuiShader : IRenderer
{
    private readonly ICoreClientAPI _api;
    private string? _reported;

    public ResetGuiShader(ICoreClientAPI api) => _api = api;

    // The export is registered in the same stage at 0.5; lower runs first.
    public double RenderOrder => 0.499;

    public int RenderRange => 0;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        var gui = _api.Render.GetEngineShader(EnumShaderProgram.Gui);
        var active = _api.Render.CurrentActiveShader;
        Report(active == null ? "none" : active == gui ? "gui" : active.PassName ?? "unnamed");
        if (active != gui)
        {
            active?.Stop();
            gui.Use();
        }

        gui.Uniform("noTexture", 0f);
        gui.Uniform("darkEdges", 0);
        gui.Uniform("transparentCenter", 0);
        gui.Uniform("overlayOpacity", 0f);
        gui.Uniform("damageEffect", 0f);
        gui.Uniform("sepiaLevel", 0f);
        gui.Uniform("tempGlowMode", 0);
        gui.Uniform("applyAnimation", 0);
    }

    /// <summary>Logs the shader found active, once per distinct value, to help find the cause.</summary>
    private void Report(string found)
    {
        if (found == _reported) return;
        _reported = found;
        _api.Logger.Notification("[seraphiconfix] shader active before the icon export: {0}", found);
    }

    public void Dispose()
    {
    }
}
