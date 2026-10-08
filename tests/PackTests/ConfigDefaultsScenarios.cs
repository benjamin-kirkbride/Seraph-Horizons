using Atlas.XUnit;
using SeraphHorizons.Mod;
using SeraphHorizons.Mod.BuckingSawmill.Core;
using SeraphHorizons.Mod.ConfigDefaults;
using SeraphHorizons.Mod.ConfigDefaults.Core;
using SeraphHorizons.Mod.TrunkEntities.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons' <c>FollowPackDefaults</c> (ConfigDefaults/, docs/config-defaults.md) end
/// to end: an install last brought to a made-up earlier pack version, <c>0.0.1-test</c>, whose
/// snapshot is seeded in the data folder's <c>ConfigDefaults/</c> (the extra snapshots a build
/// does not carry), boots this tree's pack. Its <c>seraphhorizons.json</c> still has that version's
/// trunk carry speed (the real case: 0.02, long since changed) and a blade wear set by hand; its
/// <c>betterruins.yaml</c> (ConfigKit's YAML) still has BetterRuins' own large ruin spacing, which
/// the pack now sets itself, and a megastructure spacing set by hand. The untouched values follow
/// the current defaults before any mod reads them; the hand-set ones stay. Its own world: the seeded
/// ModConfig is this class's alone (the `switches` shard).
/// </summary>
[AtlasWorld]
[AtlasDataFiles("fixtures/configdefaults")]
public class ConfigDefaultsScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private ConfigDefaultsSystem System => World.Api.ModLoader.GetModSystem<ConfigDefaultsSystem>()
                                           ?? throw new Xunit.Sdk.XunitException("no ConfigDefaultsSystem");

    private string Changes() => string.Join("\n", System.Changes.Select(c => c.Describe()));

    [AtlasScenario]
    public void An_untouched_stale_default_follows_and_a_hand_set_value_stays_in_the_mods_own_config()
    {
        output.WriteLine(Changes());
        var config = SeraphHorizonsSystem.ConfigFor(World.Api);
        Assert.Equal(new TrunkEntityConfig().CarrySpeedAtMaxLogs, config.TrunkEntitiesSettings.CarrySpeedAtMaxLogs);
        Assert.NotEqual(0.02f, config.TrunkEntitiesSettings.CarrySpeedAtMaxLogs);
        Assert.Equal(0.7f, config.BuckingSawmillSettings.BladeWearPerStoredLog);
        Assert.NotEqual(new MillConfig().BladeWearPerStoredLog, config.BuckingSawmillSettings.BladeWearPerStoredLog);
        Assert.Contains(System.Changes, c => c is { File: "seraphhorizons.json", Path: "TrunkEntitiesSettings.CarrySpeedAtMaxLogs", From: "0.02" });
        Assert.DoesNotContain(System.Changes, c => c.Path.Contains("BladeWearPerStoredLog"));
    }

    [AtlasScenario]
    public void A_pack_value_reaches_an_untouched_yaml_setting_and_a_hand_set_one_stays()
    {
        output.WriteLine(Changes());
        var yaml = File.ReadAllText(Path.Combine(GamePaths.ModConfig, "betterruins.yaml"));
        var root = SimpleYaml.Parse(yaml);
        Assert.Equal(1200, root.At(["largeruins_min_distance"])!.Number);
        Assert.Equal(3000, root.At(["megastructures_min_distance"])!.Number);
        Assert.Contains(System.Changes, c => c is { File: "betterruins.yaml", Path: "largeruins_min_distance", From: "2800", To: "1200" });
        Assert.Equal(2, System.Changes.Count);
    }

    [AtlasScenario]
    public void The_record_moves_to_the_current_pack_and_holds_the_admins_notice()
    {
        var state = DefaultsState.Parse(File.ReadAllText(Path.Combine(GamePaths.ModConfig, ConfigDefaultsSystem.StateFile)));
        Assert.Equal(SeraphHorizons.Mod.PackCheck.PackCheckSystem.ReadEmbeddedLock().PackVersion, state.PackVersion);
        Assert.Empty(state.Files);
        Assert.NotNull(state.Notice);
        Assert.Equal("0.0.1-test", state.Notice!.FromVersion);
        Assert.Equal(2, state.Notice.Changes.Count);
    }
}
