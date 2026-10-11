using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.Ore.Processing;

/// <summary>
/// Steelmaking Expanded's (<c>smex</c> 0.10.1) blast furnace burden in ore processing's units (#688).
/// smex's bell hopper counts a batch of burden in "ore units": an iron nugget is worth
/// <c>HopperIronOreRequired</c> (12) of them, a piece of crushed iron ore <c>HopperNuggetRequired</c>
/// (12), a batch is their product (144), and a batch melts to <c>BfIronPerMeltCycle</c> (102) units
/// of iron, so smex's crushed iron and a nugget are each worth 8.5 units. With ore processing a nugget
/// and a concentrate hold 5 units and crushed ore 2.5 (its half), so the live settings become 40, 20
/// and 100 (a batch is 20 nuggets or concentrates, or 40 crushed, for 100 units), and the furnace
/// takes our iron ore forms in smex's own lists (<c>IronOreCompat</c>): the iron ores' concentrate and
/// pyrite's roasted concentrate as nuggets, their crushed ore as crushed. smex's own crushed iron
/// stays in its list, now at 2.5. The settings are set live, not written to smex's file (as
/// <see cref="SteelBits.SmexScrap"/> does); exlib sends the server's live settings to clients.
/// smex is not referenced: its members are found by name, and if one is missing nothing is done.
/// </summary>
public static class SmexBurden
{
    public const string ModId = "smex";
    public const string ValuesType = "SteelmakingExpanded.SmexValues";
    public const string CompatType = "SteelmakingExpanded.Compat.IronOreCompat";

    /// <summary>The live settings ore processing gives smex's burden.</summary>
    public static readonly IReadOnlyDictionary<string, object> Settings = new Dictionary<string, object>
    {
        ["HopperIronOreRequired"] = 40,
        ["HopperNuggetRequired"] = 20,
        ["BfIronPerMeltCycle"] = 100f,
    };

    public enum Status { Applied, Changed }

    /// <summary>The iron ores' sulfide (pyrite), whose concentrate counts only once roasted.</summary>
    public static readonly IReadOnlySet<string> IronSulfides = new HashSet<string> { "pyrite" };

    /// <summary>The item paths smex counts as iron nuggets (5 units): the iron ores' concentrate, a
    /// sulfide's roasted.</summary>
    public static IEnumerable<string> NuggetPaths =>
        OreProducts.Ores.Where(OreProcessingSystem.IsIron)
            .Select(ore => IronSulfides.Contains(ore) ? "roastedconcentrate-" + ore : "concentrate-" + ore);

    /// <summary>The item paths smex counts as crushed iron ore (2.5 units).</summary>
    public static IEnumerable<string> CrushedPaths =>
        OreProducts.Ores.Where(OreProcessingSystem.IsIron).SelectMany(ore => new[] { $"crushed-{ore}-coarse", $"crushed-{ore}-fine" });

    private static FieldInfo? Set(string name) => AccessTools.TypeByName(CompatType) is { } t ? AccessTools.Field(t, name) : null;

    public static Status Apply(IWorldAccessor world, ILogger logger)
    {
        var values = AccessTools.TypeByName(ValuesType);
        var store = values == null ? null : AccessTools.Field(values, "_store")?.GetValue(null);
        var config = store?.GetType().GetProperty("Config", BindingFlags.Public | BindingFlags.Instance)?.GetValue(store);
        var props = Settings.Keys.Select(k => config?.GetType().GetProperty(k, BindingFlags.Public | BindingFlags.Instance)).ToList();
        if (config == null || props.Any(p => p == null || !p.CanWrite) || Set("IronNuggetPaths") == null || Set("CrushedIronOrePaths") == null)
        {
            logger.Warning("[seraphhorizons] Ore processing: Steelmaking Expanded's {0} or {1} is not as expected; smex changed, "
                           + "so its blast furnace counts its burden as it does itself and takes none of ore processing's iron ore", ValuesType, CompatType);
            return Status.Changed;
        }
        foreach (var (key, prop) in Settings.Keys.Zip(props))
            prop!.SetValue(config, Convert.ChangeType(Settings[key], prop.PropertyType));
        AddPaths();
        logger.Notification("[seraphhorizons] Ore processing: Steelmaking Expanded's blast furnace burden counts 5 units a nugget or "
                            + "concentrate and 2.5 a crushed ore (live settings, the file unchanged)");
        return Status.Applied;
    }

    /// <summary>Adds our iron ore forms to smex's lists (they are cleared and refilled whenever smex
    /// starts, so a postfix on its <c>Init</c> adds them again).</summary>
    public static void AddPaths()
    {
        (Set("IronNuggetPaths")?.GetValue(null) as HashSet<string>)?.UnionWith(NuggetPaths);
        (Set("CrushedIronOrePaths")?.GetValue(null) as HashSet<string>)?.UnionWith(CrushedPaths);
    }

    public static void Patch(Harmony harmony, ILogger logger)
    {
        var init = AccessTools.TypeByName(CompatType) is { } t ? AccessTools.Method(t, "Init", [typeof(ICoreAPI)]) : null;
        if (init == null)
        {
            logger.Warning("[seraphhorizons] Ore processing: Steelmaking Expanded's {0}.Init is gone; a client started later in this "
                           + "process may clear its lists of ore processing's iron ore", CompatType);
            return;
        }
        harmony.Patch(init, postfix: new HarmonyMethod(typeof(SmexBurden), nameof(InitPostfix)));
    }

    private static void InitPostfix() => AddPaths();
}
