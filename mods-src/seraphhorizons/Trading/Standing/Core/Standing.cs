namespace SeraphHorizons.Mod.Trading.Standing.Core;

/// <summary>What changed a record: a deal, an order, a delivery, a penalty, a merge or an admin.</summary>
public static class StandingKinds
{
    public const string Deal = "deal";
    public const string Order = "order";
    public const string Delivery = "delivery";
    public const string DeliveryFailed = "deliveryfailed";
    public const string OrderAbandoned = "orderabandoned";
    public const string Merge = "merge";
    public const string Admin = "admin";
}

/// <summary>One change to a record, kept in its ring for the admin views.</summary>
public sealed class StandingEvent
{
    /// <summary>The game's total days when it happened.</summary>
    public double Day { get; set; }
    public string Kind { get; set; } = "";
    public double Points { get; set; }
}

/// <summary>
/// Standing with one trader, of a player or of a company (#452, #463): points, never below 0, and
/// the last few changes. <see cref="LastDay"/> is when it last changed, which the wallet reads to
/// know who traded recently.
/// </summary>
public sealed class StandingRecord
{
    public double Points { get; set; }
    public double LastDay { get; set; } = -1;
    public List<StandingEvent> Events { get; set; } = [];

    public void Add(double points, string kind, double day, int keep)
    {
        double before = Points;
        Points = Math.Max(0, Points + points);
        LastDay = Math.Max(LastDay, day);
        Events.Add(new StandingEvent { Day = day, Kind = kind, Points = Points - before });
        if (keep >= 0 && Events.Count > keep) Events.RemoveRange(0, Events.Count - keep);
    }

    public StandingRecord Clone() => new()
    {
        Points = Points, LastDay = LastDay,
        Events = Events.Select(e => new StandingEvent { Day = e.Day, Kind = e.Kind, Points = e.Points }).ToList(),
    };
}

/// <summary>
/// What a tier gives (#452). The consumers come in later waves (maps #453–#455, pricing #450,
/// orders and deliveries #456–#457, stock); this only carries the data.
/// </summary>
public sealed class TierUnlocks
{
    /// <summary>The best map tier the trader sells (0: the basic lead to the nearest camp only).</summary>
    public int MapTier { get; set; }
    /// <summary>Whether the trader sells maps to other traders.</summary>
    public bool MapsToTraders { get; set; }
    /// <summary>Times what the player pays for the trader's goods.</summary>
    public double BuyPriceFactor { get; set; } = 1;
    /// <summary>Times what the trader pays for the player's goods.</summary>
    public double SellPriceFactor { get; set; } = 1;
    /// <summary>Times the trade list's base <c>wallet</c> the trader restocks to
    /// (<c>TradeListDef.WalletAt</c>).</summary>
    public double WalletFactor { get; set; } = 1;
    /// <summary>How large a delivery the trader hands over, times the base size (0: none).</summary>
    public double DeliveryScale { get; set; }
    /// <summary>Whether the trader shelves its rare stock for this player.</summary>
    public bool RareStock { get; set; }
}

public sealed class StandingTier
{
    public string Code { get; set; } = "";
    /// <summary>The points from which this tier holds.</summary>
    public double Points { get; set; }
    public TierUnlocks Unlocks { get; set; } = new();
}

/// <summary>Points per event (<c>points</c> in the tiers file).</summary>
public sealed class StandingPoints
{
    /// <summary>Per gear changing hands in a deal, either way.</summary>
    public double PerGear { get; set; } = 1;
    public double Order { get; set; } = 40;
    /// <summary>At each end a delivery credits.</summary>
    public double Delivery { get; set; } = 30;
    /// <summary>Taken from the sender's record (a positive number).</summary>
    public double DeliveryFailed { get; set; } = 150;
    public double OrderAbandoned { get; set; } = 60;
}

/// <summary>
/// <c>assets/seraphhorizons/config/standing-tiers.json</c>: the tiers in ascending order, points
/// per event, the share of standing with nearby traders of the same type that counts, how many
/// events a record keeps and how far back "traded recently" reaches for the wallet.
/// </summary>
public sealed class StandingRules
{
    public List<StandingTier> Tiers { get; set; } = [];
    public StandingPoints Points { get; set; } = new();
    public double SpilloverShare { get; set; } = 0.1;
    public int EventsKept { get; set; } = 16;
    public double RecentDays { get; set; } = 14;

    /// <summary>The tier of an effective standing (the highest whose threshold it reaches).</summary>
    public int TierIndex(double points)
    {
        int index = 0;
        for (int i = 0; i < Tiers.Count; i++)
            if (points >= Tiers[i].Points) index = i;
        return index;
    }

    public StandingTier Tier(int index) => Tiers.Count == 0 ? new StandingTier { Code = "stranger" } : Tiers[Math.Clamp(index, 0, Tiers.Count - 1)];

    /// <summary>The next tier up, or null at the top.</summary>
    public StandingTier? Next(double points) => TierIndex(points) + 1 < Tiers.Count ? Tiers[TierIndex(points) + 1] : null;

    /// <summary>Points for a deal of <paramref name="gearsPaid"/> by the player and
    /// <paramref name="gearsReceived"/> by them: the deal's whole gear value.</summary>
    public double DealPoints(int gearsPaid, int gearsReceived) => (Math.Max(0, gearsPaid) + Math.Max(0, gearsReceived)) * Points.PerGear;

    public List<string> Problems()
    {
        var problems = new List<string>();
        if (Tiers.Count == 0) problems.Add("no tiers");
        else if (Tiers[0].Points != 0) problems.Add($"the first tier ({Tiers[0].Code}) starts at {Tiers[0].Points}, not 0");
        for (int i = 1; i < Tiers.Count; i++)
            if (Tiers[i].Points <= Tiers[i - 1].Points)
                problems.Add($"tier {Tiers[i].Code} starts at {Tiers[i].Points}, not above {Tiers[i - 1].Code}'s {Tiers[i - 1].Points}");
        foreach (var t in Tiers)
        {
            if (string.IsNullOrWhiteSpace(t.Code)) problems.Add("a tier has no code");
            if (t.Unlocks.BuyPriceFactor <= 0 || t.Unlocks.SellPriceFactor <= 0) problems.Add($"tier {t.Code} has a price factor of 0 or less");
            if (t.Unlocks.WalletFactor <= 0) problems.Add($"tier {t.Code} has a wallet factor of 0 or less");
        }
        if (SpilloverShare < 0 || SpilloverShare > 1) problems.Add($"spilloverShare {SpilloverShare} is not within 0–1");
        if (Points.PerGear < 0 || Points.Order < 0 || Points.Delivery < 0 || Points.DeliveryFailed < 0 || Points.OrderAbandoned < 0)
            problems.Add("points are given as positive numbers (penalties are taken off)");
        return problems;
    }
}

/// <summary>A trader somewhere, for spillover: its standing id, type and position.</summary>
public readonly record struct TraderSite(string Id, string Type, double X, double Z);

/// <summary>
/// Stable trader ids, the key of every standing record. A trader in a grid camp is its cell
/// (<c>camp:x,z</c>): the camp's trader respawns as a new entity, and standing is with the camp.
/// A travelling merchant (#456) is its kind (<c>visitor:general</c>), the same at every inn.
/// Any other trader is its entity (<c>entity:id</c>).
/// </summary>
public static class TraderIds
{
    public static string Camp(int cellX, int cellZ) => $"camp:{cellX},{cellZ}";
    public static string Entity(long entityId) => $"entity:{entityId}";
    public static string Visitor(string kind) => $"visitor:{kind}";
    public static bool IsValid(string id) => id.StartsWith("camp:", StringComparison.Ordinal) || id.StartsWith("entity:", StringComparison.Ordinal)
                                             || id.StartsWith("visitor:", StringComparison.Ordinal);
}

/// <summary>Spillover (#452): a share of the best standing with other traders of the same type
/// within a radius counts at this one.</summary>
public static class Spillover
{
    public static IEnumerable<TraderSite> Neighbours(TraderSite self, IEnumerable<TraderSite> sites, double radius) =>
        sites.Where(s => s.Id != self.Id && s.Type == self.Type
                         && (s.X - self.X) * (s.X - self.X) + (s.Z - self.Z) * (s.Z - self.Z) <= radius * radius);

    /// <summary>The share of the best of <paramref name="neighbourStandings"/> (0 with none).</summary>
    public static double Bonus(IEnumerable<double> neighbourStandings, double share) =>
        share * neighbourStandings.DefaultIfEmpty(0).Max();
}

/// <summary>One player's standing at one trader, as a trader reads it.</summary>
public sealed record StandingView(
    double Personal, double? Company, int? CompanyUid, double Spill, double Effective,
    int TierIndex, StandingTier Tier, StandingTier? Next)
{
    /// <summary>What the trader reads before spillover: max(personal, company).</summary>
    public double Own => Math.Max(Personal, Company ?? 0);
}

/// <summary>
/// Everything saved (savegame key <c>seraphhorizons:standing</c>): each player's records by trader
/// id, each company's (by vanilla group uid), and the group each player chose as company.
/// </summary>
public sealed class StandingState
{
    public Dictionary<string, Dictionary<string, StandingRecord>> Players { get; set; } = new();
    public Dictionary<int, CompanyRecord> Companies { get; set; } = new();
    public Dictionary<string, int> Designated { get; set; } = new();
}

/// <summary>
/// Standing per (player, trader) and per (company, trader), and the rules joining them (#452,
/// #463): every gain and loss goes to the player's own record and to their company's; a trader
/// reads max(personal, company), plus spillover. Callers resolve the player's company first
/// (<see cref="CompanyBook.Resolve"/>, then <see cref="CompanyBook.Sync"/>) and pass it in. Not
/// thread-safe: the server's main thread uses it.
/// </summary>
public sealed class StandingLedger
{
    public StandingLedger(StandingRules rules, StandingState? state = null)
    {
        Rules = rules;
        State = state ?? new StandingState();
        Companies = new CompanyBook(State, this);
    }

    public StandingRules Rules { get; }
    public StandingState State { get; }
    public CompanyBook Companies { get; }

    public StandingRecord? Personal(string player, string trader) =>
        State.Players.TryGetValue(player, out var map) && map.TryGetValue(trader, out var r) ? r : null;

    public IReadOnlyDictionary<string, StandingRecord> PersonalRecords(string player) =>
        State.Players.TryGetValue(player, out var map) ? map : new Dictionary<string, StandingRecord>();

    internal StandingRecord PersonalOrAdd(string player, string trader)
    {
        if (!State.Players.TryGetValue(player, out var map)) State.Players[player] = map = new();
        if (!map.TryGetValue(trader, out var r)) map[trader] = r = new StandingRecord();
        return r;
    }

    /// <summary>max(personal, company) at one trader, before spillover.</summary>
    public double Own(string player, int? company, string trader) =>
        Math.Max(Personal(player, trader)?.Points ?? 0, company is int c ? Companies.Get(c, trader)?.Points ?? 0 : 0);

    /// <summary>A gain: to the player's record and their company's.</summary>
    public void Credit(string player, int? company, string trader, double points, string kind, double day)
    {
        if (points <= 0) return;
        Apply(player, company, trader, points, kind, day);
    }

    /// <summary>A loss (a failed delivery, an abandoned order): to the player who took the job and
    /// their company; nobody else's personal record.</summary>
    public void Penalise(string player, int? company, string trader, double points, string kind, double day)
    {
        if (points <= 0) return;
        Apply(player, company, trader, -points, kind, day);
    }

    private void Apply(string player, int? company, string trader, double points, string kind, double day)
    {
        PersonalOrAdd(player, trader).Add(points, kind, day, Rules.EventsKept);
        if (company is int c) Companies.GetOrAdd(c, trader).Add(points, kind, day, Rules.EventsKept);
    }

    public void OnDeal(string player, int? company, string trader, int gearsPaid, int gearsReceived, double day) =>
        Credit(player, company, trader, Rules.DealPoints(gearsPaid, gearsReceived), StandingKinds.Deal, day);

    public void OnOrderDone(string player, int? company, string trader, double day) =>
        Credit(player, company, trader, Rules.Points.Order, StandingKinds.Order, day);

    /// <summary>A delivery handed over: on time (<paramref name="bothEnds"/>) both the sender and
    /// the receiver credit it; late, only the receiver.</summary>
    public void OnDeliveryDone(string player, int? company, string from, string to, bool bothEnds, double day)
    {
        Credit(player, company, to, Rules.Points.Delivery, StandingKinds.Delivery, day);
        if (bothEnds && from != to) Credit(player, company, from, Rules.Points.Delivery, StandingKinds.Delivery, day);
    }

    /// <summary>A delivery lost or never made: standing with the sender goes.</summary>
    public void OnDeliveryFailed(string player, int? company, string from, double day) =>
        Penalise(player, company, from, Rules.Points.DeliveryFailed, StandingKinds.DeliveryFailed, day);

    public void OnOrderAbandoned(string player, int? company, string trader, double day) =>
        Penalise(player, company, trader, Rules.Points.OrderAbandoned, StandingKinds.OrderAbandoned, day);

    /// <summary>The whole view a trader takes of a player: own records, the spillover from
    /// <paramref name="neighbours"/> (other traders of the type in range), the tier.</summary>
    public StandingView View(string player, int? company, string trader, IEnumerable<string> neighbours)
    {
        double personal = Personal(player, trader)?.Points ?? 0;
        double? companyPoints = company is int c ? Companies.Get(c, trader)?.Points ?? 0 : null;
        double own = Math.Max(personal, companyPoints ?? 0);
        double spill = Spillover.Bonus(neighbours.Select(n => Own(player, company, n)), Rules.SpilloverShare);
        double effective = own + spill;
        int tier = Rules.TierIndex(effective);
        return new StandingView(personal, companyPoints, company, spill, effective, tier, Rules.Tier(tier), Rules.Next(effective));
    }

    /// <summary>Players whose own record with the trader changed since <paramref name="sinceDay"/>,
    /// for the wallet.</summary>
    public IEnumerable<string> RecentPlayers(string trader, double sinceDay) =>
        State.Players.Where(kv => kv.Value.TryGetValue(trader, out var r) && r.LastDay >= sinceDay).Select(kv => kv.Key);

    /// <summary>Moves every record's last change <paramref name="days"/> into the past, as if that
    /// much time had gone by: <c>/sh trade simulate</c> advances the "traded recently" clock
    /// (<see cref="StandingRules.RecentDays"/>) this way, since it does not move the calendar.</summary>
    public void Age(double days)
    {
        foreach (var map in State.Players.Values)
            foreach (var r in map.Values)
                if (r.LastDay >= 0) r.LastDay -= days;
    }

    /// <summary>Admin: sets a player's own record (not the company's).</summary>
    public void Set(string player, string trader, double points, double day)
    {
        var r = PersonalOrAdd(player, trader);
        r.Add(Math.Max(0, points) - r.Points, StandingKinds.Admin, day, Rules.EventsKept);
    }

    /// <summary>Admin: forgets a player's own record with one trader, or all of them.</summary>
    public void Reset(string player, string? trader)
    {
        if (!State.Players.TryGetValue(player, out var map)) return;
        if (trader is null) State.Players.Remove(player);
        else map.Remove(trader);
    }
}
