using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.SeraphTweaks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphtweaks, BoilerLidRelief: ppex's boiler burst (BlockEntityBoiler.Explode) opens
/// the boiler's lid instead, and the English text no longer says a boiler explodes. The scenarios
/// call Explode() directly: ppex's own over-pressure timer is what decides to call it.
/// Also its ConfigKit settings (assets/seraphtweaks/config/configlib-patches.json), which thin out
/// Battle Towers' surface towers.
/// </summary>
[AtlasWorld]
public class SeraphTweaksScenarios : AtlasScenarioBase
{
    private const string Boiler = "ppex:boilercornish-north";

    private IWorldAccessor W => World.Api.World;

    private async Task<BlockEntity> PlaceBoiler(BlockPos pos)
    {
        World.SetBlock(Boiler, pos);
        await World.Ticks(2);
        return W.BlockAccessor.GetBlockEntity(pos)
               ?? throw new Xunit.Sdk.XunitException($"{Boiler} placed without a block entity");
    }

    private static bool LidOpen(BlockEntity boiler) =>
        (bool)AccessTools.Property(boiler.GetType(), "LidOpen").GetValue(boiler)!;

    private static void Explode(BlockEntity boiler) =>
        AccessTools.Method(boiler.GetType(), "Explode", Type.EmptyTypes).Invoke(boiler, null);

    [AtlasScenario]
    public async Task Over_pressure_opens_the_lid_and_leaves_the_boiler_standing()
    {
        var pos = World.Spawn.AddCopy(40, 2, 40);
        var boiler = await PlaceBoiler(pos);
        Assert.False(LidOpen(boiler));

        Explode(boiler);
        await World.Ticks(2);

        Assert.Equal(Boiler, World.BlockAt(pos).Code.ToString());
        Assert.Same(boiler, W.BlockAccessor.GetBlockEntity(pos));
        Assert.True(LidOpen(boiler));
    }

    [AtlasScenario]
    public async Task Over_pressure_with_the_lid_open_leaves_it_open()
    {
        var pos = World.Spawn.AddCopy(-40, 2, -40);
        var boiler = await PlaceBoiler(pos);
        AccessTools.Method(boiler.GetType(), "ToggleLid").Invoke(boiler, null);

        Explode(boiler);
        await World.Ticks(2);

        Assert.Equal(Boiler, World.BlockAt(pos).Code.ToString());
        Assert.True(LidOpen(boiler));
    }

    // Fails when ppex rewords a passage: update BoilerLidRelief.LangEdits to match.
    [AtlasScenario]
    public void Text_says_the_lid_blows_open()
    {
        Assert.All(BoilerLidRelief.LangEdits, edit =>
        {
            var text = Lang.AvailableLanguages[edit.Language].GetAllEntries()[edit.Key];
            Assert.Contains(edit.New, text);
            Assert.DoesNotContain(edit.Old, text);
        });
        Assert.Equal("Over-pressure! 12s until the lid blows open!",
            Lang.GetL("en", "ppex:boiler-info-overpressure", 12));
        Assert.Equal("Превышение давления! Через 12 с давление откинет крышку!",
            Lang.GetL("ru", "ppex:boiler-info-overpressure", 12));
        Assert.Equal("Перевищення тиску! Через 12 с тиск відкине кришку!",
            Lang.GetL("uk", "ppex:boiler-info-overpressure", 12));
    }

    // Fails when ppex ships a new translation: add its LangEdits.
    [AtlasScenario]
    public void Every_ppex_language_is_reworded()
    {
        var shipped = World.Api.Assets.GetMany("lang/", BoilerLidRelief.ModId, loadAsset: false)
            .Select(asset => asset.Location.GetName())
            .Where(name => name.EndsWith(".json") && !name.StartsWith("worldconfig-"))
            .Select(name => name[..^".json".Length])
            .Order();
        var edited = BoilerLidRelief.LangEdits.Select(edit => edit.Language).Distinct().Order();
        Assert.Equal(shipped, edited);
    }

    // ConfigKit writes the settings into Battle Towers' own patch file, by position, before the
    // game applies it. Fails when Battle Towers reorders that file or ConfigKit stops applying:
    // match the paths in configlib-patches.json to the new layout.
    [AtlasScenario]
    public void Surface_battle_towers_are_thinned_out()
    {
        var structures = JsonObject.FromJson(
                World.Api.Assets.Get(new AssetLocation("game", "worldgen/structures.json")).ToText())
            ["structures"].AsArray()!;
        var towers = Assert.Single(structures, s => s["code"].AsString() == "surfacetowers");
        Assert.Equal(0.01f, towers["chance"].AsFloat(), 4);
        Assert.Equal(600, towers["minGroupDistance"].AsInt());
        // The other two keep what Battle Towers ships.
        var hard = Assert.Single(structures, s => s["code"].AsString() == "surfacehardtowers");
        Assert.Equal(0.005f, hard["chance"].AsFloat(), 4);
        Assert.Equal(1000, hard["minGroupDistance"].AsInt());
    }
}
