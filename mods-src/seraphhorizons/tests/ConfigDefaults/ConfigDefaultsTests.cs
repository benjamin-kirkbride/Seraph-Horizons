using System.Text.Json;
using SeraphHorizons.Mod.ConfigDefaults.Core;

namespace SeraphHorizons.Mod.Tests.ConfigDefaults;

public class ConfigReadingTests
{
    [Fact]
    public void Lenient_json_reads_comments_unquoted_keys_and_trailing_commas_with_spans()
    {
        const string text = "{\n\t// Suffix\n\tPrefix: [\"Little\", 'New',],\n\t/* block */ \"Rate\": 0.25, // after\n}";
        var root = LenientJson.Parse(text);
        var prefix = root.At(["Prefix"])!;
        Assert.Equal(NodeKind.Array, prefix.Kind);
        Assert.Equal(["Little", "New"], prefix.Items!.Select(i => i.Text));
        var rate = root.At(["Rate"])!;
        Assert.Equal(0.25, rate.Number);
        Assert.Equal("0.25", text[rate.Start..rate.End]);
    }

    [Fact]
    public void Lenient_json_finds_a_key_without_case_as_Newtonsoft_reads_it()
    {
        var root = LenientJson.Parse("""{ "carrySpeed": 1 }""");
        Assert.Equal(1, root.At(["CarrySpeed"])!.Number);
    }

    [Fact]
    public void Lenient_json_refuses_garbage()
    {
        Assert.Throws<FormatException>(() => LenientJson.Parse("{ \"a\": }"));
        Assert.Throws<FormatException>(() => LenientJson.Parse("{ \"a\": 1 } x"));
    }

    private const string ConfigKitYaml = """
        version: 12


        ##########
        ## Loot ##
        ##########


        # Allows to change the loot additions.
        loot: 1
         # from 0 to 10 (default: 1)

        largeruins_min_distance: 2800
         # from 0 to 10000 (default: 2800)
        Bell Rework: true
        name: "quoted # not a comment"
        plain: some words # a comment
        """;

    [Fact]
    public void Yaml_reads_ConfigKit_files_keys_with_spaces_and_comments()
    {
        var root = SimpleYaml.Parse(ConfigKitYaml);
        Assert.Equal(12, root.At(["version"])!.Number);
        Assert.Equal(1, root.At(["loot"])!.Number);
        Assert.True(root.At(["Bell Rework"])!.Bool);
        Assert.Equal("quoted # not a comment", root.At(["name"])!.Text);
        Assert.Equal("some words", root.At(["plain"])!.Text);
        var node = root.At(["largeruins_min_distance"])!;
        Assert.Equal("2800", ConfigKitYaml[node.Start..node.End]);
    }

    [Fact]
    public void Yaml_reads_nested_mappings_and_keeps_sequences_opaque()
    {
        const string text = "outer:\n  inner: 3\n  deeper:\n    x: a\nlist:\n- 1\n- 2\nflow: [1, 2]\nlast: 0\n";
        var root = SimpleYaml.Parse(text);
        Assert.Equal(3, root.At(["outer", "inner"])!.Number);
        Assert.Equal("a", root.At(["outer", "deeper", "x"])!.Text);
        Assert.Equal(NodeKind.Opaque, root.At(["list"])!.Kind);
        Assert.Equal(NodeKind.Opaque, root.At(["flow"])!.Kind);
        Assert.Equal(0, root.At(["last"])!.Number);
    }

    [Fact]
    public void Same_compares_numbers_by_value_and_objects_in_any_order()
    {
        Assert.True(ConfigNode.Same(SimpleYaml.Parse("a: 1.50").At(["a"])!, SimpleYaml.Parse("a: 1.5").At(["a"])!));
        Assert.True(ConfigNode.Same(LenientJson.Parse("""{"x":1,"y":[1,2]}"""), LenientJson.Parse("""{"y":[1.0,2],"x":1}""")));
        Assert.False(ConfigNode.Same(LenientJson.Parse("[1,2]"), LenientJson.Parse("[2,1]")));
        Assert.False(ConfigNode.Same(LenientJson.Parse("\"1\""), LenientJson.Parse("1")));
    }

    [Fact]
    public void SetValue_and_Rename_change_only_their_span()
    {
        var text = ConfigText.SetValue(ConfigKitYaml, ConfigFormat.Yaml, ["largeruins_min_distance"], "1200")!;
        Assert.Equal(ConfigKitYaml.Replace("largeruins_min_distance: 2800", "largeruins_min_distance: 1200"), text);
        Assert.Null(ConfigText.SetValue(ConfigKitYaml, ConfigFormat.Yaml, ["nope"], "1"));

        const string json = "{\n  \"A\": { \"Old\": 1 } // keep\n}";
        Assert.Equal("{\n  \"A\": { \"New\": 1 } // keep\n}", ConfigText.Rename(json, ConfigFormat.Json, ["A", "Old"], "New"));
        // Not when the new key is there already.
        Assert.Equal("{\"Old\":1,\"New\":2}", ConfigText.Rename("{\"Old\":1,\"New\":2}", ConfigFormat.Json, ["Old"], "New"));
    }
}

public class ConfigReconcilerTests
{
    private static (string Text, List<SettingChange> Changes) Json(string current, string old, string now, params KeyRename[] renames) =>
        ConfigReconciler.Reconcile("f.json", ConfigFormat.Json, current, old, now, renames, "0.1.0", "0.2.0");

    [Fact]
    public void A_value_still_at_the_old_default_follows_the_new_one_and_a_chosen_one_stays()
    {
        const string old = """{ "Carry": { "AtMax": 0.02, "Wear": 0.25 }, "Same": 3 }""";
        const string now = """{ "Carry": { "AtMax": 0.1, "Wear": 1.0 }, "Same": 3 }""";
        const string mine = "{\n  // my notes\n  \"Carry\": { \"AtMax\": 0.02, \"Wear\": 0.7 },\n  \"Same\": 3\n}";
        var (text, changes) = Json(mine, old, now);
        Assert.Equal("{\n  // my notes\n  \"Carry\": { \"AtMax\": 0.1, \"Wear\": 0.7 },\n  \"Same\": 3\n}", text);
        var c = Assert.Single(changes);
        Assert.Equal(("Carry.AtMax", "0.02", "0.1"), (c.Path, c.From, c.To));
    }

    [Fact]
    public void Settings_only_one_version_has_a_missing_key_and_the_version_key_are_left_alone()
    {
        const string old = """{ "version": 1, "Gone": 1, "Kept": 1 }""";
        const string now = """{ "version": 2, "New": 5, "Kept": 2 }""";
        var (text, changes) = Json("""{ "version": 1, "Gone": 1 }""", old, now);
        Assert.Equal("""{ "version": 1, "Gone": 1 }""", text);
        Assert.Empty(changes);
    }

    [Fact]
    public void Arrays_and_dictionaries()
    {
        const string old = """{ "List": [1, 2], "Map": { "a": 1, "b": 2 } }""";
        const string now = """{ "List": [1, 2, 3], "Map": { "a": 1, "b": 4, "c": 9 } }""";
        var (text, changes) = Json("""{ "List": [1, 2], "Map": { "a": 7, "b": 2 } }""", old, now);
        Assert.Equal("""{ "List": [1, 2, 3], "Map": { "a": 7, "b": 4 } }""", text);
        Assert.Equal(["List", "Map.b"], changes.Select(c => c.Path));
    }

    [Fact]
    public void Yaml_scalars_change_in_place_with_comments_kept()
    {
        const string old = "version: 3\n# Rate\nrate: 1\n # (default: 1)\nflag: true\n";
        const string now = "version: 3\n# Rate\nrate: 1.5\n # (default: 1.5)\nflag: false\n";
        const string mine = "version: 3\n# Rate\nrate: 1.0\n # (default: 1)\nflag: True\n";
        var (text, changes) = ConfigReconciler.Reconcile("m.yaml", ConfigFormat.Yaml, mine, old, now, [], "0.1.0", "0.2.0");
        Assert.Equal("version: 3\n# Rate\nrate: 1.5\n # (default: 1)\nflag: false\n", text);
        Assert.Equal(2, changes.Count);
    }

    [Fact]
    public void A_rename_carries_the_value_and_then_compares_under_the_new_key()
    {
        var rename = new KeyRename("f.json", ["S", "Old"], "New");
        const string old = """{ "S": { "Old": 1 } }""";
        const string now = """{ "S": { "New": 2 } }""";
        var (untouched, c1) = Json("""{ "S": { "Old": 1 } }""", old, now, rename);
        Assert.Equal("""{ "S": { "New": 2 } }""", untouched);
        Assert.Equal(2, c1.Count);
        var (chosen, _) = Json("""{ "S": { "Old": 7 } }""", old, now, rename);
        Assert.Equal("""{ "S": { "New": 7 } }""", chosen);
    }
}

public class DefaultsRunTests
{
    private sealed class MemFiles(Dictionary<string, string> files) : IConfigFiles
    {
        public Dictionary<string, string> Files => files;
        public string? Read(string name) => files.GetValueOrDefault(name);
        public void Write(string name, string text) => files[name] = text;
    }

    private static DefaultsSnapshot Snap(string version, Dictionary<string, (string Text, string[] Owners)> files,
        Dictionary<string, string>? mods = null, List<PackValue>? values = null)
    {
        var index = new SnapshotIndex
        {
            PackVersion = version, GameVersion = "1.22.7",
            Mods = mods ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["a"] = "1", ["b"] = "1" },
            Files = files.ToDictionary(f => f.Key, f => new SnapshotFile(f.Key, ConfigText.FormatOf(f.Key), f.Value.Owners)),
            PackValues = values ?? [], Renames = [],
        };
        return new DefaultsSnapshot(index, n => files.TryGetValue(n, out var f) ? f.Text : null);
    }

    private static readonly Dictionary<string, string> Loaded = new(StringComparer.OrdinalIgnoreCase) { ["a"] = "1", ["b"] = "1" };

    private static readonly DefaultsSnapshot V1 = Snap("0.1.0", new()
    {
        ["a.json"] = ("""{ "x": 1, "y": 1 }""", ["a"]),
        ["b.yaml"] = ("version: 1\nk: 1\n", ["b"]),
    });

    private static readonly DefaultsSnapshot V2 = Snap("0.2.0", new()
    {
        ["a.json"] = ("""{ "x": 2, "y": 2 }""", ["a"]),
        ["b.yaml"] = ("version: 1\nk: 1\n", ["b"]),
    }, values: [new PackValue("b.yaml", ["k"], "5")]);

    private static DefaultsSnapshot? Lookup(string v) => v switch { "0.1.0" => V1, "0.2.0" => V2, _ => null };

    [Fact]
    public void The_first_start_records_the_version_and_changes_nothing()
    {
        var files = new MemFiles(new() { ["a.json"] = """{ "x": 1, "y": 1 }""" });
        var r = DefaultsRun.Run(V2, Lookup, null, Loaded, createMissing: false, files);
        Assert.Equal("0.2.0", r.State.PackVersion);
        Assert.Empty(r.Changes);
        Assert.Equal("""{ "x": 1, "y": 1 }""", files.Files["a.json"]);
    }

    [Fact]
    public void A_later_version_moves_untouched_values_including_the_packs_own()
    {
        var files = new MemFiles(new() { ["a.json"] = """{ "x": 1, "y": 9 }""", ["b.yaml"] = "version: 1\nk: 1\n" });
        var r = DefaultsRun.Run(V2, Lookup, new DefaultsState { PackVersion = "0.1.0" }, Loaded, createMissing: true, files);
        Assert.Equal("""{ "x": 2, "y": 9 }""", files.Files["a.json"]);
        Assert.Equal("version: 1\nk: 5\n", files.Files["b.yaml"]);
        Assert.Equal(2, r.Changes.Count);
        Assert.Contains("set by the pack", r.Changes.Single(c => c.File == "b.yaml").Why);
        Assert.Equal("0.2.0", r.State.PackVersion);
        Assert.Empty(r.State.Files);
        Assert.Equal(2, r.State.Notice!.Changes.Count);
    }

    [Fact]
    public void A_file_whose_mod_is_another_version_is_left_behind_until_it_matches()
    {
        var other = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["a"] = "1.1", ["b"] = "1" };
        var files = new MemFiles(new() { ["a.json"] = """{ "x": 1, "y": 1 }""", ["b.yaml"] = "version: 1\nk: 1\n" });
        var r = DefaultsRun.Run(V2, Lookup, new DefaultsState { PackVersion = "0.1.0" }, other, createMissing: true, files);
        Assert.Equal("""{ "x": 1, "y": 1 }""", files.Files["a.json"]);
        Assert.Equal("version: 1\nk: 5\n", files.Files["b.yaml"]);
        Assert.Equal("0.1.0", r.State.Files["a.json"]);
        Assert.Contains(r.Messages, m => m.Warning && m.Text.Contains("a 1.1, the pack has 1"));

        // Next start, with the pack's version of the mod: brought up from where it was left.
        var again = DefaultsRun.Run(V2, Lookup, r.State, Loaded, createMissing: true, files);
        Assert.Equal("""{ "x": 2, "y": 2 }""", files.Files["a.json"]);
        Assert.Empty(again.State.Files);
    }

    [Fact]
    public void A_fresh_install_gets_the_files_with_pack_values_written_on_the_server_only()
    {
        var client = new MemFiles([]);
        DefaultsRun.Run(V2, Lookup, null, Loaded, createMissing: false, client);
        Assert.Empty(client.Files);

        var server = new MemFiles([]);
        var r = DefaultsRun.Run(V2, Lookup, null, Loaded, createMissing: true, server);
        Assert.Equal(["b.yaml"], r.Created);
        Assert.Equal("version: 1\nk: 5\n", server.Files["b.yaml"]);
        Assert.False(server.Files.ContainsKey("a.json"));
    }

    [Fact]
    public void Without_the_old_versions_snapshot_the_file_is_left_alone()
    {
        var files = new MemFiles(new() { ["a.json"] = """{ "x": 1, "y": 1 }""" });
        var r = DefaultsRun.Run(V2, Lookup, new DefaultsState { PackVersion = "0.0.9" }, Loaded, createMissing: false, files);
        Assert.Equal("""{ "x": 1, "y": 1 }""", files.Files["a.json"]);
        Assert.Contains(r.Messages, m => m.Warning && m.Text.Contains("no defaults for pack 0.0.9"));
        Assert.Equal("0.2.0", r.State.PackVersion);
    }

    [Fact]
    public void The_state_round_trips()
    {
        var state = new DefaultsState { PackVersion = "0.2.0", Files = { ["a.json"] = "0.1.0" },
            Notice = new DefaultsNotice { FromVersion = "0.1.0", ToVersion = "0.2.0", Changes = ["x"], NotifiedPlayers = ["uid"] } };
        var back = DefaultsState.Parse(state.ToJson());
        Assert.Equal("0.2.0", back.PackVersion);
        Assert.Equal("0.1.0", back.Files["a.json"]);
        Assert.Equal(["uid"], back.Notice!.NotifiedPlayers);
        Assert.Throws<FormatException>(() => DefaultsState.Parse("{}"));
    }
}

/// <summary>The snapshots the mod ships (assets/seraphhorizons/config/configdefaults), held to the
/// readers and to the pack's lock.</summary>
public class ShippedSnapshotTests
{
    private static string Root => Path.Combine(AppContext.BaseDirectory, "configdefaults");

    public static IEnumerable<object[]> Versions() =>
        Directory.GetDirectories(Root).Select(d => new object[] { Path.GetFileName(d) });

    private static DefaultsSnapshot Load(string version)
    {
        var dir = Path.Combine(Root, version);
        var index = SnapshotIndex.Parse(File.ReadAllText(Path.Combine(dir, "index.json")));
        return new DefaultsSnapshot(index, n => File.Exists(Path.Combine(dir, "files", n)) ? File.ReadAllText(Path.Combine(dir, "files", n)) : null);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void Every_file_reads_and_every_pack_value_applies(string version)
    {
        var snap = Load(version);
        Assert.Equal(version, snap.Version);
        foreach (var f in snap.Index.Files.Values)
        {
            var problems = new List<string>();
            var text = snap.Effective(f.Name, problems);
            Assert.NotNull(text);
            Assert.Empty(problems);
            var root = ConfigText.Parse(text!, f.Format);
            Assert.Equal(NodeKind.Object, root.Kind);
            Assert.NotEmpty(f.Owners);
            Assert.All(f.Owners, o => Assert.True(snap.Index.Mods.ContainsKey(o), $"{f.Name}: owner {o}"));
        }
        foreach (var v in snap.Index.PackValues)
        {
            var node = ConfigText.Parse(snap.Effective(v.File)!, snap.Index.Files[v.File].Format).At(v.Path);
            Assert.Equal(v.Value, node!.Raw);
        }
    }

    private static string LockedVersion()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "pack-lock.json")));
        return doc.RootElement.GetProperty("pack").GetProperty("version").GetString()!;
    }

    [Fact]
    public void The_current_versions_snapshot_exists()
    {
        // Its mods against the lock are tools/tests/test_configdefaults.py's: a released version's
        // snapshot is frozen while the lock moves on (docs/config-defaults.md).
        var snap = Load(LockedVersion());
        Assert.Contains("seraphhorizons.json", snap.Index.Files.Keys);
    }

    [Fact]
    public void The_real_case_a_stale_carry_speed_follows_and_a_chosen_blade_wear_stays()
    {
        var snap = Load(LockedVersion());
        var now = snap.Effective("seraphhorizons.json")!;
        var old = ConfigText.SetValue(now, ConfigFormat.Json, ["TrunkEntitiesSettings", "CarrySpeedAtMaxLogs"], "0.02")!;
        old = ConfigText.SetValue(old, ConfigFormat.Json, ["BuckingSawmillSettings", "BladeWearPerStoredLog"], "0.25")!;
        var mine = ConfigText.SetValue(old, ConfigFormat.Json, ["BuckingSawmillSettings", "BladeWearPerStoredLog"], "0.7")!;
        var (text, changes) = ConfigReconciler.Reconcile("seraphhorizons.json", ConfigFormat.Json, mine, old, now, [], "0.0.1", snap.Version);
        var root = LenientJson.Parse(text);
        Assert.Equal(LenientJson.Parse(now).At(["TrunkEntitiesSettings", "CarrySpeedAtMaxLogs"])!.Number,
            root.At(["TrunkEntitiesSettings", "CarrySpeedAtMaxLogs"])!.Number);
        Assert.Equal(0.7, root.At(["BuckingSawmillSettings", "BladeWearPerStoredLog"])!.Number);
        Assert.Equal("TrunkEntitiesSettings.CarrySpeedAtMaxLogs", Assert.Single(changes).Path);
    }
}
