using System.Text.Json;
using System.Text.Json.Serialization;
using SeraphHorizons.Mod.Trading.Standing.Core;

namespace SeraphHorizons.Mod.Trading.Deliveries.Core;

public enum DeliveryState
{
    /// <summary>The player carries the package.</summary>
    Active,
    OnTime,
    Late,
    /// <summary>Not handed in by the end of the grace: the deposit and standing with the sender go.</summary>
    Failed,
    Cancelled,
}

/// <summary>
/// A delivery (#454): trader <see cref="From"/> hands a player a package for trader
/// <see cref="To"/> and holds <see cref="Deposit"/> of the player's gears. Handed in by
/// <see cref="Deadline"/>: the deposit back, <see cref="Fee"/> (new money, not the receiver's
/// wallet), standing at both ends. By <see cref="GraceUntil"/>: the deposit and half the fee, standing at the receiver.
/// Later: failed, the deposit kept, standing with the sender lost.
/// </summary>
public sealed class Delivery
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("from")] public string From { get; set; } = "";
    [JsonPropertyName("fromType")] public string FromType { get; set; } = "";
    [JsonPropertyName("to")] public string To { get; set; } = "";
    [JsonPropertyName("toType")] public string ToType { get; set; } = "";
    [JsonPropertyName("fromX")] public double FromX { get; set; }
    [JsonPropertyName("fromZ")] public double FromZ { get; set; }
    [JsonPropertyName("toX")] public double ToX { get; set; }
    [JsonPropertyName("toZ")] public double ToZ { get; set; }
    [JsonPropertyName("distance")] public double Distance { get; set; }
    [JsonPropertyName("player")] public string PlayerUid { get; set; } = "";
    [JsonPropertyName("playerName")] public string PlayerName { get; set; } = "";
    /// <summary>What the package is worth, in gears: the deposit and fee are shares of it.</summary>
    [JsonPropertyName("value")] public int Value { get; set; }
    [JsonPropertyName("deposit")] public int Deposit { get; set; }
    [JsonPropertyName("fee")] public int Fee { get; set; }
    [JsonPropertyName("created")] public double CreatedDay { get; set; }
    [JsonPropertyName("deadline")] public double Deadline { get; set; }
    [JsonPropertyName("grace")] public double GraceUntil { get; set; }
    [JsonPropertyName("state"), JsonConverter(typeof(JsonStringEnumConverter))] public DeliveryState State { get; set; }
    [JsonPropertyName("closed")] public double? ClosedDay { get; set; }

    [JsonIgnore] public bool IsActive => State == DeliveryState.Active;
}

/// <summary>What a trader offers a player to carry, before it is taken.</summary>
public sealed record DeliveryOffer(TraderSite From, TraderSite To, double Distance, double Days, int Value, int Deposit, int Fee);

/// <summary>Which standing call an outcome makes (<c>IStandingSource</c>), so the routing is tested
/// here: done on time, both ends; late, the receiver; failed, the sender.</summary>
public sealed record StandingCall(bool Failed, string PlayerUid, string From, string To, bool BothEnds);

/// <summary>What changed for one delivery, for the game side to settle.</summary>
public sealed record DeliveryChange(Delivery Delivery, DeliveryState To, int DepositBack, int Fee, StandingCall Standing);

/// <summary>The maths of deliveries: where to, how long, how much.</summary>
public static class DeliveryPlanner
{
    /// <summary>How far a delivery reaches at standing scale 1, in blocks; <c>deliveryScale</c> multiplies it.</summary>
    public const double BaseReach = 3000;
    /// <summary>A camp nearer than this is not worth a delivery.</summary>
    public const double MinDistance = 300;
    /// <summary>Game days allowed per km of the way, straight from sender to receiver.</summary>
    public const double DaysPerKm = 1;
    /// <summary>Never less than this many game days, however near the receiver.</summary>
    public const double MinDays = 1;
    /// <summary>After the deadline, a delivery is late for this many game days, then it fails.</summary>
    public const double GraceDays = 1;
    /// <summary>A package's value at standing scale 1, in gears (±20%).</summary>
    public const double BaseValue = 20;
    /// <summary>The deposit and the fee as shares of the value. The fee went ×10 (2026-10-08; 20–40 %
    /// before) when it stopped coming out of the receiver's wallet.</summary>
    public const double MinDeposit = 0.1, MaxDeposit = 0.3, MinFee = 2.0, MaxFee = 4.0;

    /// <summary>Game days allowed for <paramref name="distance"/> blocks: <see cref="DaysPerKm"/> a
    /// km, at least <see cref="MinDays"/>.</summary>
    public static double DeadlineDays(double distance) => Math.Max(MinDays, distance / 1000 * DaysPerKm);

    public static double Reach(double scale) => BaseReach * Math.Max(0, scale);

    public static double Distance(TraderSite a, TraderSite b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

    /// <summary>Where the trader sends a package: a camp within reach and at least
    /// <see cref="MinDistance"/> away, of another type if there is one; picked by <paramref name="roll"/>.</summary>
    public static TraderSite? Destination(TraderSite from, IEnumerable<TraderSite> sites, double scale, double roll)
    {
        double reach = Reach(scale);
        var near = sites.Where(s => s.Id != from.Id)
            .Select(s => (Site: s, D: Distance(from, s)))
            .Where(t => t.D >= MinDistance && t.D <= reach)
            .OrderBy(t => t.Site.Id, StringComparer.Ordinal)
            .ToList();
        var pool = near.Where(t => t.Site.Type != from.Type).ToList();
        if (pool.Count == 0) pool = near;
        if (pool.Count == 0) return null;
        return pool[Math.Min(pool.Count - 1, (int)Math.Floor(Math.Clamp(roll, 0, 1) * pool.Count))].Site;
    }

    public static int Value(double scale, double roll) => Math.Max(1, (int)Math.Round(BaseValue * Math.Max(1, scale) * (0.8 + 0.4 * Math.Clamp(roll, 0, 1))));

    public static int Deposit(int value, double roll) => Math.Max(1, (int)Math.Round(value * (MinDeposit + (MaxDeposit - MinDeposit) * Math.Clamp(roll, 0, 1))));

    public static int Fee(int value, double roll) => Math.Max(1, (int)Math.Round(value * (MinFee + (MaxFee - MinFee) * Math.Clamp(roll, 0, 1))));

    /// <summary>Half the fee, rounded up, for a late delivery.</summary>
    public static int LateFee(int fee) => (fee + 1) / 2;

    /// <summary>The offer between two traders; <paramref name="rolls"/> are three numbers in [0, 1)
    /// for the value, the deposit and the fee.</summary>
    public static DeliveryOffer Offer(TraderSite from, TraderSite to, double scale, double[] rolls)
    {
        double d = Distance(from, to);
        int value = Value(scale, rolls[0]);
        return new DeliveryOffer(from, to, d, DeadlineDays(d), value, Deposit(value, rolls[1]), Fee(value, rolls[2]));
    }
}

/// <summary>Every delivery in the world (saved with it), and their state machine.</summary>
public sealed class DeliveryBook
{
    public const double KeepClosedDays = 30;

    private readonly Dictionary<int, Delivery> _deliveries = new();

    public int NextId { get; private set; } = 1;

    public IEnumerable<Delivery> All => _deliveries.Values.OrderBy(d => d.Id);

    public Delivery? Get(int id) => _deliveries.GetValueOrDefault(id);

    public IEnumerable<Delivery> OfPlayer(string playerUid) => All.Where(d => d.PlayerUid == playerUid);

    /// <summary>The player's active delivery from this sender: one at a time per sender.</summary>
    public Delivery? ActiveFrom(string playerUid, string from) => All.FirstOrDefault(d => d.IsActive && d.PlayerUid == playerUid && d.From == from);

    public Delivery Create(DeliveryOffer offer, string playerUid, string playerName, double today)
    {
        var d = new Delivery
        {
            Id = NextId++,
            From = offer.From.Id,
            FromType = offer.From.Type,
            FromX = offer.From.X,
            FromZ = offer.From.Z,
            To = offer.To.Id,
            ToType = offer.To.Type,
            ToX = offer.To.X,
            ToZ = offer.To.Z,
            Distance = offer.Distance,
            PlayerUid = playerUid,
            PlayerName = playerName,
            Value = offer.Value,
            Deposit = offer.Deposit,
            Fee = offer.Fee,
            CreatedDay = today,
            Deadline = today + offer.Days,
            GraceUntil = today + offer.Days + DeliveryPlanner.GraceDays,
            State = DeliveryState.Active,
        };
        _deliveries[d.Id] = d;
        return d;
    }

    /// <summary>The player hands the package to <paramref name="atTrader"/>: on time or late, or
    /// null when it is not theirs, not for this trader, or no longer active.</summary>
    public DeliveryChange? HandIn(int id, string playerUid, string atTrader, double today)
    {
        if (Get(id) is not { IsActive: true } d || d.PlayerUid != playerUid || d.To != atTrader) return null;
        if (today > d.GraceUntil) return null;
        return Settle(d, today <= d.Deadline, today);
    }

    /// <summary>Admin: handed in on time, wherever the package is.</summary>
    public DeliveryChange? Complete(int id, double today) =>
        Get(id) is { IsActive: true } d ? Settle(d, onTime: true, today) : null;

    /// <summary>Admin: fails now.</summary>
    public DeliveryChange? Fail(int id, double today) =>
        Get(id) is { IsActive: true } d ? FailNow(d, today) : null;

    /// <summary>Admin: the deadline is now; the delivery is late until the grace runs out.</summary>
    public Delivery? Expire(int id, double today)
    {
        if (Get(id) is not { IsActive: true } d) return null;
        d.Deadline = today;
        d.GraceUntil = today + DeliveryPlanner.GraceDays;
        return d;
    }

    /// <summary>Fails every active delivery past its grace, and forgets old closed ones.</summary>
    public List<DeliveryChange> Tick(double today)
    {
        var changes = All.Where(d => d.IsActive && today > d.GraceUntil).ToList().Select(d => FailNow(d, today)).ToList();
        foreach (var d in _deliveries.Values.Where(d => !d.IsActive && d.ClosedDay is double c && today - c > KeepClosedDays).ToList())
            _deliveries.Remove(d.Id);
        return changes;
    }

    /// <summary>Moves every active delivery's clock on by <paramref name="days"/> (the economy's simulate).</summary>
    public void Advance(double days)
    {
        foreach (var d in _deliveries.Values.Where(d => d.IsActive))
        {
            d.CreatedDay -= days;
            d.Deadline -= days;
            d.GraceUntil -= days;
        }
    }

    private static DeliveryChange Settle(Delivery d, bool onTime, double today)
    {
        d.State = onTime ? DeliveryState.OnTime : DeliveryState.Late;
        d.ClosedDay = today;
        int fee = onTime ? d.Fee : DeliveryPlanner.LateFee(d.Fee);
        return new DeliveryChange(d, d.State, d.Deposit, fee, new StandingCall(false, d.PlayerUid, d.From, d.To, BothEnds: onTime));
    }

    private static DeliveryChange FailNow(Delivery d, double today)
    {
        d.State = DeliveryState.Failed;
        d.ClosedDay = today;
        return new DeliveryChange(d, d.State, 0, 0, new StandingCall(true, d.PlayerUid, d.From, d.To, false));
    }

    private sealed class Dto
    {
        [JsonPropertyName("next")] public int Next { get; set; } = 1;
        [JsonPropertyName("deliveries")] public List<Delivery> Deliveries { get; set; } = [];
    }

    public string ToJson() => JsonSerializer.Serialize(new Dto { Next = NextId, Deliveries = All.ToList() });

    public static DeliveryBook FromJson(string json)
    {
        var dto = JsonSerializer.Deserialize<Dto>(json) ?? new Dto();
        var book = new DeliveryBook();
        foreach (var d in dto.Deliveries) book._deliveries[d.Id] = d;
        book.NextId = Math.Max(dto.Next, dto.Deliveries.Select(d => d.Id + 1).DefaultIfEmpty(1).Max());
        return book;
    }
}
