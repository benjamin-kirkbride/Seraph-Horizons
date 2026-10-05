using SeraphHorizons.Mod.BuckingSawmill.Core;
using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.Rosser.Core;

/// <summary>
/// The feed's pace. The feed is geared and never slips: per shaft turn the trunk travels a constant
/// for its class (thin or thick; the cradle's weight shifts the two-speed change) times the heads'
/// metal factor. The class constant is derived on a typical trunk:
/// <code>
/// BlocksPerTurn(k) = T_end(k) / TypicalTurns(k)          // blocks per shaft turn at copper speed
/// TypicalTurns(k)  = logs_k × RevolutionsPerStoredLog + branches_k × RevolutionsPerBranch
/// Rate(k, tier)    = BlocksPerTurn(k) × HeadSpeed(tier) / 2π   // blocks per radian of shaft
/// </code>
/// so a typical trunk's whole trip, nose at the infeed to tail at the outfeed stop, takes
/// TypicalTurns(k) / HeadSpeed turns, and any other trunk of the class the same.
/// </summary>
public sealed class RosserPace
{
    public TrunkPath Path { get; }
    public RosserConfig Config { get; }

    /// <summary>The limb breaker's and the ring's positions along the path.</summary>
    public double Breaker { get; }
    public double Ring { get; }

    /// <param name="path">A path with <c>breaker</c> and <c>ring</c> stations (as <see cref="RosserRig"/> checks).</param>
    public RosserPace(TrunkPath path, RosserConfig config)
    {
        if (!path.Stations.TryGetValue(RosserRig.BreakerStation, out float breaker) || !path.Stations.TryGetValue(RosserRig.RingStation, out float ring))
            throw new ArgumentException("the trunk path needs \"breaker\" and \"ring\" stations", nameof(path));
        Path = path;
        Config = config;
        Breaker = breaker;
        Ring = ring;
    }

    public RosserPace(RosserRig rig, RosserConfig config) : this(rig.Path, config) { }

    /// <summary>T_end(k): the trip's length in blocks for class <paramref name="k"/>; 0 for none.</summary>
    public double TripLength(int k) => k is 1 or 2 ? Path.End(k) : 0;

    /// <summary>Blocks per shaft turn for class <paramref name="k"/> at copper speed; 0 for none.</summary>
    public double BlocksPerTurn(int k)
    {
        double turns = Config.TypicalTurns(k);
        return k is 1 or 2 && turns > 0 ? TripLength(k) / turns : 0;
    }

    /// <summary>How much faster the heads' metal feeds: the mill blade's rule
    /// (<see cref="Cutting.BladeSpeed"/>) with <see cref="RosserConfig.HeadSpeedPerTier"/>: 1 +
    /// that per tool tier above copper's. An unknown tier (null) feeds at 1×.</summary>
    public float HeadSpeed(int? tier) => Cutting.BladeSpeed(tier, Config.HeadSpeedPerTier);

    /// <summary>Trunk travel in blocks per radian of shaft rotation for class <paramref name="k"/>
    /// with heads of tool tier <paramref name="tier"/>.</summary>
    public double Rate(int k, int? tier) => BlocksPerTurn(k) * HeadSpeed(tier) / (2 * Math.PI);

    /// <summary>Shaft turns a trip of class <paramref name="k"/> takes with heads of tier
    /// <paramref name="tier"/>.</summary>
    public double TripTurns(int k, int? tier)
    {
        double rate = Rate(k, tier);
        return rate > 0 ? TripLength(k) / (rate * 2 * Math.PI) : 0;
    }

    /// <summary>dφ/dψ: feed-train radians per shaft radian for class <paramref name="k"/> and tier
    /// <paramref name="tier"/>, when φ moves the trunk <paramref name="blocksPerFeedRadian"/> blocks
    /// per radian (rig.json's <c>feed.blocksPerRadian</c>). At copper speed and the default settings
    /// it is what the model's two-speed change is drawn with (<c>feed.gear</c>).</summary>
    public double FeedRatio(int k, int? tier, double blocksPerFeedRadian) =>
        blocksPerFeedRadian > 0 ? Rate(k, tier) / blocksPerFeedRadian : 0;
}

/// <summary>Fallback figures for the scraper heads' metal, from the game and Immersive Woodworking
/// 1.3.11 (research §2.1), for when the game layer cannot look them up from the items.</summary>
public static class HeadMetals
{
    /// <summary>The bark spud's durability by metal (the same as the game's saw's).</summary>
    public static int? SpudDurability(string? metal) => metal switch
    {
        "gold" => 70,
        "silver" => 90,
        "copper" => 250,
        "tinbronze" => 400,
        "bismuthbronze" => 450,
        "blackbronze" => 500,
        "iron" => 900,
        "meteoriciron" => 1200,
        "steel" => 2250,
        _ => null,
    };

    /// <summary>The tool tier of the game's saw of that metal, which the mill's blade kit uses too.</summary>
    public static int? SawTier(string? metal) => metal switch
    {
        "copper" or "gold" or "silver" => 2,
        "tinbronze" or "bismuthbronze" or "blackbronze" => 3,
        "iron" or "meteoriciron" => 4,
        "steel" => 5,
        _ => null,
    };
}
