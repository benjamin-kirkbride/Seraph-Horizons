using System.Globalization;
using Vintagestory.API.Client;

namespace SeraphHorizons.IconExport.Game;

/// <summary>
/// <c>.seraphicons probe</c>: for one frame, records the active shader and every GUI shader
/// uniform that is not at its default at many points (render orders) of the Ortho stage and at
/// the start of the other stages, then logs them with the Ortho renderer list. A uniform that
/// changes between two probes was changed by a renderer whose order lies between them.
/// </summary>
internal static class GuiStateProbe
{
    private static readonly double[] OrthoOrders = { 0, 0.1, 0.3, 0.39, 0.45, 0.4995, 0.51, 0.8, 0.95, 0.99, 1.01, 1.05, 1.5, 5, 9.5, 1000 };

    private static readonly EnumRenderStage[] OtherStages =
    {
        EnumRenderStage.Before, EnumRenderStage.Opaque, EnumRenderStage.AfterPostProcessing, EnumRenderStage.AfterBlit,
        EnumRenderStage.AfterFinalComposition, EnumRenderStage.Done,
    };

    private static Run? _running;

    public static string Start(ICoreClientAPI api, GlReader gl)
    {
        if (_running != null)
        {
            return "a probe is already running";
        }
        if (gl.Problem != null)
        {
            return "cannot read GL state here: " + gl.Problem;
        }
        _running = new Run(api, gl);
        return "probing the next frame; see the [seraphiconfix] lines in the client log";
    }

    private sealed class Run
    {
        private readonly ICoreClientAPI _api;
        private readonly GlReader _gl;
        private readonly List<(EnumRenderStage Stage, Probe Probe)> _probes = new();
        private readonly List<string> _lines = new();
        // Idle until the next frame begins, so the record covers exactly one whole frame.
        private int _phase;

        public Run(ICoreClientAPI api, GlReader gl)
        {
            _api = api;
            _gl = gl;
            foreach (EnumRenderStage stage in OtherStages)
            {
                Register(stage, 0);
            }
            foreach (double order in OrthoOrders)
            {
                Register(EnumRenderStage.Ortho, order);
            }
        }

        private void Register(EnumRenderStage stage, double order)
        {
            var p = new Probe(this, stage, order);
            try
            {
                _api.Event.RegisterRenderer(p, stage, "seraphicons-probe");
                _probes.Add((stage, p));
            }
            catch (Exception e)
            {
                // A render order range can be reserved by another renderer.
                Diagnostics.Warn(_api, $"probe at {stage} {order}: {e.Message}");
            }
        }

        public void Hit(EnumRenderStage stage, double order)
        {
            if (_phase == 0 && stage == EnumRenderStage.Before)
            {
                _phase = 1;
            }
            if (_phase != 1)
            {
                return;
            }
            IShaderProgram? gui = Diagnostics.SafeGui(_api);
            string changed = _gl.NonDefault(gui);
            _lines.Add($"{stage} {order.ToString("0.####", CultureInfo.InvariantCulture)}: shader "
                + $"{Diagnostics.ShaderName(_api.Render.CurrentActiveShader, gui)}; not at default: {(changed.Length == 0 ? "none" : changed)}");
            // The line cap only matters if the Done stage never comes.
            if (stage == EnumRenderStage.Done || _lines.Count > 500)
            {
                _phase = 2;
                Diagnostics.Log(_api, "---- probe: GUI shader state across one frame ----");
                foreach (string line in _lines)
                {
                    Diagnostics.Log(_api, line);
                }
                Diagnostics.Log(_api, "ortho renderers: " + Diagnostics.OrthoRenderers(_api));
                Diagnostics.Log(_api, "---- end of probe ----");
                _api.ShowChatMessage("seraphicons: probe written to the client log");
                // Not from inside the render loop, which is iterating the list being changed.
                _api.Event.EnqueueMainThreadTask(() =>
                {
                    foreach ((EnumRenderStage s, Probe p) in _probes)
                    {
                        _api.Event.UnregisterRenderer(p, s);
                    }
                    _running = null;
                }, "seraphicons-probe-end");
            }
        }
    }

    private sealed class Probe : IRenderer
    {
        private readonly Run _run;
        private readonly EnumRenderStage _stage;

        public Probe(Run run, EnumRenderStage stage, double order)
        {
            _run = run;
            _stage = stage;
            RenderOrder = order;
        }

        public double RenderOrder { get; }

        public int RenderRange => 0;

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            try
            {
                _run.Hit(_stage, RenderOrder);
            }
            catch (Exception)
            {
                // A probe must never break a frame.
            }
        }

        public void Dispose()
        {
        }
    }
}
