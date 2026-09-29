using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Items;

/// <summary>
/// Knows which mod an asset, and so an item or block, came from.
///
/// A collectible does not remember the file that defined it, so the index reads every
/// item and block type file, keys it by "domain:code" and maps the file's origin back to a
/// mod. A collectible belongs to the type whose code is the longest dash-separated prefix of
/// its own path ("ingot-copper" to "ingot"). Collectibles no type file explains (registered
/// from code) fall back to the mod owning their domain.
/// </summary>
internal sealed class ModIndex
{
    private readonly List<Mod> _mods;
    private readonly Dictionary<string, string> _originToMod = new();
    private readonly Dictionary<string, string> _typeToMod = new();
    private readonly Dictionary<string, string> _domainToMod = new();
    private readonly Dictionary<string, SortedSet<string>> _typeDomainsByMod = new();

    public ModIndex(ICoreServerAPI api)
    {
        _mods = api.ModLoader.Mods.OrderBy(m => m.Info.ModID, StringComparer.Ordinal).ToList();
        var modIds = _mods.Select(m => m.Info.ModID).ToHashSet();

        var typeAssets = api.Assets.GetMany("itemtypes/").Concat(api.Assets.GetMany("blocktypes/"))
            .OrderBy(a => a.Location.ToString(), StringComparer.Ordinal);
        var domainVotes = new Dictionary<string, Dictionary<string, int>>();
        foreach (var asset in typeAssets)
        {
            var mod = ModForAsset(asset);
            if (mod == null) continue;
            string? code;
            try { code = asset.ToObject<JObject>()?["code"]?.Value<string>(); }
            catch (Exception) { continue; }
            if (string.IsNullOrEmpty(code)) continue;

            var domain = asset.Location.Domain;
            var key = domain + ":" + code.ToLowerInvariant();
            _typeToMod.TryAdd(key, mod);
            if (!domainVotes.TryGetValue(domain, out var votes)) domainVotes[domain] = votes = new();
            votes[mod] = votes.GetValueOrDefault(mod) + 1;
            if (domain != mod && !modIds.Contains(domain))
            {
                if (!_typeDomainsByMod.TryGetValue(mod, out var set)) _typeDomainsByMod[mod] = set = new(StringComparer.Ordinal);
                set.Add(domain);
            }
        }

        foreach (var (domain, votes) in domainVotes)
        {
            _domainToMod[domain] = modIds.Contains(domain)
                ? domain
                : votes.OrderByDescending(v => v.Value).ThenBy(v => v.Key, StringComparer.Ordinal).First().Key;
        }
    }

    public IReadOnlyList<Mod> Mods => _mods;

    public IEnumerable<string> ExtraDomains(string modId) =>
        _typeDomainsByMod.TryGetValue(modId, out var set) ? set : Enumerable.Empty<string>();

    public bool Has(string modId) => _mods.Any(m => m.Info.ModID == modId);

    /// <summary>The mod that defined the collectible; never null, falls back to "game".</summary>
    public string ModForCollectible(CollectibleObject c)
    {
        var domain = c.Code.Domain;
        var path = c.Code.Path;
        while (true)
        {
            if (_typeToMod.TryGetValue(domain + ":" + path, out var mod)) return mod;
            var dash = path.LastIndexOf('-');
            if (dash <= 0) break;
            path = path[..dash];
        }
        return ModForDomain(domain);
    }

    public string ModForDomain(string domain)
    {
        if (_domainToMod.TryGetValue(domain, out var mod)) return mod;
        if (Has(domain)) return domain;
        return "game";
    }

    /// <summary>
    /// The mod whose files hold the asset. Mod zips are either read in place (the origin is
    /// the zip) or unpacked to a cache folder named after the zip, so both are matched. The
    /// base game's asset folders (assets/game, assets/survival, ...) are named after their mod.
    /// </summary>
    public string? ModForAsset(IAsset asset)
    {
        var origin = asset.Origin?.OriginPath;
        if (origin == null) return ModForDomain(asset.Location.Domain);
        if (_originToMod.TryGetValue(origin, out var cached)) return cached;

        string? found = null;
        var normalized = origin.Replace('\\', '/').TrimEnd('/');
        var segments = normalized.Split('/');
        foreach (var mod in _mods)
        {
            if (string.IsNullOrEmpty(mod.SourcePath) || string.IsNullOrEmpty(mod.FileName)) continue;
            if (mod.SourceType is EnumModSourceType.CS or EnumModSourceType.DLL) continue;
            var source = mod.SourcePath.Replace('\\', '/').TrimEnd('/');
            if (normalized == source || normalized.StartsWith(source + "/", StringComparison.Ordinal) ||
                segments.Any(s => s == mod.FileName || s.StartsWith(mod.FileName + "_", StringComparison.Ordinal)))
            {
                found = mod.Info.ModID;
                break;
            }
        }
        if (found == null && segments.Length > 0 && Has(segments[^1])) found = segments[^1];
        found ??= ModForDomain(asset.Location.Domain);
        _originToMod[origin] = found;
        return found;
    }
}
