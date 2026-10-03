using ProtoBuf;
using SeraphHorizons.Mod.CreativeModTabs.Core;

namespace SeraphHorizons.Mod.CreativeModTabs;

/// <summary>
/// The server's mod tab list, sent to each client when it is ready to play. The server is the only
/// authority on which domain belongs to which tab: the two sides load different mods (server-only and
/// client-only ones), so they could attribute a domain differently, and a tab must hold the same slots on
/// both (the server resolves a click by tab index and slot id).
/// </summary>
[ProtoContract]
public sealed class ModTabsPacket
{
    public const int CurrentVersion = 1;

    [ProtoMember(1)] public int Version { get; set; } = CurrentVersion;
    /// <summary>How many default tabs the server has: the mod tabs' indices start there.</summary>
    [ProtoMember(2)] public int DefaultTabCount { get; set; }
    [ProtoMember(3)] public List<ModTabEntry> Tabs { get; set; } = [];

    public static ModTabsPacket From(int defaultTabCount, IEnumerable<ModTabSpec> tabs) => new()
    {
        DefaultTabCount = defaultTabCount,
        Tabs = tabs.Select(t => new ModTabEntry
        {
            Code = t.Code, Name = t.Name, IsGame = t.IsGame, Domains = [.. t.Domains], Count = t.Count, Hash = t.Hash,
        }).ToList(),
    };

    public List<ModTabSpec> Specs() =>
        (Tabs ?? []).Select(t => new ModTabSpec(t.Code ?? "", t.Name ?? "", t.IsGame, t.Domains ?? [], t.Count, t.Hash)).ToList();
}

[ProtoContract]
public sealed class ModTabEntry
{
    [ProtoMember(1)] public string? Code { get; set; }
    [ProtoMember(2)] public string? Name { get; set; }
    [ProtoMember(3)] public bool IsGame { get; set; }
    [ProtoMember(4)] public List<string>? Domains { get; set; }
    [ProtoMember(5)] public int Count { get; set; }
    [ProtoMember(6)] public uint Hash { get; set; }
}
