using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.Rosser.Core;

/// <summary>Where a trunk is in the rosser (design §4.1). Derived from the trip, never stored.
/// Whether a waiting or feeding trunk moves is <see cref="RosserTrip.Running"/>.</summary>
public enum RosserState
{
    /// <summary>No trunk.</summary>
    Empty,
    /// <summary>On the infeed bed, not yet moved (T = 0). It throws the feed in; it moves once the shaft turns.</summary>
    Waiting,
    /// <summary>In the rolls, 0 &lt; T &lt; T_end (stalled when not running).</summary>
    Feeding,
    /// <summary>Debarked, on the outfeed bed (T = T_end), until a hand, a rack or the mill takes it.</summary>
    Delivered,
}

/// <summary>Why a trunk can or cannot go on (by hand or from a rack).</summary>
public enum RosserLoadVerdict
{
    Loads,
    /// <summary>A stage is missing (spent heads are gone, so that too).</summary>
    Incomplete,
    /// <summary>A trunk is already in.</summary>
    Occupied,
    /// <summary>The trunk is already debarked.</summary>
    AlreadyDebarked,
    /// <summary>The trunk holds no logs.</summary>
    NoLogs,
}

/// <summary>What Ctrl + right click takes back out.</summary>
public enum RosserTakeBack
{
    /// <summary>The trunk: waiting (as loaded) or delivered (debarked).</summary>
    Trunk,
    /// <summary>The trunk is in the rolls; refused.</summary>
    InRolls,
    /// <summary>No trunk: the four unworn heads.</summary>
    Heads,
    /// <summary>No trunk, and the heads have been used; refused.</summary>
    HeadsWorn,
    /// <summary>Nothing to take.</summary>
    Nothing,
}

/// <summary>What breaking the rosser makes of the trunk in it (design §4.7), so breaking and
/// running the trunk again never gives its sticks or bark twice.</summary>
public enum RosserBrokenTrunk
{
    /// <summary>No trunk.</summary>
    None,
    /// <summary>Its nose has not passed the limb breaker: as it went in.</summary>
    AsLoaded,
    /// <summary>Past the breaker, not past the ring: its branches are gone (the sticks already given are all there is).</summary>
    Debranched,
    /// <summary>Past the ring, or delivered: debarked.</summary>
    Debarked,
}

/// <summary>What the Trunk Storage Rack at the infeed end offers, as the server last found it
/// (synced for the block info).</summary>
public enum RosserRackState
{
    Unknown,
    /// <summary><c>AutoPullFromRack</c> is off.</summary>
    Off,
    /// <summary>Logging Expanded is not as the rosser expects.</summary>
    NoLogging,
    /// <summary>No rack touches the infeed end.</summary>
    None,
    /// <summary>A rack with no trunk on it.</summary>
    Empty,
    /// <summary>Its top trunk is already debarked: it waits there.</summary>
    Debarked,
    /// <summary>Its top trunk holds no logs.</summary>
    NoLogs,
    /// <summary>Its top trunk goes in when the rosser is empty.</summary>
    Ready,
}

/// <summary>Where a delivered trunk can go at the outfeed end, as the server last found it.</summary>
public enum RosserOutfeedState
{
    Unknown,
    /// <summary>No rack and no mill: it waits for a hand.</summary>
    None,
    /// <summary>A rack with room (pushed when delivered, if <c>AutoPushToRack</c>).</summary>
    Rack,
    /// <summary>A rack holding four trunks: the trunk waits on the bed.</summary>
    RackFull,
    /// <summary>A bucking mill in line takes it at its next top.</summary>
    Mill,
    /// <summary><c>AutoPushToRack</c> is off and no mill takes it: it waits for a hand. (Appended:
    /// the state is synced as its number.)</summary>
    Off,
}

/// <summary>What one step of the trip gives: sticks and logs' worth of bark to drop at the chute
/// now, and whether the trunk was delivered during this step (debark it and wear the heads).</summary>
public readonly record struct TripStep(int Sticks, int BarkLogs, bool Delivered)
{
    public static readonly TripStep Nothing = new(0, 0, false);
}

/// <summary>
/// One trunk's trip through the rosser, as pure transitions. Travel T runs from 0 (nose waiting at
/// <c>nose0</c>) to T_end(k) (tail at <c>tailStop</c>) at a fixed <see cref="Rate"/> (blocks per
/// radian of shaft, set at load from the class and the heads' tier). Items drop as the relevant
/// part of the trunk passes (design §4.3): stick i of S when (nose − breaker) / L_k ≥ (i + 1) / S,
/// log j's bark when (nose − ring) / L_k ≥ (j + 1) / logs. What has dropped is counted
/// (<see cref="SticksDone"/>, <see cref="BarkDone"/>), and saved, so a step only gives what is due
/// beyond the counts: nothing drops twice, however the counters and T are saved and reloaded.
/// </summary>
/// <param name="Class">k: 0 none, 1 thin, 2 thick.</param>
/// <param name="Logs">Stored logs (never the size: Logging Expanded keeps the size when logs are taken).</param>
/// <param name="Branches">The stack's <c>branchCount</c>.</param>
/// <param name="Travel">T, blocks.</param>
/// <param name="Rate">Blocks per radian of shaft rotation.</param>
public readonly record struct RosserTrip(int Class, int Logs, int Branches, double Travel, double Rate, int SticksDone, int BarkDone)
{
    // Products of floats like 3 × 0.1 land a hair off the whole number they mean.
    private const double Epsilon = 1e-9;

    public static readonly RosserTrip None = new(0, 0, 0, 0, 0, 0, 0);

    public bool HasTrunk => Class is 1 or 2;

    /// <summary>A trunk going on: at T = 0 with nothing dropped, its rate fixed for the trip.</summary>
    public static RosserTrip Load(RosserPace pace, TrunkClass trunk, int logs, int branches, int? headTier) =>
        trunk == TrunkClass.None
            ? None
            : new((int)trunk, Math.Max(0, logs), Math.Max(0, branches), 0, pace.Rate((int)trunk, headTier), 0, 0);

    /// <summary>A trip read back from a save. T is clamped to the trip; a rate of 0 or less (or
    /// missing) is recomputed; missing counters are taken as all that was due at T (so a save from
    /// before the counters drops nothing again), and counters are capped at the totals.</summary>
    public static RosserTrip Restore(RosserPace pace, TrunkClass trunk, int logs, int branches, double travel, double rate,
                                     int? sticksDone, int? barkDone, int? headTier)
    {
        if (trunk == TrunkClass.None)
            return None;
        var trip = Load(pace, trunk, logs, branches, headTier);
        double t = double.IsFinite(travel) ? Math.Clamp(travel, 0, pace.TripLength(trip.Class)) : 0;
        trip = trip with { Travel = t };
        if (rate > 0 && double.IsFinite(rate))
            trip = trip with { Rate = rate };
        return trip with
        {
            SticksDone = Math.Clamp(sticksDone ?? trip.SticksDue(pace), 0, trip.Sticks(pace)),
            BarkDone = Math.Clamp(barkDone ?? trip.BarkDue(pace), 0, trip.Logs),
        };
    }

    /// <summary>S: the sticks the whole trunk gives, floor(branches × StickFraction).</summary>
    public int Sticks(RosserPace pace) => Math.Max(0, (int)Math.Floor(Branches * (double)pace.Config.StickFraction + 1e-6));

    /// <summary>The trunk's length as shown, L_k.</summary>
    public double Length(RosserPace pace) => pace.Path.LengthOf(Class);

    public double End(RosserPace pace) => pace.TripLength(Class);

    public double Nose(RosserPace pace) => pace.Path.Nose(Travel);

    public double Tail(RosserPace pace) => pace.Path.Tail(Travel, Class);

    public RosserState State(RosserPace pace) =>
        !HasTrunk ? RosserState.Empty
        : Travel >= End(pace) ? RosserState.Delivered
        : Travel <= 0 ? RosserState.Waiting
        : RosserState.Feeding;

    /// <summary>How many of <paramref name="total"/> are due when the nose is
    /// <paramref name="past"/> blocks beyond a station on a trunk <paramref name="length"/> long:
    /// floor(total × past / length), 0 before the station and all once the tail is past it.</summary>
    public static int Due(int total, double past, double length)
    {
        if (total <= 0 || past <= 0 || length <= 0)
            return 0;
        double along = past / length;
        return along >= 1 ? total : Math.Clamp((int)Math.Floor(total * along + Epsilon), 0, total);
    }

    /// <summary>Sticks due at this T (all of them once delivered).</summary>
    public int SticksDue(RosserPace pace) =>
        State(pace) == RosserState.Delivered ? Sticks(pace) : Due(Sticks(pace), Nose(pace) - pace.Breaker, Length(pace));

    /// <summary>Logs whose bark is due at this T (all of them once delivered).</summary>
    public int BarkDue(RosserPace pace) =>
        State(pace) == RosserState.Delivered ? Logs : Due(Logs, Nose(pace) - pace.Ring, Length(pace));

    /// <summary>Turns the trip by <paramref name="radians"/> of shaft rotation (only call it while
    /// <see cref="Running"/>): T moves on at <see cref="Rate"/>, never past T_end, and the step says
    /// what dropped. A waiting trunk starts feeding; reaching T_end delivers it. Nothing moves when
    /// empty or delivered.</summary>
    public (RosserTrip Trip, TripStep Step) Advance(RosserPace pace, double radians)
    {
        var state = State(pace);
        if (state is RosserState.Empty or RosserState.Delivered)
            return (this, TripStep.Nothing);
        double end = End(pace);
        double t = Travel + Math.Max(0, double.IsFinite(radians) ? radians : 0) * Rate;
        if (t >= end - Epsilon)
            t = end;
        var next = this with { Travel = t };
        int sticks = Math.Max(0, next.SticksDue(pace) - SticksDone);
        int bark = Math.Max(0, next.BarkDue(pace) - BarkDone);
        next = next with { SticksDone = SticksDone + sticks, BarkDone = BarkDone + bark };
        return (next, new TripStep(sticks, bark, next.State(pace) == RosserState.Delivered));
    }

    /// <summary>The trip after the trunk is taken (by hand, a rack or the mill).</summary>
    public static RosserTrip Taken => None;

    /// <summary>Whether any part of the trunk lies over <paramref name="station"/> (for the scraping
    /// sound under the ring, and the drip).</summary>
    public bool Over(RosserPace pace, double station) => HasTrunk && Tail(pace) <= station && station <= Nose(pace);

    /// <summary>What breaking the rosser makes of the trunk.</summary>
    public RosserBrokenTrunk Broken(RosserPace pace)
    {
        if (!HasTrunk)
            return RosserBrokenTrunk.None;
        if (State(pace) == RosserState.Delivered || Nose(pace) > pace.Ring)
            return RosserBrokenTrunk.Debarked;
        return Nose(pace) > pace.Breaker ? RosserBrokenTrunk.Debranched : RosserBrokenTrunk.AsLoaded;
    }

    /// <summary>T as the trunk's boxes follow it, in 1/16-block steps.</summary>
    public static double BoxTravel(double travel) => Math.Floor(travel * 16 + Epsilon) / 16;

    /// <summary>"Running": complete (heads fitted and not spent) and the shaft turning at
    /// <paramref name="minSpeed"/> or faster.</summary>
    public static bool Running(bool complete, float speed, float minSpeed) => complete && Math.Abs(speed) >= minSpeed;

    /// <summary>Whether a trunk can go on: the rosser complete and empty, the trunk not already
    /// debarked and holding logs. Power is not needed.</summary>
    public static RosserLoadVerdict CanLoad(bool complete, RosserState state, bool debarked, int logs) =>
        !complete ? RosserLoadVerdict.Incomplete
        : state != RosserState.Empty ? RosserLoadVerdict.Occupied
        : debarked ? RosserLoadVerdict.AlreadyDebarked
        : logs <= 0 ? RosserLoadVerdict.NoLogs
        : RosserLoadVerdict.Loads;

    /// <summary>What the infeed rack's top trunk offers (<paramref name="hasTrunk"/> false for an
    /// empty rack).</summary>
    public static RosserRackState RackOffer(bool hasTrunk, bool debarked, int logs) =>
        !hasTrunk ? RosserRackState.Empty
        : debarked ? RosserRackState.Debarked
        : logs <= 0 ? RosserRackState.NoLogs
        : RosserRackState.Ready;

    /// <summary>Whether a delivered trunk can be pushed into a rack holding
    /// <paramref name="rackTrunks"/> (Logging Expanded's rack drops a fifth silently).</summary>
    public static bool RackHasRoom(int rackTrunks) => rackTrunks < MaxRackTrunks;

    public const int MaxRackTrunks = 4;

    /// <summary>What Ctrl + right click takes.</summary>
    public static RosserTakeBack TakeBack(RosserState state, bool headsFitted, bool headsUnworn) => state switch
    {
        RosserState.Waiting or RosserState.Delivered => RosserTakeBack.Trunk,
        RosserState.Feeding => RosserTakeBack.InRolls,
        _ => !headsFitted ? RosserTakeBack.Nothing : headsUnworn ? RosserTakeBack.Heads : RosserTakeBack.HeadsWorn,
    };
}
