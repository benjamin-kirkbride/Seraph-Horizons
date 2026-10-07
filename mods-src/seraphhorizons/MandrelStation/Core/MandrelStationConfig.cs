namespace SeraphHorizons.Mod.MandrelStation.Core;

/// <summary>
/// The mandrel forging station's figures: MandrelStationSettings in ModConfig/seraphhorizons.json.
/// A hand station: no oil and no power. The blows a hollow takes are the model's pace (the rig's
/// <c>forge.blowsPerHollow</c>; a test holds the two together), struck with the base hammer; a
/// hammer of a higher tool tier forges faster in proportion to its tier. The hammer pays for each blow.
/// </summary>
public class MandrelStationConfig
{
    /// <summary>Hammer blows (right-clicks with a hammer of <see cref="BaseHammerTier"/>) a lead hollow
    /// takes: the rig's <c>forge.blowsPerHollow.thin</c>.</summary>
    public int BlowsPerHollowLead { get; set; } = 6;

    /// <summary>Hammer blows a copper hollow takes, half as many again as lead's (harder metal): the
    /// rig's <c>forge.blowsPerHollow.thick</c>.</summary>
    public int BlowsPerHollowCopper { get; set; } = 9;

    /// <summary>The tool tier the blows above are counted for: the game's copper hammer's (2). A blow of
    /// a hammer of tier t forges t / BaseHammerTier of a base blow (at most half a hollow); a hammer
    /// with no tier counts as this one.</summary>
    public int BaseHammerTier { get; set; } = 2;

    /// <summary>Durability the hammer loses a blow, as a blow on the anvil costs it one.</summary>
    public int HammerWearPerBlow { get; set; } = 1;

    public static readonly MandrelStationConfig Defaults = new();

    /// <summary>Blows a hollow of class <paramref name="k"/> takes (1 lead, 2 copper); 0 else.</summary>
    public int BlowsPerHollow(int k) => k switch { 1 => BlowsPerHollowLead, 2 => BlowsPerHollowCopper, _ => 0 };

    /// <summary>Replaces values out of range with the default; returns a line per replaced value.</summary>
    public IReadOnlyList<string> Sanitise()
    {
        var fixes = new List<string>();
        if (BlowsPerHollowLead is < 1 or > 1000)
            Fix(nameof(BlowsPerHollowLead), BlowsPerHollowLead, BlowsPerHollowLead = Defaults.BlowsPerHollowLead, fixes);
        if (BlowsPerHollowCopper is < 1 or > 1000)
            Fix(nameof(BlowsPerHollowCopper), BlowsPerHollowCopper, BlowsPerHollowCopper = Defaults.BlowsPerHollowCopper, fixes);
        if (BaseHammerTier is < 1 or > 100)
            Fix(nameof(BaseHammerTier), BaseHammerTier, BaseHammerTier = Defaults.BaseHammerTier, fixes);
        if (HammerWearPerBlow is < 0 or > 1000)
            Fix(nameof(HammerWearPerBlow), HammerWearPerBlow, HammerWearPerBlow = Defaults.HammerWearPerBlow, fixes);
        return fixes;
    }

    private static void Fix(string name, object value, object fallback, List<string> fixes) =>
        fixes.Add($"{name} {value} is out of range, using {fallback}");
}
