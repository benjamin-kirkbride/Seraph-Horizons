namespace SeraphHorizons.Mod.Handcar.Core;

/// <summary>
/// The handcar's figures: HandcarSettings in ModConfig/seraphhorizons.json. Speeds are blocks a
/// second, accelerations blocks a second squared. The reference is Yang's primitive engine cart at
/// full steam (800 °C, 1 N per 100 °C less its own weight of 1): 7 blocks a second, accelerating at
/// 0.8. The defaults put one rider pumping at 60 % of that and two at 85 %.
/// </summary>
public class HandcarConfig
{
    /// <summary>Top speed with one rider pumping, on level track with nothing loaded.</summary>
    public float TopSpeedOne { get; set; } = 4.2f;

    /// <summary>Top speed with both riders pumping the same way.</summary>
    public float TopSpeedTwo { get; set; } = 5.95f;

    /// <summary>How fast one rider pumping gets the car up to speed.</summary>
    public float AccelerationOne { get; set; } = 0.6f;

    /// <summary>How fast two riders pumping get it up to speed.</summary>
    public float AccelerationTwo { get; set; } = 0.9f;

    /// <summary>Top speed lost per unit of weight above the bare car's (a loaded chest weighs 1, as
    /// on Yang's carts; a car coupled behind weighs its own), as a steam engine loses a block a
    /// second per unit. Never below <see cref="MinTopSpeed"/>.</summary>
    public float SpeedLossPerWeight { get; set; } = 0.5f;

    /// <summary>The slowest the riders can still push the car, however loaded.</summary>
    public float MinTopSpeed { get; set; } = 0.8f;

    /// <summary>Deceleration while nobody pumps: Yang's rolling resistance (<c>Roll0</c>), plus
    /// <see cref="DragPerWeight"/> per unit of weight above one.</summary>
    public float CoastDrag { get; set; } = 0.35f;

    /// <summary>Yang's rolling resistance per unit of weight above one (<c>RollW</c>).</summary>
    public float DragPerWeight { get; set; } = 0.05f;

    /// <summary>Deceleration while a rider pumps against the way the car rolls, on top of the drag.</summary>
    public float BrakeDeceleration { get; set; } = 2.5f;

    /// <summary>Satiety a rider spends per second of pumping, on the game's scale for sprinting (the
    /// hunger behaviour adds 1.5 per second of sprinting, times the calendar's speed): pumping is
    /// as hungry work as running.</summary>
    public float SatietyPerPumpSecond { get; set; } = 1.5f;

    /// <summary>Seconds the riders' pumping effort takes to come in or go out, eased.</summary>
    public float PumpFadeSeconds { get; set; } = 0.35f;

    public static readonly HandcarConfig Defaults = new();

    /// <summary>Replaces values out of range with the default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        void Check(string name, float value, float lo, float hi, Action reset, float fallback)
        {
            if (!float.IsFinite(value) || value < lo || value > hi)
            {
                fixes.Add($"{name} {value} is out of range, using {fallback}");
                reset();
            }
        }
        Check(nameof(TopSpeedOne), TopSpeedOne, 0.1f, 32f, () => TopSpeedOne = Defaults.TopSpeedOne, Defaults.TopSpeedOne);
        Check(nameof(TopSpeedTwo), TopSpeedTwo, 0.1f, 32f, () => TopSpeedTwo = Defaults.TopSpeedTwo, Defaults.TopSpeedTwo);
        Check(nameof(AccelerationOne), AccelerationOne, 0.01f, 20f, () => AccelerationOne = Defaults.AccelerationOne, Defaults.AccelerationOne);
        Check(nameof(AccelerationTwo), AccelerationTwo, 0.01f, 20f, () => AccelerationTwo = Defaults.AccelerationTwo, Defaults.AccelerationTwo);
        Check(nameof(SpeedLossPerWeight), SpeedLossPerWeight, 0f, 10f, () => SpeedLossPerWeight = Defaults.SpeedLossPerWeight, Defaults.SpeedLossPerWeight);
        Check(nameof(MinTopSpeed), MinTopSpeed, 0f, 32f, () => MinTopSpeed = Defaults.MinTopSpeed, Defaults.MinTopSpeed);
        Check(nameof(CoastDrag), CoastDrag, 0f, 20f, () => CoastDrag = Defaults.CoastDrag, Defaults.CoastDrag);
        Check(nameof(DragPerWeight), DragPerWeight, 0f, 5f, () => DragPerWeight = Defaults.DragPerWeight, Defaults.DragPerWeight);
        Check(nameof(BrakeDeceleration), BrakeDeceleration, 0.05f, 50f, () => BrakeDeceleration = Defaults.BrakeDeceleration, Defaults.BrakeDeceleration);
        Check(nameof(SatietyPerPumpSecond), SatietyPerPumpSecond, 0f, 100f, () => SatietyPerPumpSecond = Defaults.SatietyPerPumpSecond, Defaults.SatietyPerPumpSecond);
        Check(nameof(PumpFadeSeconds), PumpFadeSeconds, 0.01f, 5f, () => PumpFadeSeconds = Defaults.PumpFadeSeconds, Defaults.PumpFadeSeconds);
        if (TopSpeedTwo < TopSpeedOne)
        {
            fixes.Add($"{nameof(TopSpeedTwo)} {TopSpeedTwo} is below {nameof(TopSpeedOne)} {TopSpeedOne}, using {TopSpeedOne}");
            TopSpeedTwo = TopSpeedOne;
        }
        if (MinTopSpeed > TopSpeedOne)
        {
            fixes.Add($"{nameof(MinTopSpeed)} {MinTopSpeed} is above {nameof(TopSpeedOne)} {TopSpeedOne}, using {TopSpeedOne}");
            MinTopSpeed = TopSpeedOne;
        }
        return fixes;
    }
}
