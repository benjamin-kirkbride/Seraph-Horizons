using Atlas.Api;
using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons, ClearSky: the admin command <c>/clear</c> (day, no temporal storm,
/// clear weather), <c>/clear stay</c> (held) and <c>/clear stop</c> (put back as it was). A
/// survival world (<c>surviveandbuild</c>), so temporal storms run: in a creative one the game has
/// no storm schedule. Each scenario starts and ends unlocked, so they run in any order.
/// </summary>
[AtlasWorld(PlayStyle = "surviveandbuild")]
public class ClearCommandScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private ClearSky Clear => World.Api.ModLoader.GetModSystem<SeraphHorizonsSystem>().ClearSky
                              ?? throw new Xunit.Sdk.XunitException("no ClearSky on the server");
    private WeatherSystemServer Weather => World.Api.ModLoader.GetModSystem<WeatherSystemServer>();
    private SystemTemporalStability Storms => World.Api.ModLoader.GetModSystem<SystemTemporalStability>();
    private TemporalStormRunTimeData Storm => Storms.StormData;
    private GameCalendar Calendar => (GameCalendar)World.Api.World.Calendar;
    private double Now => Calendar.TotalDays;

    private async Task<string> Run(string command)
    {
        var result = await World.ExecuteCommand(command);
        output.WriteLine($"{command}: {(result.Ok ? "ok" : "FAILED")}: {result.Message}");
        Assert.True(result.Ok, $"{command}: {result.Message}");
        return result.Message ?? "";
    }

    /// <summary>Unlocked, with the game's default settings (a failed scenario may have left others).</summary>
    private async Task Unlocked()
    {
        if (Clear.Lock != null)
            await Run("/clear stop");
        Assert.Null(Clear.Lock);
        Weather.OverridePrecipitation = null;
        Weather.autoChangePatterns = true;
        Calendar.SetTimeSpeedModifier(ClearSky.Baseline, 60);
    }

    /// <summary>A player at the spawn, so the regions around it are loaded and simulated.</summary>
    private async Task<ITestPlayer> Player(string name)
    {
        var player = await World.JoinPlayer(name);
        await World.Until(() => Weather.weatherSimByMapRegion.Count > 0, 300);
        return player;
    }

    /// <summary>Night, a thunderstorm on a cumulonimbus sky in every loaded region, and the rain
    /// falling at the spawn (the rain noise moved on to where it rains there).</summary>
    private void MakeFoulWeatherAtNight()
    {
        Calendar.Add((float)((22 - Calendar.HourOfDay + Calendar.HoursPerDay) % Calendar.HoursPerDay));
        foreach (var region in Weather.weatherSimByMapRegion.Values)
        {
            Assert.True(region.SetWeatherPattern("cumulonimbus", updateInstant: true));
            Assert.True(region.SetWeatherEvent("heavythunder", updateInstant: true));
        }
        var spawn = World.Api.World.DefaultSpawnPosition.XYZ;
        for (int hour = 0; hour < 24 * 60 && Weather.GetPrecipitation(spawn) < 0.2f; hour++)
            Weather.RainCloudDaysOffset += 1.0 / Calendar.HoursPerDay;
        Assert.True(Weather.GetPrecipitation(spawn) >= 0.2f, "found no rain at the spawn in 60 days");
    }

    private HashSet<long> LoadedRegions() => Weather.weatherSimByMapRegion.Keys.ToHashSet();

    private static bool IsClear(WeatherSimulationRegion region) =>
        region.NewWePattern.config.Code == ClearSky.ClearPattern && region.Weight >= 1f
        && region.CurWeatherEvent.config.Code == ClearSky.NoEvent;

    /// <summary>Every region in <paramref name="regions"/> (all loaded ones by default) clear.
    /// Regions keep loading around a player who moved, so a one-shot check names the regions
    /// loaded when the command ran.</summary>
    private void AssertRegionsClear(HashSet<long>? regions = null)
    {
        var checkedRegions = Weather.weatherSimByMapRegion.Where(r => regions == null || regions.Contains(r.Key)).ToList();
        Assert.NotEmpty(checkedRegions);
        foreach (var (key, region) in checkedRegions)
            Assert.True(IsClear(region), $"region {key}: {region.NewWePattern.config.Code} (weight {region.Weight}), "
                                         + region.CurWeatherEvent.config.Code);
    }

    /// <summary>While locked: every loaded region is clear by the next upkeep, a second at most.</summary>
    private async Task AssertHeldClear()
    {
        await World.Until(() => Weather.weatherSimByMapRegion.Values.All(IsClear), 120);
        AssertRegionsClear();
    }

    private void AssertDay() =>
        Assert.InRange(Calendar.HourOfDay, ClearSkyPlan.DayFrom, ClearSkyPlan.DayUntil);

    /// <summary>Where the sun stands over the spawn at a number of days from now, and how much
    /// daylight there is there now.</summary>
    private Vintagestory.API.MathTools.Vec3f SunIn(double days) =>
        Calendar.GetSunPosition(World.Api.World.DefaultSpawnPosition.XYZ, Now + days);

    private float Daylight()
    {
        var spawn = World.Api.World.DefaultSpawnPosition.XYZ;
        return Calendar.GetDayLightStrength(spawn.X, spawn.Z);
    }

    private static void AssertSameSun(Vintagestory.API.MathTools.Vec3f expected, Vintagestory.API.MathTools.Vec3f actual)
    {
        Assert.Equal(expected.X, actual.X, 4);
        Assert.Equal(expected.Y, actual.Y, 4);
        Assert.Equal(expected.Z, actual.Z, 4);
    }

    private bool StormsRunning() =>
        AccessTools.Field(typeof(SystemTemporalStability), "config").GetValue(Storms) != null
        && (bool)AccessTools.Field(typeof(SystemTemporalStability), "stormsEnabled").GetValue(Storms)!;

    [AtlasScenario]
    public void Clear_is_registered_once_for_admins_only()
    {
        var clear = World.Api.ChatCommands.Get(ClearSky.Command);
        Assert.NotNull(clear);
        // Ours: another mod's /clear would have made this one's registration fail at startup.
        Assert.Equal(["stay", "stop"], clear.Subcommands.Select(c => c.Name).Order());

        // controlserver (vanilla /weather's privilege) is the admin role's alone among the default roles.
        var roles = World.Api.Server.Config.Roles.Cast<PlayerRole>().ToList();
        foreach (var role in roles)
            output.WriteLine($"{role.Code}: controlserver {role.Privileges.Contains(Privilege.controlserver)}");
        Assert.Contains(roles, r => r.Code == "admin" && r.Privileges.Contains(Privilege.controlserver));
        Assert.All(roles.Where(r => r.Code != "admin"), r => Assert.DoesNotContain(Privilege.controlserver, r.Privileges));
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_player_without_controlserver_is_refused_every_form()
    {
        await Unlocked();
        var player = await Player("clearplain");
        player.Player.SetRole("suplayer");
        try
        {
            Assert.False(player.Player.HasPrivilege(Privilege.controlserver));
            double before = Now;
            foreach (var command in new[] { "/clear", "/clear stay", "/clear stop" })
            {
                var result = await player.ExecuteCommand(command);
                output.WriteLine($"{command} as suplayer: {result.Ok} {result.Message}");
                Assert.False(result.Ok, command);
                Assert.Null(Clear.Lock);
            }
            Assert.Equal(before, Now, 3);
        }
        finally
        {
            player.Player.SetRole("admin");
        }
        Assert.True(player.Player.HasPrivilege(Privilege.controlserver));
        await player.ExecuteCommand("/clear stay");
        Assert.NotNull(Clear.Lock);
        await player.ExecuteCommand("/clear stop");
        Assert.Null(Clear.Lock);
    }

    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task Clear_sets_day_ends_the_storm_and_clears_the_weather()
    {
        await Unlocked();
        await Player("clearonce");
        Assert.True(StormsRunning(), "temporal storms are not running in this world");
        bool autoChange = Weather.autoChangePatterns;
        var speeds = new Dictionary<string, float>(Calendar.TimeSpeedModifiers);

        MakeFoulWeatherAtNight();
        Storm.nextStormTotalDays = Now;
        await World.Until(() => Storm.nowStormActive, 300);
        Assert.True(Storms.StormStrength > 0);

        var regions = LoadedRegions();
        await Run("/clear");

        AssertDay();
        Assert.False(Storm.nowStormActive);
        Assert.Equal(0f, Storms.StormStrength);
        // Scheduled afresh from now, as when a storm runs out, and not about to be announced.
        Assert.True(Storm.nextStormTotalDays - Now > ClearSky.StormWarningDays, $"next storm in {Storm.nextStormTotalDays - Now} days");
        Assert.Equal(99, Storm.stormDayNotify);
        AssertRegionsClear(regions);
        var spawn = World.Api.World.DefaultSpawnPosition.XYZ;
        Assert.True(Weather.GetPrecipitation(spawn) < ClearSkyPlan.DryPrecipitation, $"precipitation {Weather.GetPrecipitation(spawn)}");

        // One-shot: nothing is held.
        Assert.Null(Clear.Lock);
        Assert.Null(Weather.OverridePrecipitation);
        Assert.Equal(autoChange, Weather.autoChangePatterns);
        Assert.Equal(speeds, Calendar.TimeSpeedModifiers);
        double then = Calendar.TotalHours;
        await World.Ticks(30);
        Assert.True(Calendar.TotalHours > then, "time stood still after a one-shot /clear");
        // Each region has a full duration of clear sky ahead, so the game does not replace it at once.
        AssertRegionsClear(regions);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Clear_skips_a_storm_it_has_announced()
    {
        await Unlocked();
        await Player("clearwarned");
        Assert.True(StormsRunning(), "temporal storms are not running in this world");
        Storm.nextStormTotalDays = Now + 0.1;
        Storm.stormDayNotify = 1; // the game has said it is approaching

        await Run("/clear");

        Assert.False(Storm.nowStormActive);
        Assert.True(Storm.nextStormTotalDays - Now > ClearSky.StormWarningDays, $"next storm in {Storm.nextStormTotalDays - Now} days");
        Assert.Equal(99, Storm.stormDayNotify);

        // A storm further off is left where it is.
        Storm.nextStormTotalDays = Now + 3;
        await Run("/clear");
        Assert.Equal(3, Storm.nextStormTotalDays - Now, 2);
    }

    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task Stay_holds_the_sun_weather_and_storms_and_stop_puts_back_what_was()
    {
        await Unlocked();
        var player = await Player("clearstay");
        Assert.True(StormsRunning(), "temporal storms are not running in this world");

        // Settings unlike both the game's defaults and what the lock sets, to see them come back.
        Weather.OverridePrecipitation = 0.3f;
        Calendar.SetTimeSpeedModifier(ClearSky.Baseline, 30);
        bool autoChange = Weather.autoChangePatterns;
        var speeds = new Dictionary<string, float>(Calendar.TimeSpeedModifiers);
        MakeFoulWeatherAtNight();
        double stormAt = Storm.nextStormTotalDays = Now + 3;

        string said = await Run("/clear stay");
        Assert.DoesNotContain("already", said);
        var held = Clear.Lock;
        Assert.NotNull(held);
        Assert.Equal(0.3f, held.OverridePrecipitation);
        Assert.False(held.StopsTime);
        // The storm stays where it was; the clock moved on to day, closer to it.
        Assert.Equal(stormAt, Storm.nextStormTotalDays);
        double ahead = stormAt - Now;
        Assert.InRange(ahead, 2, 3);
        Assert.Equal(ahead, held.StormDaysAhead!.Value, 4);

        AssertDay();
        // The clock is left running at its speed.
        Assert.Equal(speeds, Calendar.TimeSpeedModifiers);
        Assert.True(Calendar.SpeedOfTime > 0);
        Assert.Equal(-1f, Weather.OverridePrecipitation);
        Assert.False(Weather.autoChangePatterns);
        Assert.Equal(0f, Weather.GetPrecipitation(World.Api.World.DefaultSpawnPosition.XYZ));
        await AssertHeldClear();

        // Again: said so, nothing changes.
        said = await Run("/clear stay");
        Assert.Contains("already", said);
        Assert.Same(held, Clear.Lock);

        // Time runs on, but the sun stands at summer noon whatever the hour or the season.
        double hours = Calendar.TotalHours;
        await World.Ticks(90);
        Assert.True(Calendar.TotalHours > hours, "time stood still under /clear stay");
        var noon = SunIn(0);
        float daylight = Daylight();
        output.WriteLine($"held sun {noon}, daylight {daylight}");
        Assert.True(noon.Y > 0, $"the held sun is below the horizon: {noon}");
        foreach (double days in new[] { 0.25, 0.5, 0.75, 100.3 })
            AssertSameSun(noon, SunIn(days));
        Calendar.Add(Calendar.HoursPerDay / 2f); // to the night
        await World.Ticks(5);
        Assert.Equal(daylight, Daylight(), 3);

        // The weather is held: /weather-style changes are undone within the second.
        Weather.autoChangePatterns = true;
        Weather.OverridePrecipitation = null;
        var thundering = LoadedRegions();
        foreach (var region in Weather.weatherSimByMapRegion.Values)
            region.SetWeatherEvent("heavythunder", updateInstant: true);
        await World.Ticks(60);
        Assert.False(Weather.autoChangePatterns);
        Assert.Equal(-1f, Weather.OverridePrecipitation);
        AssertRegionsClear(thundering);

        // A region loaded while held comes up clear: send the player 3 regions away.
        var keys = LoadedRegions();
        await player.TeleportTo(World.Spawn.AddCopy(3 * World.Api.WorldManager.RegionSize, 0, 0));
        await World.Until(() => Weather.weatherSimByMapRegion.Keys.Any(k => !keys.Contains(k)), 900);
        await World.Ticks(60);
        var fresh = LoadedRegions();
        fresh.ExceptWith(keys);
        output.WriteLine($"regions: {keys.Count} before, {fresh.Count} more after the teleport");
        Assert.NotEmpty(fresh);
        await AssertHeldClear();
        AssertRegionsClear(fresh);

        // No temporal storm: started by hand, it is ended within the second and kept as far off.
        Storm.nextStormTotalDays = Now;
        await World.Until(() => Storm.nowStormActive || Storm.nextStormTotalDays - Now > 1, 300);
        await World.Ticks(90);
        Assert.False(Storm.nowStormActive);
        Assert.True(Storm.nextStormTotalDays - Now >= ahead - 1e-3, $"next storm in {Storm.nextStormTotalDays - Now} days");

        // A restart: what the game does not save itself comes back from the savegame.
        Weather.autoChangePatterns = true;
        Clear.LoadLock();
        Assert.NotNull(Clear.Lock);
        Assert.Equal(0.3f, Clear.Lock!.OverridePrecipitation);
        Assert.False(Weather.autoChangePatterns);
        AssertSameSun(noon, SunIn(0.5));

        await Run("/clear stop");
        // The sun moves again: half a day on, it is somewhere else.
        Assert.True(Math.Abs(SunIn(0).Y - SunIn(0.5).Y) > 0.1, $"the sun stays put after /clear stop: {SunIn(0)}, {SunIn(0.5)}");
        Assert.Null(Clear.Lock);
        Assert.Equal(0.3f, Weather.OverridePrecipitation);
        Assert.Equal(autoChange, Weather.autoChangePatterns);
        Assert.Equal(speeds, Calendar.TimeSpeedModifiers);
        Assert.Equal(speeds.Values.Sum(), Calendar.SpeedOfTime, 3);
        Assert.True(Storm.nextStormTotalDays - Now >= ahead - 1e-3);
        Assert.Null(World.Api.WorldManager.SaveGame.GetData(ClearSky.SaveKey));
        double stopped = Calendar.TotalHours;
        await World.Ticks(30);
        Assert.True(Calendar.TotalHours > stopped, "time stood still after /clear stop");

        said = await Run("/clear stop");
        Assert.Contains("Nothing to release", said);

        // Back to the game's defaults for the other scenarios.
        Weather.OverridePrecipitation = null;
        Calendar.SetTimeSpeedModifier(ClearSky.Baseline, 60);
    }

    /// <summary>A world saved while the version that stopped time held it: the <c>baseline</c>
    /// time speed is put back once, the lock goes on with the sun held, and is saved without it.</summary>
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_lock_that_stopped_time_gets_its_time_back()
    {
        await Unlocked();
        await Player("clearold");
        // As that version left it: baseline holding the modifiers' sum at 0.
        float others = Calendar.TimeSpeedModifiers.Where(m => m.Key != ClearSky.Baseline).Sum(m => m.Value);
        Calendar.SetTimeSpeedModifier(ClearSky.Baseline, -others);
        Assert.Equal(0f, Calendar.SpeedOfTime, 3);
        World.Api.WorldManager.SaveGame.StoreData(ClearSky.SaveKey, System.Text.Encoding.UTF8.GetBytes(
            """{"HadBaseline":true,"Baseline":60,"OverridePrecipitation":null,"AutoChangePatterns":true,"StormDaysAhead":null}"""));

        Clear.LoadLock();
        Assert.NotNull(Clear.Lock);
        await World.Ticks(10);

        Assert.Equal(60f, Calendar.TimeSpeedModifiers[ClearSky.Baseline]);
        Assert.True(Calendar.SpeedOfTime > 0);
        Assert.False(Clear.Lock!.StopsTime);
        string saved = System.Text.Encoding.UTF8.GetString(World.Api.WorldManager.SaveGame.GetData(ClearSky.SaveKey));
        Assert.DoesNotContain("Baseline", saved);
        double hours = Calendar.TotalHours;
        await World.Ticks(30);
        Assert.True(Calendar.TotalHours > hours, "time stood still after the old lock was read");
        AssertSameSun(SunIn(0), SunIn(0.5));

        await Run("/clear stop");
        Assert.Null(Clear.Lock);
        Assert.Equal(60f, Calendar.TimeSpeedModifiers[ClearSky.Baseline]);
        await Unlocked();
    }

    [AtlasScenario]
    public async Task An_unknown_option_is_an_error_not_a_clear()
    {
        await Unlocked();
        var result = await World.ExecuteCommand("/clear stya");
        output.WriteLine(result.Message);
        Assert.False(result.Ok);
        Assert.Null(Clear.Lock);
    }
}
