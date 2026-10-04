namespace SeraphHorizons.Mod.Core;

/// <summary>What a player holds in the main hand, as the splitting block's upgrades tell it apart.</summary>
public enum SplittingBlockHeld
{
    Other,
    /// <summary>Any axe (a maul is one too, as Immersive Woodworking counts tools).</summary>
    Axe,
    /// <summary>Immersive Woodworking's bark spud, which has no tool type of its own.</summary>
    BarkSpud,
    IronHoops,
    IronNails,
}

/// <summary>The three upgrades, in order: each makes the next tier of the one before it.</summary>
public enum SplittingBlockStep
{
    /// <summary>Primitive to debarked: Immersive Woodworking's debarking, with its bark.</summary>
    Debark,
    /// <summary>Debarked to bound: two iron hoops, as Logging Expanded.</summary>
    Bind,
    /// <summary>Bound to advanced: eight iron nails, hammered in, as Logging Expanded.</summary>
    Nail,
}

/// <summary>One upgrade a player can make now.</summary>
/// <param name="Step">Which upgrade.</param>
/// <param name="From">The tier it upgrades; it makes the next one.</param>
/// <param name="Consumes">How many of the held items it takes.</param>
/// <param name="HoldSeconds">How long the player holds the mouse button; 0 is instant.</param>
/// <param name="MainWear">Durability the held tool loses.</param>
/// <param name="HammerWear">Durability the hammer in the offhand loses.</param>
public sealed record SplittingBlockUpgrade(SplittingBlockStep Step, SplittingBlockTier From, int Consumes, float HoldSeconds,
    int MainWear, int HammerWear)
{
    public SplittingBlockTier To => From + 1;

    public bool IsHold => HoldSeconds > 0;
}

/// <summary>
/// The splitting block's rules that need no game types (<c>UnifiedWoodworking</c>): which upgrade
/// what the player holds makes at which tier and what it costs, and the yields. The game side
/// (<c>Woodworking/SplittingBlock*.cs</c>) reads the player's hands and Immersive Woodworking's
/// settings, and asks here.
///
/// An upgrade is made on an empty block only (no log, half-log or firewood on it, no axe stuck
/// in it), so it never meets Immersive Woodworking's own interactions there, and never with
/// Shift or Ctrl held, which Immersive Woodworking uses to stick an axe in and to take things off.
/// None of the items an upgrade takes can be laid on the block or chopped, so on an empty block
/// they did nothing before.
/// </summary>
public static class SplittingBlockRules
{
    public const string HoopCode = "game:hoop-iron";
    public const string NailCode = "game:metalnailsandstrips-iron";

    /// <summary>The first part of the bark spud's code (<c>immersivewoodworking:barkspud-&lt;metal&gt;</c>),
    /// in any domain, as Immersive Woodworking tells it.</summary>
    public const string BarkSpudCodePart = "barkspud";

    /// <summary>Logging Expanded's costs: 2 hoops to bind; 8 nails, held 3 s with a hammer in the
    /// offhand that loses 1 durability, to finish.</summary>
    public const int HoopsToBind = 2;
    public const int NailsToFinish = 8;
    public const float NailSeconds = 3f;
    public const int NailHammerWear = 1;

    /// <summary>A hold counts as done this much before its full time, as Logging Expanded's
    /// (2.9 s of 3): the last tick before the release may fall just short.</summary>
    public const float HoldTolerance = 0.1f;

    /// <summary>Immersive Woodworking's chopping block: <c>immersivewoodworking:choppingblock</c>,
    /// and its old per-wood blocks (<c>choppingblock-&lt;wood&gt;</c>), which turn into it.</summary>
    public const string BlockDomain = "immersivewoodworking";
    public const string BlockCode = "choppingblock";

    /// <summary>What a held item is, from its code (<c>domain:path</c>) and whether its tool type is
    /// an axe.</summary>
    public static SplittingBlockHeld Classify(string? code, bool isAxe)
    {
        if (isAxe)
            return SplittingBlockHeld.Axe;
        switch (code)
        {
            case null:
                return SplittingBlockHeld.Other;
            case HoopCode:
                return SplittingBlockHeld.IronHoops;
            case NailCode:
                return SplittingBlockHeld.IronNails;
        }
        string path = code[(code.IndexOf(':') + 1)..];
        int dash = path.IndexOf('-');
        return (dash < 0 ? path : path[..dash]) == BarkSpudCodePart ? SplittingBlockHeld.BarkSpud : SplittingBlockHeld.Other;
    }

    /// <summary>The upgrade a block of <paramref name="tier"/> would get next, whatever is held:
    /// what its interaction help offers. Null for the advanced tier.</summary>
    public static SplittingBlockStep? NextStep(SplittingBlockTier tier) => tier switch
    {
        SplittingBlockTier.Primitive => SplittingBlockStep.Debark,
        SplittingBlockTier.Debarked => SplittingBlockStep.Bind,
        SplittingBlockTier.Bound => SplittingBlockStep.Nail,
        _ => null,
    };

    /// <summary>The upgrade an empty block of <paramref name="tier"/> gets from what the player
    /// holds, or null for none (the block then does what Immersive Woodworking has it do).</summary>
    /// <param name="main">What is in the main hand.</param>
    /// <param name="mainCount">How many of it.</param>
    /// <param name="hammerInOffhand">Whether the offhand holds a hammer.</param>
    /// <param name="debarkSeconds">How long Immersive Woodworking takes to debark a log with the
    /// held tool (<see cref="DebarkSeconds"/>).</param>
    /// <param name="debarkWear">Immersive Woodworking's durability per debarked log, which an axe
    /// and its hammer both lose.</param>
    public static SplittingBlockUpgrade? For(SplittingBlockTier tier, SplittingBlockHeld main, int mainCount,
        bool hammerInOffhand, float debarkSeconds, int debarkWear)
    {
        switch (NextStep(tier))
        {
            case SplittingBlockStep.Debark when main == SplittingBlockHeld.BarkSpud:
                return new(SplittingBlockStep.Debark, tier, 0, debarkSeconds, debarkWear, 0);
            case SplittingBlockStep.Debark when main == SplittingBlockHeld.Axe && hammerInOffhand:
                return new(SplittingBlockStep.Debark, tier, 0, debarkSeconds, debarkWear, debarkWear);
            case SplittingBlockStep.Bind when main == SplittingBlockHeld.IronHoops && mainCount >= HoopsToBind:
                return new(SplittingBlockStep.Bind, tier, HoopsToBind, 0, 0, 0);
            case SplittingBlockStep.Nail when main == SplittingBlockHeld.IronNails && mainCount >= NailsToFinish
                                              && hammerInOffhand:
                return new(SplittingBlockStep.Nail, tier, NailsToFinish, NailSeconds, 0, NailHammerWear);
            default:
                return null;
        }
    }

    /// <summary>How long debarking takes: Immersive Woodworking's base time
    /// (<c>DebarkSeconds</c>) over the tool's speed, as its sawhorse advances the strip. A speed at
    /// or near 0 is taken as 0.05, Immersive Woodworking's own floor.</summary>
    public static float DebarkSeconds(float baseSeconds, float speed) => baseSeconds / Math.Max(speed, 0.05f);

    /// <summary>Whether a hold of <paramref name="secondsUsed"/> finishes an upgrade that takes
    /// <paramref name="holdSeconds"/>.</summary>
    public static bool IsHoldDone(float secondsUsed, float holdSeconds) => secondsUsed >= holdSeconds - HoldTolerance;

    /// <summary>Firewood per half-log: Immersive Woodworking splits a log into 2 half-logs with an
    /// axe, and each into half the tier's firewood per log (a maul splits the log at once).</summary>
    public static int FirewoodPerHalfLog(this SplittingBlockTier tier) => tier.FirewoodPerLog() / 2;

    /// <summary>Whether a block code is a splitting block (Immersive Woodworking's chopping block),
    /// which may not be laid on one and chopped: the bound and advanced ones' iron would be lost.</summary>
    public static bool IsSplittingBlock(string? domain, string? path) =>
        domain == BlockDomain && path != null
        && (path == BlockCode || path.StartsWith(BlockCode + "-", StringComparison.Ordinal));
}
