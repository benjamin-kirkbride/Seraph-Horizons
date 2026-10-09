using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.SteelBits;

/// <summary>
/// Steelmaking Expanded (<c>smex</c>, 0.10.1): its Bessemer converter takes as cold scrap the item
/// codes in its <c>BessemerScrapCodes</c> setting (<c>ModConfig/ex_values.json</c>, section
/// <c>smex</c>), each worth <c>MoltenUnitsPerBit</c> (5) units of the steel it makes, a steel ingot
/// being 100: 20 bits an ingot, the bit's own ratio, and no loss. Its default already lists
/// <c>game:metalbit-steel</c>; a file that does not gets it added to the live setting, read through
/// <c>SmexValues._store.Config</c> (exlib's <c>ExConfigRegister</c>), and not saved, so the file stays
/// the admin's. exlib sends the server's live settings to each client that joins. smex is not
/// referenced at build time: its members are found by name, and if one is missing nothing is done,
/// with a warning.
/// </summary>
public static class SmexScrap
{
    public const string ValuesType = "SteelmakingExpanded.SmexValues";
    public const string Setting = "BessemerScrapCodes";

    public enum Status
    {
        /// <summary>Not looked at yet.</summary>
        NotChecked,
        /// <summary>The switch is off: smex's setting is left as it is.</summary>
        Off,
        /// <summary>smex is not installed.</summary>
        Absent,
        /// <summary>smex's members are not as expected; nothing was done.</summary>
        Changed,
        /// <summary>The setting already lists the steel bit.</summary>
        Listed,
        /// <summary>The setting did not list it, and now does.</summary>
        Added,
    }

    /// <summary>smex's live <see cref="Setting"/>, or null when it cannot be read.</summary>
    public static string? Codes() =>
        AccessTools.TypeByName(ValuesType)?.GetProperty(Setting, BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string;

    /// <summary>Makes sure the live setting lists the steel bit.</summary>
    public static Status Ensure(ILogger logger) => EnsureListed(logger, SteelBitsRules.SteelBit, "Steel bits recovery", "steel bits");

    /// <summary>Makes sure the live setting lists <paramref name="code"/>, logging as
    /// <paramref name="feature"/>; <paramref name="what"/> names the item in the warning.</summary>
    public static Status EnsureListed(ILogger logger, string code, string feature, string what)
    {
        var values = AccessTools.TypeByName(ValuesType);
        var codes = values?.GetProperty(Setting, BindingFlags.Public | BindingFlags.Static);
        var store = values == null ? null : AccessTools.Field(values, "_store")?.GetValue(null);
        var config = store?.GetType().GetProperty("Config", BindingFlags.Public | BindingFlags.Instance)?.GetValue(store);
        var setting = config?.GetType().GetProperty(Setting, BindingFlags.Public | BindingFlags.Instance);
        if (codes?.PropertyType != typeof(string) || setting?.PropertyType != typeof(string) || !setting.CanWrite)
        {
            logger.Warning("[seraphhorizons] {0}: Steelmaking Expanded's {1}.{2} or its config store is not as expected; "
                           + "smex changed, so whether its Bessemer converter takes {3} is up to its own setting", feature, ValuesType, Setting, what);
            return Status.Changed;
        }
        var current = (string?)codes.GetValue(null);
        var added = SteelBitsRules.With(current, code);
        if (added == null)
            return Status.Listed;
        setting.SetValue(config, added);
        logger.Notification("[seraphhorizons] {0}: Steelmaking Expanded's {1} did not list {2}; added for this run "
                            + "(the file is unchanged)", feature, Setting, code);
        return Status.Added;
    }
}
