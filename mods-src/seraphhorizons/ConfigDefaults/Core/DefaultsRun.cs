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
/// One start's pass over the config folder (docs/config-defaults.md), game-independent:
/// <list type="bullet">
/// <item>No state yet (the first start with this system): record <paramref name="current"/>'s
/// version and change nothing.</item>
/// <item>Otherwise, for each file of the current snapshot whose last version (the state's, or the
/// file's own when it was left behind) differs from the current one: if every mod that writes the
/// file (its owners) is loaded at the snapshot's version, bring it to the current defaults
/// (<see cref="ConfigReconciler"/>) from that version's snapshot; if not, leave it, and keep its
/// version in the state so a later start can.</item>
/// <item>Either way, on a side that may (<paramref name="createMissing"/>, the server), a file the
/// pack sets values in that does not exist yet is written whole, as a fresh install has it, when
/// its owners are verified: that is how a fresh install gets the pack's own values.</item>
/// </list>
/// </summary>
public static class DefaultsRun
{
    public static RunResult Run(DefaultsSnapshot current, Func<string, DefaultsSnapshot?> snapshotFor,
        DefaultsState? state, IReadOnlyDictionary<string, string> loadedMods, bool createMissing, IConfigFiles files)
    {
        bool firstRun = state == null;
        var next = new DefaultsState { PackVersion = current.Version, Notice = state?.Notice };
        var result = new RunResult { State = next };
        var unverified = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var problems = new List<string>();

        foreach (var meta in current.Index.Files.Values.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            var name = meta.Name;
            var mismatch = Mismatch(meta, current.Index, loadedMods);
            string? text;
            try
            {
                text = files.Read(name);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                result.Messages.Add(new RunMessage(true, $"{name}: could not read it ({e.Message}); left as it is"));
                continue;
            }
            if (text == null)
            {
                if (createMissing && mismatch == null && current.HasPackValues(name)
                    && current.Effective(name, problems) is { } fresh)
                {
                    files.Write(name, fresh);
                    result.Created.Add(name);
                    result.Messages.Add(new RunMessage(false,
                        $"{name}: written as a fresh install of pack {current.Version} has it, with the pack's own values ({string.Join(", ", current.PackSet(name).Order())})"));
                }
                continue;
            }
            if (firstRun) continue;
            var from = state!.Files.GetValueOrDefault(name) ?? state.PackVersion;
            if (from == current.Version) continue;
            if (mismatch != null)
            {
                next.Files[name] = from;
                unverified[name] = mismatch;
                continue;
            }
            var old = snapshotFor(from);
            if (old == null)
            {
                result.Messages.Add(new RunMessage(true,
                    $"{name}: this build has no defaults for pack {from}, the version it was last brought to; left as it is"));
                continue;
            }
            if (!old.Index.Files.ContainsKey(name)) continue; // new to the pack since then: its mod's own defaults
            var oldText = old.Effective(name, problems);
            var newText = current.Effective(name, problems);
            if (oldText == null || newText == null) continue;
            try
            {
                var (updated, changes) = ConfigReconciler.Reconcile(name, meta.Format, text, oldText, newText,
                    current.Index.Renames, from, current.Version, current.PackSet(name));
                if (changes.Count == 0) continue;
                if (updated != text) files.Write(name, updated);
                result.Changes.AddRange(changes);
            }
            catch (FormatException e)
            {
                result.Messages.Add(new RunMessage(true, $"{name}: could not read it ({e.Message}); left as it is"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                result.Messages.Add(new RunMessage(true, $"{name}: could not write it ({e.Message}); left as it is"));
            }
        }

        foreach (var p in problems.Distinct())
            result.Messages.Add(new RunMessage(true, p));
        if (unverified.Count > 0)
            result.Messages.Add(new RunMessage(true,
                $"{unverified.Count} file(s) left at an older pack's defaults, because a mod that writes them is not the version pack {current.Version} has: "
                + string.Join("; ", unverified.Select(kv => $"{kv.Key} ({kv.Value})"))));
        if (result.Changes.Count > 0)
            next.Notice = new DefaultsNotice
            {
                FromVersion = state!.PackVersion, ToVersion = current.Version,
                Changes = result.Changes.Select(c => $"{c.File}: {c.Path} {c.From} -> {c.To}").ToList(),
            };
        return result;
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
