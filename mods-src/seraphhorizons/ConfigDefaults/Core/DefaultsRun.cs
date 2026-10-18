namespace SeraphHorizons.Mod.ConfigDefaults.Core;

/// <summary>The config folder (ModConfig), by file name relative to it.</summary>
public interface IConfigFiles
{
    string? Read(string name);
    void Write(string name, string text);
}

/// <summary>A line for the log; warnings are things an admin may want to act on.</summary>
public readonly record struct RunMessage(bool Warning, string Text);

public sealed class RunResult
{
    public required DefaultsState State { get; init; }
    public List<SettingChange> Changes { get; } = [];
    /// <summary>Files written whole because a fresh install has them with the pack's own values.</summary>
    public List<string> Created { get; } = [];
    public List<RunMessage> Messages { get; } = [];
}

/// <summary>
/// One start's pass over the config folder (docs/config-defaults.md), game-independent. A file's
/// <b>baseline</b> is the defaults it was last brought to: the file as the snapshot of that start
/// had it (pack values set), kept under <see cref="BaselineFolder"/> by the file's own name. For
/// each file of the current snapshot:
/// <list type="bullet">
/// <item>its owners (the mods that write it) are not all loaded at the snapshot's versions: left,
/// baseline and all, with its version kept in the state, so a later start can bring it up;</item>
/// <item>it has a baseline that differs from the current snapshot's file: brought to the current
/// defaults (<see cref="ConfigReconciler"/>) from that baseline, whether or not the pack version
/// changed (an unreleased version's snapshot keeps changing on main, and every <c>next</c> build
/// between two releases has the same version);</item>
/// <item>no baseline, and the state records another pack version (a record from a build that
/// kept only the version): brought up from that version's shipped snapshot, as before baselines
/// were kept;</item>
/// <item>no baseline, and no state (the first start with this system, a fresh install) or the
/// state records this same version: nothing is changed, since nothing says which defaults the
/// file had.</item>
/// </list>
/// After each file that is verified, the current snapshot's file becomes its baseline. On a side
/// that may (<paramref name="createMissing"/>, the server), a file the pack sets values in that
/// does not exist yet is written whole, as a fresh install has it, when its owners are verified:
/// that is how a fresh install gets the pack's own values.
/// </summary>
public static class DefaultsRun
{
    /// <summary>The folder in ModConfig holding each file's baseline, under the file's own name.</summary>
    public const string BaselineFolder = "seraphhorizons-configdefaults";

    public static string BaselineOf(string name) => BaselineFolder + "/" + name;

    public static RunResult Run(DefaultsSnapshot current, Func<string, DefaultsSnapshot?> snapshotFor,
        DefaultsState? state, IReadOnlyDictionary<string, string> loadedMods, bool createMissing, IConfigFiles files)
    {
        var next = new DefaultsState { PackVersion = current.Version, Notice = state?.Notice };
        var result = new RunResult { State = next };
        var unverified = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var problems = new List<string>();
        var unrecorded = new List<string>();

        foreach (var meta in current.Index.Files.Values.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            var name = meta.Name;
            var from = state == null ? current.Version : state.Files.GetValueOrDefault(name) ?? state.PackVersion;
            string? text, baseline;
            try
            {
                text = files.Read(name);
                baseline = files.Read(BaselineOf(name));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                result.Messages.Add(new RunMessage(true, $"{name}: could not read it ({e.Message}); left as it is"));
                if (from != current.Version) next.Files[name] = from;
                continue;
            }
            var mismatch = Mismatch(meta, current.Index, loadedMods);
            if (mismatch != null)
            {
                // Left where it is: its baseline stays, and its version is for the log (and,
                // without a baseline, names the snapshot to bring it up from).
                if (text != null && state != null && (baseline != null || from != current.Version))
                {
                    next.Files[name] = from;
                    unverified[name] = mismatch;
                }
                continue;
            }
            var newText = current.Effective(name, problems);
            if (newText == null) continue;
            if (text == null)
            {
                if (createMissing && current.HasPackValues(name))
                {
                    files.Write(name, newText);
                    result.Created.Add(name);
                    result.Messages.Add(new RunMessage(false,
                        $"{name}: written as a fresh install of pack {current.Version} has it, with the pack's own values ({string.Join(", ", current.PackSet(name).Order())})"));
                }
                // Its mod writes it with the current defaults (or the pack just did).
                Record(files, name, baseline, newText, result);
                continue;
            }

            var oldText = baseline;
            if (oldText == null && state != null && from != current.Version)
            {
                var old = snapshotFor(from);
                if (old == null)
                    result.Messages.Add(new RunMessage(true,
                        $"{name}: this build has no defaults for pack {from}, the version it was last brought to; left as it is"));
                // A file new to the pack since then has its mod's own defaults: the current ones.
                else oldText = old.Index.Files.ContainsKey(name) ? old.Effective(name, problems) : newText;
            }
            else if (oldText == null && state != null)
                unrecorded.Add(name);
            if (oldText == null || oldText == newText)
            {
                Record(files, name, baseline, newText, result);
                continue;
            }
            try
            {
                var (updated, changes) = ConfigReconciler.Reconcile(name, meta.Format, text, oldText, newText,
                    current.Index.Renames, from, current.Version, current.PackSet(name));
                if (updated != text) files.Write(name, updated);
                result.Changes.AddRange(changes);
                Record(files, name, baseline, newText, result);
            }
            catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException)
            {
                // Left at its baseline, to be brought up once it reads and writes again.
                var what = e is FormatException ? "read" : "write";
                result.Messages.Add(new RunMessage(true, $"{name}: could not {what} it ({e.Message}); left as it is"));
                if (from != current.Version) next.Files[name] = from;
            }
        }

        foreach (var p in problems.Distinct())
            result.Messages.Add(new RunMessage(true, p));
        if (unrecorded.Count > 0)
            result.Messages.Add(new RunMessage(false,
                $"{unrecorded.Count} file(s) had only pack {current.Version} recorded, not its defaults (a record from an older build of the pack's mod): "
                + $"recorded this build's defaults for them and changed nothing, since nothing says which defaults they had; "
                + $"from now on a changed default follows. {string.Join(", ", unrecorded)}"));
        if (unverified.Count > 0)
            result.Messages.Add(new RunMessage(true,
                $"{unverified.Count} file(s) left at older defaults, because a mod that writes them is not the version pack {current.Version} has: "
                + string.Join("; ", unverified.Select(kv => $"{kv.Key} ({kv.Value})"))));
        if (result.Changes.Count > 0)
            next.Notice = new DefaultsNotice
            {
                FromVersion = state!.PackVersion, ToVersion = current.Version,
                Changes = result.Changes.Select(c => $"{c.File}: {c.Path} {c.From} -> {c.To}").ToList(),
            };
        return result;
    }

    /// <summary>Makes <paramref name="text"/> the file's baseline, writing only when it changed.</summary>
    private static void Record(IConfigFiles files, string name, string? baseline, string text, RunResult result)
    {
        if (baseline == text) return;
        try
        {
            files.Write(BaselineOf(name), text);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            result.Messages.Add(new RunMessage(true, $"{name}: could not record its defaults ({e.Message})"));
        }
    }

    /// <summary>Why the file's owners are not verified on this side (the first that is not
    /// loaded at the snapshot's version), or null when they all are.</summary>
    public static string? Mismatch(SnapshotFile file, SnapshotIndex index, IReadOnlyDictionary<string, string> loadedMods)
    {
        if (file.Owners.Count == 0) return "no mod is known to write it";
        foreach (var owner in file.Owners)
        {
            if (!index.Mods.TryGetValue(owner, out var want)) return $"{owner} is not in the snapshot";
            if (!loadedMods.TryGetValue(owner, out var have)) return $"{owner} is not loaded";
            if (have != want) return $"{owner} {have}, the pack has {want}";
        }
        return null;
    }
}
