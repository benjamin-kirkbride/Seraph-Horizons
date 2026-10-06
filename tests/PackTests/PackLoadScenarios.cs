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

    /// <summary>The server sends a client each item's and block's behaviours by registered class
    /// name. One added in code without a registration goes out with no name, and the client
    /// crashes reading it (ArgumentNullException in ReadItemTypePacket or ReadBlockTypePacket),
    /// which no scenario sees: Atlas runs no client.</summary>
    [AtlasScenario(TimeoutMs = 120_000)]
    public void Every_behaviour_sent_to_a_client_has_a_class_name()
    {
        var registry = World.Api.ClassRegistry;
        var unnamed = World.Api.World.Collectibles
            .Where(c => c?.Code != null)
            .SelectMany(c => c.CollectibleBehaviors.Select(b => (c.Code, Type: b.GetType())))
            .Where(x => (typeof(BlockBehavior).IsAssignableFrom(x.Type)
                ? registry.GetBlockBehaviorClassName(x.Type)
                : registry.GetCollectibleBehaviorClassName(x.Type)) == null)
            .GroupBy(x => x.Type, x => x.Code)
            .Select(g => $"{g.Key.FullName} (on {g.Count()}, e.g. {g.First()})")
            .ToList();
        Assert.True(unnamed.Count == 0, "Behaviours with no registered class:\n" + string.Join("\n", unnamed));
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
