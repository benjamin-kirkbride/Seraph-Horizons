namespace SeraphHorizons.Mod.Rocker.Core;

/// <summary>
/// The renderer's rocking clock θ (the rig's input: one turn a rock). While the rocker is worked θ
/// turns at the rocks a second; when the hold ends it runs on at the same pace to the next multiple of
/// π, where the cradle is level, and stops there (the model's README, "For the gameplay").
/// </summary>
public sealed class RockerClock
{
    public double Theta { get; private set; }

    /// <summary>Whether the cradle is moving (worked, or coming to rest).</summary>
    public bool Moving { get; private set; }

    public void Advance(double dt, bool held, double rocksPerSecond)
    {
        double step = 2 * Math.PI * Math.Max(0, rocksPerSecond) * Math.Max(0, dt);
        if (held)
        {
            Theta += step;
            Moving = step > 0;
        }
        else
        {
            double rest = Math.Ceiling(Theta / Math.PI - 1e-9) * Math.PI;
            Theta = Math.Min(rest, Theta + step);
            Moving = Theta < rest;
        }
        if (Theta > 1e6)
            Theta %= 2 * Math.PI;
    }
}
