using System.Text.Json;
using System.Text.RegularExpressions;
using Atlas.XUnit;
using Vintagestory.API.Common;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SeraphHorizons.PackTests;

/// <summary>The pack as a whole: every locked mod loads, at its locked version, cleanly.</summary>
public partial class SharedWorldScenarios
{
    public static TheoryData<string, string> LockedMods()
    {
        var data = new TheoryData<string, string>();
        // Client-only mods never load on a dedicated server, which is what Atlas runs.
        foreach (var (id, version, side) in PackLock.Mods)
            if (side != "client") data.Add(id, version);
        return data;
    }

    [AtlasTheory(TimeoutMs = 120_000), MemberData(nameof(LockedMods))]
    public void Locked_mod_is_loaded_at_locked_version(string modId, string version)
    {
        Assert.True(World.Api.ModLoader.IsModEnabled(modId), $"{modId} is not enabled");
        Assert.Equal(version, World.Api.ModLoader.GetMod(modId).Info.Version);
    }

    [AtlasScenario(TimeoutMs = 120_000), ReadsBootLog]
    public async Task Server_boots_and_ticks_without_errors()
    {
        await World.Ticks(20);
        // Known cross-mod errors (pack/known-errors.json, one issue each) are tolerated.
        var errors = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Error or EnumLogType.Fatal)
            .Where(e => !PackLock.KnownErrors.Any(k => k.IsMatch(e.Message)))
            .Select(e => $"[{e.Level}] {e.DescribeSource()}: {e.Message}")
            .ToList();
        Assert.True(errors.Count == 0, "Errors logged:\n" + string.Join("\n", errors));
    }
}

internal static class PackLock
{
    public static IReadOnlyList<(string Id, string Version, string Side)> Mods { get; } = Load();

    /// <summary>pack/known-errors.json: understood cross-mod problems, one issue each.</summary>
    public static IReadOnlyList<Regex> KnownErrors { get; } = LoadKnown("errors", anchored: false);

    public static IReadOnlyList<Regex> KnownSchematicBlocks { get; } = LoadKnown("schematicBlocks", anchored: true);

    private static List<Regex> LoadKnown(string section, bool anchored)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "known-errors.json")));
        return doc.RootElement.GetProperty(section).EnumerateArray()
            .Select(e => e.GetProperty("pattern").GetString()!)
            .Select(p => new Regex(anchored ? $"^(?:{p})$" : p))
            .ToList();
    }

    private static List<(string, string, string)> Load()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "lock.json")));
        return doc.RootElement.GetProperty("mods").EnumerateArray()
            .Select(m => (m.GetProperty("id").GetString()!, m.GetProperty("version").GetString()!,
                          m.GetProperty("side").GetString()!))
            .ToList();
    }
}
