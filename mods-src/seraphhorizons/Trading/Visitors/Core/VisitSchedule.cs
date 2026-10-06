using System.Text.Json;
using System.Text.Json.Serialization;
using SeraphHorizons.Mod.Trading.Core;

namespace SeraphHorizons.Mod.Trading.Visitors.Core;

/// <summary>The two travelling traders (#456): a kind (as commands and trader ids name it) and its
/// trader type (entity variant and trade list).</summary>
public static class VisitorKinds
{
    public const string General = "general";
    public const string Curio = "curio";

    public static readonly IReadOnlyList<string> All = [General, Curio];

    public static string TypeOf(string kind) => kind == Curio ? TraderTypes.TravellingCurio : TraderTypes.TravellingMerchant;

    public static string? KindOf(string type) =>
        type == TraderTypes.TravellingMerchant ? General : type == TraderTypes.TravellingCurio ? Curio : null;

    /// <summary>The standing id every visitor of a kind shares, wherever it visits.</summary>
    public static string TraderId(string kind) => Standing.Core.TraderIds.Visitor(kind);
}

public enum InnPhase { Idle, Pending, Visiting, Cooldown }

/// <summary>One inn, keyed by its flag's position, as saved.</summary>
public sealed class InnRecord
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    /// <summary>Who raised the flag: whose standing calls the visitors. Empty when placed by a command.</summary>
    public string Owner { get; set; } = "";
    public InnPhase Phase { get; set; }
    public string Kind { get; set; } = "";
    public double ArriveDay { get; set; }
    public double LeaveDay { get; set; }
    public double CooldownUntil { get; set; }
    public long EntityId { get; set; }
    /// <summary>The last whole day the conditions were evaluated (once a day while idle).</summary>
    public int CheckedDay { get; set; } = int.MinValue;
    /// <summary>What the last daily evaluation found missing (rules and conditions), for <c>inn check</c>.</summary>
    public List<string> LastMissing { get; set; } = [];

    [JsonIgnore] public InnPos Pos => new(X, Y, Z);
    [JsonIgnore] public string Key => Pos.ToString();
}

public sealed record VisitSettings
{
    public double DelayMinDays { get; init; } = 2;
    public double DelayMaxDays { get; init; } = 4;
    public double StayMinDays { get; init; } = 3;
    public double StayMaxDays { get; init; } = 5;
    public double CooldownDays { get; init; } = 10;
}

public enum VisitAction { None, Evaluate, Arrive, Leave, CooldownOver }

/// <summary>
/// An inn's visit cycle: idle (the conditions are evaluated once a day) → pending (a visitor is on
/// the way, arriving 2–4 days after the day everything passed) → visiting (3–5 days) → cooldown
/// (10 days) → idle. Days are the visitor clock's (<see cref="InnBook.Offset"/> on top of the
/// calendar), which <c>/sh trade simulate</c> advances. Rolls are passed in (0..1) so the cycle is
/// testable.
/// </summary>
public static class VisitPlanner
{
    public static VisitAction Due(InnRecord r, double now) => r.Phase switch
    {
        InnPhase.Idle when Math.Floor(now) > r.CheckedDay => VisitAction.Evaluate,
        InnPhase.Pending when now >= r.ArriveDay => VisitAction.Arrive,
        InnPhase.Visiting when now >= r.LeaveDay => VisitAction.Leave,
        InnPhase.Cooldown when now >= r.CooldownUntil => VisitAction.CooldownOver,
        _ => VisitAction.None,
    };

    /// <summary>The day's evaluation: with every condition met for some kinds, one of them (by
    /// <paramref name="roll"/>) is scheduled. Returns the kind scheduled, or null.</summary>
    public static string? Evaluated(InnRecord r, double now, IReadOnlyList<string> passingKinds, IEnumerable<string> missing,
        double roll, double delayRoll, VisitSettings? settings = null)
    {
        r.CheckedDay = (int)Math.Floor(now);
        r.LastMissing = missing.ToList();
        if (r.Phase != InnPhase.Idle || passingKinds.Count == 0) return null;
        string kind = passingKinds[Math.Clamp((int)(roll * passingKinds.Count), 0, passingKinds.Count - 1)];
        Schedule(r, kind, now, delayRoll, settings);
        return kind;
    }

    /// <summary>A visitor is on its way (also <c>inn call</c>, which skips the conditions and the cooldown).</summary>
    public static void Schedule(InnRecord r, string kind, double now, double delayRoll, VisitSettings? settings = null)
    {
        var s = settings ?? new VisitSettings();
        r.Phase = InnPhase.Pending;
        r.Kind = kind;
        r.ArriveDay = now + Lerp(s.DelayMinDays, s.DelayMaxDays, delayRoll);
        r.EntityId = 0;
    }

    public static void Arrived(InnRecord r, long entityId, double now, double stayRoll, VisitSettings? settings = null)
    {
        var s = settings ?? new VisitSettings();
        r.Phase = InnPhase.Visiting;
        r.EntityId = entityId;
        r.LeaveDay = now + Lerp(s.StayMinDays, s.StayMaxDays, stayRoll);
    }

    public static void Left(InnRecord r, double now, VisitSettings? settings = null)
    {
        var s = settings ?? new VisitSettings();
        r.Phase = InnPhase.Cooldown;
        r.CooldownUntil = now + s.CooldownDays;
        r.EntityId = 0;
        r.Kind = "";
    }

    /// <summary>A pending visit called off (<c>inn dismiss</c> before it arrived): back to idle, no cooldown.</summary>
    public static void Cancelled(InnRecord r)
    {
        r.Phase = InnPhase.Idle;
        r.Kind = "";
        r.EntityId = 0;
    }

    public static void CooledDown(InnRecord r) => r.Phase = InnPhase.Idle;

    private static double Lerp(double a, double b, double t) => a + (b - a) * Math.Clamp(t, 0, 1);
}

/// <summary>Every inn in the world, and the visitor clock's offset, saved as JSON.</summary>
public sealed class InnBook
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    public Dictionary<string, InnRecord> Inns { get; set; } = new();

    /// <summary>Days <c>/sh trade simulate</c> has added on top of the calendar.</summary>
    public double Offset { get; set; }

    public InnRecord? Get(InnPos pos) => Inns.GetValueOrDefault(pos.ToString());

    public InnRecord Raise(InnPos pos, string owner)
    {
        if (!Inns.TryGetValue(pos.ToString(), out var r))
            Inns[pos.ToString()] = r = new InnRecord { X = pos.X, Y = pos.Y, Z = pos.Z };
        if (owner.Length > 0) r.Owner = owner;
        return r;
    }

    public InnRecord? Lower(InnPos pos) => Inns.Remove(pos.ToString(), out var r) ? r : null;

    /// <summary>The inn whose flag is nearest to pos within radius (Chebyshev), or null.</summary>
    public InnRecord? Nearest(InnPos pos, int radius) =>
        Inns.Values.Where(r => Math.Abs(r.X - pos.X) <= radius && Math.Abs(r.Y - pos.Y) <= radius && Math.Abs(r.Z - pos.Z) <= radius)
            .OrderBy(r => r.Pos.Manhattan(pos)).FirstOrDefault();

    public InnRecord? ByEntity(long entityId) => entityId == 0 ? null : Inns.Values.FirstOrDefault(r => r.EntityId == entityId);

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static InnBook FromJson(string json) => JsonSerializer.Deserialize<InnBook>(json, Json) ?? new InnBook();
}
