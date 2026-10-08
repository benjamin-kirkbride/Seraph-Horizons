using System.Text.Json;
using System.Text.Json.Serialization;

namespace SeraphHorizons.Mod.Trading.Maps.Core;

/// <summary>What a map, a lead or a meeting puts on a player's map: its target (<see cref="Key"/>:
/// <c>deposit:&lt;id&gt;</c> for an ore deposit or a gravel field, the camp's standing id
/// <c>camp:x,z</c> for a trader camp, <c>settlement:x,z</c> for settlement ground) and how precisely
/// (<see cref="MapMarks.Rough"/> to <see cref="MapMarks.Exact"/>).</summary>
public readonly record struct MarkTarget(string Key, int Precision);

/// <summary>A waypoint on a player's map as the matching reads it.</summary>
public sealed record WaypointView(string? Guid, double X, double Z, string? Icon, string? Title);

/// <summary>A waypoint the mod made, remembered by its guid: which target it marks and how precisely.</summary>
public sealed class MarkRecord
{
    [JsonPropertyName("guid")] public string Guid { get; set; } = "";
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("precision")] public int Precision { get; set; }
}

/// <summary>Why a map offer is refused to a player who has it already.</summary>
public enum MarkCheck
{
    /// <summary>Theirs to buy.</summary>
    Free,
    /// <summary>Its target is on their map already, as precisely or more.</summary>
    Marked,
    /// <summary>They carry a copy (read or not, or still being checked), as precise or more.</summary>
    Held,
}

/// <summary>
/// Which of a player's waypoints mark what (#455, the playtest after the trade window): the rule
/// behind refusing a map the player has, replacing a rough marker by a better one, and marking a
/// trader camp exactly once its trader is met. Waypoints the mod makes are remembered by guid with
/// their target and precision (<see cref="MarkBook"/>); markers made before that (or by hand) are
/// matched by icon, place and title (<see cref="LegacyMatches"/>).
/// <para>Precision follows the ore maps' tiers (the Standing tab's "ore maps of precision n"): 1
/// within about 400 blocks, 2 within 150, 3 exact. A lead marks a camp's site, not where its trader
/// stands (<see cref="LeadPrecision"/>, within <see cref="LeadReach"/>); meeting the trader marks it
/// exactly.</para>
/// </summary>
public static class MapMarks
{
    public const int Rough = 1, Fair = 2, Exact = 3;

    /// <summary>A lead's precision: the camp's site, short of where its trader stands.</summary>
    public const int LeadPrecision = Fair;

    /// <summary>How far a lead's marker may be from the camp's trader, in blocks.</summary>
    public const int LeadReach = 64;

    /// <summary>How far an old trader marker (made before markers were remembered) may be from the
    /// trader met for it to count as that camp's.</summary>
    public const double LegacyReach = 96;

    public static string DepositKey(string depositId) => "deposit:" + depositId;

    public static string SettlementKey(int x, int z) => $"settlement:{x},{z}";

    /// <summary>The best precision the player's map holds of <paramref name="key"/>, from the
    /// remembered markers that still exist; 0 if none.</summary>
    public static int MarkedPrecision(string key, IEnumerable<MarkRecord> records, IEnumerable<WaypointView> waypoints)
    {
        var live = waypoints.Select(w => w.Guid).Where(g => g != null).ToHashSet();
        return records.Where(r => r.Key == key && live.Contains(r.Guid)).Select(r => r.Precision).DefaultIfEmpty(0).Max();
    }

    /// <summary>Whether an offer of <paramref name="offer"/> is refused: marked as precisely or more,
    /// or a copy as precise or more carried (<paramref name="held"/>: the targets of the player's maps
    /// and leads). A rougher marker or copy does not refuse a better map: it is an upgrade, and
    /// reading it replaces the rougher marker.</summary>
    public static MarkCheck Check(MarkTarget offer, int markedPrecision, IEnumerable<MarkTarget> held)
    {
        if (markedPrecision >= offer.Precision) return MarkCheck.Marked;
        if (held.Any(h => h.Key == offer.Key && h.Precision >= offer.Precision)) return MarkCheck.Held;
        return MarkCheck.Free;
    }

    /// <summary>Marking <paramref name="target"/>: the remembered markers of the same target it
    /// replaces (rougher ones), or null when one as precise or better is there already (nothing to
    /// add).</summary>
    public static List<string>? Replaces(MarkTarget target, IEnumerable<MarkRecord> records, IEnumerable<WaypointView> waypoints)
    {
        var live = waypoints.Select(w => w.Guid).Where(g => g != null).ToHashSet();
        var same = records.Where(r => r.Key == target.Key && live.Contains(r.Guid)).ToList();
        if (same.Any(r => r.Precision >= target.Precision)) return null;
        return same.Select(r => r.Guid).ToList();
    }

    /// <summary>Old markers of a trader camp, not remembered (made before markers were, or by hand
    /// with the same title): the <paramref name="icon"/>, within <paramref name="reach"/> of
    /// (<paramref name="x"/>, <paramref name="z"/>), titled as a lead to it is
    /// (<paramref name="titles"/>, a prefix, so a qualifier after it still matches).</summary>
    public static List<WaypointView> LegacyMatches(IEnumerable<WaypointView> waypoints, IEnumerable<MarkRecord> records, double x, double z,
        double reach, string icon, IReadOnlyCollection<string> titles)
    {
        var known = records.Select(r => r.Guid).ToHashSet();
        return waypoints.Where(w => w.Icon == icon && (w.Guid is null || !known.Contains(w.Guid))
                                    && (w.X - x) * (w.X - x) + (w.Z - z) * (w.Z - z) <= reach * reach
                                    && w.Title is { } t && titles.Any(p => p.Length > 0 && t.StartsWith(p, StringComparison.Ordinal)))
            .ToList();
    }

    /// <summary>How far off a marker of this precision may be, in blocks (0: exact).</summary>
    public static int ReachOf(int precision) => precision switch
    {
        Rough => 400,
        Fair => 150,
        _ => 0,
    };
}

/// <summary>Every player's remembered markers (saved with the world). Markers that no longer exist
/// (deleted on the map) are dropped by <see cref="Prune"/>.</summary>
public sealed class MarkBook
{
    private readonly Dictionary<string, List<MarkRecord>> _byPlayer = new();

    public IReadOnlyList<MarkRecord> Of(string playerUid) => _byPlayer.TryGetValue(playerUid, out var l) ? l : [];

    public void Add(string playerUid, MarkRecord record)
    {
        if (!_byPlayer.TryGetValue(playerUid, out var list)) _byPlayer[playerUid] = list = [];
        list.RemoveAll(r => r.Guid == record.Guid);
        list.Add(record);
    }

    public void Remove(string playerUid, IEnumerable<string> guids)
    {
        if (!_byPlayer.TryGetValue(playerUid, out var list)) return;
        var gone = guids.ToHashSet();
        list.RemoveAll(r => gone.Contains(r.Guid));
    }

    /// <summary>Forgets the player's records whose waypoint is gone.</summary>
    public void Prune(string playerUid, IEnumerable<string?> liveGuids)
    {
        if (!_byPlayer.TryGetValue(playerUid, out var list)) return;
        var live = liveGuids.Where(g => g != null).ToHashSet();
        list.RemoveAll(r => !live.Contains(r.Guid));
    }

    public string ToJson() => JsonSerializer.Serialize(_byPlayer);

    public static MarkBook FromJson(string json)
    {
        var book = new MarkBook();
        foreach (var (uid, list) in JsonSerializer.Deserialize<Dictionary<string, List<MarkRecord>>>(json) ?? new())
            book._byPlayer[uid] = list;
        return book;
    }
}
