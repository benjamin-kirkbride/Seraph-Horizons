using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.GameContent;
using Vintagestory.Server;

namespace SeraphHorizons.Mod;

/// <summary>
/// The <c>/clear</c> command, for admins (the <c>controlserver</c> privilege, as <c>/weather</c>):
/// <list type="bullet">
/// <item><c>/clear</c> sets the time to day, ends a temporal storm that is on or about to start,
/// and clears the weather: every loaded region to the clear sky pattern with no weather event, and
/// the rain moved on to the next dry spell.</item>
/// <item><c>/clear stay</c> does the same and holds it: time stands still, the weather stays clear
/// (regions loaded later too), and no temporal storm comes.</item>
/// <item><c>/clear stop</c> puts back what <c>stay</c> changed, as it was.</item>
/// </list>
///
/// Everything goes through what the game's own commands use. The weather: each region's
/// <c>WeatherSimulationRegion.SetWeatherPattern</c> and <c>SetWeatherEvent</c> (<c>/weather seti</c>
/// and <c>setev</c>), and for the rain, which is one noise over the whole world and not part of a
/// region's pattern, <c>RainCloudDaysOffset</c> (<c>/weather stoprain</c>, which moves it to the next
/// dry spell where the caller stands; here, where every player and the spawn are). The lock: the
/// precipitation override (<c>/weather setprecip -1</c>), auto-changing patterns off
/// (<c>/weather acp</c>), and the calendar's <c>baseline</c> time speed (<c>/time stop</c>), held so
/// that every modifier together is 0: the sleeping speed-up is cancelled too, the moment it is set
/// (a postfix on the calendar's <c>CalculateCurrentTimeSpeed</c>), so the clock neither drifts nor
/// runs back. The storms:
/// <c>SystemTemporalStability</c>'s run-time data, ended or skipped through its own tick.
///
/// The lock is kept in the savegame (<see cref="SaveKey"/>) with the settings as they were, and is
/// enforced again every second, so a region loaded later comes up clear and <c>/weather acp</c> or
/// <c>/time resume</c> do not undo it. With the switch off the command does not exist, and a lock
/// left in the savegame is released.
/// </summary>
public sealed class ClearSky
{
    public const string Command = "clear";
    public const string SaveKey = "seraphhorizons:clearlock";
    public const string ClearPattern = "clearsky";
    public const string NoEvent = "noevent";
    public const string Baseline = "baseline";

    /// <summary>A temporal storm this close (in days) is skipped: the game warns of it from 0.35.</summary>
    public const double StormWarningDays = 0.35;

    private static readonly FieldInfo? CalendarSentAt =
        AccessTools.Field(typeof(ServerMain), "lastCalendarFullUpdateSentToClient");
    private static readonly FieldInfo? StormConfig = AccessTools.Field(typeof(SystemTemporalStability), "config");
    private static readonly FieldInfo? StormsEnabled = AccessTools.Field(typeof(SystemTemporalStability), "stormsEnabled");
    private static readonly MethodInfo? StormTick =
        AccessTools.Method(typeof(SystemTemporalStability), "onTempStormTick", [typeof(float)]);
    private static readonly FieldInfo? RegionsLock = AccessTools.Field(typeof(WeatherSystemBase), "weatherSimByMapRegionLock");
    private static readonly MethodInfo? CalculateTimeSpeed = AccessTools.Method(typeof(GameCalendar), "CalculateCurrentTimeSpeed");

    // The server's calendar while locked, for the postfix, which sees every calendar (in
    // singleplayer the client's is in the same process). Set and cleared on the server thread.
    private static GameCalendar? _heldCalendar;
    private static Action? _calendarChanged;
    private static bool _holding;

    private readonly ICoreServerAPI _api;
    private ClearLock? _lock;

    public ClearSky(ICoreServerAPI api) => _api = api;

    /// <summary>The lock in force, or null.</summary>
    public ClearLock? Lock => _lock;

    private WeatherSystemServer Weather => _api.ModLoader.GetModSystem<WeatherSystemServer>();
    private SystemTemporalStability? Storms => _api.ModLoader.GetModSystem<SystemTemporalStability>();
    private IGameCalendar Calendar => _api.World.Calendar;

    /// <summary>Registers the command and the lock's upkeep.</summary>
    public void Register(Harmony harmony)
    {
        if (CalculateTimeSpeed != null)
            harmony.Patch(CalculateTimeSpeed, postfix: new HarmonyMethod(typeof(ClearSky), nameof(CalculateTimeSpeedPostfix)));
        else
            _api.Logger.Warning("[seraphhorizons] GameCalendar.CalculateCurrentTimeSpeed is gone; /clear stay holds the "
                                + "time from a tick instead, so sleeping can move the clock a little");
        _calendarChanged = ResendCalendar;
        _api.ChatCommands.Create(Command)
            .WithDescription("Stop all weather and temporal storms and set the time to day. "
                             + "'stay' also holds time and weather there until 'stop'.")
            .RequiresPrivilege(Privilege.controlserver)
            .HandleWith(OnClear)
            .BeginSubCommand("stay")
                .WithDescription("Clear, then hold it: time stands still, the weather stays clear and no "
                                 + "temporal storm comes, until /clear stop.")
                .HandleWith(OnStay)
            .EndSubCommand()
            .BeginSubCommand("stop")
                .WithDescription("Release /clear stay: time, weather and storms run again, with the settings as they were.")
                .HandleWith(OnStop)
            .EndSubCommand();
        // In case a time speed change gets past the postfix.
        _api.Event.RegisterGameTickListener(_ => HoldTime(), 100);
        _api.Event.RegisterGameTickListener(_ => HoldWeatherAndStorms(), 1000);
        _api.Event.SaveGameLoaded += LoadLock;
    }

    /// <summary>With the switch off: releases a lock left in the savegame, once the game's own
    /// systems have loaded theirs.</summary>
    public void ReleaseLeftoverLock()
    {
        _api.Event.SaveGameLoaded += LoadLock;
        _api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () =>
        {
            if (_lock == null)
                return;
            Release();
            _api.Logger.Notification("[seraphhorizons] /clear is switched off: released the /clear stay lock left in the savegame");
        });
    }

    /// <summary>Reads the lock from the savegame (on load). Auto-changing patterns are not saved by
    /// the game, so they are switched off again here; everything else the lock changed is saved by
    /// the game itself or held by the upkeep.</summary>
    public void LoadLock()
    {
        try
        {
            _lock = ClearLock.FromBytes(_api.WorldManager.SaveGame.GetData(SaveKey));
        }
        catch (Exception e)
        {
            _api.Logger.Error($"[seraphhorizons] Could not read the /clear stay lock from the savegame, dropping it: {e.Message}");
            _lock = null;
        }
        _heldCalendar = _lock != null ? Calendar as GameCalendar : null;
        if (_lock != null && Weather is { } weather)
            weather.autoChangePatterns = false;
    }

    /// <summary>Drops the static hold on the calendar (the mod system's <c>Dispose</c>).</summary>
    public static void Unbind()
    {
        _heldCalendar = null;
        _calendarChanged = null;
    }

    private TextCommandResult OnClear(TextCommandCallingArgs args)
    {
        if (args.RawArgs.PeekWord() is { } extra)
            return TextCommandResult.Error(Text(args, "unknown", extra), "wrongarg");
        return TextCommandResult.Success(string.Join(" ", ClearNow(args, moveRain: true)));
    }

    private TextCommandResult OnStay(TextCommandCallingArgs args)
    {
        if (_lock != null)
            return TextCommandResult.Success(Text(args, "stay-already"));
        var said = ClearNow(args, moveRain: false);

        var weather = Weather;
        var storms = Storms;
        var modifiers = (Calendar as GameCalendar)?.TimeSpeedModifiers;
        _lock = new ClearLock
        {
            HadBaseline = modifiers?.ContainsKey(Baseline) == true,
            Baseline = modifiers?.GetValueOrDefault(Baseline) ?? 0,
            OverridePrecipitation = weather.OverridePrecipitation,
            AutoChangePatterns = weather.autoChangePatterns,
            StormDaysAhead = storms != null && StormsRunning(storms)
                ? Math.Max(0, storms.StormData.nextStormTotalDays - Calendar.TotalDays)
                : null,
        };
        Save();
        _heldCalendar = Calendar as GameCalendar;
        HoldTime();
        HoldWeatherAndStorms();
        said.Add(Text(args, "stay"));
        return TextCommandResult.Success(string.Join(" ", said));
    }

    private TextCommandResult OnStop(TextCommandCallingArgs args)
    {
        if (_lock == null)
            return TextCommandResult.Success(Text(args, "stop-notlocked"));
        Release();
        return TextCommandResult.Success(Text(args, "stop"));
    }

    /// <summary>The one-shot clear: day, no storm, clear weather. Returns what it did, to say.</summary>
    private List<string> ClearNow(TextCommandCallingArgs args, bool moveRain)
    {
        var said = new List<string>();

        // The clock first: moving it on can bring the rain or a storm closer.
        double hours = ClearSkyPlan.HoursToDay(Calendar.HourOfDay, Calendar.HoursPerDay);
        if (hours > 0)
        {
            Calendar.Add((float)hours);
            ResendCalendar();
            said.Add(Text(args, "time-set", Calendar.PrettyDate()));
        }

        if (Storms is { } storms && StormsRunning(storms))
        {
            switch (EndStorm(storms))
            {
                case StormEnd.Ended:
                    said.Add(Text(args, "storm-ended", storms.StormData.nextStormTotalDays - Calendar.TotalDays));
                    break;
                case StormEnd.Skipped:
                    said.Add(Text(args, "storm-skipped", storms.StormData.nextStormTotalDays - Calendar.TotalDays));
                    break;
            }
        }

        var weather = Weather;
        said.Add(Text(args, "regions", ClearRegions(weather, fresh: true)));
        if (moveRain)
            said.Add(MoveRainOn(args, weather));
        return said;
    }

    /// <summary>Moves the rain noise on to the next dry spell at every player and the spawn, as
    /// <c>/weather stoprain</c> does for the caller's position.</summary>
    private string MoveRainOn(TextCommandCallingArgs args, WeatherSystemServer weather)
    {
        if (weather.OverridePrecipitation is { } fixedPrecip)
            return fixedPrecip <= 0 ? Text(args, "rain-none-overridden") : Text(args, "rain-overridden");

        var places = _api.World.AllOnlinePlayers.Select(p => p.Entity?.Pos.XYZ).OfType<Vintagestory.API.MathTools.Vec3d>()
            .Append(_api.World.DefaultSpawnPosition.XYZ).ToList();
        double now = Calendar.TotalDays;
        var spell = ClearSkyPlan.FindDrySpell(
            days => places.All(p => weather.GetPrecipitation(p.X, p.Y, p.Z, now + days) < ClearSkyPlan.DryPrecipitation),
            1.0 / Calendar.HoursPerDay);
        if (spell == null)
            return Text(args, "rain-nodry");
        if (spell.Value.StartDays > 0)
        {
            weather.RainCloudDaysOffset += spell.Value.StartDays;
            weather.broadCastConfigUpdate();
        }
        return Text(args, "rain-dry", spell.Value.LengthDays);
    }

    /// <summary>Every loaded region to the clear sky pattern with no weather event, at once.
    /// Returns how many regions are loaded. <paramref name="fresh"/> sets them even where they are
    /// already clear, so each starts a full duration: one whose time ran out (the clock may just
    /// have moved on) would be replaced at the next tick while patterns change by themselves.</summary>
    private int ClearRegions(WeatherSystemServer weather, bool fresh)
    {
        WeatherSimulationRegion[] regions;
        object? sync = RegionsLock?.GetValue(weather);
        if (sync != null)
            lock (sync) regions = weather.weatherSimByMapRegion.Values.ToArray();
        else
            regions = weather.weatherSimByMapRegion.Values.ToArray();

        foreach (var region in regions)
        {
            bool changed = false;
            if (fresh || region.NewWePattern?.config.Code != ClearPattern || region.Transitioning || region.Weight < 1)
                changed |= region.SetWeatherPattern(ClearPattern, updateInstant: true);
            if (fresh || region.CurWeatherEvent?.config.Code != NoEvent)
                changed |= region.SetWeatherEvent(NoEvent, updateInstant: true);
            if (changed)
                region.TickEvery25ms(0.025f);
        }
        return regions.Length;
    }

    private enum StormEnd { None, Ended, Skipped }

    /// <summary>Ends a temporal storm that is on, or skips one the game is about to warn of or has
    /// warned of, through the game's own storm tick: it ends the storm (and sends half of its
    /// creatures away) and schedules the next one from now, as when a storm runs out. A storm
    /// further off is left as scheduled.</summary>
    private StormEnd EndStorm(SystemTemporalStability storms)
    {
        var data = storms.StormData;
        double now = Calendar.TotalDays;
        StormEnd result;
        if (data.nowStormActive)
        {
            data.stormActiveTotalDays = now - 0.001;
            data.stormDayNotify = -1; // no "waning" message
            result = StormEnd.Ended;
        }
        else if (data.nextStormTotalDays - now < StormWarningDays)
        {
            // Long enough ago that the tick takes it as missed and schedules the next one.
            data.nextStormTotalDays = now - 10;
            data.stormDayNotify = 0; // no "imminent" message
            result = StormEnd.Skipped;
        }
        else
            return StormEnd.None;

        StormTick!.Invoke(storms, [0f]);
        data = storms.StormData;
        data.stormDayNotify = 99; // the next storm is announced as usual
        return result;
    }

    /// <summary>Whether the game is running temporal storms (not off, and not a creative world),
    /// and this code can reach them.</summary>
    private bool StormsRunning(SystemTemporalStability storms)
    {
        if (StormConfig == null || StormsEnabled == null || StormTick == null)
        {
            _api.Logger.Warning("[seraphhorizons] SystemTemporalStability has changed (config, stormsEnabled or "
                                + "onTempStormTick missing); /clear leaves temporal storms alone");
            return false;
        }
        return StormConfig.GetValue(storms) != null && (bool)StormsEnabled.GetValue(storms)!;
    }

    /// <summary>While locked, keeps every time speed modifier together at 0 through the
    /// <c>baseline</c> one: the sleeping speed-up and a <c>/time</c> command are cancelled.</summary>
    private void HoldTime()
    {
        if (_lock != null && Calendar is GameCalendar calendar)
            Hold(calendar);
    }

    /// <summary>Runs after a calendar sums its time speed modifiers; on the held one, sets
    /// <c>baseline</c> so that they sum to 0.</summary>
    public static void CalculateTimeSpeedPostfix(GameCalendar __instance)
    {
        if (!_holding && ReferenceEquals(__instance, _heldCalendar))
            Hold(__instance);
    }

    private static void Hold(GameCalendar calendar)
    {
        float speed = calendar.SpeedOfTime;
        if (Math.Abs(speed) < 1e-4f)
            return;
        _holding = true;
        try
        {
            calendar.SetTimeSpeedModifier(Baseline, calendar.TimeSpeedModifiers.GetValueOrDefault(Baseline) - speed);
        }
        finally
        {
            _holding = false;
        }
        _calendarChanged?.Invoke();
    }

    /// <summary>While locked: no rain, patterns that stay put, every loaded region clear (regions
    /// loaded since the last second too), and the next temporal storm kept as far off as it was.</summary>
    private void HoldWeatherAndStorms()
    {
        if (_lock == null)
            return;
        var weather = Weather;
        weather.autoChangePatterns = false;
        if (weather.OverridePrecipitation != -1)
        {
            weather.OverridePrecipitation = -1;
            weather.broadCastConfigUpdate();
        }
        ClearRegions(weather, fresh: false);

        if (_lock.StormDaysAhead is not { } ahead || Storms is not { } storms || !StormsRunning(storms))
            return;
        if (storms.StormData.nowStormActive)
            EndStorm(storms);
        double keepAt = Calendar.TotalDays + ahead;
        if (storms.StormData.nextStormTotalDays < keepAt - 1e-6)
        {
            storms.StormData.nextStormTotalDays = keepAt;
            BroadcastStorms(storms);
        }
    }

    /// <summary>Puts back what the lock changed, as it was before, and drops it.</summary>
    private void Release()
    {
        var lk = _lock!;
        _lock = null;
        _heldCalendar = null;
        Save();

        var weather = Weather;
        weather.OverridePrecipitation = lk.OverridePrecipitation;
        weather.broadCastConfigUpdate();
        weather.autoChangePatterns = lk.AutoChangePatterns;

        if (Calendar is GameCalendar calendar)
        {
            if (lk.HadBaseline)
                calendar.SetTimeSpeedModifier(Baseline, lk.Baseline);
            else
                calendar.RemoveTimeSpeedModifier(Baseline);
            ResendCalendar();
        }

        if (lk.StormDaysAhead is { } ahead && Storms is { } storms && StormsRunning(storms))
        {
            storms.StormData.nextStormTotalDays = Calendar.TotalDays + ahead;
            BroadcastStorms(storms);
        }
    }

    private void Save() => _api.WorldManager.SaveGame.StoreData(SaveKey, _lock?.ToBytes());

    private void BroadcastStorms(SystemTemporalStability storms) =>
        _api.Network.GetChannel("temporalstability")?.BroadcastPacket(storms.StormData);

    /// <summary>Sends the calendar to the clients now rather than within the minute, as
    /// <c>/time</c> does.</summary>
    private void ResendCalendar()
    {
        if (_api.World is ServerMain server)
            CalendarSentAt?.SetValue(server, -3_600_000L);
    }

    private static string Text(TextCommandCallingArgs args, string key, params object[] values) =>
        Lang.GetL(args.LanguageCode ?? Lang.DefaultLocale, $"seraphhorizons:clearsky-{key}", values);
}
