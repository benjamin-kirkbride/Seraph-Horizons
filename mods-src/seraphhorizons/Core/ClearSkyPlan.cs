using System.Text.Json;

namespace SeraphHorizons.Mod.Core;

/// <summary>
/// The <c>/clear</c> command (<c>ClearSky</c>): how far to move the clock to reach daytime, and how
/// far to move the rain forward to reach a dry spell. Game-independent, so tests/ runs it without
/// the game.
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
/// What <c>/clear stay</c> changed, as it was before, so <c>/clear stop</c> puts it back. Kept in
/// the savegame, so the lock outlasts a restart.
/// </summary>
public sealed class ClearLock
{
    /// <summary>Whether the calendar had a <c>baseline</c> time speed modifier (the one
    /// <c>/time speed</c> and <c>/time stop</c> set), and its value.</summary>
    public bool HadBaseline { get; set; }
    public float Baseline { get; set; }

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
