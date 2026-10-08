using System.Text.Json;
using System.Text.Json.Serialization;

namespace SeraphHorizons.Mod.Trading.Maps.Core;

/// <summary>One group's lead history: maps bought per trader (never decays), the traders that sold
/// it its stranger's map, and the camps it has visited (met the trader at).</summary>
public sealed class GroupLeads
{
    [JsonPropertyName("bought")] public Dictionary<string, int> Bought { get; set; } = new();
    [JsonPropertyName("strangerMaps")] public HashSet<string> StrangerMaps { get; set; } = new();
    [JsonPropertyName("visited")] public HashSet<string> Visited { get; set; } = new();
}

/// <summary>
/// Every group's camp lead history (saved with the world as <c>seraphhorizons:leads</c>, versioned).
/// A group is a player's company (<c>company:&lt;group uid&gt;</c>) or the player alone
/// (<c>player:&lt;uid&gt;</c>). As standing pools by company, every write goes to the player's own
/// key and their company's, and a read takes the most of them: the higher count, a stranger's map
/// had by either, the camps visited by either. So leaving a company keeps what the player did, and
/// joining one does not wipe the slate.
/// </summary>
public sealed class LeadBook
{
    public const int Version = 1;

    [JsonPropertyName("version")] public int SavedVersion { get; set; } = Version;
    [JsonPropertyName("groups")] public Dictionary<string, GroupLeads> Groups { get; set; } = new();

    public static string PlayerKey(string uid) => "player:" + uid;
    public static string CompanyKey(int group) => "company:" + group;

    /// <summary>The keys a player reads and writes: their own, and their company's if they have one.</summary>
    public static List<string> KeysOf(string uid, int? company) =>
        company is int c ? [PlayerKey(uid), CompanyKey(c)] : [PlayerKey(uid)];

    private GroupLeads? Get(string key) => Groups.GetValueOrDefault(key);

    private GroupLeads GetOrAdd(string key)
    {
        if (!Groups.TryGetValue(key, out var g)) Groups[key] = g = new GroupLeads();
        return g;
    }

    /// <summary>Maps bought from <paramref name="trader"/> (the most of the keys').</summary>
    public int Bought(IEnumerable<string> keys, string trader) =>
        keys.Select(k => Get(k)?.Bought.GetValueOrDefault(trader) ?? 0).DefaultIfEmpty(0).Max();

    public bool StrangerUsed(IEnumerable<string> keys, string trader) => keys.Any(k => Get(k)?.StrangerMaps.Contains(trader) == true);

    /// <summary>The camps (standing ids, <c>camp:x,z</c>) any of the keys visited.</summary>
    public HashSet<string> Visited(IEnumerable<string> keys) =>
        keys.SelectMany(k => Get(k)?.Visited ?? []).ToHashSet();

    /// <summary>A map bought from <paramref name="trader"/>: the count goes up to one past the most
    /// of the keys' (so they agree after), and a stranger's map is noted.</summary>
    public void RecordBought(IReadOnlyCollection<string> keys, string trader, bool asStranger)
    {
        int next = Bought(keys, trader) + 1;
        foreach (string k in keys)
        {
            var g = GetOrAdd(k);
            g.Bought[trader] = next;
            if (asStranger) g.StrangerMaps.Add(trader);
        }
    }

    /// <summary>The group met the trader of camp <paramref name="camp"/>; whether that is new.</summary>
    public bool RecordVisit(IEnumerable<string> keys, string camp)
    {
        bool added = false;
        foreach (string k in keys) added |= GetOrAdd(k).Visited.Add(camp);
        return added;
    }

    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>Reads a saved book. A newer version than this code knows starts empty (with the reason
    /// for the log), as does nothing saved.</summary>
    public static LeadBook FromJson(string? json, out string? problem)
    {
        problem = null;
        if (string.IsNullOrEmpty(json)) return new LeadBook();
        var book = JsonSerializer.Deserialize<LeadBook>(json) ?? new LeadBook();
        if (book.SavedVersion > Version)
        {
            problem = $"saved as version {book.SavedVersion}, newer than {Version}";
            return new LeadBook();
        }
        book.SavedVersion = Version;
        return book;
    }
}
