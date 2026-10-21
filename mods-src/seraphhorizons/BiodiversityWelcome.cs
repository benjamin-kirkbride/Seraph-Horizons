using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod;

/// <summary>
/// Biodiversity (<c>biodiversity</c>, 1.1.4) greets every player in chat when they join a world: a
/// welcome naming its sub-mods, a link to its Discord and a link to turn the message off
/// (<c>bdWelcomeSystem.Event_WelcomeMessage</c>, on the client's <c>PlayerJoin</c>). Its only switch
/// is per client (<c>ModConfig/biodiversity/client.json</c>'s <c>DisableWelcomeMsg</c>, set by the
/// <c>.nobdwelcome</c> command), which the pack's config defaults cannot reach: a client-only file is
/// in no snapshot, so every player of the pack would see it until they clicked it away. A prefix
/// skips the method instead, so the message is never shown.
///
/// Biodiversity is not referenced at build time: the method is found by name, and if it is gone the
/// tweak logs a warning and leaves the message as it ships. Client side only: the message is the
/// client's.
/// </summary>
public static class BiodiversityWelcome
{
    public const string ModId = "biodiversity";
    public const string HarmonyId = "seraphhorizons.biodiversitywelcome";
    public const string WelcomeType = "biodiversity.src.System.bdWelcomeSystem";

    public static bool Applies(ICoreAPI api) => api.ModLoader.IsModEnabled(ModId);

    public static MethodInfo? Target => AccessTools.TypeByName(WelcomeType) is { } type
        ? AccessTools.DeclaredMethod(type, "Event_WelcomeMessage")
        : null;

    /// <summary>Patches the welcome; logs and leaves it when it is not there. Returns whether the
    /// patch went in.</summary>
    public static bool Patch(Harmony harmony, ILogger logger)
    {
        if (Target is not { } target)
        {
            logger.Warning("[seraphhorizons] Biodiversity welcome: {0}.Event_WelcomeMessage not found; Biodiversity changed, so its welcome message shows as it ships",
                WelcomeType);
            return false;
        }
        harmony.Patch(target, prefix: new HarmonyMethod(typeof(BiodiversityWelcome), nameof(Prefix)));
        logger.Notification("[seraphhorizons] Biodiversity welcome: its join message (and Discord link) is not shown");
        return true;
    }

    private static bool Prefix() => false;
}
