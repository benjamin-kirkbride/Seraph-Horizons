using System.Text.Json;
using Atlas.XUnit;
using Vintagestory.API.Common;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SeraphHorizons.PackTests;

/// <summary>The pack as a whole: every locked mod loads, at its locked version, cleanly.</summary>
[AtlasWorld]
public class PackLoadScenarios : AtlasScenarioBase
{
    public static TheoryData<string, string> LockedMods()
    {
        var data = new TheoryData<string, string>();
        foreach (var (id, version) in PackLock.Mods) data.Add(id, version);
        return data;
    }

    [AtlasTheory(TimeoutMs = 120_000), MemberData(nameof(LockedMods))]
    public void Locked_mod_is_loaded_at_locked_version(string modId, string version)
    {
        Assert.True(World.Api.ModLoader.IsModEnabled(modId), $"{modId} is not enabled");
        Assert.Equal(version, World.Api.ModLoader.GetMod(modId).Info.Version);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Server_boots_and_ticks_without_errors()
    {
        await World.Ticks(20);
        var errors = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Error or EnumLogType.Fatal)
            .Select(e => $"[{e.Level}] {e.DescribeSource()}: {e.Message}")
            .ToList();
        Assert.True(errors.Count == 0, "Errors logged:\n" + string.Join("\n", errors));
    }
}

internal static class PackLock
{
    public static IReadOnlyList<(string Id, string Version)> Mods { get; } = Load();

    private static List<(string, string)> Load()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "lock.json")));
        return doc.RootElement.GetProperty("mods").EnumerateArray()
            .Select(m => (m.GetProperty("id").GetString()!, m.GetProperty("version").GetString()!))
            .ToList();
    }
}
