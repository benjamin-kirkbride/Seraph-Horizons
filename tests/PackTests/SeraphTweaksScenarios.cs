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
/// CreativeSteamSource: the creative steam source fills a ppex pipe placed against it with steam,
/// up to its set pressure. It targets ppex 0.7.1 / exlib 0.8.4, which are not on the ModDB yet:
/// while the pack pins an older ppex the scenarios require the block left out instead.
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

    // Fails when ppex's library renames or changes the engine-explosion sound: point
    // BoilerLidRelief.BlowSound at the new one. A sound played from the server leaves no trace to
    // assert on, so this checks the sound itself.
    [AtlasScenario]
    public void The_lid_blows_open_with_the_engine_explosion_sound()
    {
        var engineSound = AccessTools.Field(AccessTools.TypeByName("ExpandedLib.Helpers.ExSounds"), "MediumExplosion");
        Assert.NotNull(engineSound);
        Assert.Equal(BoilerLidRelief.BlowSound, (AssetLocation?)engineSound.GetValue(null));
        Assert.True(World.Api.Assets.Exists(BoilerLidRelief.BlowSound.Clone().WithPathAppendixOnce(".ogg")));
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

    private const string SteamSource = "seraphtweaks:" + CreativeSteamSource.BlockCode;

    // A pipe's view of its network (ppex's IPipeNode), read by name as the mod does.
    private static T PipeValue<T>(BlockEntity pipe, string property) =>
        (T)AccessTools.Property(AccessTools.TypeByName(CreativeSteamSource.PipeNodeType), property).GetValue(pipe)!;

    // The first ppex whose pipes the steam source binds to (exlib 0.8 moved them to ExpandedLib.Industry).
    private static readonly Version SteamSourcePpex = new(0, 7, 1);

    /// <summary>Whether the loaded ppex predates <see cref="SteamSourcePpex"/>; if so, requires the
    /// steam source left out of the game, as the mod does when ppex's members are not where it looks.</summary>
    private bool SteamSourceLeftOutForOldPpex()
    {
        var ppex = Version.Parse(World.Api.ModLoader.GetMod("ppex").Info.Version.Split('-')[0]);
        if (ppex >= SteamSourcePpex)
            return false;
        Assert.False(CreativeSteamSource.Bound);
        Assert.Null(W.GetBlock(new AssetLocation(SteamSource)));
        return true;
    }

    [AtlasScenario]
    public void Steam_source_is_in_the_creative_inventory_only()
    {
        if (SteamSourceLeftOutForOldPpex())
            return;
        Assert.True(CreativeSteamSource.Bound);
        var block = W.GetBlock(new AssetLocation(SteamSource));
        Assert.NotNull(block);
        Assert.IsType<BlockCreativeSteamSource>(block);
        Assert.Contains("ppex", block.CreativeInventoryTabs);
        Assert.Empty(block.Drops ?? []);
        Assert.DoesNotContain(W.GridRecipes, r => r.Output?.Code == block.Code);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Steam_source_fills_a_connected_pipe_up_to_its_pressure()
    {
        if (SteamSourceLeftOutForOldPpex())
            return;
        // source | pipe (west-east) | rock: the pipe opens only onto the source, so nothing leaks.
        var pos = World.Spawn.AddCopy(0, 2, 60);
        World.SetBlock(SteamSource, pos);
        World.SetBlock("ppex:pipe-straight-we-iron", pos.EastCopy());
        World.SetBlock("game:rock-granite", pos.EastCopy(2));
        await World.Ticks(2);
        var source = Assert.IsType<BlockEntityCreativeSteamSource>(W.BlockAccessor.GetBlockEntity(pos));
        var pipe = W.BlockAccessor.GetBlockEntity(pos.EastCopy())
                   ?? throw new Xunit.Sdk.XunitException("pipe placed without a block entity");

        // At full flow and 2 atm: the network fills to the set pressure and goes no higher.
        source.Configure(pressure: 2, flow: CreativeSteamSource.MaxFlowSetting);
        await World.Until(() => PipeValue<string>(pipe, "Medium") == "Steam"
                                && PipeValue<float>(pipe, "Pressure") > 1.9f, 900);
        await World.Ticks(90);
        Assert.InRange(PipeValue<float>(pipe, "Pressure"), 1.9f, 2.05f);
        Assert.Equal("Steam", PipeValue<string>(pipe, "Medium"));
    }
}
