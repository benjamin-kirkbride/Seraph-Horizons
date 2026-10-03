namespace BuckingSawmill.Core;

/// <summary>What the mill is doing: cutting a trunk, winding its saws back up after one, or
/// waiting at the top for the next.</summary>
public enum MillPhase { Idle, Cutting, Raising }

/// <summary>How far the saws travel, rig.json's optional <c>saw</c>: the height of the blades'
/// cutting edge when latched at the top and at the end of a cut, in native-frame blocks.</summary>
public sealed record SawTravel(float TopY, float BottomY)
{
    public static readonly SawTravel Default = new(3.0f, 0.5f);
}

/// <summary>
/// The saws' depth, 0 (latched at the top) to 1 (at the bed, through the trunk). A loaded trunk
/// drops them at once to where they touch it; the cut sinks them the rest of the way with its
/// progress; then the windlass winds them back up with the shaft's turning.
/// </summary>
public static class SawDepth
{
    /// <summary>The rate, per second, at which the client's shown depth closes the gap to its
    /// estimate when a sync moves it (a new trunk's drop takes about a quarter second).</summary>
    public const float EaseRate = 12f;

    /// <summary>A trunk's thickness in blocks from its Logging Expanded <c>size</c> variant:
    /// <c>xl</c> and <c>xxl</c> are two blocks high, the rest one.</summary>
    public static int TrunkThickness(string? size) => size is "xl" or "xxl" ? 2 : 1;

    /// <summary>The depth at which the saws touch the top of a trunk of <paramref name="size"/>
    /// lying on <paramref name="bed"/>; without a bed, the trunk lies at the saws' bottom.</summary>
    public static float Touch(SawTravel saw, TrunkBed? bed, string? size)
    {
        float trunkTop = (bed?.Origin.Y ?? saw.BottomY) + TrunkThickness(size);
        float travel = saw.TopY - saw.BottomY;
        return travel <= 0 ? 0 : Math.Clamp((saw.TopY - trunkTop) / travel, 0, 1);
    }

    /// <summary>The depth while cutting: from <paramref name="touch"/> at progress 0 to 1 when
    /// the trunk is through.</summary>
    public static float Cutting(float touch, float progress) =>
        touch + (1 - touch) * Math.Clamp(progress, 0, 1);

    /// <summary>The depth after <paramref name="radians"/> of shaft rotation wind the saws up:
    /// all the way takes <paramref name="raiseRevolutions"/> turns.</summary>
    public static float Raise(float depth, float radians, float raiseRevolutions)
    {
        if (radians <= 0)
            return depth;
        if (raiseRevolutions <= 0)
            return 0;
        return Math.Max(0, depth - (float)(radians / (2 * Math.PI * raiseRevolutions)));
    }

    public static MillPhase Phase(bool hasTrunk, float depth) =>
        hasTrunk ? MillPhase.Cutting : depth > 0 ? MillPhase.Raising : MillPhase.Idle;

    /// <summary>Moves a shown depth toward its target by <see cref="EaseRate"/> over
    /// <paramref name="dt"/> seconds, landing on it once within a hair.</summary>
    public static float Ease(float shown, float target, float dt)
    {
        float next = shown + (target - shown) * (1 - MathF.Exp(-EaseRate * Math.Max(dt, 0)));
        return Math.Abs(target - next) < 0.002f ? target : next;
    }
}
