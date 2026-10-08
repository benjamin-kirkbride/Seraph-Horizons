namespace SeraphHorizons.Mod.ConfigDefaults.Core;

/// <summary>A setting moved to the pack's new default.</summary>
/// <param name="File">The file under ModConfig, e.g. <c>seraphhorizons.json</c>.</param>
/// <param name="Path">The setting's keys joined with dots.</param>
/// <param name="From">The value the file had: the old default.</param>
/// <param name="To">The new default it has now.</param>
/// <param name="Why">Why, in a phrase for the log.</param>
public sealed record SettingChange(string File, string Path, string From, string To, string Why)
{
    public string Describe() => $"{File}: {Path} {From} -> {To} ({Why})";
}

/// <summary>
/// The model (docs/config-defaults.md): a config file stays whole, as its mod writes it, and the
/// pack remembers what the defaults were in each pack version. For each setting in both the old
/// version's defaults and the new one's: when the file still has the old default and the new
/// default differs, the file gets the new default. A value equal to the default counts as not
/// chosen and follows the default; any other value is the player's, and is kept. A setting only
/// one of the two versions has is left to its mod, as is a top-level <c>version</c> key (a mod's
/// own marker of its file's shape; ConfigKit starts the file over when it differs).
/// </summary>
public static class ConfigReconciler
{
    /// <summary>
    /// <paramref name="current"/> (the file as it is) brought to the new defaults.
    /// <paramref name="oldDefaults"/> and <paramref name="newDefaults"/> are the whole file as a
    /// fresh install of each pack version has it (the pack's own values included). Renames apply
    /// first, to the file and to the old defaults, so a renamed setting compares under its new
    /// key. <paramref name="packSet"/> says which settings (by <see cref="SettingPath.Show"/>) the
    /// pack sets in the new version, for the log. Throws <see cref="FormatException"/> when a text
    /// cannot be read.
    /// </summary>
    public static (string Text, List<SettingChange> Changes) Reconcile(
        string file, ConfigFormat format, string current, string oldDefaults, string newDefaults,
        IEnumerable<KeyRename> renames, string fromVersion, string toVersion, IReadOnlySet<string>? packSet = null)
    {
        var changes = new List<SettingChange>();
        foreach (var r in renames.Where(r => r.File == file))
        {
            var renamed = ConfigText.Rename(current, format, r.From, r.To);
            if (renamed != current)
            {
                var to = r.From.Take(r.From.Count - 1).Append(r.To).ToList();
                changes.Add(new SettingChange(file, SettingPath.Show(r.From), SettingPath.Show(r.From), SettingPath.Show(to),
                    "renamed; the value is kept"));
                current = renamed;
            }
            oldDefaults = ConfigText.Rename(oldDefaults, format, r.From, r.To);
        }
        var have = ConfigText.Parse(current, format);
        var old = ConfigText.Parse(oldDefaults, format);
        var now = ConfigText.Parse(newDefaults, format);
        var edits = new List<TextEdit>();
        Walk(old, now, have, [], edits, changes, file, fromVersion, toVersion, packSet ?? new HashSet<string>());
        return (edits.Count == 0 ? current : ConfigText.Apply(current, edits), changes);
    }

    private static void Walk(ConfigNode old, ConfigNode now, ConfigNode have, List<string> path,
        List<TextEdit> edits, List<SettingChange> changes, string file, string fromVersion, string toVersion,
        IReadOnlySet<string> packSet)
    {
        if (old.Kind == NodeKind.Object && now.Kind == NodeKind.Object)
        {
            if (have.Kind != NodeKind.Object) return;
            foreach (var p in old.Properties!)
            {
                if (path.Count == 0 && p.Key.Equals("version", StringComparison.OrdinalIgnoreCase)) continue;
                var q = now.Properties!.FirstOrDefault(x => x.Key == p.Key);
                var h = have.Find(p.Key);
                if (q == null || h == null) continue;
                path.Add(p.Key);
                Walk(p.Value, q.Value, h.Value, path, edits, changes, file, fromVersion, toVersion, packSet);
                path.RemoveAt(path.Count - 1);
            }
            return;
        }
        if (ConfigNode.Same(old, now) || !ConfigNode.Same(have, old)) return;
        if (now.Kind == NodeKind.Opaque || have.Kind == NodeKind.Opaque) return;
        var shown = SettingPath.Show(path);
        edits.Add(new TextEdit(have.Start, have.End, now.Raw));
        var why = $"the default was {Short(old.Raw)} in pack {fromVersion} and is {Short(now.Raw)} in {toVersion}"
                  + (packSet.Contains(shown) ? ", set by the pack" : "")
                  + "; this file still had the old default";
        changes.Add(new SettingChange(file, shown, Short(have.Raw), Short(now.Raw), why));
    }

    /// <summary>A value's text for a log line: one line, cut short.</summary>
    public static string Short(string raw)
    {
        var s = string.Join(" ", raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));
        return s.Length <= 80 ? s : s[..77] + "...";
    }
}
