using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.Woodworking;

/// <summary>
/// Immersive Woodworking's bark, for debarking done outside its own (retired) sawhorse: one roll
/// per log, exactly as that sawhorse's <c>DropRolledBark</c> does it, through the mod's public
/// calls found by name.
///
/// - The kind comes from its bark table (<c>ImmersiveWoodworkingModSystem.BarkDrops</c>, the asset
///   <c>immersivewoodworking:config/barkdrops.json</c>):
///   <c>BarkDropTable.Roll(species, world.Rand, chance multiplier)</c>, null for generic bark, as
///   an item by <c>BarkDropTable.ItemCodeForKind</c>.
/// - The count is the table's for the species, else the <c>BarkPerLog</c> setting (3):
///   <c>BarkDropTable.ResolveCount</c>. All pieces of one log are the same kind.
/// - The chance multiplier is the tool set's (<see cref="ChanceMultiplier"/>): a bark spud
///   <c>BarkChanceMultSpud</c> (1.0) plus <c>BarkChanceMultSpudSteelBonus</c> (0.25) times the
///   spud's metal tier fraction (<c>ImmersiveWoodworkingModSystem.MetalTierBonusFraction</c>), an
///   axe with a hammer in the offhand <c>BarkChanceMultAxeHammer</c> (0.75).
///
/// The settings and the table are read on the side that asks, from its own Immersive Woodworking
/// mod system (a client's settings are the server's). Debarking is done on the server, so that is
/// where this is called. Static, for use from Harmony patches; <see cref="Bind"/> finds every
/// member (the same on both sides). Nothing clears them: in singleplayer the server's patches may
/// still call this after the client has disposed, and the next Bind sets the same members.
/// </summary>
public static class BarkDrops
{
    public const string TableType = WoodworkingMods.IwNamespace + ".BarkDropTable";
    public const string SpudCode = "barkspud";

    private static PropertyInfo? _table;
    private static PropertyInfo? _config;
    private static MethodInfo? _roll;
    private static MethodInfo? _resolveCount;
    private static MethodInfo? _itemCodeForKind;
    private static MethodInfo? _metalTierBonus;
    private static FieldInfo? _barkPerLog;
    private static FieldInfo? _multSpud;
    private static FieldInfo? _multSpudSteelBonus;
    private static FieldInfo? _multAxeHammer;
    private static FieldInfo? _durabilityPerLog;

    /// <summary>Finds Immersive Woodworking's bark table and settings. Returns null when all of it
    /// is there, else what is missing (for <see cref="WoodworkingPart.Bind"/>). Safe to call from
    /// several parts.</summary>
    public static string? Bind(WoodworkingMods mods)
    {
        var system = mods.IwSystem.GetType();
        var table = AccessTools.TypeByName(TableType);
        _config = AccessTools.DeclaredProperty(system, "Config");
        _table = AccessTools.DeclaredProperty(system, "BarkDrops");
        _metalTierBonus = AccessTools.DeclaredMethod(system, "MetalTierBonusFraction", [typeof(ItemStack)]);
        _roll = table == null ? null : AccessTools.DeclaredMethod(table, "Roll", [typeof(string), typeof(Random), typeof(double)]);
        _resolveCount = table == null ? null : AccessTools.DeclaredMethod(table, "ResolveCount", [typeof(string), typeof(int)]);
        _itemCodeForKind = table == null ? null : AccessTools.DeclaredMethod(table, "ItemCodeForKind", [typeof(string)]);
        _barkPerLog = mods.IwSetting<int>("BarkPerLog");
        _multSpud = mods.IwSetting<float>("BarkChanceMultSpud");
        _multSpudSteelBonus = mods.IwSetting<float>("BarkChanceMultSpudSteelBonus");
        _multAxeHammer = mods.IwSetting<float>("BarkChanceMultAxeHammer");
        _durabilityPerLog = mods.IwSetting<int>("DebarkDurabilityPerLog");
        if (table == null || _table?.PropertyType != table || _table.GetMethod is not { IsStatic: false }
            || _config?.GetMethod is not { IsStatic: false })
            return $"{WoodworkingMods.IwSystemType}.BarkDrops ({TableType}) is missing";
        if (_roll is not { IsStatic: false } || _roll.ReturnType != typeof(string)
            || _resolveCount is not { IsStatic: false } || _resolveCount.ReturnType != typeof(int)
            || _itemCodeForKind is not { IsStatic: true } || _itemCodeForKind.ReturnType != typeof(string))
            return $"{TableType}.Roll, ResolveCount or ItemCodeForKind is not as expected";
        if (_metalTierBonus is not { IsStatic: false } || _metalTierBonus.ReturnType != typeof(float))
            return $"{WoodworkingMods.IwSystemType}.MetalTierBonusFraction(ItemStack) is missing";
        if (_barkPerLog == null || _multSpud == null || _multSpudSteelBonus == null || _multAxeHammer == null
            || _durabilityPerLog == null)
            return $"{WoodworkingMods.IwConfigType}'s bark settings are not as expected";
        return null;
    }

    /// <summary>Whether <paramref name="stack"/> is Immersive Woodworking's bark spud
    /// (<c>immersivewoodworking:barkspud-*</c>), which, unlike the axe, has no tool type.</summary>
    public static bool IsSpud(ItemStack? stack) =>
        stack?.Item?.Code is { } code && code.Domain == WoodworkingMods.IwModId && code.FirstCodePart() == SpudCode;

    /// <summary>The bark chance multiplier of a debark with <paramref name="mainHand"/>, and
    /// <paramref name="offHand"/> in the offhand: a bark spud's, or an axe with a hammer's;
    /// 0 for anything else, which does not debark here (Immersive Woodworking's knife is off).</summary>
    public static double ChanceMultiplier(ICoreAPI api, ItemStack? mainHand, ItemStack? offHand)
    {
        var system = IwSystem(api);
        object config = _config!.GetValue(system)!;
        if (IsSpud(mainHand))
            return (float)_multSpud!.GetValue(config)! + (float)_multSpudSteelBonus!.GetValue(config)!
                * (float)_metalTierBonus!.Invoke(system, [mainHand])!;
        if (mainHand?.Collectible?.Tool == EnumTool.Axe && offHand?.Collectible?.Tool == EnumTool.Hammer)
            return (float)_multAxeHammer!.GetValue(config)!;
        return 0;
    }

    /// <summary>The bark one debarked log of <paramref name="species"/> (the bare wood name, e.g.
    /// <c>oak</c>; any case) gives, rolled with <paramref name="chanceMultiplier"/>
    /// (<see cref="ChanceMultiplier"/>): one stack of the table's count, or null if the item is
    /// missing. Server side; the caller spawns it.</summary>
    public static ItemStack? Roll(IWorldAccessor world, string? species, double chanceMultiplier)
    {
        var system = IwSystem(world.Api);
        object table = _table!.GetValue(system)!;
        int perLog = (int)_barkPerLog!.GetValue(_config!.GetValue(system))!;
        string? kind = (string?)_roll!.Invoke(table, [species, world.Rand, chanceMultiplier]);
        string code = (string)_itemCodeForKind!.Invoke(null, [kind])!;
        int count = (int)_resolveCount!.Invoke(table, [species, perLog])!;
        var item = world.GetItem(new AssetLocation(WoodworkingMods.IwModId, code));
        if (item == null || count <= 0)
        {
            world.Logger.Warning($"[seraphhorizons] Unified woodworking: no bark item {WoodworkingMods.IwModId}:{code}, "
                                 + $"so a {species} log dropped no bark");
            return null;
        }
        return new ItemStack(item, count);
    }

    /// <summary>The durability a debarking tool loses per log, Immersive Woodworking's
    /// <c>DebarkDurabilityPerLog</c> (1; 0 = none).</summary>
    public static int DurabilityPerLog(ICoreAPI api) =>
        (int)_durabilityPerLog!.GetValue(_config!.GetValue(IwSystem(api)))!;

    private static ModSystem IwSystem(ICoreAPI api) => api.ModLoader.GetModSystem(WoodworkingMods.IwSystemType);
}
