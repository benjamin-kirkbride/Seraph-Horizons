using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.SlittingMill.Core;

/// <summary>What Ctrl + right-click takes back, in order: the knife, then the plate on the table; the stages
/// only come back by breaking the frame.</summary>
public enum SlittingMillTakeBack { Nothing, Knife, Plate }

/// <summary>The slitting mill's <c>requires</c> vocabulary: its five stages (config/builds/slittingmill.json,
/// <see cref="Stages"/>), the knife (the wearing part, fitted with the shear) and the work's (<c>plate</c>: a
/// plate on the table, its rods, nails and strips).</summary>
public static class SlittingMillRequires
{
    public const string Knife = "knife";
    public const string Plate = "plate";

    /// <summary>The stages in their only order: the slitter rolls, the pinions and great wheel, the drive, the
    /// shear and the push bar, each its own <c>requires</c> (the shear's also adds the knife's).</summary>
    public static readonly IReadOnlyList<string> Stages = ["rolls", "pinions", "drive", "shear", "pusher"];

    /// <summary>Every <c>requires</c> value the rig may use.</summary>
    public static readonly IReadOnlySet<string> KnownRequires = Stages.Concat([Knife, Plate]).ToHashSet();
}

/// <summary>
/// The slitting mill's build as the shared staged build holds it (config/builds/slittingmill.json,
/// <see cref="MachineAssembly"/>): five stages after the frame in one order, their materials the game's (and
/// the pack's own slitter cutters, forged on the anvil), and the knife, the game's cleaver
/// (<see cref="KnifeCodes"/>), its durable wearing part, fitted as the shear stage's last material. What the
/// machine adds to the shared rules: what Ctrl + right-click takes back.
/// </summary>
public static class SlittingMillBuild
{
    /// <summary>The knife's wearing part id.</summary>
    public const string KnifePart = "knife";

    /// <summary>The cleavers the knife may be, steel first (the creative shortcut's): a heavy square blade,
    /// the game's nearest to a guillotine's, its own durability its uses.</summary>
    public static readonly IReadOnlyList<string> KnifeCodes = ["game:cleaver-steel", "game:cleaver-meteoriciron", "game:cleaver-iron"];

    public const string CutterCode = "seraphhorizons:slittercutter-steel";

    /// <summary>Whether the knife is in with durability left.</summary>
    public static bool KnifeIn(MachineAssembly assembly) => assembly.Wearing.Fitted(KnifePart) is { Left: > 0 };

    /// <summary>What Ctrl + right-click takes now: the knife, else the plate on the table
    /// (<paramref name="plateOn"/>).</summary>
    public static SlittingMillTakeBack TakeBack(MachineAssembly assembly, bool plateOn) =>
        KnifeIn(assembly) ? SlittingMillTakeBack.Knife
        : plateOn ? SlittingMillTakeBack.Plate
        : SlittingMillTakeBack.Nothing;
}
