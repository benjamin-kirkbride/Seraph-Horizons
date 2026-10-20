namespace SeraphHorizons.Mod.Springs.Core;

/// <summary>
/// A pumped spring (README "Springs", "Pumped"): a ppex mechanical pump drawing on a tapped
/// spring's run makes it give more, its rate times (1 + <see cref="SpringsConfig.PumpBoostPerTurn"/>
/// × the pump's shaft turns a second), the extra into the pumped run only, from the fastest pump
/// alone when several draw on one spring. What the pump moves on is still its own rate at most.
/// </summary>
public static class SpringPumps
{
    /// <summary>Radians a network turns per unit of speed per second (speed 1 is 5 rad/s, as
    /// <c>MachineRun.RadiansPerSpeedSecond</c>).</summary>
    public const float RadiansPerSpeedSecond = 5f;

    /// <summary>Milliseconds a pump's or a spring's last word stands: a pump works and a spring feeds
    /// once a second, so two missed seconds mean it has stopped.</summary>
    public const long FreshMs = 2500;

    /// <summary>Shaft turns a second at axle speed <paramref name="speed"/> (0 for none or not a
    /// number).</summary>
    public static float TurnsPerSecond(float speed) =>
        float.IsFinite(speed) && speed > 0 ? speed * RadiansPerSpeedSecond / (2 * MathF.PI) : 0;

    /// <summary>The litres a second a spring of <paramref name="litresPerSecond"/> gives the run of a
    /// pump turning <paramref name="turnsPerSecond"/>, on top of its own rate.</summary>
    public static float Extra(float litresPerSecond, float boostPerTurn, float turnsPerSecond)
    {
        if (!(litresPerSecond > 0) || !(boostPerTurn > 0) || !(turnsPerSecond > 0)
            || !float.IsFinite(litresPerSecond) || !float.IsFinite(boostPerTurn) || !float.IsFinite(turnsPerSecond))
            return 0;
        return litresPerSecond * boostPerTurn * turnsPerSecond;
    }

    /// <summary>Of the spring's runs, the one whose pump turns fastest, and that pump's axle speed;
    /// (-1, 0) when no pump draws on any. <paramref name="speeds"/>: per run, its fastest pump's axle
    /// speed, or 0.</summary>
    public static (int Run, float Speed) Fastest(IReadOnlyList<float> speeds)
    {
        int best = -1;
        float top = 0;
        for (int i = 0; i < speeds.Count; i++)
            if (float.IsFinite(speeds[i]) && speeds[i] > top)
                (best, top) = (i, speeds[i]);
        return (best, top);
    }
}
