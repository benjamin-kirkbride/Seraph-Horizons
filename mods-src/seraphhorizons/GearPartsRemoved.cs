using System.Collections;
using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod;

/// <summary>
/// BetterLoot+ (<c>betterlootplus</c>, domain <c>betterloot</c>) adds a rusty gear part
/// (<c>betterloot:gearpart</c>) to drifters', shivers' and bowtorns' loot: four craft into a rusty
/// gear and a rusty gear splits into four. The pack's rusty gear is a whole corroded stainless gear
/// and its money (#484), so the part goes:
/// <list type="bullet">
/// <item>the item type and both grid recipes are disabled (<c>enabled: false</c>) by a JSON patch,
/// <c>patches/gearparts-betterlootplus.json</c>; parts already in a world vanish when it loads;</item>
/// <item>every gear part drop in BetterLoot+'s loot config becomes rusty gears at a quarter of its
/// average and variance, added to the creature's own rusty gear drop (or, without one, in its
/// place; <see cref="GearPartDropRules"/>), so the same rusty gears come in.</item>
/// </list>
///
/// BetterLoot+ reads its loot from <c>ModConfig/betterlootplus.json</c> (written from its bundled
/// <c>config/betterlootplus.default.json</c> when missing) in its system's <c>AssetsLoaded</c>
/// (ExecuteOrder 0.25, server only), after the item types are registered (0.2), and writes each
/// creature's drops into its harvestable behaviour in a private static
/// <c>ApplyConfig(ICoreAPI, BetterLootConfig)</c>. A Harmony prefix on that method rewrites the
/// loaded config's drops in memory before they are applied, so the file stays as BetterLoot+ wrote
/// it (a player's own edits too) and the switch can be turned off again. BetterLoot+ is not
/// referenced at build time: its types are found by name, and if they changed the tweak logs a
/// warning, and its gear part drops are skipped by BetterLoot+ itself as an unknown item.
///
/// With the switch off, <see cref="DisablePatches"/> empties the patch file in <c>Start</c>, before
/// the game's patch loader runs in <c>AssetsLoaded</c>, and the loot is not touched.
/// </summary>
public static class GearPartsRemoved
{
    public const string ModId = "betterlootplus";
    public const string HarmonyId = "seraphhorizons.gearparts";
    public const string SystemType = "BetterLootPlus.BetterLootPlusModSystem";
    public const string ConfigType = "BetterLootPlus.BetterLootConfig";
    public const string CreatureType = "BetterLootPlus.CreatureLootConfig";
    public const string DropType = "BetterLootPlus.LootDropConfig";

    public static readonly AssetLocation PatchAsset = new("seraphhorizons", "patches/gearparts-betterlootplus.json");

    private static PropertyInfo? _creatures, _drops, _code, _avg, _var;
    private static ILogger? _logger;

    public static bool Applies(ICoreAPI api) => api.ModLoader.IsModEnabled(ModId);

    /// <summary>Empties the patch file, so the patch loader applies none of it. Runs in Start: the
    /// assets are there, and the patches are applied in AssetsLoaded.</summary>
    public static void DisablePatches(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(PatchAsset);
        if (asset != null)
            asset.Data = "[]"u8.ToArray();
    }

    /// <summary>Prefixes BetterLoot+'s <c>ApplyConfig</c>. Returns whether the patch went in.</summary>
    public static bool Patch(Harmony harmony, ILogger logger)
    {
        var system = AccessTools.TypeByName(SystemType);
        var config = AccessTools.TypeByName(ConfigType);
        var creature = AccessTools.TypeByName(CreatureType);
        var drop = AccessTools.TypeByName(DropType);
        var apply = system == null || config == null
            ? null
            : AccessTools.DeclaredMethod(system, "ApplyConfig", [typeof(ICoreAPI), config]);
        _creatures = config == null ? null : AccessTools.DeclaredProperty(config, "Creatures");
        _drops = creature == null ? null : AccessTools.DeclaredProperty(creature, "Drops");
        _code = drop == null ? null : AccessTools.DeclaredProperty(drop, "Code");
        _avg = drop == null ? null : AccessTools.DeclaredProperty(drop, "Avg");
        _var = drop == null ? null : AccessTools.DeclaredProperty(drop, "Var");
        if (apply == null || !apply.IsStatic
            || _creatures == null || !typeof(IDictionary).IsAssignableFrom(_creatures.PropertyType)
            || _drops == null || !typeof(IList).IsAssignableFrom(_drops.PropertyType)
            || _code?.PropertyType != typeof(string) || _avg?.PropertyType != typeof(double)
            || _var?.PropertyType != typeof(double) || _code.SetMethod == null || _avg.SetMethod == null
            || _var.SetMethod == null)
        {
            logger.Warning($"[seraphhorizons] {SystemType} does not have ApplyConfig(ICoreAPI, BetterLootConfig) and the "
                           + "config shape expected; BetterLoot+ changed, so its gear part drops are left as they are "
                           + "(and, the item being gone, it skips them)");
            Unbind();
            return false;
        }
        _logger = logger;
        harmony.Patch(apply, prefix: new HarmonyMethod(typeof(GearPartsRemoved), nameof(ApplyConfigPrefix)));
        return true;
    }

    public static void Unbind() => _creatures = _drops = _code = _avg = _var = null;

    /// <summary>Turns every gear part drop of the config BetterLoot+ is about to apply into a rusty
    /// gear drop. Arguments by position, so a renamed parameter does not break the patch.</summary>
    public static void ApplyConfigPrefix(object? __1)
    {
        if (__1 == null || _drops == null || _code == null || _avg == null || _var == null
            || _creatures?.GetValue(__1) is not IDictionary creatures)
            return;
        int changed = 0;
        foreach (var creature in creatures.Values)
        {
            if (creature == null || _drops.GetValue(creature) is not IList drops)
                continue;
            var read = new List<GearPartDropRules.Drop>(drops.Count);
            foreach (var drop in drops)
                read.Add(drop == null
                    ? new GearPartDropRules.Drop(null, 0, 0)
                    : new((string?)_code.GetValue(drop), (double)_avg.GetValue(drop)!, (double)_var.GetValue(drop)!));
            var (edits, removed) = GearPartDropRules.Plan(read);
            if (edits.Count == 0)
                continue;
            foreach (var edit in edits)
            {
                var drop = drops[edit.Index]!;
                _code.SetValue(drop, edit.Code);
                _avg.SetValue(drop, edit.Avg);
                _var.SetValue(drop, edit.Var);
            }
            foreach (int index in removed)
                drops.RemoveAt(index);
            changed++;
        }
        _logger?.Notification($"[seraphhorizons] Gear parts removed: the gear parts of {changed} BetterLoot+ creatures drop as rusty gears at a quarter of the rate");
    }
}
