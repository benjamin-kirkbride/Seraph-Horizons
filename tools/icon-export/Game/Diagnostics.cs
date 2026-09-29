using System.Collections;
using System.Globalization;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SeraphHorizons.IconExport.Game;

/// <summary>
/// Writes what the client log needs to explain a bad export: the render state the export found,
/// the renderers that run before it, and the mods loaded. Everything that is not in the API is
/// read by reflection and may be "unavailable"; nothing here changes any state.
/// </summary>
internal static class Diagnostics
{
    public const string Prefix = "[seraphiconfix] ";

    public static void Log(ICoreClientAPI api, string line) => api.Logger.Notification("{0}", Prefix + line);

    public static void Warn(ICoreClientAPI api, string line) => api.Logger.Warning("{0}", Prefix + line);

    public static void Block(ICoreClientAPI api, GlReader gl, string title)
    {
        IRenderAPI r = api.Render;
        IShaderProgram? gui = SafeGui(api);
        IShaderProgram? active = r.CurrentActiveShader;
        FrameBufferRef? fb = r.CurrentFrameBuffer;
        Log(api, $"---- diagnostics: {title} ----");
        Log(api, $"game {GameVersion.OverallVersion}, mod {api.ModLoader.GetMod("seraphiconfix")?.Info.Version ?? "?"}, "
            + $"render stage {r.CurrentRenderStage}, window {r.FrameWidth}x{r.FrameHeight}");
        Log(api, "active shader: " + ShaderName(active, gui));
        Log(api, "framebuffer: " + (fb == null ? "default (null)" : $"fbo {fb.FboId} {fb.Width}x{fb.Height}")
            + $", scissor stack {r.ScissorStack?.Count.ToString(CultureInfo.InvariantCulture) ?? "?"}");
        Log(api, "gui uniforms: " + gl.Uniforms(gui));
        Log(api, "gui uniforms not at the export's defaults: " + Nonempty(gl.NonDefault(gui)));
        Log(api, "gl: " + gl.State());
        Log(api, "ortho renderers: " + OrthoRenderers(api));
        List<string> mods = api.ModLoader.Mods.Select(m => $"{m.Info.ModID}@{m.Info.Version}").OrderBy(s => s, StringComparer.Ordinal).ToList();
        Log(api, $"mods ({mods.Count}): " + string.Join(", ", mods));
        Log(api, "---- end of diagnostics ----");
    }

    public static IShaderProgram? SafeGui(ICoreClientAPI api)
    {
        try
        {
            return api.Render.GetEngineShader(EnumShaderProgram.Gui);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string ShaderName(IShaderProgram? shader, IShaderProgram? gui) =>
        shader == null ? "none" : shader == gui ? $"gui (program {shader.ProgramId})"
            : $"{shader.PassName ?? "unnamed"} (program {shader.ProgramId}, domain {shader.AssetDomain ?? "?"})";

    private static string Nonempty(string s) => s.Length == 0 ? "none" : s;

    /// <summary>
    /// The Ortho stage's renderers in the order they run, with the assembly that registered
    /// each: whatever runs before the export can leave the GUI shader in a bad state.
    /// </summary>
    public static string OrthoRenderers(ICoreClientAPI api)
    {
        try
        {
            object world = api.World;
            object? events = world.GetType().GetField("eventManager", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(world);
            if (events?.GetType().GetField("renderersByStage", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(events) is not Array stages)
            {
                return "unavailable (renderer list not found)";
            }
            if (stages.GetValue((int)EnumRenderStage.Ortho) is not IList handlers)
            {
                return "unavailable";
            }
            var parts = new List<string>();
            foreach (object? h in handlers)
            {
                if (h == null)
                {
                    continue;
                }
                Type ht = h.GetType();
                var renderer = ht.GetField("Renderer")?.GetValue(h) as IRenderer;
                string name = ht.GetField("ProfilingName")?.GetValue(h) as string ?? "?";
                string owner = renderer == null ? "?" : Owner(renderer);
                parts.Add($"{renderer?.RenderOrder.ToString("0.###", CultureInfo.InvariantCulture) ?? "?"} {name} [{owner}]");
            }
            return string.Join("; ", parts);
        }
        catch (Exception e)
        {
            return "unavailable (" + e.GetType().Name + ": " + e.Message + ")";
        }
    }

    private static string Owner(IRenderer renderer)
    {
        if (renderer is DummyRenderer { action: { } action })
        {
            MethodInfo m = action.Method;
            return $"{m.DeclaringType?.FullName}.{m.Name} in {m.DeclaringType?.Assembly.GetName().Name}";
        }
        Type t = renderer.GetType();
        return $"{t.FullName} in {t.Assembly.GetName().Name}";
    }
}
