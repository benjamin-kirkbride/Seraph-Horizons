using System.Text.Json;
using System.Text.Json.Serialization;

namespace SeraphHorizons.Mod.Core;

/// <summary>
/// The <c>/clear</c> command (<c>ClearSky</c>): how far to move the clock to reach daytime, how
/// far to move the rain forward to reach a dry spell, and where <c>/clear stay</c> holds the sun.
/// Game-independent, so tests/ runs it without the game.
/// </summary>
public static class ClearSkyPlan
{
    /// <summary>Daytime, in hours of a 24-hour day: from the game's <c>/time set morning</c> (8)
    /// up to 16, two hours before <c>sunset</c> (17.5). Inside it the clock is left alone.</summary>
    public const double DayFrom = 8, DayUntil = 16;

    /// <summary>Where the clock goes when it is not daytime: the game's <c>/time set day</c>.</summary>
    public const double Noon = 12;

    /// <summary>Below this the game counts a place as not raining: <c>/weather stoprain</c>'s
    /// threshold on <c>GetPrecipitation</c>.</summary>
    public const float DryPrecipitation = 0.04f;

    /// <summary>How far ahead to look for a dry spell: <c>/weather stoprain</c>'s 21 days.</summary>
    public const double SearchDays = 21;

    /// <summary>A dry spell this long (in days) is taken as soon as one starts.</summary>
    public const double WantedDryDays = 1;

    /// <summary>The day fraction <c>/clear stay</c> holds the sun at: noon, hour angle 0 for the
    /// survival mod's sun (<c>SurvivalCoreSystem.GetSolarSphericalCoords</c> takes the hour angle
    /// from <c>dayRel - 0.5</c>).</summary>
    public const float NoonDayRel = 0.5f;

    /// <summary>The survival mod's sun takes its declination from
    /// <c>-tilt * cos(2 pi (yearRel + 10/365))</c>: the year starts 10 days after the northern
    /// winter solstice.</summary>
    public const double SolsticeLeadYears = 10.0 / 365;

    /// <summary>The year fraction of midsummer, where the noon sun stands highest: half a year after
    /// the northern winter solstice in the northern hemisphere (the declination at its most
    /// northern), and the northern winter solstice itself in the southern one (most southern).
    /// <c>/clear stay</c> holds the sun there.</summary>
    public static float MidsummerYearRel(bool southern)
    {
        double rel = (southern ? 1.0 : 0.5) - SolsticeLeadYears;
        return (float)(rel - Math.Floor(rel));
    }

    /// <summary>Hours to add to the clock to reach daytime: 0 inside
    /// [<see cref="DayFrom"/>, <see cref="DayUntil"/>), otherwise to the next <see cref="Noon"/>.
    /// The hours scale with <paramref name="hoursPerDay"/>, as the game's <c>/time set</c> does.
    /// The clock only moves forward, as <c>/time set</c>'s does.</summary>
    public static double HoursToDay(double hourOfDay, double hoursPerDay)
    {
        double scale = hoursPerDay / 24;
        if (hourOfDay >= DayFrom * scale && hourOfDay < DayUntil * scale)
            return 0;
        double noon = Noon * scale;
        return noon >= hourOfDay ? noon - hourOfDay : noon + hoursPerDay - hourOfDay;
    }

    /// <summary>A dry spell found ahead: it starts <see cref="StartDays"/> from now and lasts
    /// <see cref="LengthDays"/> (the search stops counting at <see cref="WantedDryDays"/>).</summary>
    public readonly record struct DrySpell(double StartDays, double LengthDays);

    /// <summary>The first dry spell of at least <see cref="WantedDryDays"/> within
    /// <see cref="SearchDays"/>, or failing that the longest shorter one; null if it rains
    /// throughout. <paramref name="dryAt"/> says whether it is dry everywhere that counts at a
    /// number of days from now; it is sampled every <paramref name="stepDays"/> (an hour).</summary>
    public static DrySpell? FindDrySpell(Func<double, bool> dryAt, double stepDays)
    {
        if (stepDays <= 0)
            throw new ArgumentOutOfRangeException(nameof(stepDays));
        int steps = (int)Math.Ceiling(SearchDays / stepDays), wanted = (int)Math.Ceiling(WantedDryDays / stepDays);
        DrySpell? best = null;
        int start = -1;
        for (int i = 0; i <= steps; i++)
        {
            bool dry = i < steps && dryAt(i * stepDays);
            if (dry && start < 0)
                start = i;
            int length = start < 0 ? 0 : i - start + (dry ? 1 : 0);
            if (start >= 0 && (length >= wanted || !dry))
            {
                if (length >= wanted)
                    return new DrySpell(start * stepDays, length * stepDays);
                if (best == null || length * stepDays > best.Value.LengthDays)
                    best = new DrySpell(start * stepDays, length * stepDays);
                start = -1;
            }
        }
        return best;
    }
}

/// <summary>
/// One wrap of a delegate kept in a settable slot (a calendar's <c>OnGetSolarSphericalCoords</c>):
/// wraps whatever is installed, never its own wrapper again, and on release puts the wrapped
/// delegate back if the wrapper is still the one installed (and leaves the slot alone if something
/// replaced it since). Only the newest wrapper <see cref="IsLive"/>: an older one that something
/// else still calls (it wrapped ours, or was replaced and wrapped again) passes calls through.
/// </summary>
public sealed class DelegateWrap<T> where T : class
{
    private T? _wrapper, _inner;

    /// <summary>Whether a wrap is in force.</summary>
    public bool Held => _wrapper != null;

    /// <summary>Whether <paramref name="wrapper"/> is the wrapper in force.</summary>
    public bool IsLive(T wrapper) => ReferenceEquals(wrapper, _wrapper);

    /// <summary>Wraps <paramref name="current"/> with <paramref name="make"/>, unless it is our
    /// wrapper already or nothing is installed. Returns the delegate to install, or null for no
    /// change.</summary>
    public T? Wrap(T? current, Func<T, T> make)
    {
        if (current == null || ReferenceEquals(current, _wrapper))
            return null;
        _inner = current;
        _wrapper = make(current);
        return _wrapper;
    }

    /// <summary>Ends the wrap. Returns the delegate to install in place of
    /// <paramref name="current"/>, or null to leave the slot as it is.</summary>
    public T? Unwrap(T? current)
    {
        var (wrapper, inner) = (_wrapper, _inner);
        _wrapper = _inner = null;
        return wrapper != null && ReferenceEquals(current, wrapper) ? inner : null;
    }
}

/// <summary>
/// What <c>/clear stay</c> changed, as it was before, so <c>/clear stop</c> puts it back. Kept in
/// the savegame, so the lock outlasts a restart.
/// </summary>
public sealed class ClearLock
{
    /// <summary>Left by the version that stopped time: whether the calendar had a <c>baseline</c>
    /// time speed modifier (the one <c>/time speed</c> and <c>/time stop</c> set) before the lock,
    /// and its value. Null in a lock written since; an old lock's <c>baseline</c> is put back once
    /// on load and these dropped (<see cref="StopsTime"/>).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? HadBaseline { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Baseline { get; set; }

    /// <summary>Whether this lock is from the version that stopped time, with the calendar's
    /// <c>baseline</c> still holding the speed modifiers' sum at 0.</summary>
    [JsonIgnore]
    public bool StopsTime => HadBaseline != null;

    /// <summary>The weather system's precipitation override (<c>/weather setprecip</c>), null for
    /// none.</summary>
    public float? OverridePrecipitation { get; set; }

    /// <summary>Whether weather patterns changed by themselves (<c>/weather acp</c>).</summary>
    public bool AutoChangePatterns { get; set; } = true;

    /// <summary>Days from the lock to the next temporal storm, kept that far ahead while locked;
    /// null when storms were not running.</summary>
    public double? StormDaysAhead { get; set; }

    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this);

    public static ClearLock? FromBytes(byte[]? data) =>
        data == null || data.Length == 0 ? null : JsonSerializer.Deserialize<ClearLock>(data);
}
