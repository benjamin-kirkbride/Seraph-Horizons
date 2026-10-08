using Atlas.XUnit;
using SeraphHorizons.Mod.ConfigDefaults;
using SeraphHorizons.Mod.ConfigDefaults.Core;
using SeraphHorizons.Mod.PackCheck;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SeraphHorizons.PackTests;

/// <summary>mods-src/seraphhorizons' <c>FollowPackDefaults</c> on a fresh install (the shared world
/// starts with an empty ModConfig): the first start records the pack version and changes nothing,
/// and the files the pack sets values in, BetterRuins' <c>betterruins.yaml</c> and Primitive
/// Survival's <c>primitivesurvival5.json</c>, are written with them before their mods read them. The
/// upgrade is <see cref="ConfigDefaultsScenarios"/>.</summary>
public partial class SharedWorldScenarios
{
    [AtlasScenario, ReadsBootLog]
    public void A_fresh_install_records_the_pack_and_gets_the_packs_own_values()
    {
        var system = World.Api.ModLoader.GetModSystem<ConfigDefaultsSystem>();
        Assert.NotNull(system);
        Assert.Empty(system.Changes);
        Assert.Equal(["betterruins.yaml", "primitivesurvival5.json"], system.Created);

        var state = DefaultsState.Parse(File.ReadAllText(Path.Combine(GamePaths.ModConfig, ConfigDefaultsSystem.StateFile)));
        Assert.Equal(PackCheckSystem.ReadEmbeddedLock().PackVersion, state.PackVersion);
        Assert.Null(state.Notice);

        // ConfigKit read the file the pack's mod wrote (and kept its values).
        var root = SimpleYaml.Parse(File.ReadAllText(Path.Combine(GamePaths.ModConfig, "betterruins.yaml")));
        Assert.Equal(500, root.At(["largeruins_min_spawn_distance"])!.Number);
        Assert.Equal(1200, root.At(["largeruins_min_distance"])!.Number);
        Assert.Equal(1.5, root.At(["vanilla_structures_spawn_chance"])!.Number);

        // Primitive Survival multiplied the Living Dead's spawn chance by the pack's 0 (its
        // UpdateSpawnRates, in AssetsFinalize), so they never spawn.
        var ps = LenientJson.Parse(File.ReadAllText(Path.Combine(GamePaths.ModConfig, "primitivesurvival5.json")));
        Assert.Equal(0, ps.At(["SpawnMultiplierLivingDead"])!.Number);
        var livingDead = World.Api.World.GetEntityType(new AssetLocation("primitivesurvival:livingdead-normal"));
        Assert.NotNull(livingDead);
        Assert.Equal(0, livingDead.Server.SpawnConditions.Runtime.Chance);

        var logged = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal)
            .Where(e => e.Message.Contains("Config defaults", StringComparison.Ordinal))
            .Select(e => $"[{e.Level}] {e.Message}")
            .ToList();
        Assert.True(logged.Count == 0, "Logged:\n" + string.Join("\n", logged));
    }
}
