using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SeraphHorizons.Mod.PackCheck.Core;

/// <summary>Which side a check runs on. A locked mod is expected on a side its lock entry's
/// <c>side</c> loads on: a dedicated server never loads a client-only mod, and a client never a
/// server-only one.</summary>
public enum CheckSide { Server, Client }

/// <summary>One mod of the pack as <c>pack/lock.json</c> pins it. <paramref name="Side"/> is the
/// lock's <c>universal</c>, <c>server</c> or <c>client</c>.</summary>
public sealed record LockedMod(string Id, string Version, string Side);

/// <summary>The pack this build of the mod was released with: <c>pack/lock.json</c>, embedded at
/// build time. The mod itself is not in the lock: it is released with the pack, at the pack's
/// version (<paramref name="PackVersion"/>).</summary>
public sealed record PackLock(string PackId, string PackVersion, string GameVersion, IReadOnlyList<LockedMod> Mods);

/// <summary>A mod as the mod loader has it on this side.</summary>
public readonly record struct LoadedMod(string Id, string Version);

public enum FindingKind
{
    /// <summary>The game is another version than the pack's.</summary>
    GameVersion,
    /// <summary>This mod (the pack's own) is another version than the pack it was built with.</summary>
    OwnVersion,
    /// <summary>A locked mod is loaded at another version than locked, higher or lower.</summary>
    WrongVersion,
    /// <summary>A locked mod that loads on this side is not loaded.</summary>
    Missing,
    /// <summary>A loaded mod the pack does not have.</summary>
    NotInPack,
}

/// <summary>One way this install differs from the pack. <paramref name="Expected"/> is null for
/// <see cref="FindingKind.NotInPack"/>, <paramref name="Found"/> for <see cref="FindingKind.Missing"/>.
/// <paramref name="Subject"/> is the modid, or <c>game</c> for <see cref="FindingKind.GameVersion"/>.</summary>
public sealed record Finding(FindingKind Kind, string Subject, string? Expected, string? Found)
{
    /// <summary>Plain English, for the log: <c>modid: expected X, found Y</c>.</summary>
    public string Describe() => Kind switch
    {
        FindingKind.GameVersion => $"game: expected {Expected}, found {Found}",
        FindingKind.OwnVersion => $"{Subject} (this mod): expected {Expected} (the pack's version), found {Found}",
        FindingKind.WrongVersion => $"{Subject}: expected {Expected}, found {Found}",
        FindingKind.Missing => $"{Subject}: expected {Expected}, missing",
        FindingKind.NotInPack => $"{Subject} {Found}: not in the pack",
        _ => $"{Subject}: {Kind}",
    };
}

/// <summary>
/// The pack version check (switch <c>PackVersionCheck</c>): what this side's loaded mods and game
/// version differ from the pack this build of the mod was released with. Game-independent; the
/// game-facing side is <c>PackCheck/Game/</c>.
/// </summary>
public static class PackComparison
{
    /// <summary>The game's own mods (VSEssentials is <c>game</c>), never in a lock.</summary>
    public static readonly IReadOnlySet<string> BuiltIn =
        new HashSet<string>(["game", "creative", "survival"], StringComparer.OrdinalIgnoreCase);

    /// <summary>The release meta-mod's modid is the pack id plus this (packtool assemble): it is
    /// part of the pack, and only lists the locked mods as dependencies.</summary>
    public const string MetaModSuffix = "pack";

    /// <summary>Reads <c>pack/lock.json</c>: its <c>pack</c> id, version and game version, and each
    /// mod's id, version and side. Throws <see cref="FormatException"/> on anything else.</summary>
    public static PackLock ParseLock(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var pack = root.GetProperty("pack");
            var mods = new List<LockedMod>();
            foreach (var m in root.GetProperty("mods").EnumerateArray())
                mods.Add(new LockedMod(
                    Required(m, "id"), Required(m, "version"),
                    m.TryGetProperty("side", out var side) ? side.GetString() ?? "universal" : "universal"));
            return new PackLock(Required(pack, "id"), Required(pack, "version"), Required(pack, "game_version"), mods);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new FormatException($"not a pack lock: {e.Message}", e);
        }
    }

    private static string Required(JsonElement e, string name) =>
        e.GetProperty(name).GetString() is { Length: > 0 } s ? s : throw new FormatException($"empty {name}");

    /// <summary>Whether a mod with this lock side loads on <paramref name="side"/>.</summary>
    public static bool LoadsOn(string lockSide, CheckSide side) => lockSide.ToLowerInvariant() switch
    {
        "server" => side == CheckSide.Server,
        "client" => side == CheckSide.Client,
        _ => true,
    };

    /// <summary>
    /// Every way this side differs from <paramref name="pack"/>, sorted (<see cref="Sorted"/>):
    /// the game version; this mod's own version (<paramref name="ownId"/>, which is the pack id, at
    /// <paramref name="ownVersion"/>) against the pack's; each locked mod that loads on this side,
    /// loaded at another version or not at all; and each loaded mod the lock does not have, except
    /// the game's own (<see cref="BuiltIn"/>), the meta-mod and this mod. Mod ids compare without
    /// case; versions exactly, as the lock pins them.
    /// </summary>
    public static List<Finding> Compare(PackLock pack, IEnumerable<LoadedMod> loaded, CheckSide side,
        string gameVersion, string ownId, string ownVersion)
    {
        var findings = new List<Finding>();
        if (gameVersion != pack.GameVersion)
            findings.Add(new Finding(FindingKind.GameVersion, "game", pack.GameVersion, gameVersion));
        if (ownVersion != pack.PackVersion)
            findings.Add(new Finding(FindingKind.OwnVersion, ownId, pack.PackVersion, ownVersion));

        var byId = new Dictionary<string, LoadedMod>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in loaded)
            byId.TryAdd(m.Id, m);
        var locked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in pack.Mods)
        {
            locked.Add(m.Id);
            if (byId.TryGetValue(m.Id, out var have))
            {
                if (have.Version != m.Version)
                    findings.Add(new Finding(FindingKind.WrongVersion, m.Id, m.Version, have.Version));
            }
            else if (LoadsOn(m.Side, side))
            {
                findings.Add(new Finding(FindingKind.Missing, m.Id, m.Version, null));
            }
        }
        foreach (var m in byId.Values)
        {
            if (locked.Contains(m.Id) || BuiltIn.Contains(m.Id)
                || string.Equals(m.Id, ownId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(m.Id, pack.PackId + MetaModSuffix, StringComparison.OrdinalIgnoreCase))
                continue;
            findings.Add(new Finding(FindingKind.NotInPack, m.Id, null, m.Version));
        }
        return Sorted(findings);
    }

    /// <summary>By kind (<see cref="FindingKind"/>'s order), then modid without case, then exactly:
    /// the same findings always come out in the same order.</summary>
    public static List<Finding> Sorted(IEnumerable<Finding> findings) =>
        findings.OrderBy(f => f.Kind)
            .ThenBy(f => f.Subject, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Subject, StringComparer.Ordinal)
            .ThenBy(f => f.Expected, StringComparer.Ordinal)
            .ThenBy(f => f.Found, StringComparer.Ordinal)
            .ToList();

    /// <summary>A stable hash of the findings, whatever order they come in: what a client stores
    /// when the player dismisses the dialog until something changes. Empty for no findings.</summary>
    public static string Fingerprint(IEnumerable<Finding> findings)
    {
        var lines = Sorted(findings)
            .Select(f => $"{f.Kind}\u001f{f.Subject.ToLowerInvariant()}\u001f{f.Expected}\u001f{f.Found}")
            .ToList();
        if (lines.Count == 0) return "";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines)));
        return Convert.ToHexStringLower(hash);
    }
}
