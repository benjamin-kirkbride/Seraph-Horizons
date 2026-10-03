using System.Runtime.CompilerServices;

namespace SeraphHorizons.PackTests;

/// <summary>
/// Deletes Atlas scratch worlds that earlier runs left behind, once per test process, before
/// any host boots. Atlas deletes a green class's scratch itself, but keeps a red or crashed
/// one for its server-main.log, and never disposes a run's last host (0.15.0), so even a
/// green run leaves one world behind, as does a run that is killed. Each world is ~750 MB
/// under <c>$TMPDIR/atlas</c>, and <c>/tmp</c> is often a tmpfs, so they pile up in RAM.
/// Only worlds idle for <see cref="MaxIdle"/> go, so a concurrent run's live worlds and a
/// fresh failure's logs survive. Setting <c>ATLAS_KEEP_SCRATCH</c> (Atlas's own
/// keep-everything switch) skips the sweep.
/// </summary>
internal static class StaleScratchSweep
{
    private static readonly TimeSpan MaxIdle = TimeSpan.FromHours(2);

    [ModuleInitializer]
    internal static void Run()
    {
        var keep = Environment.GetEnvironmentVariable("ATLAS_KEEP_SCRATCH");
        if (!string.IsNullOrWhiteSpace(keep) && keep != "0") return;

        var root = Path.Combine(Path.GetTempPath(), "atlas");
        if (!Directory.Exists(root)) return;

        var cutoff = DateTime.UtcNow - MaxIdle;
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            try
            {
                if (LastActivity(dir) > cutoff) continue;
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"PackTests: could not delete stale Atlas scratch {dir}: {e.Message}");
            }
        }
    }

    /// <summary>A live server keeps writing its logs, so the newest log is the world's pulse.</summary>
    private static DateTime LastActivity(string dir)
    {
        var last = Directory.GetLastWriteTimeUtc(dir);
        var logs = Path.Combine(dir, "Logs");
        if (Directory.Exists(logs))
            foreach (var log in Directory.EnumerateFiles(logs))
                last = Max(last, File.GetLastWriteTimeUtc(log));
        return last;
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
}
