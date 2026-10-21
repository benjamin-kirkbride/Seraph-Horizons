using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.GearCutter.Core;

/// <summary>What Ctrl + right-click takes back, in order: the cutter kit, then the blank on the
/// arbor, then the master; the other stages only come back by breaking the frame.</summary>
public enum GearCutterTakeBack { Nothing, Kit, Blank, Master }

/// <summary>The gear cutter's <c>requires</c> vocabulary: its stages' (config/builds/gearcutter.json,
/// <see cref="Stages"/>), the large master's, the blanks' (the work's) and the cover's (always drawn).</summary>
public static class GearCutterRequires
{
    public const string MasterSmall = "master";
    public const string MasterLarge = "masterlarge";
    public const string BlankSmall = "blanksmall";
    public const string BlankLarge = "blanklarge";
    public const string Cover = "cover";

    /// <summary>The build's stages in their only order (#480's decided build order), each its own
    /// <c>requires</c>; the master stage draws <see cref="MasterSmall"/> or <see cref="MasterLarge"/> by the
    /// temporal gear fitted.</summary>
    public static readonly IReadOnlyList<string> Stages =
        ["spindle", "feedscrew", "camfeed", "camindex", "liftcam", "index", "oiler", "head", "cutter", MasterSmall];

    /// <summary>Every <c>requires</c> value the rig may use.</summary>
    public static readonly IReadOnlySet<string> KnownRequires =
        Stages.Concat([MasterLarge, BlankSmall, BlankLarge, Cover]).ToHashSet();
}

/// <summary>
/// The gear cutter's build (#480) as the shared staged build holds it (config/builds/gearcutter.json,
/// <see cref="MachineAssembly"/>): ten stages after the frame in one order, the spindle and the index fed the
/// bill their own items once had, the cutter kit (<see cref="KitPart"/>) its durable wearing part fitted as the
/// cutter stage, and the master its last stage, either temporal gear, which decides the gear size. What the
/// machine adds to the shared rules: the master's class, and what Ctrl + right-click takes back.
/// </summary>
public static class GearCutterBuild
{
    public const string FeedScrewCode = "seraphhorizons:gearcutterfeedscrew";
    public const string GearboxCode = "game:jonasframes-gearbox02";
    public const string LiftCamCode = "seraphhorizons:gearcutterliftcam";
    public const string ValveCode = "game:jonasparts-valve01";
    public const string HeadCode = "game:jonasframes-gears02";
    public const string HeadAltCode = "game:jonasframes-gears01";
    public const string KitCode = "seraphhorizons:gearcutterkit-steel";
    public const string MasterCode = "game:gear-temporal";
    public const string LargeMasterCode = "game:largegear-temporal";

    /// <summary>The spindle's and the index's bill: steel rods, steel plates and one lot of the game's metal
    /// parts (a block).</summary>
    public const string RodCode = "game:rod-steel";
    public const string PlateCode = "game:metalplate-steel";
    public const string MetalPartsCode = "game:metal-parts";

    /// <summary>The cutter kit's wearing part id.</summary>
    public const string KitPart = "kit";

    /// <summary>The master's stage id: the build's last.</summary>
    public const string MasterStage = "master";

    /// <summary>The class a master cuts: 1 the temporal gear (12 teeth), 2 the large one (20); 0 else.</summary>
    public static int MasterClass(string? code) => CodePattern.Normalise(code) switch
    {
        MasterCode => 1,
        LargeMasterCode => 2,
        _ => 0,
    };

    /// <summary>The master stage's index in <paramref name="build"/>, or -1.</summary>
    public static int MasterIndex(StagedBuild build)
    {
        for (int s = 0; s < build.Stages.Count; s++)
            if (build.Stages[s].Id == MasterStage)
                return s;
        return -1;
    }

    /// <summary>The master fitted: its class (1 small, 2 large, 0 none).</summary>
    public static int Master(MachineAssembly assembly) =>
        MasterIndex(assembly.Build) is var s and >= 0 && assembly.Build.StageComplete(s) ? MasterClass(assembly.Build.CodeIn(s, 0)) : 0;

    /// <summary>Whether the cutter kit is in with durability left.</summary>
    public static bool KitIn(MachineAssembly assembly) => assembly.Wearing.Fitted(KitPart) is { Left: > 0 };

    /// <summary>What Ctrl + right-click takes now: the kit, else the blank on the arbor
    /// (<paramref name="blankOn"/>), else the master.</summary>
    public static GearCutterTakeBack TakeBack(MachineAssembly assembly, bool blankOn) =>
        KitIn(assembly) ? GearCutterTakeBack.Kit
        : blankOn ? GearCutterTakeBack.Blank
        : Master(assembly) > 0 ? GearCutterTakeBack.Master
        : GearCutterTakeBack.Nothing;

    /// <summary>Takes the master back off (only once the kit is out): its code, or null. The stages before it stay.</summary>
    public static string? TakeMaster(MachineAssembly assembly)
    {
        if (KitIn(assembly) || Master(assembly) == 0)
            return null;
        return assembly.Build.TakeStage(MasterIndex(assembly.Build)) is [var drop, ..] ? drop.Code : null;
    }
}

/// <summary>
/// The renderer's own W, master presence and class, from the cutter each frame (<c>BEGearCutter.FrameInput</c>;
/// the model's contract, README "Rig"): W advances with the shaft at 1 / TurnsPerTooth a turn while the cut runs,
/// never behind the server's and at most <see cref="Snap"/> ahead of it, and stands at the master's teeth with no
/// blank on; k is the master's class, held while its presence p eases out once it comes off.
/// </summary>
public sealed class GearCutterClock
{
    /// <summary>How far the shown W may stray ahead of the server's, teeth.</summary>
    public const double Snap = 0.3;

    /// <summary>Presence per second, in and out (0.4 s each way).</summary>
    public const float EaseRate = 2.5f;

    public double Work { get; private set; }
    public float Presence { get; private set; }
    public int Class { get; private set; }

    /// <summary>Steps the clock by <paramref name="dt"/> seconds, the shaft having turned
    /// <paramref name="radians"/> (unsigned) since the last step.</summary>
    public void Advance(float dt, double radians, int master, bool blankOn, double serverWork, int end, bool running, double turnsPerTooth)
    {
        Presence = Math.Clamp(Presence + (master > 0 ? dt : -dt) * EaseRate, 0, 1);
        if (master > 0)
            Class = master;
        else if (Presence <= 0)
            Class = 0;
        if (!blankOn)
        {
            Work = GearCut.Teeth(Class);
            return;
        }
        if (running)
            Work += GearCut.TeethFor(radians, turnsPerTooth);
        // the server's W is the truth: never more than a little ahead of it, never behind it
        if (Work < serverWork || Work > serverWork + Snap)
            Work = serverWork;
        Work = Math.Min(Work, end);
    }

    /// <summary>The rig's inputs at this moment: θ and ψ the shaft's, the clock's W, k and p, and the oil's fill.</summary>
    public RigInput Input(double theta, double psi, double oil) =>
        new(theta, Travel: psi, Work: Work, Class: Class, Presence: Presence, Oil: oil);
}
