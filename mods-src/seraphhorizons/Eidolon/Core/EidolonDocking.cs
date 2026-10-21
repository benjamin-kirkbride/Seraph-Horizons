namespace SeraphHorizons.Mod.Eidolon.Core;

/// <summary>Where a docking eidolon is: walking in (its dock order), backing onto the spine (<c>dock</c>
/// playing, before the clamps close), or held hung on it.</summary>
public enum DockPhase
{
    None,

    /// <summary><c>dock</c> is playing; the clamps close at <see cref="EidolonDocking.ClampSeconds"/>.</summary>
    Docking,

    /// <summary>Clamped: the gantry holds it, hung on the spine.</summary>
    Docked,
}

/// <summary>Why a strip is refused, or that it goes ahead.</summary>
public enum StripVerdict
{
    /// <summary>The outermost layer comes off.</summary>
    Strips,

    /// <summary>Not hung in a gantry: layers come off only there.</summary>
    NotDocked,

    /// <summary>Below full health: it is repaired first.</summary>
    Damaged,

    /// <summary>No layer is on: only the torso and the mind are left, which never come off here.</summary>
    Bare,
}

/// <summary>Why a turn of the crank does not wake a docked eidolon, or that it does.</summary>
public enum DockWakeVerdict
{
    Wakes,

    /// <summary>A layer is off (or only part fitted): all five must be on.</summary>
    LayersMissing,
}

/// <summary>
/// Docking an eidolon on a gantry's spine, to strip and refit its layers (Eidolon/README.md, "Docking"):
/// the timing of its <c>dock</c> animation and the rules for stripping and waking. The game side is
/// <c>EntityBehaviorEidolonDock</c> (the entity) and <c>GantryBody</c> (the gantry).
/// </summary>
public static class EidolonDocking
{
    /// <summary>The <c>dock</c> animation's length and its clamp frame (Eidolon/tools/make_shape.py's
    /// <c>DOCK_FRAMES</c> and <c>EVENTS["dock"]["clamp"]</c>), at 30 frames a second.</summary>
    public const int DockFrames = 60;

    public const int ClampFrame = 46;

    public const double FramesPerSecond = 30;

    /// <summary>From the start of <c>dock</c> to the clamps closing: the gantry takes hold then.</summary>
    public static double ClampSeconds => ClampFrame / FramesPerSecond;

    /// <summary>The whole of <c>dock</c>: after it, <c>hung</c> holds the pose.</summary>
    public static double DockSeconds => DockFrames / FramesPerSecond;

    /// <summary>How close to the gantry's body point a docking eidolon must have walked before it backs
    /// onto the spine (it is then set on the point exactly), blocks.</summary>
    public const double ArriveBlocks = 0.5;

    /// <summary>How far round an eidolon a dock order with no gantry marked looks for one, blocks.</summary>
    public const double SearchBlocks = 24;

    /// <summary>Whether a strip may go ahead: docked, at full health, with a layer on.</summary>
    public static StripVerdict CanStrip(EidolonBuild build, bool docked, double health, double maxHealth)
    {
        if (!docked)
            return StripVerdict.NotDocked;
        if (build.NextToStrip == null)
            return StripVerdict.Bare;
        return build.CanStrip(health, maxHealth) ? StripVerdict.Strips : StripVerdict.Damaged;
    }

    /// <summary>Whether a turn of the crank wakes a docked eidolon wearing <paramref name="build"/>: all
    /// five layers on (a layer part fitted is not on).</summary>
    public static DockWakeVerdict CanWake(EidolonBuild build) =>
        build.Complete ? DockWakeVerdict.Wakes : DockWakeVerdict.LayersMissing;

    /// <summary>The order a docking eidolon is given back when the crank wakes it: what it was doing
    /// before it docked, unless that was walking somewhere or docking itself.</summary>
    public static bool Resumes(string? orderCode) => orderCode is not (null or "goto" or "dock");
}
