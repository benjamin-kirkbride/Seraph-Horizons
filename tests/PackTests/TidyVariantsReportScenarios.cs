using System.Reflection;
using System.Text;
using System.Text.Json;
using Atlas.XUnit;
using Vintagestory.API.Config;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The Tidy Variants report (#259): resolves the rules against every locked mod (the server's own
/// resolution at WorldReady) and reports creative entries before and after hiding and grouping, per
/// asset domain and per creative tab, the hidden entries, the largest groups, automatic groups with no
/// title and the engine's issues. Written to the test output and to <c>build/tidyvariants-report.md</c>
/// and <c>.json</c> (or <c>$TIDYVARIANTS_REPORT_DIR</c>; CI uploads it with the atlas results).
///
/// Hard assertions only for invariants whose failure is a real bug (in the engine, the bridge or the
/// shipped override file), plus a generous ceiling on tiles after grouping. Everything else is
/// report-only: how a mod's variants group is a matter of taste, not of correctness.
/// See docs/variant-grouping/report.md.
/// </summary>
[AtlasWorld]
public class TidyVariantsReportScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    /// <summary>
    /// Regression budget: tiles in the full creative list with nothing expanded. About 6,600 when this
    /// was written (29,449 entries). A new mod or a rule change that adds many ungrouped variants trips it.
    /// To raise it deliberately: look at the report's largest domains/tabs and untitled groups first; if the
    /// growth is wanted (a big new mod), raise this to about 15% above the new number and say why in the PR.
    /// </summary>
    private const int TileCeiling = 7_600;

    // Issue kinds that only the shipped override file can cause: each is a mistake in it.
    private static readonly string[] OverrideFileIssueKinds =
        ["rule-unused", "representative-unmatched", "duplicate-group-id", "placeholder-unresolved"];

    private sealed record Entry(int Index, string Code, string Domain, string[] Tabs, string Hide, int Group);
    private sealed record Group(int Index, string Id, string Source, string? Title, int[] Members, int Representative, string? ShippedPattern);
    private sealed record Issue(string Origin, string Kind, string Message);
    private sealed record CountRow(string Key, int Before, int Hidden, int After);

    private sealed class Data
    {
        public required Entry[] Entries;
        public required Group[] Groups;
        public required Issue[] Issues;
        public required bool OverridesPresent;
        public required string[] OverrideErrors;
        public required int StatsEntriesAfter;
        public required string Summary;
        public int Visible => Entries.Count(e => e.Hide == "None");
        public int Grouped => Entries.Count(e => e.Group >= 0);
        public int Tiles => Visible - Grouped + Groups.Length;
    }

    // One server per class: read the resolution once, share it between the scenarios.
    private static Data? _data;
    private static readonly object Gate = new();

    private Data Read()
    {
        lock (Gate) return _data ??= Load();
    }

    private Data Load()
    {
        // The game loads its own copy of the mod assembly, so its types are reached by name and
        // `dynamic`, never by a compile-time reference (as in TidyVariantsScenarios).
        var system = World.Api.ModLoader.GetMod("tidyvariants").Systems
            .Single(s => s.GetType().FullName == "SeraphHorizons.TidyVariants.TidyVariantsModSystem");
        dynamic? bridge = system.GetType().GetProperty("Bridge")!.GetValue(system);
        Assert.True(bridge is not null, "the server resolution is null (see the log for the error)");
        dynamic res = bridge!.Resolution;

        int n = ((System.Collections.ICollection)res.Entries).Count;
        var entries = new Entry[n];
        for (int i = 0; i < n; i++)
        {
            dynamic e = res.Entries[i];
            entries[i] = new Entry(i, (string)e.ToString(), (string)e.Domain, ((IEnumerable<string>)e.Tabs).ToArray(),
                ((object)res.HideReasonOf(i)).ToString()!, (int)res.GroupOf(i));
        }
        var groups = new List<Group>();
        foreach (dynamic g in (IEnumerable<object>)res.Groups)
            groups.Add(new Group((int)g.Index, (string)g.Id, ((object)g.Source).ToString()!, (string?)g.Title,
                ((IEnumerable<int>)g.Members).ToArray(), (int)g.Representative, (string?)g.ShippedPattern));

        var issues = new List<Issue>();
        foreach (dynamic i in (IEnumerable<object>)res.Issues) issues.Add(new Issue("resolve", (string)i.Kind, (string)i.Message));
        var asm = ((object)res).GetType().Assembly;
        dynamic plan = asm.GetType("SeraphHorizons.TidyVariants.Core.Handbook")!
            .GetMethod("Build", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [res, false])!;
        foreach (dynamic i in (IEnumerable<object>)plan.Issues) issues.Add(new Issue("handbook", (string)i.Kind, (string)i.Message));
        dynamic stats = asm.GetType("SeraphHorizons.TidyVariants.Core.TidyStats")!
            .GetMethod("Compute", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [res, 1])!;

        return new Data
        {
            Entries = entries,
            Groups = groups.ToArray(),
            Issues = issues.ToArray(),
            OverridesPresent = (bool)bridge.OverridesPresent,
            OverrideErrors = ((IEnumerable<string>)bridge.OverrideErrors).ToArray(),
            StatsEntriesAfter = (int)stats.EntriesAfter,
            Summary = (string)bridge.Summary(),
        };
    }

    // ---- invariants (fail) ----

    [AtlasScenario(TimeoutMs = 120_000)]
    public void Every_entry_is_hidden_plain_or_in_exactly_one_group()
    {
        var d = Read();
        var bad = new List<string>();
        foreach (var e in d.Entries)
        {
            if (e.Hide != "None" && e.Group >= 0) bad.Add($"{e.Code}: hidden ({e.Hide}) and in group {d.Groups[e.Group].Id}");
            if (e.Group >= d.Groups.Length) bad.Add($"{e.Code}: group index {e.Group} out of range");
        }
        var seen = new int[d.Entries.Length];
        foreach (var g in d.Groups)
        {
            if (g.Members.Length < 2) bad.Add($"group {g.Id}: {g.Members.Length} member(s)");
            foreach (int m in g.Members)
            {
                seen[m]++;
                if (d.Entries[m].Group != g.Index) bad.Add($"group {g.Id}: member {d.Entries[m].Code} has GroupOf {d.Entries[m].Group}");
            }
        }
        foreach (var e in d.Entries)
        {
            int expected = e.Group >= 0 ? 1 : 0;
            if (seen[e.Index] != expected) bad.Add($"{e.Code}: member of {seen[e.Index]} groups, GroupOf {e.Group}");
        }
        Assert.True(bad.Count == 0, $"{bad.Count} partition violations:\n" + string.Join("\n", bad.Take(50)));
        Assert.Equal(d.Tiles, d.StatsEntriesAfter);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public void Every_group_is_shown_by_a_visible_member()
    {
        var d = Read();
        var bad = d.Groups
            .Where(g => !g.Members.Contains(g.Representative) || d.Entries[g.Representative].Hide != "None")
            .Select(g => $"group {g.Id}: representative {g.Representative} ({(g.Representative >= 0 && g.Representative < d.Entries.Length ? d.Entries[g.Representative].Code : "?")}) is not a visible member")
            .ToList();
        Assert.True(bad.Count == 0, string.Join("\n", bad.Take(50)));
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public void Shipped_override_file_parses_and_every_rule_applies()
    {
        var d = Read();
        Assert.True(d.OverridesPresent, "the shipped override file was not found");
        Assert.True(d.OverrideErrors.Length == 0, "override file errors:\n" + string.Join("\n", d.OverrideErrors));
        var bad = d.Issues.Where(i => OverrideFileIssueKinds.Contains(i.Kind)).ToList();
        Assert.True(bad.Count == 0, $"{bad.Count} override file issues against the pack:\n" +
            string.Join("\n", bad.Select(i => $"[{i.Kind}] {i.Message}")));
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public void Every_override_title_resolves()
    {
        var d = Read();
        var missing = d.Groups.Where(g => g.Title is not null).Select(g => g.Title!).Distinct()
            .Where(k => !Lang.HasTranslation(k)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, $"{missing.Count} group title lang keys are missing:\n" + string.Join("\n", missing));
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public void Tiles_after_grouping_stay_within_budget()
    {
        var d = Read();
        Assert.True(d.Tiles <= TileCeiling,
            $"{d.Tiles} creative tiles after grouping exceed the budget of {TileCeiling} (see TileCeiling and the report)");
    }

    // ---- report (never fails on the numbers) ----

    [AtlasScenario(TimeoutMs = 120_000)]
    public void Report_is_written()
    {
        var d = Read();
        var (md, json) = Build(d);
        var dir = ReportDir();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "tidyvariants-report.md"), md);
        File.WriteAllText(Path.Combine(dir, "tidyvariants-report.json"), json);
        output.WriteLine(md);
        output.WriteLine($"written to {dir}");
    }

    private static string ReportDir()
    {
        var env = Environment.GetEnvironmentVariable("TIDYVARIANTS_REPORT_DIR");
        if (!string.IsNullOrEmpty(env)) return Path.GetFullPath(env);
        // Not AppContext.BaseDirectory: after boot it points into the game install.
        var dir = new DirectoryInfo(Path.GetDirectoryName(typeof(TidyVariantsReportScenarios).Assembly.Location)!);
        for (var d = dir; d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "pack", "pack.toml"))) return Path.Combine(d.FullName, "build");
        return dir.FullName;
    }

    private static List<CountRow> CountBy(Data d, Func<Entry, IEnumerable<string>> keys)
    {
        var acc = new Dictionary<string, (int before, int hidden, HashSet<int> groups, int plain)>(StringComparer.Ordinal);
        foreach (var e in d.Entries)
            foreach (var k in keys(e))
            {
                if (!acc.TryGetValue(k, out var c)) c = (0, 0, [], 0);
                c.before++;
                if (e.Hide != "None") c.hidden++;
                else if (e.Group < 0) c.plain++;
                else c.groups.Add(e.Group);
                acc[k] = c;
            }
        return acc.Select(kv => new CountRow(kv.Key, kv.Value.before, kv.Value.hidden, kv.Value.plain + kv.Value.groups.Count))
            .OrderByDescending(c => c.After).ThenBy(c => c.Key, StringComparer.Ordinal).ToList();
    }

    private static (string md, string json) Build(Data d)
    {
        var perDomain = CountBy(d, e => [e.Domain]);
        var perTab = CountBy(d, e => e.Tabs);
        var hidden = d.Entries.Where(e => e.Hide != "None").ToList();
        object GroupRow(Group g) => new
        {
            g.Id, g.Source, g.Title, members = g.Members.Length,
            representative = d.Entries[g.Representative].Code, shippedPattern = g.ShippedPattern,
            domains = g.Members.Select(m => d.Entries[m].Domain).Distinct().ToArray(),
        };
        var bySize = d.Groups.OrderByDescending(g => g.Members.Length).ThenBy(g => g.Id, StringComparer.Ordinal).ToList();
        var untitledAuto = bySize.Where(g => g.Title is null && g.Source != "Override").ToList();
        var untitledOverride = bySize.Where(g => g.Title is null && g.Source == "Override").ToList();
        var issueKinds = d.Issues.GroupBy(i => i.Kind).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("# Tidy Variants report");
        sb.AppendLine();
        sb.AppendLine($"Server resolution against every locked mod. {d.Summary}");
        sb.AppendLine();
        sb.AppendLine("| | count |\n|---|---:|");
        sb.AppendLine($"| creative entries before | {d.Entries.Length:N0} |");
        sb.AppendLine($"| hidden | {hidden.Count:N0} (variant rule {hidden.Count(e => e.Hide == "Variant"):N0}, override {hidden.Count(e => e.Hide == "Override"):N0}) |");
        sb.AppendLine($"| visible | {d.Visible:N0} |");
        sb.AppendLine($"| groups (2+ members) | {d.Groups.Length:N0} (automatic {d.Groups.Count(g => g.Source == "Automatic"):N0}, shipped groupBy {d.Groups.Count(g => g.Source == "Shipped"):N0}, override {d.Groups.Count(g => g.Source == "Override"):N0}) |");
        sb.AppendLine($"| entries in a group | {d.Grouped:N0} |");
        sb.AppendLine($"| **tiles after** (nothing expanded) | **{d.Tiles:N0}** (budget {TileCeiling:N0}) |");
        sb.AppendLine($"| untitled automatic groups | {untitledAuto.Count:N0} |");
        sb.AppendLine($"| override groups without a title | {untitledOverride.Count:N0} |");
        sb.AppendLine($"| engine issues | {d.Issues.Length:N0} |");

        void CountTable(string title, List<CountRow> rows, int take)
        {
            sb.AppendLine().AppendLine($"## {title}").AppendLine();
            sb.AppendLine("| key | before | hidden | after | after/before |\n|---|---:|---:|---:|---:|");
            foreach (var c in rows.Take(take))
                sb.AppendLine($"| `{c.Key}` | {c.Before:N0} | {c.Hidden:N0} | {c.After:N0} | {(c.Before == 0 ? 0 : 100.0 * c.After / c.Before):0}% |");
            if (rows.Count > take) sb.AppendLine($"\n{rows.Count - take} more in the JSON.");
        }
        CountTable("Domains with the most tiles after grouping", perDomain, 40);
        CountTable("Creative tabs with the most tiles after grouping", perTab, 40);

        void GroupTable(string title, List<Group> rows, int take)
        {
            sb.AppendLine().AppendLine($"## {title}").AppendLine();
            if (rows.Count == 0) { sb.AppendLine("None."); return; }
            sb.AppendLine("| members | id | source | title | representative |\n|---:|---|---|---|---|");
            foreach (var g in rows.Take(take))
                sb.AppendLine($"| {g.Members.Length:N0} | `{g.Id}` | {g.Source} | {(g.Title is null ? "" : $"`{g.Title}`")} | `{d.Entries[g.Representative].Code}` |");
            if (rows.Count > take) sb.AppendLine($"\n{rows.Count - take} more in the JSON.");
        }
        GroupTable("Largest groups", bySize, 30);
        GroupTable($"Largest untitled automatic groups ({untitledAuto.Count:N0}; candidates for override titles)", untitledAuto, 40);
        GroupTable($"Override groups without a title ({untitledOverride.Count:N0})", untitledOverride, 40);

        sb.AppendLine().AppendLine($"## Hidden entries ({hidden.Count:N0})").AppendLine();
        if (hidden.Count == 0) sb.AppendLine("None.");
        else
        {
            sb.AppendLine("| reason | entry |\n|---|---|");
            foreach (var e in hidden.Take(100)) sb.AppendLine($"| {e.Hide} | `{e.Code}` |");
            if (hidden.Count > 100) sb.AppendLine($"\n{hidden.Count - 100} more in the JSON.");
        }

        sb.AppendLine().AppendLine("## Engine issues").AppendLine();
        if (issueKinds.Count == 0) sb.AppendLine("None.");
        foreach (var k in issueKinds)
        {
            sb.AppendLine($"### `{k.Key}` ({k.Count():N0}){(OverrideFileIssueKinds.Contains(k.Key) ? " - fails the override file scenario" : "")}").AppendLine();
            foreach (var i in k.Take(10)) sb.AppendLine($"- ({i.Origin}) {i.Message}");
            if (k.Count() > 10) sb.AppendLine($"- ... {k.Count() - 10} more in the JSON");
            sb.AppendLine();
        }
        if (d.OverrideErrors.Length > 0)
        {
            sb.AppendLine("## Override file errors").AppendLine();
            foreach (var e in d.OverrideErrors) sb.AppendLine($"- {e}");
        }

        var json = JsonSerializer.Serialize(new
        {
            summary = d.Summary,
            entriesBefore = d.Entries.Length,
            hidden = hidden.Count,
            hiddenByVariantRule = hidden.Count(e => e.Hide == "Variant"),
            hiddenByOverride = hidden.Count(e => e.Hide == "Override"),
            visible = d.Visible,
            groups = d.Groups.Length,
            groupsBySource = d.Groups.GroupBy(g => g.Source).ToDictionary(g => g.Key, g => g.Count()),
            groupedEntries = d.Grouped,
            tilesAfter = d.Tiles,
            tileCeiling = TileCeiling,
            untitledAutomaticGroups = untitledAuto.Count,
            overrideGroupsWithoutTitle = untitledOverride.Count,
            perDomain = perDomain,
            perTab = perTab,
            largestGroups = bySize.Take(100).Select(GroupRow),
            untitledAutomatic = untitledAuto.Select(GroupRow),
            untitledOverride = untitledOverride.Select(GroupRow),
            hiddenEntries = hidden.Select(e => new { e.Code, reason = e.Hide }),
            issueCounts = issueKinds.ToDictionary(k => k.Key, k => k.Count()),
            issues = d.Issues,
            overrideErrors = d.OverrideErrors,
        }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return (sb.ToString(), json);
    }
}
