using System.Diagnostics;
using SeraphHorizons.IconExport.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace SeraphHorizons.IconExport.Game;

/// <summary>
/// One export run: the planned jobs, the frame-by-frame rendering, the manifest and the totals.
/// Called once per frame from <see cref="ExportRenderer"/> in the Ortho stage.
/// </summary>
internal sealed class ExportSession
{
    private const int MaxPerFrame = 40;
    private const double MaxMsPerFrame = 25;
    private const double ProgressEveryMs = 5000;
    private const double SaveEveryMs = 30000;
    private const int LoggedFailures = 20;
    // Warn early, once this many icons are written, if they already look like the known bug.
    private const int EarlyCheckAfter = 50;

    private readonly ICoreClientAPI _api;
    private readonly GlReader _gl;
    private readonly string _outDir;
    private readonly string _label;
    private readonly Manifest _manifest;
    private readonly FrameScheduler _scheduler;
    private readonly IconDrawer _drawer;
    private readonly CustomRenderers _custom;
    private readonly RunTotals _totals = new();
    private readonly Dictionary<string, ItemStack> _stacks;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly IntervalTimer _progress;
    private readonly IntervalTimer _save;
    private bool _started;
    private bool _warnedEarly;
    private bool _stopRequested;

    public ExportSession(ICoreClientAPI api, GlReader gl, string outDir, string label, Manifest manifest, Plan plan,
        Dictionary<string, ItemStack> stacks, int size, IReadOnlyList<UniformDefault> reset, int failedBeforeStart)
    {
        _api = api;
        _gl = gl;
        _outDir = outDir;
        _label = label;
        _manifest = manifest;
        _stacks = stacks;
        _scheduler = new FrameScheduler(plan.ToRender, MaxPerFrame, MaxMsPerFrame);
        _drawer = new IconDrawer(api, size, reset);
        _custom = new CustomRenderers(api);
        _totals.Planned = plan.ToRender.Count;
        _totals.SkippedExisting = plan.SkippedExisting.Count;
        _totals.Failed = failedBeforeStart;
        _progress = new IntervalTimer(ProgressEveryMs, 0);
        _save = new IntervalTimer(SaveEveryMs, 0);
    }

    public bool Finished { get; private set; }

    public string OutDir => _outDir;

    public void RequestStop() => _stopRequested = true;

    public string Status() => $"{_label}: {_totals.Progress(_clock.Elapsed.TotalMilliseconds)}";

    public void OnFrame()
    {
        if (Finished)
        {
            return;
        }
        if (!_started)
        {
            _started = true;
            // Before the drawer touches anything: this is the state other renderers left.
            Diagnostics.Block(_api, _gl, $"{_label}, first frame, before the export changes anything");
            Diagnostics.Log(_api, $"custom GUI item renderers: {(_custom.Available ? _custom.Count.ToString() : "unavailable (" + _custom.Problem + ")")}");
            _api.ShowChatMessage($"seraphicons: {_label}: {_totals.Planned} to render, {_totals.SkippedExisting} already there, into {_outDir}");
        }
        if (_stopRequested)
        {
            Finish("stopped");
            return;
        }
        if (!_scheduler.Done)
        {
            IconDrawer.Saved saved = _drawer.BeginFrame();
            try
            {
                var frame = Stopwatch.StartNew();
                _scheduler.RunFrame(() => frame.Elapsed.TotalMilliseconds, RenderOne);
            }
            finally
            {
                _drawer.EndFrame(saved);
            }
        }
        double now = _clock.Elapsed.TotalMilliseconds;
        if (!_warnedEarly && _totals.Written >= EarlyCheckAfter && _totals.MostlyBroken)
        {
            _warnedEarly = true;
            string msg = $"WARNING: {_totals.Untextured} of the first {_totals.Written} icons look untextured (white shapes). "
                + "That is the bug this tool works around; see the client log. `.seraphicons stop` to cancel.";
            _api.ShowChatMessage("seraphicons: " + msg);
            Diagnostics.Warn(_api, msg);
            Diagnostics.Block(_api, _gl, "untextured icons after the first " + _totals.Written);
        }
        if (_scheduler.Done)
        {
            Finish("done");
            return;
        }
        if (_progress.Due(now))
        {
            _api.ShowChatMessage("seraphicons: " + Status());
        }
        if (_save.Due(now))
        {
            SaveManifest();
        }
    }

    private void RenderOne(RenderJob job)
    {
        string path = Path.Combine(_outDir, job.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            WriteInflight(job);
            if (!_stacks.TryGetValue(job.RelativePath, out ItemStack? stack))
            {
                Fail(job, "no stack resolved for this job");
                return;
            }
            bool custom = _custom.Available && _custom.Has(stack.Collectible);
            ImageCheck check;
            using (BitmapRef bmp = _drawer.Draw(stack))
            {
                check = ImageChecks.ClassifyArgb(bmp.Pixels);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                // Save opens without truncating, so write a fresh file and move it into place;
                // an interrupted write then never leaves a file a later run would skip.
                string tmp = path + ".tmp";
                File.Delete(tmp);
                bmp.Save(tmp);
                File.Move(tmp, path, overwrite: true);
            }
            _manifest.AddIcon(job.RelativePath, new IconEntry(job.Code, job.Kind, _drawer.Size, check, custom));
            _totals.Written++;
            if (custom)
            {
                _totals.CustomRenderer++;
            }
            if (check == ImageCheck.Transparent)
            {
                _totals.Transparent++;
            }
            else if (check == ImageCheck.Untextured)
            {
                _totals.Untextured++;
            }
        }
        catch (Exception e)
        {
            Fail(job, e.GetType().Name + ": " + e.Message, e);
        }
    }

    private void Fail(RenderJob job, string reason, Exception? e = null)
    {
        _manifest.AddFailure(new FailedEntry(job.Code, job.Kind, reason));
        _totals.Failed++;
        if (_totals.Failed <= LoggedFailures)
        {
            Diagnostics.Warn(_api, $"{IconKinds.Name(job.Kind)} {job.Code} failed: {(e != null ? e.ToString() : reason)}");
        }
        else if (_totals.Failed == LoggedFailures + 1)
        {
            Diagnostics.Warn(_api, "more failures follow; manifest.json lists them all");
        }
    }

    private void WriteInflight(RenderJob job)
    {
        File.WriteAllText(Path.Combine(_outDir, InflightMarker.FileName), InflightMarker.Format(new[] { job }));
    }

    private void SaveManifest()
    {
        try
        {
            _manifest.Save(_outDir);
        }
        catch (Exception e)
        {
            Diagnostics.Warn(_api, "could not write the manifest: " + e);
        }
    }

    /// <summary>Ends the run: frees the framebuffer, writes the manifest and reports.</summary>
    public void Finish(string how)
    {
        if (Finished)
        {
            return;
        }
        Finished = true;
        try
        {
            _drawer.Dispose();
        }
        catch (Exception e)
        {
            Diagnostics.Warn(_api, "could not free the framebuffer: " + e.Message);
        }
        SaveManifest();
        try
        {
            File.Delete(Path.Combine(_outDir, InflightMarker.FileName));
        }
        catch (Exception)
        {
            // The marker only matters after a crash.
        }
        double secs = _clock.Elapsed.TotalSeconds;
        string summary = $"{_label} {how} in {secs:0} s: {_totals.Summary()}"
            + (_totals.CustomRenderer > 0 ? $", drawn by a mod's own renderer {_totals.CustomRenderer}" : "")
            + $". Output: {_outDir}";
        _api.ShowChatMessage("seraphicons: " + summary);
        Diagnostics.Log(_api, summary);
        if (_totals.MostlyBroken)
        {
            string msg = $"WARNING: {_totals.Untextured} of {_totals.Written} icons look untextured (white or grey shapes "
                + "without colour). Do not import them. Send the [seraphiconfix] lines of client-main.log.";
            _api.ShowChatMessage("seraphicons: " + msg);
            Diagnostics.Warn(_api, msg);
        }
    }
}

/// <summary>Runs the current session, if any, once per frame in the Ortho stage.</summary>
internal sealed class ExportRenderer : IRenderer
{
    private readonly ICoreClientAPI _api;

    public ExportRenderer(ICoreClientAPI api) => _api = api;

    public ExportSession? Session { get; set; }

    // Before the game's own export (0.5) and the shader reset (0.499), so the diagnostics see
    // the state other renderers left and the --reset option is a real test.
    public double RenderOrder => 0.49;

    public int RenderRange => 0;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        ExportSession? s = Session;
        if (s == null)
        {
            return;
        }
        try
        {
            s.OnFrame();
        }
        catch (Exception e)
        {
            // Anything thrown here would take the client down; end the run instead.
            Diagnostics.Warn(_api, "export aborted: " + e);
            _api.ShowChatMessage("seraphicons: export aborted: " + e.Message + " (details in the client log)");
            try
            {
                s.Finish("aborted");
            }
            catch (Exception)
            {
                // Already reported.
            }
        }
        if (s.Finished)
        {
            Session = null;
        }
    }

    public void Dispose()
    {
        Session?.Finish("interrupted (leaving the world)");
        Session = null;
    }
}
