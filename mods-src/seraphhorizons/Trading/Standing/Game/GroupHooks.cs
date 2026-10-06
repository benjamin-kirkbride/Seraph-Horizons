using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Trading.Standing;

/// <summary>
/// Joining, leaving and disbanding groups (#463). The game fires no events for them; every path
/// (<c>/group create|join|acceptinvite|add|leave|kick|remove|confirmdisband</c>, and mods) ends in
/// <c>ServerPlayerData.JoinGroup</c> / <c>LeaveGroup</c> (both overloads) and, for a disband,
/// <c>PlayerDataManager.RemovePlayerGroup</c> (game 1.22.7, VintagestoryLib, bound by name). A
/// disband removes only online members' memberships; offline ones lose theirs at their next login,
/// and an unknown group uid is no company anyway. If a method is missing the hooks are skipped with
/// a log line: companies still sync, at the next use (<see cref="StandingSystem.CompanyOf"/>).
/// </summary>
public static class GroupHooks
{
    public const string PlayerDataType = "Vintagestory.Server.ServerPlayerData";
    public const string ManagerType = "Vintagestory.Server.PlayerDataManager";

    internal static StandingSystem? Owner;
    private static FieldInfo? _uidField;

    public static bool Patch(Harmony harmony, StandingSystem owner, ILogger log)
    {
        var data = AccessTools.TypeByName(PlayerDataType);
        var manager = AccessTools.TypeByName(ManagerType);
        _uidField = data is null ? null : AccessTools.Field(data, "PlayerUID");
        var join = data is null ? null : AccessTools.Method(data, "JoinGroup", [typeof(PlayerGroup), typeof(EnumPlayerGroupMemberShip)]);
        var leave = data is null ? null : AccessTools.Method(data, "LeaveGroup", [typeof(PlayerGroup)]);
        var leaveId = data is null ? null : AccessTools.Method(data, "LeaveGroup", [typeof(int)]);
        var remove = manager is null ? null : AccessTools.Method(manager, "RemovePlayerGroup", [typeof(PlayerGroup)]);
        var missing = new[] { ("ServerPlayerData.PlayerUID", (object?)_uidField), ("ServerPlayerData.JoinGroup", join),
                ("ServerPlayerData.LeaveGroup(PlayerGroup)", leave), ("ServerPlayerData.LeaveGroup(int)", leaveId),
                ("PlayerDataManager.RemovePlayerGroup", remove) }
            .Where(m => m.Item2 is null).Select(m => m.Item1).ToList();
        if (missing.Count > 0)
        {
            log.Warning("[seraphhorizons] Trader standing: the game has no {0}; companies sync at each use instead of on join and leave",
                string.Join(", ", missing));
            return false;
        }
        Owner = owner;
        harmony.Patch(join, postfix: new HarmonyMethod(typeof(GroupHooks), nameof(Joined)));
        harmony.Patch(leave, postfix: new HarmonyMethod(typeof(GroupHooks), nameof(Left)));
        harmony.Patch(leaveId, postfix: new HarmonyMethod(typeof(GroupHooks), nameof(Left)));
        harmony.Patch(remove, postfix: new HarmonyMethod(typeof(GroupHooks), nameof(Disbanded)));
        return true;
    }

    private static string? Uid(object instance) => _uidField?.GetValue(instance) as string;

    // Postfixes catch their own failures: a standing bug must never break the game's group commands.

    public static void Joined(object __instance, PlayerGroup group)
    {
        try
        {
            if (Owner is { } o && Uid(__instance) is { Length: > 0 } uid && group != null) o.OnJoined(uid, group.Uid);
        }
        catch (Exception e) { Owner?.Mod.Logger.Error("[seraphhorizons] Trader standing: on joining a group: {0}", e); }
    }

    public static void Left(object __instance)
    {
        try
        {
            if (Owner is { } o && Uid(__instance) is { Length: > 0 } uid) o.OnLeft(uid);
        }
        catch (Exception e) { Owner?.Mod.Logger.Error("[seraphhorizons] Trader standing: on leaving a group: {0}", e); }
    }

    public static void Disbanded(PlayerGroup group)
    {
        try
        {
            if (Owner is { } o && group != null) o.OnDisbanded(group.Uid);
        }
        catch (Exception e) { Owner?.Mod.Logger.Error("[seraphhorizons] Trader standing: on disbanding a group: {0}", e); }
    }
}
