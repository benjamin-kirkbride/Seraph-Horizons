using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Items;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.ServerMods.NoObf;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>
/// One entry of a collectible's TransitionableProps: the stack turns into another after a
/// number of in-game hours in an inventory, on the ground or in a container (wet sinew cures
/// into dry sinew, raw bowstaves dry, food rots). Not a recipe registry: the engine checks
/// the collectible's own list whenever it updates a stack (CollectibleObject.
/// UpdateAndGetTransitionStatesNative), so the list on the registered collectible, with
/// ByType resolved and patches applied, is what the game uses.
/// </summary>
public sealed record Transition(CollectibleObject From, int Index, TransitionableProperties Props, ItemStack Output);

public static class Transitions
{
    /// <summary>The record type of each kind of transition, with its English name.</summary>
    public static readonly IReadOnlyDictionary<EnumTransitionType, (string Code, string Name)> Types =
        new Dictionary<EnumTransitionType, (string, string)>
        {
            [EnumTransitionType.Perish] = ("perishing", "Perishing"),
            [EnumTransitionType.Dry] = ("drying", "Drying"),
            [EnumTransitionType.Cure] = ("curing", "Curing"),
            [EnumTransitionType.Ripen] = ("ripening", "Ripening"),
            [EnumTransitionType.Melt] = ("melting", "Melting"),
            [EnumTransitionType.Harden] = ("hardening", "Hardening"),
            [EnumTransitionType.Burn] = ("burning", "Burning"),
            [EnumTransitionType.Convert] = ("converting", "Converting"),
        };

    /// <summary>
    /// Every transition of every registered collectible, by code and then list position.
    /// Entries of type None (the engine's placeholder for cooked outputs without one) are left
    /// out. So are entries whose transitioned stack did not resolve, which the engine cannot
    /// carry out either; they are counted in <paramref name="skipped"/>.
    /// </summary>
    public static List<Transition> Find(ICoreServerAPI api, out int skipped)
    {
        skipped = 0;
        var found = new List<Transition>();
        var all = api.World.Collectibles
            .Where(c => c?.Code != null && !c.IsMissing && Json.IsValidCode(c.Code.ToString()))
            .OrderBy(c => c.Code.ToString(), StringComparer.Ordinal);
        foreach (var c in all)
        {
            var props = c.TransitionableProps ?? [];
            for (int i = 0; i < props.Length; i++)
            {
                var p = props[i];
                if (p == null || !Types.ContainsKey(p.Type)) continue;
                var output = p.TransitionedStack?.ResolvedItemstack;
                if (output?.Collectible?.Code == null || output.Collectible.IsMissing ||
                    !Json.IsValidCode(output.Collectible.Code.ToString()))
                {
                    skipped++;
                    continue;
                }
                found.Add(new Transition(c, i, p, output));
            }
        }
        return found;
    }
}

/// <summary>
/// Which file a transition comes from: the JSON patch that wrote it into the item's type
/// file, or else the type file itself. A collectible does not remember either, so this reads
/// the patch files the way the engine's ModJsonPatchLoader selects them (enabled, for the
/// server side, world-config condition met, every dependsOn mod present or absent as asked)
/// and, for each item type file, keeps the patches aimed at it, in load order.
///
/// A patch is the transition's origin when its value holds an entry of the same kind whose
/// transitionedStack code is the transition's output (domain-less codes take the type
/// file's domain, `{placeholders}` match any value), the last such patch winning, as the
/// last one applied is in effect. "move" and "copy" operations carry no value and are never
/// origins. A patch that rewrites a whole list is the origin of every entry it copies along.
/// </summary>
internal sealed class TransitionOrigins
{
    private readonly ModIndex _mods;
    private readonly Dictionary<string, List<(int Order, IAsset Asset, JsonPatch Patch)>> _byFile = new(StringComparer.Ordinal);
    private readonly List<(int Order, IAsset Asset, JsonPatch Patch)> _wildcard = new();

    public TransitionOrigins(ICoreServerAPI api, ModIndex mods)
    {
        _mods = mods;
        var loaded = api.ModLoader.Mods.Select(m => m.Info.ModID).ToHashSet();
        var config = api.World.Config;
        var order = 0;
        foreach (var asset in api.Assets.GetMany("patches/", null, true))
        {
            JsonPatch[]? patches;
            try { patches = asset.ToObject<JsonPatch[]>(); }
            catch (Exception) { continue; }
            foreach (var p in patches ?? [])
            {
                if (p?.File == null || !p.Enabled || p.Value == null) continue;
                var side = p.Side ?? p.File.Category?.SideType ?? EnumAppSide.Universal;
                if (side != EnumAppSide.Universal && side != EnumAppSide.Server) continue;
                if (p.Condition != null)
                {
                    var value = config?[p.Condition.When];
                    if (value == null || p.Condition.useValue) continue;
                    if (!p.Condition.IsValue.Equals(value.GetValue()?.ToString() ?? "", StringComparison.InvariantCultureIgnoreCase)) continue;
                }
                if (p.DependsOn != null && !p.DependsOn.All(d => loaded.Contains(d.modid) ^ d.invert)) continue;
                if (p.File.Path.EndsWith('*')) _wildcard.Add((order++, asset, p));
                else
                {
                    var key = p.File.ToString().ToLowerInvariant();
                    if (!_byFile.TryGetValue(key, out var list)) _byFile[key] = list = new();
                    list.Add((order++, asset, p));
                }
            }
        }
    }

    /// <summary>The asset the transition is defined in and the mod whose files hold it.</summary>
    public (AssetLocation? Source, string Mod) Of(Transition t)
    {
        var file = _mods.TypeAsset(t.From);
        if (file == null) return (null, _mods.ModForCollectible(t.From));
        var candidates = (_byFile.GetValueOrDefault(file.ToString().ToLowerInvariant()) ?? new())
            .Concat(_wildcard.Where(w => w.Patch.File.Domain == file.Domain &&
                                         file.Path.StartsWith(w.Patch.File.Path.ToLowerInvariant().TrimEnd('*'), StringComparison.Ordinal)));
        IAsset? origin = null;
        foreach (var (_, asset, patch) in candidates.OrderBy(c => c.Order))
            if (Holds(patch, t, file.Domain)) origin = asset;
        return origin == null
            ? (file, _mods.ModForCollectible(t.From))
            : (origin.Location, _mods.ModForAsset(origin) ?? _mods.ModForCollectible(t.From));
    }

    /// <summary>
    /// Whether the patch writes an entry equal to the transition: same kind, output, fresh and
    /// transition hours and ratio (an absent field counts as the engine's default), under a
    /// ByType key that matches the collectible's code, or under plain transitionableProps.
    /// </summary>
    private static bool Holds(JsonPatch patch, Transition t, string domain)
    {
        if (patch.Value?.Token is not { } value) return false;
        var path = (patch.Path ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Replace("~1", "/").Replace("~0", "~")).ToList();
        return Entries(value, path).Any(e => Same(e.Entry, e.Path, t, domain));
    }

    /// <summary>Every object in the value, with the property path that leads to it from the file's root.</summary>
    private static IEnumerable<(JObject Entry, List<string> Path)> Entries(JToken token, List<string> path)
    {
        if (token is JObject o)
        {
            yield return (o, path);
            foreach (var p in o.Properties())
            foreach (var e in Entries(p.Value, path.Append(p.Name).ToList()))
                yield return e;
        }
        else if (token is JArray a)
        {
            for (int i = 0; i < a.Count; i++)
                foreach (var e in Entries(a[i], path.Append(i.ToString()).ToList()))
                    yield return e;
        }
    }

    private static bool Same(JObject entry, List<string> path, Transition t, string domain)
    {
        JToken? Get(string name) => entry.Properties().FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
        if (Get("type") is not JValue { Type: JTokenType.String } type || Get("transitionedStack") is not JObject stack) return false;
        if (!((string)type!).Equals(t.Props.Type.ToString(), StringComparison.OrdinalIgnoreCase)) return false;

        // Under transitionablePropsByType/<key> the key must match the collectible; under
        // transitionableProps the entry applies to every variant.
        var byType = path.FindIndex(p => p.Equals("transitionablePropsByType", StringComparison.OrdinalIgnoreCase));
        if (byType >= 0 && (byType + 1 >= path.Count || !KeyMatches(path[byType + 1], t.From.Code))) return false;
        if (byType < 0 && !path.Any(p => p.Equals("transitionableProps", StringComparison.OrdinalIgnoreCase))) return false;

        // TransitionableProperties' defaults: 36 fresh hours, 12 transition hours, ratio 1.
        static float Avg(JToken? natFloat, float fallback) =>
            natFloat is JObject o && o.Properties().FirstOrDefault(p => p.Name.Equals("avg", StringComparison.OrdinalIgnoreCase))?.Value is JValue v
                ? v.Value<float>() : fallback;
        if (Avg(Get("freshHours"), 36) != t.Props.FreshHours.avg) return false;
        if (Avg(Get("transitionHours"), 12) != t.Props.TransitionHours.avg) return false;
        if ((Get("transitionRatio") is JValue r ? r.Value<float>() : 1f) != t.Props.TransitionRatio) return false;

        var codes = new List<string>();
        foreach (var p in stack.Properties())
        {
            if (p.Name.Equals("code", StringComparison.OrdinalIgnoreCase) && p.Value.Type == JTokenType.String) codes.Add((string)p.Value!);
            if (p.Name.Equals("codeByType", StringComparison.OrdinalIgnoreCase) && p.Value is JObject byTypeCodes)
                codes.AddRange(byTypeCodes.Properties().Select(b => b.Value).Where(v => v.Type == JTokenType.String).Select(v => (string)v!));
        }
        var output = t.Output.Collectible.Code.ToString();
        return codes.Any(c => Matches(c, domain, output));
    }

    /// <summary>A ByType key against a code, with the engine's own matcher, on the path or the full code.</summary>
    private static bool KeyMatches(string key, AssetLocation code)
    {
        try { return WildcardUtil.Match(key, code.Path) || WildcardUtil.Match(key, code.ToString()); }
        catch (ArgumentException) { return false; }
    }

    private static bool Matches(string code, string domain, string output)
    {
        var full = code.Contains(':') ? code : domain + ":" + code;
        var pattern = "^" + Regex.Replace(Regex.Escape(full.ToLowerInvariant()), @"\\\{[^}]*}", ".+") + "$";
        return Regex.IsMatch(output, pattern);
    }
}
